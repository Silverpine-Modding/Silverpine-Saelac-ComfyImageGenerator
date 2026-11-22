using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1X8AAAAASUVORK5CYII=");
    private static int assertions;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    public static int Main()
    {
        try
        {
            if (Type.GetType("Mono.Runtime") == null) throw new Exception("Run these checks with the included Unity Mono host.");
            Console.WriteLine("Runtime: " + typeof(object).Assembly.Location);
            Run().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + assertions + " Unity Mono assertions; multipart success, cancellation, HTTP errors and safe diagnostics.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ComfyMonoTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (ModelFamily family in new[] { ModelFamily.Anima, ModelFamily.Illustrious })
            {
                var settings = new GenerationSettings { Family = family, Model = "test", Positive = "test scene", Seed = 1,
                    PortraitUploadNamespace = Guid.NewGuid().ToString("N"), PortraitSettings = PortraitReferenceSettings.Default(family) };
                settings.PortraitSettings.Enabled = true;
                settings.SceneCharacterKeys.Add("player:Dan");
                if (family == ModelFamily.Anima) settings.SceneCharacterKeys.Add("npc:Oriana");
                settings.Portraits = settings.SceneCharacterKeys.Select(k => new PortraitReference { CharacterKey = k, Png = Png }).ToList();
                using var api = new UploadApi(settings);
                using var client = new ComfyClient("http://mock.invalid/", handler: api, pollInterval: TimeSpan.FromMilliseconds(1));
                var result = await client.GenerateAsync(settings, root, TimeSpan.FromSeconds(10), _ => { }, CancellationToken.None);
                Check(result.Images.Count == 1 && File.ReadAllBytes(result.Images[0]).SequenceEqual(Png), "Image download succeeds under Mono");
                Check(api.Uploads == settings.Portraits.Count && api.Queued == 1, "All reference uploads complete before a single job");
                TemporaryImages.DeleteRun(root, result.Directory);

                using var cancel = new CancellationTokenSource();
                api.OnUpload = cancel.Cancel;
                try { await client.GenerateAsync(settings, root, TimeSpan.FromSeconds(10), _ => { }, cancel.Token); throw new Exception("Expected upload cancellation"); }
                catch (OperationCanceledException) { assertions++; }
                Check(api.Queued == 1, "Cancellation during upload does not submit another job");
                api.OnUpload = null; api.FailUpload = true;
                try { await client.GenerateAsync(settings, root, TimeSpan.FromSeconds(10), _ => { }, CancellationToken.None); throw new Exception("Expected upload HTTP failure"); }
                catch (HttpRequestException ex) { Check(ex.Message.Contains("HTTP 500"), "HTTP upload error is not masked by disposal exception"); }
                Check(api.Queued == 1, "Failed upload does not submit another job");
            }
            await VisionRequests();
            var scene = new SceneSnapshot { Characters = new System.Collections.Generic.List<SceneCharacter> {
                new SceneCharacter { Key = "player:Dan", Name = "Dan", Description = "An anthro fox with white fur." } }, Environment = "bedroom" };
            string completed = "Dan is a white-furred anthro fox standing by the bed.";
            var answer = new JObject { ["characters"] = new JArray(new JObject { ["character_id"] = "player:Dan", ["prompt"] = completed }), ["environment"] = "bedroom" };
            string assembled = CharacterScenePrompts.ApplyVision(answer.ToString(), scene, ModelFamily.Anima, out _);
            Check(assembled.Contains("Dan: " + completed) && !assembled.Contains("An anthropomorphic fox with white fur."), "Vision prompt avoids second appearance on Unity Mono");
            Check(ScenePrompts.CharacterReference(scene.Characters[0], false)["physical_identity_anchor"] == null, "Vision reference omits derived identity copy on Unity Mono");
            answer["characters"]![0]!["species"] = "arctic fox";
            answer["characters"]![0]!["prompt"] = "Dan stands in a blue tunic.";
            Check(CharacterScenePrompts.ApplyVision(answer.ToString(), scene, ModelFamily.Anima, out _).Contains("Dan (arctic fox): Dan stands"),
                "Explicit species metadata survives vision prompt assembly on Mono");
            answer["characters"]![0]!["prompt"] = new JObject { ["identity"] = "Dan is an arctic fox", ["body"] = new JObject { ["surface"] = "fur", ["colors"] = new JArray("brown", "white") },
                ["features"] = new JArray("His neck ruff is white.", JValue.CreateNull()), ["action"] = "Dan stands by the bed." };
            Check(CharacterScenePrompts.ApplyVision(answer.ToString(), scene, ModelFamily.Anima, out _).Contains(
                "Dan: Dan is an arctic fox. Dan has brown and white fur. His neck ruff is white. Dan stands by the bed."),
                "Structured vision facts retain separate sentences and color scope on Unity Mono");
            answer["characters"]![0]!["prompt"] = new JObject { ["appearance"] = new JArray(new JObject { ["concept"] = "FUR", ["modifiers"] = new JArray("WHITE") }, "blue eyes"),
                ["identity"] = new JArray("arctic fox", "anthro"), ["pose"] = new JArray("standing"), ["clothing"] = new JArray(new JObject { ["concept"] = "tunic", ["color"] = "blue", ["details"] = new JArray("lacing") }, "white fur") };
            answer["environment"] = new JArray("bedroom", "warm_light");
            string tagged = CharacterScenePrompts.ApplyVision(answer.ToString(), scene, ModelFamily.Illustrious, out _);
            Check(tagged.Contains("arctic fox, anthro, standing, white fur, blue eyes, blue tunic") && tagged.EndsWith("bedroom, warm light"),
                "Illustrious tag arrays normalize, deduplicate and assemble under Unity Mono");
            try { ThrowPrivateMessage(); }
            catch (Exception ex)
            {
                string log = GenerationDiagnostics.ForLog(new InvalidOperationException("secret outer", ex));
                Check(log.Contains("NullReferenceException") && log.Contains("ThrowPrivateMessage"), "Log retains exception type and failing method");
                Check(!log.Contains("secret") && !log.Contains("https://") && !log.Contains("Bearer"), "Log omits exception messages and credentials");
            }
        }
        finally { if (TemporaryImages.IsInside(Path.GetTempPath(), root)) Directory.Delete(root, true); }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowPrivateMessage() => throw new NullReferenceException("secret prompt https://private.invalid Bearer secret");
    private static async Task VisionRequests()
    {
        foreach (VisionProvider provider in Enum.GetValues(typeof(VisionProvider)))
        {
            var options = new VisionSettings { Provider = provider, Model = "test", Address = "http://mock.invalid", Enabled = true };
            using var api = new VisionApi();
            using var client = new VisionClient(options, api, TimeSpan.FromMilliseconds(1));
            string result = await client.RunAsync((v, c) => v.ChatAsync("Describe faithfully.", "Portrait of one character", Png, 256, c), _ => { }, CancellationToken.None);
            Check(result == "A white fox with blue eyes." && api.ImageReceived, "Vision JSON/image body survives Unity Mono: " + provider);
            Check(!api.Loaded && api.Unloads > 0, "Vision model unloaded on Unity Mono: " + provider);
        }
    }
    private sealed class VisionApi : HttpMessageHandler
    {
        internal bool Loaded, ImageReceived;
        internal int Unloads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string route = request.RequestUri!.AbsolutePath;
            JObject body = request.Content == null ? new JObject() : JObject.Parse(await request.Content.ReadAsStringAsync());
            JToken reply;
            if (route == "/api/tags") reply = JObject.Parse("{\"models\":[{\"name\":\"test\"}]}");
            else if (route == "/api/show") reply = JObject.Parse("{\"capabilities\":[\"vision\"]}");
            else if (route == "/api/v1/models") reply = new JObject { ["models"] = new JArray(new JObject { ["key"] = "test", ["type"] = "llm",
                ["capabilities"] = JObject.Parse("{\"vision\":true,\"reasoning\":{\"allowed_options\":[\"off\",\"on\"]}}"),
                ["loaded_instances"] = Loaded ? new JArray(new JObject { ["id"] = "test-instance" }) : new JArray() }) };
            else if (route == "/api/v1/models/load") { Loaded = true; reply = JObject.Parse("{\"instance_id\":\"test-instance\"}"); }
            else if (route == "/api/admin/list_options") reply = new JArray("test", "unload_model");
            else if (route == "/api/admin/reload_config") { Loaded = (string?)body["filename"] != "unload_model"; if (!Loaded) Unloads++; reply = JObject.Parse("{\"success\":true}"); }
            else if (route == "/api/extra/version") reply = new JObject { ["llm"] = Loaded, ["vision"] = Loaded };
            else if (route == "/api/ps") reply = new JObject { ["models"] = Loaded ? new JArray(new JObject { ["name"] = "test" }) : new JArray() };
            else if (route == "/api/generate" || route == "/api/v1/models/unload") { Loaded = false; Unloads++; reply = new JObject(); }
            else if (route == "/api/chat" || route == "/v1/chat/completions" || route == "/api/v1/chat")
            {
                Loaded = true;
                string? image = route == "/api/chat" ? (string?)body["messages"]?[1]?["images"]?[0] : route == "/api/v1/chat" ?
                    (string?)body["input"]?[1]?["data_url"] : (string?)body["messages"]?[1]?["content"]?[1]?["image_url"]?["url"];
                ImageReceived = image != null && image.EndsWith(Convert.ToBase64String(Png), StringComparison.Ordinal);
                reply = route == "/api/chat" ? JObject.Parse("{\"message\":{\"content\":\"A white fox with blue eyes.\"}}") : JObject.Parse("{\"choices\":[{\"message\":{\"content\":\"A white fox with blue eyes.\"}}]}");
                if (route == "/api/v1/chat")
                {
                    Check((string?)body["reasoning"] == "off" && (bool?)body["store"] == false, "LM Studio native reasoning and persistence controls survive Mono");
                    reply = JObject.Parse("{\"output\":[{\"type\":\"reasoning\",\"content\":\"private thinking\"},{\"type\":\"message\",\"content\":\"A white fox with blue eyes.\"}]}");
                }
            }
            else throw new Exception("Unexpected vision route " + route);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply.ToString(), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class UploadApi : HttpMessageHandler
    {
        private readonly JObject nodes = new JObject();
        private string id = "";
        internal int Uploads, Queued;
        internal bool FailUpload;
        internal Action? OnUpload;
        internal UploadApi(GenerationSettings settings)
        {
            foreach (JToken node in Workflow.Build(settings, 1).Values()) nodes[(string)node["class_type"]!] = new JObject();
            if (nodes["AnimaIPAdapterApply"] != null)
            {
                nodes["AnimaIPAdapterApply"]!["description"] = PortraitRequirements.AnimaCompatibility;
                nodes["AnimaIPAdapterApply"]!["input"] = new JObject { ["optional"] = new JObject { ["ref_image_2"] = new JArray("IMAGE") } };
            }
        }
        private static HttpResponseMessage Json(JObject value, HttpStatusCode status = HttpStatusCode.OK) => new HttpResponseMessage(status)
        { Content = new StringContent(value.ToString(), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string route = request.RequestUri!.AbsolutePath;
            if (route == "/object_info") return Json(nodes);
            if (route.EndsWith("/cancel")) return Json(new JObject { ["cancelled"] = false });
            if (route == "/upload/image")
            {
                var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(p => p.Headers.ContentDisposition!.Name.Trim('"'));
                Check((await parts["image"].ReadAsByteArrayAsync()).SequenceEqual(Png), "Multipart PNG bytes survive Mono");
                Check(await parts["type"].ReadAsStringAsync() == "temp", "Upload stays temporary");
                Uploads++; OnUpload?.Invoke(); cancel.ThrowIfCancellationRequested();
                if (FailUpload) return Json(new JObject { ["error"] = "test error" }, HttpStatusCode.InternalServerError);
                return Json(new JObject { ["name"] = parts["image"].Headers.ContentDisposition!.FileName.Trim('"'),
                    ["subfolder"] = await parts["subfolder"].ReadAsStringAsync(), ["type"] = "temp" });
            }
            if (route == "/prompt")
            {
                var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
                id = (string)body["prompt_id"]!; Queued++;
                return Json(new JObject { ["prompt_id"] = id });
            }
            if (route.StartsWith("/history/")) return Json(new JObject { [id] = new JObject
            { ["status"] = new JObject { ["completed"] = true }, ["outputs"] = new JObject { ["9"] = new JObject
                { ["images"] = new JArray(new JObject { ["filename"] = "image.png", ["type"] = "temp" }) } } } });
            if (route == "/view") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
            throw new Exception("Unexpected mock route: " + route);
        }
    }
}
