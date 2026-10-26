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
