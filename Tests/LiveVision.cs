using System.Diagnostics;
using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    // Explicit opt-in smoke test against a local server. Never records images, credentials or reasoning text.
    private static async Task<int> LiveVision(string configFile)
    {
        JObject config = JObject.Parse(await File.ReadAllTextAsync(configFile));
        var options = config["vision"]!.ToObject<VisionSettings>()!;
        var scene = config["scene"]?.ToObject<SceneSnapshot>() ?? CompositionScene();
        var family = (string?)config["family"] == "Illustrious" ? ModelFamily.Illustrious : ModelFamily.Anima;
        if (family == ModelFamily.Illustrious && config["scene"] == null) scene.Characters.RemoveAt(0);
        var portraits = new List<PortraitReference>();
        foreach (SceneCharacter character in scene.Characters)
        {
            string? path = (string?)config["portraits"]?[character.Key];
            if (!string.IsNullOrWhiteSpace(path))
                portraits.Add(new PortraitReference { CharacterKey = character.Key, Png = await File.ReadAllBytesAsync(path) });
        }
        string output = Path.GetFullPath((string)config["output"]!);
        Directory.CreateDirectory(output);
        using var api = new VisionTrace(options.Model);
        using var client = new VisionClient(options, api);
        var report = new JObject { ["family"] = family.ToString(), ["requests"] = api.Rows };
        var timer = Stopwatch.StartNew();
        try
        {
            using var comfy = new ComfyClient((string)config["comfy"]!);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            report["comfy_release_notice"] = await comfy.ReleaseMemoryAsync(Console.WriteLine, cleanup.Token);
            string prompt = await VisionPrompts.GenerateAsync(client, options, scene, family, "", portraits, Console.WriteLine, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(output, "final-prompt.txt"), prompt);
            report["success"] = true;
            report["prompt_characters"] = prompt.Length;
            report["portrait_observations"] = new JObject(scene.Characters.Select(c => new JProperty(c.Key, c.PortraitObservation)));
        }
        catch (Exception ex)
        {
            report["success"] = false;
            report["error_type"] = ex.GetType().Name;
            report["error"] = ex.Message;
        }
        report["selected_loaded_instances_after"] = api.LoadedCount;
        report["elapsed_seconds"] = timer.Elapsed.TotalSeconds;
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), report.ToString());
        Console.WriteLine(report.ToString());
        return (bool)report["success"]! && api.LoadedCount == 0 ? 0 : 1;
    }

    private sealed class VisionTrace : DelegatingHandler
    {
        internal readonly JArray Rows = new();
        internal int LoadedCount = -1;
        private readonly string model;
        internal VisionTrace(string model) : base(new HttpClientHandler { AllowAutoRedirect = false }) { this.model = model; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string path = request.RequestUri!.AbsolutePath;
            JObject body = request.Content == null ? new() : JObject.Parse(await request.Content.ReadAsStringAsync(cancel));
            HttpResponseMessage response = await base.SendAsync(request, cancel);
            JObject data = JObject.Parse(await response.Content.ReadAsStringAsync(cancel));
            if (path.EndsWith("/api/v1/models"))
                LoadedCount = data["models"]!.OfType<JObject>().Where(m => (string?)m["key"] == model)
                    .Sum(m => (m["loaded_instances"] as JArray)?.Count ?? 0);
            if (path.EndsWith("/chat") || path.EndsWith("/chat/completions"))
            {
                JArray items = data["output"] as JArray ?? new();
                string answer = string.Join("\n", items.OfType<JObject>().Where(i => (string?)i["type"] == "message").Select(i => (string?)i["content"] ?? ""));
                if (items.Count == 0) answer = (string?)data["choices"]?[0]?["message"]?["content"] ?? "";
                Rows.Add(new JObject {
                    ["route"] = path, ["http_status"] = (int)response.StatusCode,
                    ["requested_output_tokens"] = body["max_output_tokens"] ?? body["max_tokens"],
                    ["reasoning_setting"] = body["reasoning"], ["store"] = body["store"],
                    ["answer_characters"] = answer.Length,
                    ["reasoning_present"] = items.OfType<JObject>().Any(i => (string?)i["type"] == "reasoning") ||
                        !string.IsNullOrEmpty((string?)(data["choices"]?[0]?["message"]?["reasoning_content"] ?? data["choices"]?[0]?["message"]?["reasoning"])),
                    ["finish_reason"] = data["choices"]?[0]?["finish_reason"],
                    ["output_tokens"] = data["stats"]?["total_output_tokens"] ?? data["usage"]?["completion_tokens"],
                    ["reasoning_tokens"] = data["stats"]?["reasoning_output_tokens"] ?? data["usage"]?["completion_tokens_details"]?["reasoning_tokens"]
                });
            }
            return response;
        }
    }
}
