using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;
using System.Net;
using System.Text;

internal static partial class Program
{
    private static int assertions;
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestOutput", Guid.NewGuid().ToString("N"));
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1X8AAAAASUVORK5CYII=");
    private static GenerationSettings Settings(ModelFamily family = ModelFamily.Illustrious) => new()
    { Family = family, Model = family == ModelFamily.Anima ? "anima.safetensors" : "illustrious.safetensors", Positive = "A fox reading beside a lantern.", Seed = 42 };
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception("FAIL: " + label); assertions++; }
    private static void Throws<T>(Action action, string label) where T : Exception
    { try { action(); } catch (T) { assertions++; return; } throw new Exception("FAIL: expected " + typeof(T).Name + " - " + label); }
    private static async Task ThrowsAsync<T>(Func<Task> action, string label) where T : Exception
    { try { await action(); } catch (T) { assertions++; return; } throw new Exception("FAIL: expected " + typeof(T).Name + " - " + label); }
    private static string DirectoryFor(string name) => Path.Combine(Root, name);
    private static int ImageCount(string path) => Directory.Exists(path) ? Directory.GetFiles(path, "image-*", SearchOption.AllDirectories).Length : 0;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--portrait-live") return await LivePortraits(args[1]);
        if (args.Length == 2 && args[0] == "--reference-size-live") return await LivePortraits(args[1], referenceSizes: true);
        Directory.CreateDirectory(Root);
        try
        {
            Workflows(); DetailerWorkflows(); DetailerWildcards(); SceneUnderstanding(); SceneSelections(); AnimaIdentity(); PromptTolerance(); SaveDialogRequests(); Profiles(); Prompts(); BasePositivePrompts();
            await DetailerWildcardApi();
            await LlmWaits();
            await ApiSuccess();
            PortraitWorkflows();
            await PortraitApi();
            await PermissivePromptApi();
            await CancelQueued();
            await CancelDuringSubmission();
            await ApiErrors();
            await GenerationTimeout();
            await CancellationFailure();
            InspectAssembly();
            Console.WriteLine($"PASS: {assertions} assertions; workflows, LoRAs/cache/detailers, node requirements, separated prompts, reusable positive prefixes, profiles/presets, LLM waits, HTTP jobs, scoped cancellation and temporary-file cleanup.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (TemporaryImages.IsInside(Path.Combine(AppContext.BaseDirectory, "TestOutput"), Root)) Directory.Delete(Root, true); }
    }

    private static void Workflows()
    {
        GenerationSettings settings = Settings();
        JObject ill = Workflow.Build(settings, 123);
        Check((string?)ill["1"]?["class_type"] == "CheckpointLoaderSimple", "Illustrious checkpoint loader");
        Check((int?)ill["4"]?["inputs"]?["clip"]?[1] == 1, "Illustrious checkpoint CLIP output");
        Check((int?)ill["8"]?["inputs"]?["vae"]?[1] == 2, "Illustrious checkpoint VAE output");
        Check((string?)ill["9"]?["class_type"] == "PreviewImage", "normal generation uses server temp image");
        Check((long?)ill["7"]?["inputs"]?["seed"] == 123, "seed preserved");
        settings.Loras.Add(new LoraSetting { Name = "style.safetensors", ModelStrength = .7, ClipStrength = .4 });
        settings.Loras.Add(new LoraSetting { Name = "character.safetensors", ModelStrength = .9 });
        settings.EasyCache = true;
        JObject withLoras = Workflow.Build(settings, 5);
        Check((string?)withLoras["lora_1"]?["inputs"]?["model"]?[0] == "lora_0", "LoRAs chain in chosen order");
        Check((string?)withLoras["lora_1"]?["inputs"]?["clip"]?[0] == "lora_0", "Illustrious CLIP chain");
        Check((double?)withLoras["lora_0"]?["inputs"]?["strength_clip"] == .4, "independent CLIP strength");
        Check((string?)withLoras["cache"]?["inputs"]?["model"]?[0] == "lora_1", "cache after LoRAs");
        Check((string?)withLoras["7"]?["inputs"]?["model"]?[0] == "cache", "sampler uses cached model");
        settings.Family = ModelFamily.Anima;
        JObject anima = Workflow.Build(settings, 5);
        Check((string?)anima["1"]?["class_type"] == "UNETLoader", "Anima diffusion loader");
        Check((string?)anima["2"]?["inputs"]?["type"] == "stable_diffusion", "official Anima text encoder loader type");
        Check((string?)anima["lora_0"]?["class_type"] == "LoraLoaderModelOnly", "Anima model-only LoRA");
        Check(anima["lora_0"]?["inputs"]?["clip"] == null, "no inappropriate Anima CLIP LoRA patch");
        Check((string?)anima["4"]?["inputs"]?["clip"]?[0] == "2", "Anima still uses Qwen encoder");
        settings.Positive = "quote \" test\\path\n{{negative}}";
        JObject template = JObject.Parse("{\"42\":{\"class_type\":\"Text\",\"inputs\":{\"text\":\"{{positive}}\",\"seed\":\"{{seed}}\",\"size\":\"{{width}}\"}}}");
        JObject expanded = Workflow.FromTemplate(template.ToString(), settings, 99);
        Check((string?)expanded["42"]?["inputs"]?["text"] == settings.Positive, "JSON prompt not interpolated or reinjected");
        Check(expanded["42"]?["inputs"]?["seed"]?.Type == JTokenType.Integer, "numeric placeholder remains numeric");
        Throws<InvalidDataException>(() => Workflow.FromTemplate("{\"nodes\":[]}", settings, 0), "reject canvas workflow");
        Throws<InvalidDataException>(() => Workflow.FromTemplate("{\"42\":5}", settings, 0), "reject malformed graph");
        Throws<InvalidDataException>(() => Workflow.FromTemplate("{\"1\":{\"class_type\":\"X\",\"inputs\":{}}}", settings, 0), "require prompt placeholder");
        settings.WorkflowFile = "custom.json";
        Throws<ArgumentException>(settings.Validate, "custom workflow cannot silently ignore LoRAs/cache");
        settings = Settings(); settings.Width = 1000; Throws<ArgumentException>(settings.Validate, "dimension alignment");
        settings = Settings(); settings.Cfg = double.NaN; Throws<ArgumentException>(settings.Validate, "reject NaN CFG");
        settings = Settings(); settings.EasyCache = true; settings.CacheStart = .9; settings.CacheEnd = .1; Throws<ArgumentException>(settings.Validate, "cache range order");
        settings = Settings(); settings.Loras.Add(new LoraSetting { Name = "x", ModelStrength = double.PositiveInfinity }); Throws<ArgumentException>(settings.Validate, "reject infinite LoRA strength");
        settings = Settings(); settings.Seed = -1;
        Check(settings.ResolveSeed() >= 0 && settings.ResolveSeed() <= 9007199254740991L, "random seed valid range");
        Throws<ArgumentException>(() => ComfyClient.NormalizeAddress("file:///etc"), "HTTP only");
        Throws<ArgumentException>(() => ComfyClient.NormalizeAddress("http://name:secret@localhost"), "no credentials embedded in URL");
        Check(ComfyClient.NormalizeAddress("http://192.168.0.3:8188/comfy").AbsoluteUri == "http://192.168.0.3:8188/comfy/", "remote address and base path");
        Check(ComfyClient.NormalizeAddress("http://[::1]:8188").Port == 8188, "IPv6");
    }

    private static void Profiles()
    {
        string directory = DirectoryFor("profiles");
        var store = new CharacterProfileStore(directory);
        string key = CharacterProfileStore.Key("Mara", false);
        store.Set(key, "Mara", "Red fox with silver glasses.");
        store.SavePreset("Fox cast");
        store.Set(key, "Mara", "Blue coat.");
        store.LoadPreset("Fox cast");
        Check(store.Description(key) == "Red fox with silver glasses.", "preset replaces active profiles");
        Check(File.Exists(Path.Combine(directory, "before-preset-load.json")), "preset load recovery file");
        var reload = new CharacterProfileStore(directory); reload.LoadActive();
        Check(reload.Description(key) == store.Description(key), "profiles survive restart");
        Check(reload.Presets().Single() == "Fox cast", "named preset list");
        Check(CharacterProfileStore.Key("Mara", true) != key, "player and NPC names cannot collide");
        Throws<ArgumentException>(() => store.SavePreset("../escape"), "preset traversal blocked");
        Throws<ArgumentException>(() => store.SavePreset("CON"), "reserved Windows name blocked");
        store.Reset(key); Check(store.Description(key) == null, "reset restores missing state");
        File.WriteAllText(Path.Combine(directory, "Presets", "Broken.json"), "{\"version\":99}");
        Throws<InvalidDataException>(() => reload.LoadPreset("Broken"), "bad preset rejected");
        Check(reload.Description(key) == "Red fox with silver glasses.", "bad preset cannot erase active profiles");
        string cache = DirectoryFor("temp"); string run = Path.Combine(cache, "run"); Directory.CreateDirectory(run);
        string source = Path.Combine(run, "image-01.png"); File.WriteAllBytes(source, Png);
        string permanent = Path.Combine(Root, "kept.png"); TemporaryImages.SavePermanent(cache, source, permanent);
        Throws<IOException>(() => TemporaryImages.SavePermanent(cache, source, Path.Combine(cache, "doomed.png")), "Save As cannot save inside temporary root");
        TemporaryImages.DeleteRun(cache, Root);
        Check(File.Exists(permanent), "cleanup refuses outside root");
        TemporaryImages.DeleteRun(cache, run);
        Check(!Directory.Exists(run) && File.Exists(permanent), "close deletes temp but preserves explicit save");
    }

    private static GenerationSettings DetailedSettings(ModelFamily family = ModelFamily.Illustrious)
    {
        var settings = Settings(family);
        settings.Detailers.Add(new DetailerSetting { Name = "Faces", Detector = "bbox/face.pt", Steps = 15, Cfg = 4.5, Denoise = .25 });
        settings.Detailers.Add(new DetailerSetting { Name = "Disabled incomplete pass", Enabled = false });
        settings.Detailers.Add(new DetailerSetting { Name = "Hands", Detector = "segm/hand.pt", Steps = 12, Cfg = 3, Positive = "detailed hands, correct fingers", Negative = "deformed fingers", Sampler = "dpmpp_2m", Scheduler = "karras" });
        return settings;
    }

    private static void DetailerWorkflows()
    {
        var settings = DetailedSettings();
        settings.Loras.Add(new LoraSetting { Name = "style.safetensors", ModelStrength = .6 });
        settings.EasyCache = true;
        JObject graph = Workflow.Build(settings, 42);
        Check((string?)graph["detailer_0"]?["inputs"]?["image"]?[0] == "8", "first detailer uses decoded base image");
        Check((string?)graph["detailer_2"]?["inputs"]?["image"]?[0] == "detailer_0", "enabled passes chain and skip disabled pass");
        Check(graph["detailer_1"] == null && graph["detailer_1_detector"] == null, "disabled incomplete pass creates no nodes");
        Check((string?)graph["9"]?["inputs"]?["images"]?[0] == "detailer_2", "preview receives final full image");
        Check(graph.Values().Count(n => (string?)n["class_type"] == "PreviewImage") == 1, "one preview after all passes");
        Check((string?)graph["detailer_0"]?["inputs"]?["model"]?[0] == "lora_0", "detailers preserve LoRAs without sharing EasyCache state");
        Check((string?)graph["detailer_0"]?["inputs"]?["clip"]?[0] == "lora_0" && (int?)graph["detailer_0"]?["inputs"]?["clip"]?[1] == 1, "detailer uses patched Illustrious CLIP");
        Check((int?)graph["detailer_0"]?["inputs"]?["steps"] == 15 && (double?)graph["detailer_0"]?["inputs"]?["cfg"] == 4.5 && (double?)graph["detailer_0"]?["inputs"]?["denoise"] == .25, "independent detailer sampling parameters");
        Check((string?)graph["detailer_0"]?["inputs"]?["positive"]?[0] == "4" && (string?)graph["detailer_0"]?["inputs"]?["negative"]?[0] == "5", "blank prompts inherit scene conditioning");
        Check((string?)graph["detailer_2_positive"]?["inputs"]?["text"] == settings.Detailers[2].Positive && (string?)graph["detailer_2"]?["inputs"]?["positive"]?[0] == "detailer_2_positive", "per-pass prompt wired independently");
        Check((string?)graph["detailer_2_negative"]?["inputs"]?["text"] == "deformed fingers", "per-pass negative override");
        Check((string?)graph["detailer_0"]?["inputs"]?["scheduler"] == settings.Scheduler && (string?)graph["detailer_2"]?["inputs"]?["scheduler"] == "karras", "sampler defaults and per-pass overrides");
        Check(graph["detailer_0"]?["inputs"]?["segm_detector_opt"] == null && (int?)graph["detailer_2"]?["inputs"]?["segm_detector_opt"]?[1] == 1, "segmentation masks only connected for segmentation detectors");
        settings.Family = ModelFamily.Anima;
        graph = Workflow.Build(settings, 42);
        Check((string?)graph["detailer_0"]?["inputs"]?["clip"]?[0] == "2" && (string?)graph["detailer_0"]?["inputs"]?["vae"]?[0] == "3", "Anima detailer uses Qwen encoder and VAE");
        settings.Detailers.Reverse();
        graph = Workflow.Build(settings, 42);
        Check((string?)graph["detailer_0_detector"]?["inputs"]?["model_name"] == "segm/hand.pt", "reordering changes execution order");
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(settings.Detailers);
        var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DetailerSetting>>(json)!;
        Check(reloaded.Count == 3 && reloaded[0].Positive == "detailed hands, correct fingers" && !reloaded[1].Enabled && reloaded[2].Cfg == 4.5, "detailer order, overrides and enable state persist");
        JObject info = Info(settings);
        Check(Workflow.MissingNodes(info, "FaceDetailer", "UltralyticsDetectorProvider").Length == 0, "installed detailer nodes enable option");
        Workflow.ValidateServer(graph, info);
        info.Remove("FaceDetailer");
        Check(Workflow.MissingNodes(info, "FaceDetailer", "UltralyticsDetectorProvider").SequenceEqual(new[] { "FaceDetailer" }), "missing detailer node prevents option activation");
        Throws<InvalidDataException>(() => Workflow.ValidateServer(graph, info), "missing detailer node blocks preflight");
        info = Info(settings);
        info["UltralyticsDetectorProvider"]!["input"]!["required"]!["model_name"] = new JArray { new JArray("bbox/face.pt", "segm/hand.pt") };
        Workflow.ValidateServer(graph, info);
        info["UltralyticsDetectorProvider"]!["input"]!["required"]!["model_name"] = new JArray { new JArray() };
        Throws<InvalidDataException>(() => Workflow.ValidateServer(graph, info), "empty detector list rejects configured model");
        info = Info(settings);
        info["FaceDetailer"]!["input"]!["required"]!["new_required_input"] = new JArray("MODEL");
        Throws<InvalidDataException>(() => Workflow.ValidateServer(graph, info), "changed required detailer schema rejected");
        settings = DetailedSettings(); settings.WorkflowFile = "custom.json";
        Throws<ArgumentException>(settings.Validate, "custom workflows cannot silently ignore built-in detailers");
        settings.Detailers.ForEach(d => d.Enabled = false); settings.Validate();
        Check((string?)Workflow.Build(Settings(), 42)["9"]?["inputs"]?["images"]?[0] == "8", "no passes keeps original generation path");
        foreach (Action<DetailerSetting> invalidate in new Action<DetailerSetting>[] {
            d => d.Denoise = double.NaN, d => d.Denoise = 0, d => d.Cfg = double.PositiveInfinity,
            d => d.GuideSize = 2048, d => d.MaxSize = 1000 + 1, d => d.Cycles = 0, d => d.Detector = "", d => d.CropFactor = .5 })
        {
            var pass = new DetailerSetting { Detector = "bbox/face.pt" }; invalidate(pass);
            Throws<ArgumentException>(pass.Validate, "invalid detailer controls rejected");
        }
    }

    private static void Prompts()
    {
        var scene = new SceneSnapshot { Dialogue = "Mara: *sets a cup on the table*", Environment = "A candlelit tavern.",
            Characters = new() { new SceneCharacter { Name = "Mara", Description = "Red fox with silver glasses." },
                new SceneCharacter { Name = "Robin", Description = "Grey wolf wearing a blue coat." } } };
        string anima = ScenePrompts.Build(scene, ModelFamily.Anima, "soft lighting");
        string illustrious = ScenePrompts.Build(scene, ModelFamily.Illustrious, "soft lighting");
        Check(anima.Contains("natural-language") && illustrious.Contains("booru-style"), "model-specific LLM format");
        Check(anima.Contains(scene.Dialogue) && anima.Contains(scene.Characters[0].Description), "scene and customized appearance included");
        Check(ScenePrompts.Clean("<think>reasoning</think>Prompt: red_fox, silver_glasses", true) == "red fox, silver glasses", "clean reasoning and tag format");
        Check(anima.Contains("one paragraph per visible character") && illustrious.Contains("one block per visible character"), "both families instructed to separate subjects");
        Check(anima.Contains("\n\nCharacter 2") && anima.Contains("</character_visual_profiles>\n") && anima.Contains("\n<environment>") && !anima.Contains("named_bystander_references"), "participant profiles and environment remain separate with no bystander lookup block");
        string blocks = ScenePrompts.Clean("<think>skip</think>Prompt: RED_FOX,   silver_glasses\r\n \t\r\nGREY_WOLF, blue_coat\r\n\r\n\r\nTAVERN, candlelight", true);
        Check(blocks == "red fox, silver glasses\n\ngrey wolf, blue coat\n\ntavern, candlelight", "tag cleanup preserves character/environment paragraphs");
        Check(ScenePrompts.Clean("A red fox.\n\nA grey wolf.\n\nA candlelit tavern.").Split(new[] { "\n\n" }, StringSplitOptions.None).Length == 3, "Anima paragraphs preserved");
        var settings = Settings(); settings.Positive = blocks;
        Check((string?)JObject.Parse(Workflow.Build(settings, 42).ToString())["4"]?["inputs"]?["text"] == blocks, "paragraphs survive ComfyUI graph serialization");
        Throws<InvalidOperationException>(() => ScenePrompts.Clean("<think>unfinished"), "unfinished reasoning rejected");
        Throws<InvalidOperationException>(() => ScenePrompts.Clean("{\"prompt\":\"text here\"}"), "JSON response not submitted as visual text");
        Check(ScenePrompts.ProfileInstruction(scene.Characters[0]).Contains("do not invent"), "initial profile extraction grounded");
    }
    private static void BasePositivePrompts()
    {
        const string scene = "Red fox with glasses.\n\nGrey wolf in a blue coat.\n\nCandlelit tavern.";
        const string style = "watercolor, score_7, \"Warm Light\"\nsoft edges";
        string combined = style + "\n\n" + scene;
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            var settings = Settings(family); settings.Positive = scene; settings.BasePositive = style; settings.Negative = "blurry, watermark";
            JObject graph = Workflow.Build(settings, 42);
            Check((string?)graph["4"]?["inputs"]?["text"] == combined, "base style precedes scene for " + family);
            Check((string?)graph["5"]?["inputs"]?["text"] == settings.Negative, "base style never changes the negative prompt");
            Check((string?)Workflow.Build(settings, 42)["4"]?["inputs"]?["text"] == combined && settings.Positive == scene, "repeated generation does not accumulate style prefixes");
            settings.BasePositive = "pencil sketch";
            Check(settings.ComposePositive(settings.Positive) == "pencil sketch\n\n" + scene, "changing style replaces the prefix without retaining its old value");
            settings.BasePositive = " \r\n ";
            Check(settings.ComposePositive(settings.Positive) == scene, "blank prefix preserves exact scene text");
        }
        var detailed = DetailedSettings(); detailed.BasePositive = style; detailed.Positive = scene;
        JObject detailGraph = Workflow.Build(detailed, 42);
        Check((string?)detailGraph["detailer_0"]?["inputs"]?["positive"]?[0] == "4", "inherited detailer uses the combined scene prompt");
        Check((string?)detailGraph["detailer_2_positive"]?["inputs"]?["text"] == style + "\n\n" + detailed.Detailers[2].Positive, "detailer overrides retain the style prefix");
        var template = new JObject { ["1"] = new JObject { ["class_type"] = "CLIPTextEncode", ["inputs"] = new JObject { ["text"] = "{{positive}}" } } };
        var expanded = Workflow.FromTemplate(template.ToString(), detailed, 42);
        Check((string?)JObject.Parse(expanded.ToString())["1"]?["inputs"]?["text"] == combined, "custom workflow positive placeholder includes prefix with exact casing, quotes and newlines");
        var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<GenerationSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(detailed))!;
        Check(reloaded.BasePositive == style && reloaded.Positive == scene, "prefix and scene text persist independently");
        var limit = Settings(); limit.BasePositive = new string('x', 32000); limit.Positive = ""; limit.Validate();
        Check(limit.ComposePositive(limit.Positive).Length == 32000, "base-only manual prompt permitted within limit");
        limit.Positive = "a";
        Throws<ArgumentException>(limit.Validate, "combined prefix and scene length limit enforced");
        detailed.BasePositive = new string('x', 31900); detailed.Positive = "small scene"; detailed.Detailers[2].Positive = new string('y', 100);
        Throws<ArgumentException>(detailed.Validate, "combined detailer and prefix length limit enforced");
    }

    private static async Task LlmWaits()
    {
        var pending = new TaskCompletionSource<string>();
        using var cancel = new CancellationTokenSource();
        Task<string> waiting = ScenePrompts.AwaitLlm(pending.Task, TimeSpan.FromSeconds(2), cancel.Token);
        cancel.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => waiting, "close during LLM stops pipeline");
        pending.SetResult("late response must never submit a job");
        Check(waiting.IsCanceled, "late response discarded");
        var late = new TaskCompletionSource<string>();
        await ThrowsAsync<TimeoutException>(() => ScenePrompts.AwaitLlm(late.Task, TimeSpan.FromMilliseconds(5), CancellationToken.None), "LLM timeout");
        late.SetException(new InvalidOperationException("observed late failure"));
    }

    private static JObject Info(GenerationSettings settings)
    {
        JObject graph = Workflow.Build(settings, 42), result = new();
        foreach (JToken node in graph.Values()) result[(string)node["class_type"]!] = new JObject { ["input"] = new JObject { ["required"] = new JObject() } };
        if (result["AnimaIPAdapterApply"] != null)
        {
            result["AnimaIPAdapterApply"]!["description"] = PortraitRequirements.AnimaCompatibility;
            result["AnimaIPAdapterApply"]!["input"]!["optional"] = new JObject { ["ref_image_2"] = new JArray("IMAGE") };
        }
        return result;
    }
    private static JObject Done(string id, bool error = false) => new()
    {
        [id] = new JObject
        {
            ["status"] = new JObject { ["completed"] = !error, ["status_str"] = error ? "error" : "success",
                ["messages"] = error ? new JArray(new JArray("execution_error", new JObject { ["exception_message"] = "GPU out of memory" })) : new JArray() },
            ["outputs"] = new JObject { ["9"] = new JObject { ["images"] = new JArray(
                new JObject { ["filename"] = "../weird & image.png", ["subfolder"] = "a/b #c", ["type"] = "temp" },
                new JObject { ["filename"] = "second.png", ["type"] = "temp" }) } }
        }
    };
    private static HttpResponseMessage Json(JObject body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
    private sealed class MockApi : HttpMessageHandler
    {
        internal string Id = "";
        internal int Queued, History, Downloads;
        internal bool FailHistory, NoCancelApi, FailCancel, NeverComplete;
        internal Func<CancellationToken, Task>? OnSubmit;
        internal Action? OnHistory;
        internal Func<CancellationToken, Task>? OnHistoryAsync;
        internal readonly List<string> Cancelled = new();
        internal readonly List<string> Routes = new();
        internal JObject Nodes = Info(Settings());
        internal string ViewQuery = "";
        internal JObject? SubmittedGraph;
        internal int Uploads;
        internal bool BadUpload;
        internal Action? OnUpload;
        internal readonly List<string> UploadedPaths = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string route = request.RequestUri!.AbsolutePath.Replace("/proxy/", "");
            Routes.Add(request.Method + " " + route);
            if (route == "object_info") return Json(Nodes);
            if (route == "upload/image")
            {
                var form = (MultipartFormDataContent)request.Content!;
                var parts = form.ToDictionary(p => p.Headers.ContentDisposition!.Name!.Trim('"'));
                Check(await parts["type"].ReadAsStringAsync(cancel) == "temp", "portraits go to server temp");
                Check(await parts["overwrite"].ReadAsStringAsync(cancel) == "true", "reusable portrait slots overwrite");
                Check(parts["image"].Headers.ContentType!.MediaType == "image/png", "portrait content type");
                Check((await parts["image"].ReadAsByteArrayAsync(cancel)).SequenceEqual(Png), "portrait bytes preserved");
                string folder = await parts["subfolder"].ReadAsStringAsync(cancel);
                string name = parts["image"].Headers.ContentDisposition!.FileName!.Trim('"');
                Uploads++; UploadedPaths.Add(folder + "/" + name);
                OnUpload?.Invoke(); cancel.ThrowIfCancellationRequested();
                return Json(new JObject { ["name"] = BadUpload ? "../../wrong.png" : name, ["subfolder"] = folder, ["type"] = "temp" });
            }
            if (route.StartsWith("api/jobs/") && route.EndsWith("/cancel"))
            {
                string target = route.Split('/')[2];
                if (NoCancelApi || (FailCancel && target == Id)) return Json(new JObject { ["error"] = "unavailable" }, HttpStatusCode.NotFound);
                if (target == Id) Cancelled.Add(target);
                return Json(new JObject { ["cancelled"] = target == Id });
            }
            if (route == "prompt")
            {
                JObject body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancel));
                Id = (string)body["prompt_id"]!;
                SubmittedGraph = (JObject?)body["prompt"];
                Check(Guid.TryParse(Id, out _), "client supplied UUID");
                Queued++;
                if (OnSubmit != null) await OnSubmit(cancel);
                return Json(new JObject { ["prompt_id"] = Id });
            }
            if (route.StartsWith("history/"))
            {
                History++;
                OnHistory?.Invoke();
                if (OnHistoryAsync != null) await OnHistoryAsync(cancel);
                cancel.ThrowIfCancellationRequested();
                if (NeverComplete || History == 1) return Json(new JObject());
                JObject completed = Done(Id, FailHistory);
                return Json(completed);
            }
            if (route == "view")
            { Downloads++; ViewQuery = request.RequestUri.Query; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) }; }
            throw new Exception("Unexpected request: " + route);
        }
    }
    private static ComfyClient Client(MockApi api) => new("http://localhost:8188/proxy", handler: api,
        requestTimeout: TimeSpan.FromSeconds(2), pollInterval: TimeSpan.FromMilliseconds(1));
    private static async Task ApiSuccess()
    {
        var settings = DetailedSettings(); settings.Positive = "red fox, silver glasses\n\ngrey wolf, blue coat\n\ncandlelit tavern";
        settings.BasePositive = "watercolor, soft lighting";
        using var api = new MockApi { Nodes = Info(settings) }; using var client = Client(api);
        string output = DirectoryFor("success");
        GenerationResult result = await client.GenerateAsync(settings, output, TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
        Check((string?)api.SubmittedGraph?["4"]?["inputs"]?["text"] == settings.BasePositive + "\n\n" + settings.Positive, "HTTP submission includes style before separated scene blocks");
        Check((string?)api.SubmittedGraph?["9"]?["inputs"]?["images"]?[0] == "detailer_2", "HTTP submits whole detailer chain as one job");
        Check(result.Seed == 42 && result.Images.Count == 1, "one image retained with seed");
        Check(api.History == 2 && api.Downloads == 1, "poll until complete and download only first output");
        Check(api.ViewQuery.Contains("%26") && api.ViewQuery.Contains("%23"), "download query safely encoded");
        Check(Path.GetFileName(result.Images[0]) == "image-01.png", "server filename never becomes a local path");
        Check(File.ReadAllBytes(result.Images[0]).SequenceEqual(Png), "image bytes preserved");
        Check(api.Cancelled.Count == 0, "successful job not interrupted");
        TemporaryImages.DeleteRun(output, result.Directory);
        Check(ImageCount(output) == 0, "viewer cleanup after success");
    }
    private static async Task CancelQueued()
    {
        using var cancel = new CancellationTokenSource();
        var settings = DetailedSettings();
        using var api = new MockApi { Nodes = Info(settings), OnHistory = cancel.Cancel }; using var client = Client(api);
        string output = DirectoryFor("cancel");
        await ThrowsAsync<OperationCanceledException>(() => client.GenerateAsync(settings, output, TimeSpan.FromSeconds(2), _ => { }, cancel.Token), "cancel queued or running job including all detailers");
        Check(api.Cancelled.SequenceEqual(new[] { api.Id }), "cancellation targets only own ID");
        Check(api.Routes.All(r => !r.Contains("interrupt") && !r.EndsWith("queue")), "no global interruption or queue wipe");
        Check(ImageCount(output) == 0, "cancel leaves no image");
    }
    private static async Task CancelDuringSubmission()
    {
        using var cancel = new CancellationTokenSource();
        using var api = new MockApi { OnSubmit = async token => { cancel.Cancel(); await Task.Delay(5, token); } };
        using var client = Client(api);
        await ThrowsAsync<OperationCanceledException>(() => client.GenerateAsync(Settings(), DirectoryFor("submit-cancel"), TimeSpan.FromSeconds(2), _ => { }, cancel.Token), "close during POST");
        Check(api.Cancelled.Single() == api.Id && api.History == 0, "acknowledged submission cancelled without starting poll");
    }
    private static async Task ApiErrors()
    {
        using (var api = new MockApi { FailHistory = true })
        using (var client = Client(api))
        {
            string output = DirectoryFor("error");
            await ThrowsAsync<InvalidDataException>(() => client.GenerateAsync(Settings(), output, TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None), "execution error surfaced");
            Check(api.Downloads == 0 && ImageCount(output) == 0, "failed job not shown or retained");
        }
        using (var api = new MockApi { NoCancelApi = true })
        using (var client = Client(api))
        {
            await ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(Settings(), DirectoryFor("old-server"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None), "require scoped cancellation API");
            Check(api.Queued == 0, "unsupported cancellation rejected before queuing");
        }
        using (var api = new MockApi())
        using (var client = Client(api))
        {
            api.Nodes.Remove("KSampler");
            await ThrowsAsync<InvalidDataException>(() => client.GenerateAsync(Settings(), DirectoryFor("missing-node"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None), "missing node preflight");
            Check(api.Queued == 0, "no submission with missing node");
        }
        foreach (string missing in new[] { "FaceDetailer", "UltralyticsDetectorProvider", "EasyCache" })
        {
            var settings = DetailedSettings(); settings.EasyCache = true;
            using var api = new MockApi { Nodes = Info(settings) }; using var client = Client(api);
            api.Nodes.Remove(missing);
            await ThrowsAsync<InvalidDataException>(() => client.GenerateAsync(settings, DirectoryFor("missing-" + missing), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None), "optional dependency rejected: " + missing);
            Check(api.Queued == 0 && api.Downloads == 0, "missing optional node cannot enqueue a generation");
        }
        byte[] oversized = (byte[])Png.Clone(); oversized[16] = 1;
        Throws<InvalidDataException>(() => ComfyClient.ImageExtension(oversized), "oversized PNG header blocked before Unity decode");
        Throws<InvalidDataException>(() => ComfyClient.ImageExtension(Encoding.UTF8.GetBytes("not an image")), "invalid image rejected");
        byte[] jpegHeader = { 255, 216, 255, 192, 0, 11, 8, 0, 16, 0, 24, 1, 1, 17, 0 };
        Check(ComfyClient.ImageExtension(jpegHeader) == ".jpg", "JPEG SOF dimensions recognized");
        jpegHeader[9] = 255;
        Throws<InvalidDataException>(() => ComfyClient.ImageExtension(jpegHeader), "oversized JPEG blocked before decode");
        Throws<InvalidDataException>(() => ComfyClient.ImageExtension(new byte[] { 255, 216, 255 }), "truncated JPEG rejected");
    }
    private static async Task GenerationTimeout()
    {
        // A stalled history response models a queued job without assuming setup
        // will finish inside 20 ms on a busy machine.
        using var api = new MockApi { NeverComplete = true, OnHistoryAsync = token => Task.Delay(Timeout.Infinite, token) }; using var client = Client(api);
        await ThrowsAsync<TimeoutException>(() => client.GenerateAsync(Settings(), DirectoryFor("timeout"), TimeSpan.FromSeconds(1), _ => { }, CancellationToken.None), "generation timeout");
        Check(api.Queued == 1 && api.History == 1, "timeout occurs while waiting for the submitted job");
        Check(api.Cancelled.Count == 1, "timed out job cancelled server-side");
    }
    private static async Task CancellationFailure()
    {
        using var cancel = new CancellationTokenSource();
        using var api = new MockApi { FailCancel = true, OnHistory = cancel.Cancel }; using var client = Client(api);
        await ThrowsAsync<ComfyCancellationException>(() => client.GenerateAsync(Settings(), DirectoryFor("failed-cancel"), TimeSpan.FromSeconds(2), _ => { }, cancel.Token), "cancel failure cannot be reported as successful cancellation");
    }
    private static void InspectAssembly()
    {
        string assemblyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin/Release/netstandard2.1/ComfyImageGenerator.dll"));
        using Mono.Cecil.AssemblyDefinition assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(assemblyPath);
        var plugin = assembly.MainModule.Types.Single(t => t.Name == "Plugin");
        Check(plugin.Methods.All(m => m.Name != "OnDestroy"), "bootstrap destruction cannot tear down plugin");
        var patches = assembly.MainModule.Types.Single(t => t.Name == "DialogueSceneButtonPatch");
        Check(patches.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch"), "direct ellipsis-adjacent UI injection installed");
        var window = assembly.MainModule.Types.Single(t => t.Name == "GeneratorWindow");
        Check(assembly.MainModule.Types.All(t => t.Name != "RegionalPrompts" && t.Name != "CharacterRegion") && window.Methods.All(m => !m.Name.Contains("Region")), "coupler graph builder and region UI removed from compiled plugin");
        Check(window.Methods.Single(m => m.Name == "Close").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "CloseViewer"), "Close invokes job cancellation and cleanup");
        var dialog = assembly.MainModule.Types.Single(t => t.Name == "SaveImageDialog");
        Check(dialog.Methods.All(m => !m.IsPInvokeImpl) && dialog.NestedTypes.Count == 0, "Save As has no native dialog or recursive marshaled data structure");
        Check(window.Methods.Any(m => m.Name == "DrawSaveImage") && window.Methods.Any(m => m.Name == "WriteSavedImage"), "dark in-game save form included in installed plugin");
        Check(window.Methods.Single(m => m.Name == "OpenScene").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "OpenCastPicker"), "dialogue scene button opens character picker before generation");
        Check(window.Methods.Single(m => m.Name == "SelectedLiveScene").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.DeclaringType.Name == "SceneSelection" && r.Name == "Select"), "production UI filters recaptured dialogue through cast limits");
        var generation = assembly.MainModule.Types.Single(t => t.Name == "GenerationService");
        Check(generation.Methods.Single(m => m.Name == "Start").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.DeclaringType.Name == "SceneSelection" && r.Name == "ValidateGeneration"), "every production submission including regeneration checks selected cast limits");
        var capture = assembly.MainModule.Types.Single(t => t.Name == "SceneCapture");
        var promptState = capture.NestedTypes.Single(t => t.Name.StartsWith("<GeneratePrompt>"));
        var calls = promptState.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Where(i => i.Operand is Mono.Cecil.MethodReference).Select(i => (Mono.Cecil.MethodReference)i.Operand).ToList();
        Check(calls.FindIndex(r => r.DeclaringType.Name == "SceneSelection" && r.Name == "Validate") < calls.FindIndex(r => r.Name == "ReadyInference") && calls.Any(r => r.DeclaringType.Name == "SceneSelection" && r.Name == "Validate"), "cast validated before profile or scene LLM requests");
    }

    private static void SaveDialogRequests()
    {
        string temp = DirectoryFor("save-temp"), permanent = DirectoryFor("named-saves");
        string target = SaveImageDialog.Destination(permanent, "Tavern scene", ".png", temp);
        Check(target == Path.Combine(permanent, "Tavern scene.png"), "save form adds image extension and preserves readable custom name");
        Check(SaveImageDialog.Destination(permanent, "Winter.JPG", ".jpg", temp).EndsWith("Winter.JPG"), "JPEG filename matching is case insensitive");
        Check(SaveImageDialog.DefaultName(".png").EndsWith(".png"), "default filename retains actual image format");
        Throws<ArgumentException>(() => SaveImageDialog.Destination("relative", "scene", ".png", temp), "relative save folder rejected");
        foreach (string bad in new[] { "", "../scene", "sub\\scene", "CON.png", "LPT1", "scene.", "bad:name", "scene.jpg" })
            Throws<ArgumentException>(() => SaveImageDialog.Destination(permanent, bad, ".png", temp), "invalid or misleading filename rejected: " + bad);
        Throws<IOException>(() => SaveImageDialog.Destination(temp, "scene", ".png", temp), "permanent save form cannot target temporary image directory");
        string run = Path.Combine(temp, "run"); Directory.CreateDirectory(run); Directory.CreateDirectory(permanent);
        string source = Path.Combine(run, "image-01.png"); File.WriteAllBytes(source, Png);
        TemporaryImages.SavePermanent(temp, source, target);
        Check(File.ReadAllBytes(target).SequenceEqual(Png), "named save writes image bytes permanently");
        byte[] original = { 1, 2, 3 }; File.WriteAllBytes(target, original);
        Throws<IOException>(() => TemporaryImages.SavePermanent(temp, source, target), "existing file cannot be overwritten without confirmation");
        Check(File.ReadAllBytes(target).SequenceEqual(original), "unconfirmed overwrite preserves previous file");
        TemporaryImages.SavePermanent(temp, source, target, overwrite: true);
        Check(File.ReadAllBytes(target).SequenceEqual(Png), "confirmed overwrite replaces existing image");
        TemporaryImages.DeleteRun(temp, run);
        Check(File.Exists(target) && !File.Exists(source), "closing viewer preserves named permanent save");
    }
}
