using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SilverpineMods.ComfyImageGenerator;

// Uses native lifecycle APIs, even where inference is OpenAI-compatible.
// Never assumes that an inference response or a closed socket released a model.
public sealed class VisionClient : IDisposable
{
    private readonly VisionSettings settings;
    private readonly HttpClient http;
    private readonly TimeSpan poll;
    private readonly TimeSpan cleanupTimeout;
    private string model, instance = "";
    private bool touched, interrupted, loadUncertain, canDisableReasoning;
    private readonly string generationKey = "Silverpine-" + Guid.NewGuid().ToString("N");

    public VisionClient(VisionSettings settings, HttpMessageHandler? handler = null, TimeSpan? pollInterval = null, TimeSpan? cleanupTimeout = null)
    {
        settings.Validate(false);
        this.settings = settings;
        model = settings.Model.Trim();
        poll = pollInterval ?? TimeSpan.FromSeconds(1);
        this.cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(120);
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = VisionSettings.NormalizeAddress(settings.Address), Timeout = Timeout.InfiniteTimeSpan };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
    }

    public async Task<string[]> ListModelsAsync(CancellationToken cancel)
    {
        JToken data = await Send(HttpMethod.Get, settings.Provider switch
        {
            VisionProvider.Ollama => "api/tags", VisionProvider.LMStudio => "api/v1/models", _ => "api/admin/list_options"
        }, null, cancel);
        if (settings.Provider == VisionProvider.KoboldCpp)
        {
            if (data is not JArray options || !options.Values<string>().Contains("unload_model"))
                throw new InvalidOperationException("KoboldCPP needs Enable Model Administration, a config directory and the correct admin password for loading/unloading.");
            return options.Values<string>().Where(s => s != null && s != "unload_model").Select(s => s!).ToArray();
        }
        JArray rows = data["models"] as JArray ?? throw new InvalidDataException("The vision server returned no model list. LM Studio requires the native v1 model-management API (0.4.0 or newer).");
        return rows.OfType<JObject>().Where(r => settings.Provider != VisionProvider.LMStudio || (string?)r["type"] == "llm")
            .Select(r => (string?)(r[settings.Provider == VisionProvider.Ollama ? "name" : "key"]) ?? "").Where(s => s.Length > 0).ToArray();
    }

    public async Task<T> RunAsync<T>(Func<VisionClient, CancellationToken, Task<T>> action, Action<string> progress, CancellationToken cancel)
    {
        settings.Validate();
        Exception? failure = null;
        try
        {
            progress("Checking vision LLM model and memory controls...");
            await Prepare(cancel, progress);
            cancel.ThrowIfCancellationRequested();
            return await action(this, cancel);
        }
        catch (Exception ex) { failure = ex; interrupted = true; throw; }
        finally
        {
            if (touched)
            {
                progress("Unloading vision LLM before image generation...");
                try
                {
                    using var cleanup = new CancellationTokenSource(cleanupTimeout);
                    await Unload(cleanup.Token);
                    progress("Vision LLM unloaded.");
                }
                catch (Exception ex)
                {
                    // A failed hand-off is operational, not a prompt-content veto.
                    throw new InvalidOperationException("Could not confirm vision LLM unloading. No image job was started. Check the LLM server and unload its model before retrying." +
                        (failure == null ? "" : " The preceding vision request also failed or was cancelled."), ex);
                }
            }
        }
    }

    private async Task Prepare(CancellationToken cancel, Action<string> progress)
    {
        string[] available = await ListModelsAsync(cancel);
        if (settings.Provider == VisionProvider.Ollama && !available.Contains(model) && available.Contains(model + ":latest")) model += ":latest";
        if (!available.Contains(model)) throw new InvalidOperationException("The chosen vision model/config is not available. Refresh the model list and select its exact name.");
        if (settings.Provider == VisionProvider.Ollama)
        {
            JToken details = await Send(HttpMethod.Post, "api/show", new JObject { ["model"] = model }, cancel);
            if (details["capabilities"] is JArray caps && !caps.Values<string>().Contains("vision"))
                throw new InvalidOperationException("This Ollama model does not report vision support. Choose a model that accepts images.");
            // Ollama loads on the first chat. Set ownership first, including a cancelled/failed load.
            touched = true;
        }
        else if (settings.Provider == VisionProvider.LMStudio)
        {
            JToken listed = await Send(HttpMethod.Get, "api/v1/models", null, cancel);
            JObject entry = listed["models"]!.OfType<JObject>().First(m => (string?)m["key"] == model);
            if ((bool?)entry["capabilities"]?["vision"] == false) throw new InvalidOperationException("This LM Studio model does not support images. Choose a vision model.");
            canDisableReasoning = ((entry["capabilities"] as JObject)?["reasoning"] as JObject)?["allowed_options"] is JArray reasoning && reasoning.Values<string>().Contains("off");
            instance = (string?)entry["loaded_instances"]?.FirstOrDefault()?["id"] ?? "";
            touched = true;
            if (instance.Length == 0)
            {
                progress("Loading vision model in LM Studio...");
                // Complete the bounded load handshake even after Close, so its exact instance can be unloaded.
                loadUncertain = true;
                JToken loaded = await Send(HttpMethod.Post, "api/v1/models/load", new JObject { ["model"] = model, ["context_length"] = settings.ContextLength }, CancellationToken.None);
                instance = (string?)loaded["instance_id"] ?? "";
                if (instance.Length == 0) throw new InvalidDataException("LM Studio did not return a loaded instance ID.");
                loadUncertain = false;
            }
        }
        else
        {
            touched = true;
            // Establish an inactive state before loading; otherwise the old worker's
            // ready response could be mistaken for the requested config's readiness.
            progress("Preparing KoboldCPP model switch...");
            await KoboldSwitch("unload_model", cancel);
            await WaitKobold(false, cancel);
            cancel.ThrowIfCancellationRequested();
            progress("Loading KoboldCPP vision config...");
            // Let the bounded load finish so cancellation cannot mistake the old
            // inactive worker for a completed unload of a still-loading new worker.
            await KoboldSwitch(model, CancellationToken.None);
            await WaitKobold(true, CancellationToken.None);
        }
    }

    public async Task<string> ChatAsync(string system, string prompt, byte[]? image, int maxTokens, CancellationToken cancel)
    {
        JObject body;
        string route;
        if (settings.Provider == VisionProvider.Ollama)
        {
            var message = new JObject { ["role"] = "user", ["content"] = prompt };
            if (image != null) message["images"] = new JArray(Convert.ToBase64String(image));
            body = new JObject { ["model"] = model, ["stream"] = false, ["keep_alive"] = "2m", ["think"] = false,
                ["messages"] = new JArray(new JObject { ["role"] = "system", ["content"] = system }, message),
                ["options"] = new JObject { ["temperature"] = settings.Temperature, ["num_predict"] = maxTokens, ["num_ctx"] = settings.ContextLength } };
            route = "api/chat";
        }
        else if (settings.Provider == VisionProvider.LMStudio)
        {
            JToken input = new JValue(prompt);
            if (image != null) input = new JArray(new JObject { ["type"] = "text", ["content"] = prompt },
                new JObject { ["type"] = "image", ["data_url"] = "data:image/png;base64," + Convert.ToBase64String(image) });
            body = new JObject { ["model"] = instance, ["stream"] = false, ["store"] = false,
                ["system_prompt"] = system, ["input"] = input,
                ["temperature"] = settings.Temperature, ["max_output_tokens"] = maxTokens };
            // Native chat has a documented reasoning control. Only send a supported value;
            // otherwise LM Studio rejects models without that reasoning option.
            if (canDisableReasoning) body["reasoning"] = "off";
            route = "api/v1/chat";
        }
        else
        {
            JToken content = new JValue(prompt);
            if (image != null) content = new JArray(new JObject { ["type"] = "text", ["text"] = prompt },
                new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(image) } });
            body = new JObject { ["model"] = model, ["stream"] = false,
                ["temperature"] = settings.Temperature, ["max_tokens"] = maxTokens,
                ["messages"] = new JArray(new JObject { ["role"] = "system", ["content"] = system }, new JObject { ["role"] = "user", ["content"] = content }) };
            if (settings.Provider == VisionProvider.KoboldCpp) body["genkey"] = generationKey;
            route = "v1/chat/completions";
        }
        JToken response = await Send(HttpMethod.Post, route, body, cancel);
        string text = settings.Provider == VisionProvider.LMStudio
            ? string.Join("\n", (response["output"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(item => (string?)item["type"] == "message").Select(item => ReadText(item["content"])))
            : ReadText(settings.Provider == VisionProvider.Ollama ? response["message"]?["content"] : response["choices"]?[0]?["message"]?["content"]);
        // Some servers put reasoning in tagged content rather than a separate field.
        // An unfinished thinking block is not an answer and must not become an image prompt.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<think>.*?(?:</think>|$)", "",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(text)) throw EmptyAnswer(response, maxTokens);
        return text;
    }

    private static string ReadText(JToken? content)
    {
        if (content?.Type == JTokenType.String) return (string?)content ?? "";
        if (content is JArray parts) return string.Join("\n", parts.OfType<JObject>()
            .Where(p => (string?)p["type"] == "text").Select(p => p["text"]?.Type == JTokenType.String ? (string)p["text"]! : ""));
        return "";
    }

    private InvalidDataException EmptyAnswer(JToken response, int maxTokens)
    {
        JToken? message = settings.Provider == VisionProvider.Ollama ? response["message"] : response["choices"]?[0]?["message"];
        JToken? total = response["stats"]?["total_output_tokens"] ?? response["usage"]?["completion_tokens"] ?? response["eval_count"];
        JToken? thinking = response["stats"]?["reasoning_output_tokens"] ?? response["usage"]?["completion_tokens_details"]?["reasoning_tokens"];
        long? used = total?.Type == JTokenType.Integer ? (long?)total : null;
        long? reasoned = thinking?.Type == JTokenType.Integer ? (long?)thinking : null;
        bool hasReasoning = reasoned > 0 || !string.IsNullOrWhiteSpace(ReadText(message?["reasoning_content"] ?? message?["reasoning"] ?? message?["thinking"])) ||
            (response["output"] as JArray)?.OfType<JObject>().Any(i => (string?)i["type"] == "reasoning") == true;
        bool limited = used >= maxTokens || (string?)response["choices"]?[0]?["finish_reason"] == "length" || (string?)response["done_reason"] == "length";
        string diagnostic = settings.Provider + " returned no final answer text." + (hasReasoning ? " It returned reasoning without an answer." : "");
        if (used.HasValue) diagnostic += " Output tokens: " + used + " / " + maxTokens + ".";
        if (reasoned.HasValue) diagnostic += " Reasoning tokens: " + reasoned + ".";
        diagnostic += limited ? " The output token limit was reached; raise the output token limit in Vision LLM settings." : " Check the server's model/chat-template and vision support.";
        return new InvalidDataException(diagnostic); // Only numeric metadata; no portraits, prompts, reasoning or server response bodies.
    }

    private async Task Unload(CancellationToken cancel)
    {
        if (settings.Provider == VisionProvider.KoboldCpp)
        {
            if (interrupted)
            {
                try { await Send(HttpMethod.Post, "api/extra/abort", new JObject { ["genkey"] = generationKey }, cancel, 10); }
                catch (HttpRequestException) { } // Admin unload also stops the owned worker.
            }
            while (true)
            {
                try { await KoboldSwitch("unload_model", cancel); break; }
                catch (HttpRequestException) { await Task.Delay(poll, cancel); }
            }
            await WaitKobold(false, cancel);
            return;
        }
        if (settings.Provider == VisionProvider.Ollama)
        {
            await Send(HttpMethod.Post, "api/generate", new JObject { ["model"] = model, ["keep_alive"] = 0, ["stream"] = false }, cancel);
            while (true)
            {
                JToken response = await Send(HttpMethod.Get, "api/ps", null, cancel, 10);
                JArray running = response["models"] as JArray ?? throw new InvalidDataException("Ollama did not report its running models.");
                if (!running.OfType<JObject>().Any(r => (string?)r["name"] == model || (string?)r["model"] == model)) return;
                await Task.Delay(poll, cancel);
            }
        }
        while (true)
        {
            JToken response = await Send(HttpMethod.Get, "api/v1/models", null, cancel, 10);
            JArray models = response["models"] as JArray ?? throw new InvalidDataException("LM Studio did not report model instances.");
            // If the load response was lost, recover instances from the exact requested model.
            string[] ids = models.OfType<JObject>().Where(m => (string?)m["key"] == model)
                .SelectMany(m => (m["loaded_instances"] as JArray ?? new JArray()).OfType<JObject>())
                .Select(i => (string?)i["id"] ?? "").Where(id => id.Length > 0).ToArray();
            if (ids.Length == 0 && !loadUncertain) return;
            if (ids.Length > 0) loadUncertain = false;
            foreach (string id in ids)
            {
                try { await Send(HttpMethod.Post, "api/v1/models/unload", new JObject { ["instance_id"] = id }, cancel, 15); }
                catch (HttpRequestException) { } // A cancelling inference may briefly keep the instance busy; retry within cleanup budget.
            }
            await Task.Delay(poll, cancel);
        }
    }

    private async Task KoboldSwitch(string config, CancellationToken cancel)
    {
        JToken result = await Send(HttpMethod.Post, "api/admin/reload_config", new JObject { ["filename"] = config }, cancel);
        if ((bool?)result["success"] != true) throw new InvalidOperationException("KoboldCPP refused the model switch. Enable Model Administration and check its admin password/config directory.");
    }

    private async Task WaitKobold(bool loaded, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        int confirmations = 0;
        while (true)
        {
            try
            {
                JToken state = await Send(HttpMethod.Get, "api/extra/version", null, timeout.Token, 10);
                bool ready = (bool?)state["llm"] == loaded && (!loaded || (bool?)state["vision"] == true);
                confirmations = ready ? confirmations + 1 : 0;
                if (confirmations >= (loaded ? 1 : 3)) return;
            }
            catch (HttpRequestException) { confirmations = 0; } // KoboldCPP restarts its worker during an admin switch.
            await Task.Delay(poll, timeout.Token);
        }
    }

    private async Task<JToken> Send(HttpMethod method, string route, JObject? body, CancellationToken cancel, int? seconds = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds ?? settings.TimeoutSeconds));
        using var request = new HttpRequestMessage(method, route);
        if (body != null) request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(settings.Provider + " HTTP " + (int)response.StatusCode + " at " + route + ". Check the server, credentials and model-management API support.");
            const int limit = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Vision server response is too large.");
            using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[16384]; int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > limit) throw new InvalidDataException("Vision server response is too large.");
                output.Write(buffer, 0, count);
            }
            try { return JToken.Parse(Encoding.UTF8.GetString(output.ToArray())); }
            catch (JsonException) { throw new InvalidDataException(settings.Provider + " returned invalid JSON at " + route + ". Check the server base URL and API version."); }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        { throw new TimeoutException(settings.Provider + " request timed out at " + route + ". Check the server's model loading/inference status."); }
    }

    public void Dispose() => http.Dispose();
}
