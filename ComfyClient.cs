using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SilverpineMods.ComfyImageGenerator;

public sealed class GenerationResult
{
    public string PromptId { get; }
    public long Seed { get; }
    public string Directory { get; }
    public IReadOnlyList<string> Images { get; }
    public IReadOnlyList<string> DetailerReports { get; }
    public GenerationResult(string promptId, long seed, string directory, IReadOnlyList<string> images, IReadOnlyList<string>? detailerReports = null)
    { PromptId = promptId; Seed = seed; Directory = directory; Images = images; DetailerReports = detailerReports ?? Array.Empty<string>(); }
}

public sealed class ComfyClient : IDisposable
{
    private const int JsonLimit = 16 * 1024 * 1024;
    private const int ImageLimit = 32 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly TimeSpan requestTimeout;
    private readonly TimeSpan pollInterval;
    public static Uri NormalizeAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP(S) ComfyUI base URL, without credentials, query, or fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public ComfyClient(string address, string bearerToken = "", HttpMessageHandler? handler = null,
        TimeSpan? requestTimeout = null, TimeSpan? pollInterval = null)
    {
        Uri uri = NormalizeAddress(address);
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = uri, Timeout = Timeout.InfiniteTimeSpan };
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken.Trim());
    }

    public Task<JObject> GetNodeInfoAsync(CancellationToken cancel) => JsonAsync(HttpMethod.Get, "object_info", null, cancel);
    public async Task<JObject> CheckConnectionAsync(CancellationToken cancel)
    {
        JObject nodes = await GetNodeInfoAsync(cancel).ConfigureAwait(false);
        await CheckCancellationAsync(cancel).ConfigureAwait(false);
        return nodes;
    }
    private async Task CheckCancellationAsync(CancellationToken cancel)
    {
        try
        {
            // Unknown UUID is an explicitly documented no-op. Verifies the specific
            // cancel route, rather than guessing support from a version number.
            JObject answer = await JsonAsync(HttpMethod.Post, "api/jobs/" + Guid.NewGuid().ToString() + "/cancel", new JObject(), cancel).ConfigureAwait(false);
            if (answer["cancelled"]?.Type != JTokenType.Boolean) throw new InvalidDataException("Unexpected cancellation API response.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException("This plugin needs ComfyUI's job-ID cancellation API (/api/jobs/{id}/cancel). Update ComfyUI or expose this route through your proxy so closing the viewer can cancel its job.", ex); }
    }
    public async Task CancelPromptAsync(string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // Idempotent and scoped to this plugin's UUID; never uses global /interrupt.
        JObject response = await JsonAsync(HttpMethod.Post, "api/jobs/" + Uri.EscapeDataString(id) + "/cancel", new JObject(), timeout.Token).ConfigureAwait(false);
        if (response["cancelled"]?.Type != JTokenType.Boolean) throw new InvalidDataException("Server did not acknowledge job cancellation.");
    }

    public async Task<GenerationResult> GenerateAsync(GenerationSettings settings, string outputDirectory,
        TimeSpan timeout, Action<string> progress, CancellationToken cancel)
    {
        settings.Validate();
        long seed = settings.ResolveSeed();
        JObject graph = Workflow.Build(settings, seed);
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        lifetime.CancelAfter(timeout);
        CancellationToken token = lifetime.Token;
        string promptId = "";
        string runDirectory = "";
        bool succeeded = false;
        try
        {
            progress("Checking ComfyUI nodes and model filenames...");
            JObject info = await CheckConnectionAsync(token).ConfigureAwait(false);
            Workflow.ValidateServer(graph, info);
            token.ThrowIfCancellationRequested();
            runDirectory = Path.Combine(outputDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(runDirectory);
            progress("Submitting workflow...");
            token.ThrowIfCancellationRequested();
            promptId = Guid.NewGuid().ToString();
            JObject queued = await JsonAsync(HttpMethod.Post, "prompt", new JObject
            {
                ["prompt"] = graph, ["client_id"] = Guid.NewGuid().ToString("N"), ["prompt_id"] = promptId
            }, CancellationToken.None).ConfigureAwait(false);
            // Finish the bounded POST handshake even after Close, then cancel the
            // acknowledged ID. This prevents losing the ID in a submission race.
            string acknowledgedId = (string?)queued["prompt_id"] ?? "";
            if (string.IsNullOrWhiteSpace(acknowledgedId) || queued["error"] != null)
                throw new InvalidDataException("ComfyUI rejected the workflow: " + Summarize(queued.ToString(Formatting.None)));
            promptId = acknowledgedId;
            token.ThrowIfCancellationRequested();
            progress($"Queued / generating. Seed {seed}. Prompt {promptId}");
            JObject record;
            while (true)
            {
                JObject history = await JsonAsync(HttpMethod.Get, "history/" + Uri.EscapeDataString(promptId), null, token).ConfigureAwait(false);
                if (history[promptId] is JObject entry)
                {
                    ThrowExecutionError(entry);
                    if (entry["status"]?["completed"]?.Value<bool>() == true)
                    { record = entry; break; }
                }
                await Task.Delay(pollInterval, token).ConfigureAwait(false);
            }
            List<JObject> descriptors = ImageDescriptors(record);
            if (descriptors.Count == 0) throw new InvalidDataException("Workflow completed without images. Add a SaveImage or PreviewImage output node.");
            // The viewer owns one temporary image, even for a custom workflow with multiple outputs.
            descriptors = descriptors.Take(1).ToList();
            var saved = new List<string>();
            for (int i = 0; i < descriptors.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                JObject descriptor = descriptors[i];
                progress($"Downloading image {i + 1} of {descriptors.Count}...");
                string query = "view?filename=" + Uri.EscapeDataString((string)descriptor["filename"]!) +
                    "&subfolder=" + Uri.EscapeDataString((string?)descriptor["subfolder"] ?? "") +
                    "&type=" + Uri.EscapeDataString((string?)descriptor["type"] ?? "output");
                byte[] bytes = await SendAsync(HttpMethod.Get, query, null, ImageLimit, token).ConfigureAwait(false);
                string extension = ImageExtension(bytes);
                // Never use a server-controlled filename as a local path.
                string path = Path.Combine(runDirectory, "image-" + (i + 1).ToString("D2") + extension);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                saved.Add(path);
            }
            succeeded = true;
            return new GenerationResult(promptId, seed, runDirectory, saved.AsReadOnly(), ReadDetailerReports(record, settings));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new TimeoutException("Generation timed out; cancellation requested for this job." +
                (promptId.Length == 0 ? "" : " Prompt: " + promptId));
        }
        finally
        {
            if (!succeeded)
            {
                try
                {
                    if (promptId.Length != 0)
                    {
                        progress("Cancelling ComfyUI job " + promptId + "...");
                        try { await CancelPromptAsync(promptId).ConfigureAwait(false); }
                        catch (Exception) { throw new ComfyCancellationException(promptId); }
                    }
                }
                finally { if (runDirectory.Length != 0) TemporaryImages.DeleteRun(outputDirectory, runDirectory); }
            }
        }
    }

    public static IReadOnlyList<string> ReadDetailerReports(JObject record, GenerationSettings settings)
    {
        var reports = new List<string>();
        if (!settings.AttentionCouple || !string.IsNullOrEmpty(settings.WorkflowFile) || !(record["outputs"] is JObject outputs)) return reports;
        for (int i = 0; i < settings.Detailers.Count; i++)
        {
            DetailerSetting pass = settings.Detailers[i];
            string id = "detailer_" + i;
            if (!pass.Enabled || !TryDetectionCount(outputs, id + "_detected_report", out int detected)) continue;
            for (int j = 0; j < settings.Regions.Count; j++)
            {
                CharacterRegion region = settings.Regions[j];
                if ((!string.IsNullOrEmpty(pass.RegionId) && pass.RegionId != region.Id) ||
                    !TryDetectionCount(outputs, id + "_region_" + j + "_selected_report", out int assigned) || assigned > detected) continue;
                string report = (i + 1) + ". " + ScenePrompts.Limit(pass.Name, 100) + " / " + ScenePrompts.Limit(region.Name, 100) + $": {detected} detected, {assigned} selected for detailing.";
                if (detected == 0) report += " No detections: check the detector model and threshold.";
                else if (assigned == 0) report += " No detections assigned: cover at least half the face with this box; earlier regions take priority.";
                reports.Add(report);
                if (reports.Count >= 90) return reports;
            }
        }
        return reports;
    }

    private static bool TryDetectionCount(JObject outputs, string id, out int count)
    {
        count = 0;
        return outputs[id] is JObject output && output["text"] is JArray text && text.Count == 1 &&
            text[0].Type == JTokenType.String && int.TryParse((string?)text[0], out count) && count >= 0 && count <= 10000;
    }

    public static void ThrowExecutionError(JObject record)
    {
        JToken? status = record["status"];
        if (status?["messages"] is JArray messages)
        foreach (JArray message in messages.OfType<JArray>())
        {
            if (message.Count < 1) continue;
            string kind = (string?)message[0] ?? "";
            if (kind == "execution_error" || kind == "execution_interrupted")
            {
                string detail = message.Count > 1 ? (string?)message[1]?["exception_message"] ?? "" : "";
                throw new InvalidDataException("ComfyUI " + kind + ": " + Summarize(detail));
            }
        }
        if ((string?)status?["status_str"] == "error") throw new InvalidDataException("ComfyUI execution failed. Check the ComfyUI console.");
    }

    public static List<JObject> ImageDescriptors(JObject record)
    {
        var results = new List<JObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (record["outputs"] is JObject outputs)
        foreach (JProperty node in outputs.Properties())
        if (node.Value["images"] is JArray images)
        foreach (JObject image in images.OfType<JObject>())
        {
            if (string.IsNullOrWhiteSpace((string?)image["filename"])) continue;
            string type = (string?)image["type"] ?? "output";
            if (type != "output" && type != "temp") continue;
            string key = new JArray(type, (string?)image["subfolder"] ?? "", (string)image["filename"]!).ToString(Formatting.None);
            if (seen.Add(key)) results.Add(image);
        }
        return results.OrderBy(d => (string?)d["type"] == "temp" ? 1 : 0).ToList();
    }

    public static string ImageExtension(byte[] data)
    {
        if (data.Length >= 24 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 &&
            data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10)
        {
            uint width = BigEndian(data, 16), height = BigEndian(data, 20);
            if (width == 0 || height == 0 || width > 8192 || height > 8192 || (ulong)width * height > 16777216)
                throw new InvalidDataException("PNG exceeds the preview limit of 8192 pixels per side / 16 megapixels.");
            return ".png";
        }
        if (data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255)
        {
            int position = 2;
            while (position + 4 < data.Length)
            {
                if (data[position++] != 255) break;
                while (position < data.Length && data[position] == 255) position++;
                if (position >= data.Length) break;
                byte marker = data[position++];
                if (marker == 0xD9 || marker == 0xDA) break;
                if (marker == 0x01 || marker >= 0xD0 && marker <= 0xD8) continue;
                if (position + 2 > data.Length) break;
                int length = (data[position] << 8) | data[position + 1];
                if (length < 2 || position + length > data.Length) break;
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    if (length < 8) break;
                    int h = data[position + 3] << 8 | data[position + 4], w = data[position + 5] << 8 | data[position + 6];
                    if (w < 1 || h < 1 || w > 8192 || h > 8192 || (long)w * h > 16777216)
                        throw new InvalidDataException("JPEG exceeds the preview dimension limit.");
                    return ".jpg";
                }
                position += length;
            }
            throw new InvalidDataException("JPEG has no readable dimension header.");
        }
        throw new InvalidDataException("ComfyUI returned an unsupported image. Use PNG (SaveImage) or JPEG outputs.");
    }
    private static uint BigEndian(byte[] b, int i) => (uint)b[i] << 24 | (uint)b[i + 1] << 16 | (uint)b[i + 2] << 8 | b[i + 3];
    private static string Summarize(string value) => value.Length > 3000 ? value.Substring(0, 3000) + "..." : value;

    private async Task<JObject> JsonAsync(HttpMethod method, string route, JObject? body, CancellationToken cancel)
    {
        byte[] bytes = await SendAsync(method, route, body, JsonLimit, cancel).ConfigureAwait(false);
        try { return JObject.Parse(Encoding.UTF8.GetString(bytes)); }
        catch (JsonException ex) { throw new InvalidDataException("ComfyUI returned invalid JSON for " + route + ". Check the server URL.", ex); }
    }

    private async Task<byte[]> SendAsync(HttpMethod method, string route, JObject? body, int limit, CancellationToken cancel)
    {
        using CancellationTokenSource requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        requestLifetime.CancelAfter(requestTimeout);
        CancellationToken token = requestLifetime.Token;
        using var request = new HttpRequestMessage(method, route);
        if (body != null) request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            int responseLimit = response.IsSuccessStatusCode ? limit : Math.Min(limit, 64 * 1024);
            if (response.Content.Headers.ContentLength > responseLimit) throw new InvalidDataException("ComfyUI response exceeds the download size limit.");
            using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > responseLimit) throw new InvalidDataException("ComfyUI response exceeds the download size limit.");
                buffer.Write(chunk, 0, read);
            }
            byte[] result = buffer.ToArray();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"ComfyUI HTTP {(int)response.StatusCode} for {route}: " + Summarize(Encoding.UTF8.GetString(result)));
            return result;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        { throw new TimeoutException("ComfyUI request timed out. If submission had started, check the server queue before retrying."); }
    }

    public void Dispose() => client.Dispose();
}

public sealed class ComfyCancellationException : Exception
{
    public ComfyCancellationException(string id) : base("Could not confirm server cancellation for job " + id + ". Check the ComfyUI queue; the server may be offline.") { }
}
