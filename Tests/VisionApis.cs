using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private sealed class VisionApi : HttpMessageHandler
    {
        internal readonly VisionProvider Provider;
        internal readonly List<string> Events;
        internal readonly List<JObject> Chats = new();
        internal bool Loaded, FailChat, FailUnload, RetainLoaded, NoAdmin, NoVision;
        internal int Unloads, Loads, StatusReads;
        internal Action? OnLoad, OnChat;
        internal bool LoseLoadReply;
        internal JArray? ReasoningOptions = new("off", "on");
        internal Func<int, string>? Reply;
        internal Func<JObject>? RawReply;
        internal VisionApi(VisionProvider provider, List<string>? events = null) { Provider = provider; Events = events ?? new(); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            Check(request.Headers.Authorization?.Parameter == "private-test-key", "vision credentials go in header");
            string route = request.RequestUri!.AbsolutePath.Replace("/proxy/", "");
            JObject body = request.Content == null ? new() : JObject.Parse(await request.Content.ReadAsStringAsync(cancel));
            Events.Add(route);
            if (route == "api/tags") return Json(new JObject { ["models"] = new JArray(new JObject { ["name"] = "vision:latest" }) });
            if (route == "api/show") return Json(new JObject { ["capabilities"] = new JArray(NoVision ? "completion" : "vision") });
            if (route == "api/ps")
            {
                StatusReads++;
                return Json(new JObject { ["models"] = Loaded ? new JArray(new JObject { ["name"] = "vision:latest" }) : new JArray() });
            }
            if (route == "api/v1/models")
            {
                StatusReads++;
                return Json(new JObject { ["models"] = new JArray(
                    new JObject { ["key"] = "vision", ["type"] = "llm", ["capabilities"] = new JObject { ["vision"] = !NoVision,
                        ["reasoning"] = ReasoningOptions == null ? null : new JObject { ["allowed_options"] = ReasoningOptions, ["default"] = "on" } },
                        ["loaded_instances"] = Loaded ? new JArray(new JObject { ["id"] = "owned-instance" }) : new JArray() },
                    new JObject { ["key"] = "unrelated", ["type"] = "llm", ["loaded_instances"] = new JArray(new JObject { ["id"] = "leave-alone" }) }) });
            }
            if (route == "api/v1/models/load")
            {
                Check((string?)body["model"] == "vision" && (int?)body["context_length"] == 8192, "LM Studio explicit model/context load");
                Loads++; Loaded = true; OnLoad?.Invoke();
                if (LoseLoadReply) throw new HttpRequestException("load acknowledgement lost");
                return Json(new JObject { ["instance_id"] = "owned-instance", ["status"] = "loaded" });
            }
            if (route == "api/admin/list_options")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(NoAdmin ? "[]" : "[\"vision.kcpps\",\"initial_model\",\"unload_model\"]") };
            if (route == "api/admin/reload_config")
            {
                if ((string?)body["filename"] == "vision.kcpps")
                { Loads++; Loaded = true; OnLoad?.Invoke(); return Json(new JObject { ["success"] = true }); }
                Check((string?)body["filename"] == "unload_model", "Kobold unload special config");
                // First unload is the known inactive baseline, later unload is session cleanup.
                Unloads++;
                if (FailUnload && Loads > 0) return Json(new JObject { ["success"] = false });
                if (!RetainLoaded || Loads == 0) Loaded = false;
                return Json(new JObject { ["success"] = true });
            }
            if (route == "api/extra/version")
            { StatusReads++; return Json(new JObject { ["llm"] = Loaded, ["vision"] = Loaded && !NoVision }); }
            if (route == "api/extra/abort")
            {
                Check(((string?)body["genkey"])?.StartsWith("Silverpine-") == true, "Kobold abort scoped by generation key");
                return Json(new JObject { ["success"] = true });
            }
            if (route == "api/generate" || route == "api/v1/models/unload")
            {
                Unloads++;
                if (route == "api/generate") Check((int?)body["keep_alive"] == 0 && (string?)body["model"] == "vision:latest", "Ollama unload canonical model");
                else Check((string?)body["instance_id"] == "owned-instance", "only owned LM Studio instance unloaded");
                if (FailUnload) return Json(new JObject { ["error"] = "secret should not be reflected" }, HttpStatusCode.InternalServerError);
                if (!RetainLoaded) Loaded = false;
                return Json(new JObject { ["done"] = true });
            }
            if (route == "api/chat" || route == "v1/chat/completions" || route == "api/v1/chat")
            {
                Loaded = true; Chats.Add(body); OnChat?.Invoke(); cancel.ThrowIfCancellationRequested();
                if (FailChat) return Json(new JObject { ["error"] = "private-test-key" }, HttpStatusCode.BadRequest);
                if (RawReply != null) return Json(RawReply());
                string response = Reply != null ? Reply(Chats.Count) : Chats.Count switch
                {
                    1 => "Dan is an adult anthro arctic fox with white fur, blue eyes, a fluffy fox tail and digitigrade legs.",
                    2 => "Oriana is an adult anthro snake with white scales, golden eyes, a long scaled tail and digitigrade legs.",
                    _ => VisionCompositionResponse().ToString(Formatting.None)
                };
                if (route == "api/v1/chat") return Json(new JObject { ["output"] = new JArray(new JObject { ["type"] = "message", ["content"] = response }) });
                return route == "api/chat" ? Json(new JObject { ["message"] = new JObject { ["content"] = response } }) :
                    Json(new JObject { ["choices"] = new JArray(new JObject { ["message"] = new JObject { ["content"] = response } }) });
            }
            throw new Exception("Unexpected vision route: " + route);
        }
    }

    private static VisionSettings VisionOptions(VisionProvider provider) => new()
    {
        Enabled = true, Provider = provider, Address = "http://localhost:5555/proxy/v1", ApiKey = "private-test-key",
        Model = provider == VisionProvider.KoboldCpp ? "vision.kcpps" : "vision"
    };
    private static VisionClient Vision(VisionSettings options, VisionApi api) => new(options, api, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(180));
    private static List<PortraitReference> VisionPortraits() => new()
    {
        new() { CharacterKey = "npc:Oriana", Png = Png }, new() { CharacterKey = "player:Dan", Png = Png }
    };

    private static async Task VisionApis()
    {
        Check(VisionSettings.NormalizeAddress("http://localhost:1234/v1").AbsoluteUri == "http://localhost:1234/", "vision OpenAI URL normalized");
        Check(VisionSettings.NormalizeAddress("https://localhost/proxy/api/v1/").AbsoluteUri == "https://localhost/proxy/", "vision proxy preserved");
        Throws<ArgumentException>(() => VisionSettings.NormalizeAddress("http://user:secret@localhost"), "no embedded vision credentials");
        var invalid = VisionOptions(VisionProvider.Ollama); invalid.Temperature = double.NaN;
        Throws<ArgumentException>(() => invalid.Validate(), "finite vision temperature");
        Check(!JsonConvert.SerializeObject(VisionOptions(VisionProvider.Ollama)).Contains("private-test-key"), "vision config serialization excludes secret");
        var saved = Settings(); saved.Vision = VisionOptions(VisionProvider.Ollama);
        Check(!JsonConvert.SerializeObject(saved).Contains("private-test-key") && !JsonConvert.SerializeObject(saved).Contains("localhost:5555"), "generation serialization excludes vision connection data");

        foreach (VisionProvider provider in Enum.GetValues<VisionProvider>())
        {
            var events = new List<string>();
            var opts = VisionOptions(provider);
            using var visionApi = new VisionApi(provider, events);
            using var vision = Vision(opts, visionApi);
            var settings = Settings(ModelFamily.Anima); settings.Vision = opts;
            settings.SceneCharacterKeys = new() { "player:Dan", "npc:Oriana" };
            settings.Portraits = VisionPortraits(); // IP-Adapter disabled: only the vision server gets these.
            using var api = new MockApi { Nodes = Info(settings), OnFree = () => events.Add("comfy-free"), OnGenerate = () =>
            { Check(!visionApi.Loaded, provider + " image cannot start while vision model loaded"); events.Add("comfy-generate"); } };
            using var comfy = Client(api);
            SceneSnapshot scene = CompositionScene(); string danProfile = scene.Characters[0].Description;
            GenerationResult result = await GenerationPipeline.RunAsync(comfy, settings, DirectoryFor("vision-" + provider), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None,
                (progress, token) => VisionPrompts.GenerateAsync(vision, opts, scene, settings.Family, "watercolor", settings.Portraits, progress, token), cleanupTimeout: TimeSpan.FromSeconds(1));
            Check(visionApi.Chats.Count == 3, provider + " two separate portrait reads then scene composition");
            Check(api.Uploads == 0, provider + " vision portraits do not turn on IP-Adapter upload");
            Check(events[0] == "comfy-free" && events.LastIndexOf("comfy-free") > events.IndexOf("comfy-generate"), provider + " Comfy cleanup before LLM and after generation");
            Check(visionApi.Unloads >= 1 && !visionApi.Loaded && visionApi.StatusReads > 0, provider + " explicit unload verified");
            Check(File.Exists(result.Images[0]) && settings.CleanupNotice == "", provider + " result survives successful memory cleanup");
            Check(scene.Characters[0].Description == danProfile, provider + " explicit profile not replaced by portrait read");
            Check(settings.Positive.StartsWith("1boy, 1girl") && settings.Positive.Contains("Oriana"), provider + " existing cast-aware prompt assembly retained");
            Check(settings.Positive.Split("white fur").Length == 2 && settings.Positive.Split("white scales").Length == 2,
                provider + " full vision appearances are emitted once without adding saved anchors");
            for (int i = 0; i < 3; i++)
            {
                JObject body = visionApi.Chats[i];
                Check((bool?)body["stream"] == false, provider + " waits for complete reply");
                Check((int?)(body["max_output_tokens"] ?? body["max_tokens"] ?? body["options"]?["num_predict"]) == opts.MaxTokens,
                    provider + " configured output limit used for portraits and scene without hidden cap");
                if (provider == VisionProvider.Ollama)
                {
                    Check((int?)body["options"]?["num_ctx"] == 8192, "Ollama context limit");
                    Check(i == 2 ? body["messages"]?[1]?["images"] == null : body["messages"]?[1]?["images"] is JArray images && images.Count == 1, "one Ollama image per character, none for final text composition");
                }
                else if (provider == VisionProvider.LMStudio)
                {
                    Check((string?)body["model"] == "owned-instance", "inference uses LM Studio instance ID");
                    Check((string?)body["reasoning"] == "off" && (bool?)body["store"] == false, "LM Studio disables supported reasoning and chat persistence");
                    Check(i == 2 ? body["input"]?.Type == JTokenType.String : ((string?)body["input"]?[1]?["data_url"])?.StartsWith("data:image/png;base64,") == true,
                        "LM Studio native image input vs final text");
                    Check(!string.IsNullOrWhiteSpace((string?)body["system_prompt"]), "LM Studio receives system instruction separately");
                }
                else
                {
                    Check(i == 2 ? body["messages"]?[1]?["content"]?.Type == JTokenType.String : ((string?)body["messages"]?[1]?["content"]?[1]?["image_url"]?["url"])?.StartsWith("data:image/png;base64,") == true, "OpenAI-compatible image content vs final text");
                }
            }
            string first = visionApi.Chats[0].ToString(), second = visionApi.Chats[1].ToString(), final = visionApi.Chats[2].ToString();
            Check(first.Contains("player:Dan") && !first.Contains("npc:Oriana") && second.Contains("npc:Oriana") && !second.Contains("player:Dan"), "identity bound to each independent image request");
            Check(final.Contains("portrait_observation") && final.Contains("Blue tunic") && final.Contains("bedside") && final.Contains("watercolor"), "vision composition gets observations, scene, live clothing and guidance");
            Check(!final.Contains("physical_identity_anchor") && !final.Contains("physical_identity_tags") && final.Contains("ONE coherent appearance"),
                "vision scene request combines references without supplying redundant preassembled anchors");
        }

        foreach (VisionProvider provider in Enum.GetValues<VisionProvider>())
        {
            var opts = VisionOptions(provider);
            using var api = new VisionApi(provider) { FailChat = true };
            using var client = Vision(opts, api);
            try { await client.RunAsync((v, c) => v.ChatAsync("system", "prompt", Png, 400, c), _ => { }, CancellationToken.None); throw new Exception("Expected vision error"); }
            catch (HttpRequestException ex) { Check(!ex.Message.Contains("private-test-key"), "server error never reflects response body or token"); }
            Check(!api.Loaded && api.Unloads > 0, provider + " failure still unloads model");

            using var cancelled = new CancellationTokenSource();
            using var cancelApi = new VisionApi(provider) { OnChat = cancelled.Cancel };
            using var cancelClient = Vision(opts, cancelApi);
            await ThrowsAsync<OperationCanceledException>(() => cancelClient.RunAsync((v, c) => v.ChatAsync("system", "prompt", Png, 400, c), _ => { }, cancelled.Token), provider + " cancellation stops prompt");
            Check(!cancelApi.Loaded && cancelApi.Unloads > 0, provider + " cancellation uses independent cleanup token");

            using var failedApi = new VisionApi(provider) { FailUnload = true };
            using var failedClient = Vision(opts, failedApi);
            await ThrowsAsync<InvalidOperationException>(() => failedClient.RunAsync((v, c) => v.ChatAsync("system", "prompt", Png, 400, c), _ => { }, CancellationToken.None), provider + " failed unload blocks handoff");
        }

        foreach (VisionProvider provider in new[] { VisionProvider.LMStudio, VisionProvider.KoboldCpp })
        {
            using var cancelled = new CancellationTokenSource();
            using var api = new VisionApi(provider) { OnLoad = cancelled.Cancel };
            using var client = Vision(VisionOptions(provider), api);
            await ThrowsAsync<OperationCanceledException>(() => client.RunAsync((v, c) => v.ChatAsync("s", "p", Png, 400, c), _ => { }, cancelled.Token), "close during model load");
            Check(api.Chats.Count == 0 && !api.Loaded && api.Unloads > 0, "bounded load handshake followed by cleanup without inference");
        }
        using (var api = new VisionApi(VisionProvider.KoboldCpp) { NoAdmin = true })
        using (var client = Vision(VisionOptions(VisionProvider.KoboldCpp), api))
        {
            await ThrowsAsync<InvalidOperationException>(() => client.ListModelsAsync(CancellationToken.None), "Kobold missing admin capability explained");
            Check(api.Loads == 0, "capability check does not change server models");
        }
        using (var api = new VisionApi(VisionProvider.Ollama) { RetainLoaded = true })
        using (var client = Vision(VisionOptions(VisionProvider.Ollama), api))
            await ThrowsAsync<InvalidOperationException>(() => client.RunAsync((v, c) => v.ChatAsync("s", "p", Png, 400, c), _ => { }, CancellationToken.None), "HTTP unload acknowledgement without removed model does not pass");
        foreach (VisionProvider provider in new[] { VisionProvider.Ollama, VisionProvider.LMStudio })
        {
            using var api = new VisionApi(provider) { NoVision = true };
            using var client = Vision(VisionOptions(provider), api);
            await ThrowsAsync<InvalidOperationException>(() => client.RunAsync((v, c) => v.ChatAsync("s", "p", Png, 400, c), _ => { }, CancellationToken.None), "text-only model rejected as unsupported vision transport");
            Check(api.Chats.Count == 0 && api.Loads == 0, "vision capability checked before model loading");
        }
        using (var api = new VisionApi(VisionProvider.LMStudio) { LoseLoadReply = true })
        using (var client = Vision(VisionOptions(VisionProvider.LMStudio), api))
        {
            await ThrowsAsync<HttpRequestException>(() => client.RunAsync((v, c) => v.ChatAsync("s", "p", Png, 400, c), _ => { }, CancellationToken.None), "lost LM Studio load acknowledgement propagated");
            Check(!api.Loaded && api.Unloads > 0 && api.Chats.Count == 0, "recover and unload instance when load response is lost");
        }
        using (var api = new VisionApi(VisionProvider.Ollama) { Reply = _ => "A fox stands by the bed. Warm light falls across the room." })
        using (var client = Vision(VisionOptions(VisionProvider.Ollama), api))
        {
            string prompt = await VisionPrompts.GenerateAsync(client, VisionOptions(VisionProvider.Ollama), CompositionScene(), ModelFamily.Anima, "", new List<PortraitReference>(), _ => { }, CancellationToken.None);
            Check(api.Chats.Count == 1 && prompt.StartsWith("A fox"), "missing portraits and plain text still allow user-correctable generation");
        }
        using (var api = new VisionApi(VisionProvider.LMStudio) { Reply = n => n == 1 ? "Oriana is an adult anthro snake with golden eyes and white scales." :
            VisionTagResponse().ToString(Formatting.None) })
        using (var client = Vision(VisionOptions(VisionProvider.LMStudio), api))
        {
            SceneSnapshot solo = CompositionScene(); solo.Characters.RemoveAt(0);
            string prompt = await VisionPrompts.GenerateAsync(client, VisionOptions(VisionProvider.LMStudio), solo, ModelFamily.Illustrious, "", VisionPortraits(), _ => { }, CancellationToken.None);
            Check(api.Chats.Count == 2 && prompt.StartsWith("1girl, solo") && !api.Chats[0].ToString().Contains("player:Dan"), "Illustrious uses only one selected portrait and keeps booru prompt assembly");
            Check(prompt.Contains("snake, anthro, reclining, white scales, golden eyes, cream robe") && prompt.EndsWith("bedroom, warm light"),
                "structured tag arrays pass through the actual vision request pipeline as comma-separated prompt text");
        }

        await VisionResponseCases();
        VisionAppearanceCases();
        VisionSpeciesCases();
        VisionSentenceCases();
        VisionTagCases();
        await MemoryPipelineCases();
    }

    private static JObject VisionTagResponse() => new()
    {
        ["characters"] = new JArray(new JObject { ["character_id"] = "npc:Oriana", ["species"] = "snake", ["gender"] = "female",
            ["prompt"] = new JObject { ["clothing"] = new JArray(new JObject { ["concept"] = "robe", ["modifiers"] = new JArray("cream") }),
                ["appearance"] = new JArray(new JObject { ["concept"] = "SCALES", ["modifiers"] = new JArray("WHITE") }, "golden eyes"),
                ["pose"] = new JArray("reclining"), ["identity"] = new JArray("snake", "anthro") } }),
        ["environment"] = new JArray("bedroom", "warm_light", "BEDROOM")
    };

    private static void VisionTagCases()
    {
        SceneSnapshot scene = CompositionScene(); scene.Characters.RemoveAt(0);
        JObject reply = VisionTagResponse();
        JObject parts = (JObject)reply["characters"]![0]!["prompt"]!;
        ((JArray)parts["clothing"]!).Add("white scales");
        ((JArray)parts["appearance"]!).Add(JValue.CreateNull());
        ((JArray)parts["appearance"]!).Add(new JObject { ["untrusted"] = "not a tag" });
        parts["unknown"] = new JArray("do not include this");
        string[] blocks = CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out string notice).Split("\n\n");
        Check(blocks.Length == 3 && blocks[0] == "1girl, solo" && notice == "", "structured Illustrious keeps the single selected cast and separate environment");
        Check(blocks[1] == "snake, anthro, reclining, white scales, golden eyes, cream robe", "tag groups are ordered, normalized and deduplicated across groups without adding profile prose");
        Check(blocks[2] == "bedroom, warm light", "environment tag arrays normalize and deduplicate too");
        reply["characters"]![0]!["prompt"] = new JObject { ["clothing"] = new JArray(new JObject { ["concept"] = "top", ["color"] = "red", ["material"] = "silk", ["pattern"] = "",
            ["details"] = new JArray("plunging neckline", "puffy sleeves", JValue.CreateNull(), 42) }) };
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out _).Contains("snake, red silk top, plunging neckline, puffy sleeves"),
            "garment attributes and individual clothing details assemble as separate tags rather than one long outfit caption");
        reply["characters"]![0]!["prompt"] = new JObject { ["appearance"] = new JArray(new JObject { ["concept"] = "MUZZLE", ["modifiers"] = new JArray("beige_muzzle") }) };
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out _).Split("\n\n")[1] == "snake, beige muzzle",
            "a complete tag returned as a modifier does not repeat its feature noun");
        reply["characters"]![0]!["prompt"] = new JObject { ["appearance"] = new JArray(new JObject { ["concept"] = "scales", ["modifiers"] = "white" },
            new JObject { ["concept"] = "digitigrade", ["modifiers"] = new JArray(JValue.CreateNull(), 42, new JObject()) }, new JObject { ["modifiers"] = new JArray("unattached color") }) };
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out _).Contains("snake, white scales, digitigrade"),
            "single modifiers and unmodified concepts survive while malformed descriptors never leak JSON or float without a feature");
        reply["characters"]![0]!["prompt"] = new JArray("anthro", "white scales", "golden eyes");
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out _).Contains("snake, anthro, white scales, golden eyes"),
            "flat tag arrays remain usable and missing species is added once");
        reply["characters"]![0]!["prompt"] = new JObject { ["appearance"] = "white_scales, golden eyes", ["clothing"] = new JArray("cream robe") };
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out _).Contains("snake, white scales, golden eyes, cream robe"),
            "mixed string and array groups keep usable tags when the model misses part of the schema");
        reply["characters"]![0]!["prompt"] = new JArray(JValue.CreateNull(), 42, new JObject());
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out notice).Contains("snake") && notice.Length > 0,
            "empty tag arrays use existing reference fallback instead of rejecting generation");
        const string custom = "a deliberately long custom pose tag that the player may want to keep";
        reply["characters"]![0]!["prompt"] = new JArray(custom);
        Check(CharacterScenePrompts.ApplyVision(reply.ToString(), scene, ModelFamily.Illustrious, out _).Contains(custom), "long tags are not truncated or vetoed by a word-count check");
    }

    private static JObject VisionCompositionResponse()
    {
        JObject response = CompositionResponse();
        response["characters"]![0]!["species"] = "snake";
        response["characters"]![1]!["species"] = "arctic fox";
        response["characters"]![0]!["prompt"] = "Oriana is an adult female anthropomorphic snake. Her body has white scales. Her eyes are golden. She has a long scaled tail and digitigrade legs. Oriana reclines against pillows and looks up at Dan. She wears a cream robe.";
        response["characters"]![1]!["prompt"] = "Dan is an adult male anthropomorphic arctic fox. His body has white fur. His eyes are blue. He has a fluffy tail and digitigrade legs. Dan stands beside the bed and looks toward Oriana. He wears a blue tunic.";
        return response;
    }

    private static void VisionSentenceCases()
    {
        SceneSnapshot scene = CompositionScene();
        JObject response = VisionCompositionResponse();
        // Deliberately unordered fields/rows: assembly follows meaning and character ID.
        response["characters"]![1]!["prompt"] = new JObject {
            ["clothing"] = "He wears a blue tunic.", ["action"] = "Dan stands beside Oriana's bed",
            ["features"] = new JArray("His neck ruff is white", "His eyes are blue.", JValue.CreateNull(), new JObject { ["ignored"] = "not prose" }),
            ["body"] = new JObject { ["colors"] = new JArray("brown", "white"), ["texture"] = "soft", ["surface"] = "fur" }, ["identity"] = "Dan is a male anthropomorphic arctic fox.",
            ["extra"] = "This unknown field must not be serialized as prompt text." };
        string result = CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out string notice);
        string dan = result.Split("\n\n")[1];
        Check(dan == "Dan: Dan is a male anthropomorphic arctic fox. Dan has soft brown and white fur. His neck ruff is white. His eyes are blue. Dan stands beside Oriana's bed. He wears a blue tunic.",
            "structured vision sentences retain color scope, ordering, local punctuation and exact character ownership without duplicate anchors");
        Check(notice == "" && !result.Contains("not prose") && !result.Contains("unknown field"), "nontext feature values and unsupported fields do not leak JSON into a usable prompt");
        Check(result.Split("\n\n")[2] == "Oriana: " + (string)response["characters"]![0]!["prompt"]!, "structured and older string entries can coexist");
        response["characters"]![1]!["prompt"] = new JObject { ["body"] = new JObject { ["colors"] = "brown", ["surface"] = "fur" } };
        Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _).Contains("Dan (arctic fox): Dan has brown fur."),
            "a single color string remains usable when the LLM omits the requested list shape");
        response["characters"]![1]!["prompt"] = new JObject { ["body"] = new JObject { ["colors"] = new JArray("brown", JValue.CreateNull(), new JObject()) } };
        Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _).Contains("Dan's body colors are brown."),
            "partial color data preserves supported facts without inferring a missing body covering or serializing nontext values");
        response["characters"]![1]!["prompt"] = new JObject { ["features"] = "His eyes are blue." };
        Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _).Contains("Dan (arctic fox): His eyes are blue."),
            "partial structured reply with string features stays usable without inventing missing prose");
        response["characters"]![1]!["prompt"] = new JObject { ["features"] = new JArray(JValue.CreateNull(), 42, new JObject()) };
        Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out notice).Contains(SceneIdentity.Anchor(scene.Characters[0])) && notice.Length > 0,
            "empty structured reply uses existing reference fallback without declining generation");
        string longSentence = "Dan has " + string.Join(", ", Enumerable.Repeat("a user-chosen feature", 30)) + ".";
        response["characters"]![1]!["prompt"] = new JObject { ["features"] = new JArray(longSentence) };
        Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _).Contains(longSentence), "no word-count truncation or semantic rejection of returned prose");
    }

    private static void VisionSpeciesCases()
    {
        SceneSnapshot scene = CompositionScene();
        scene.Characters[0].Species = "wolf"; // A customized fox profile must win over this hint.
        JObject response = VisionCompositionResponse();
        response["characters"]![0]!["prompt"] = "Oriana reclines in a cream robe, her white scales catching the lamplight.";
        response["characters"]![1]!["prompt"] = "Dan stands in a blue tunic, his white fur catching the lamplight.";
        string result = CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _);
        string[] blocks = result.Split("\n\n");
        Check(blocks[1].StartsWith("Dan (arctic fox):") && blocks[2].StartsWith("Oriana (snake):"), "species metadata reaches the correct character even when prose omits it and rows are reversed");
        Check(!result.Contains("wolf") && !result.Contains(SceneIdentity.Anchor(scene.Characters[0])), "resolved custom species does not add stale game identity or a duplicate appearance");
        response["characters"]![1]!["prompt"] = "Dan, an ARCTIC FOX, stands in a blue tunic.";
        result = CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _);
        Check(result.Split("\n\n")[1] == "Dan: Dan, an ARCTIC FOX, stands in a blue tunic.", "species already present in prose is not prepended again");
        response["characters"]![1]!["species"] = "fox";
        response["characters"]![1]!["prompt"] = "Dan carries a foxglove flower.";
        Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _).Contains("Dan (fox):"), "partial word does not count as the species");
        foreach (string absent in new[] { "", "unknown", "unspecified" })
        {
            response["characters"]![1]!["species"] = absent;
            Check(CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out _).Contains("Dan: Dan carries"), "missing or unknown species does not veto a usable prompt or guess from game hints");
        }
        var solo = SceneSelection.Select(scene, new[] { "npc:Oriana" }, ModelFamily.Illustrious);
        var tagged = new JObject { ["characters"] = new JArray(new JObject { ["character_id"] = "npc:Oriana", ["species"] = "snake",
            ["gender"] = "female", ["prompt"] = "resting, anthro, white scales, cream robe" }), ["environment"] = "bedroom" };
        string output = CharacterScenePrompts.ApplyVision(tagged.ToString(), solo, ModelFamily.Illustrious, out _);
        Check(output.Split("\n\n")[1] == "snake, resting, anthro, white scales, cream robe", "Illustrious gets the specific species tag when only generic anthro was returned");
        tagged["characters"]![0]!["species"] = "king_cobra";
        tagged["characters"]![0]!["prompt"] = "king cobra, white scales";
        Check(CharacterScenePrompts.ApplyVision(tagged.ToString(), solo, ModelFamily.Illustrious, out _).Split("\n\n")[1] == "king cobra, white scales", "species tag spelling is normalized without duplicate species tags");
        string normal = CharacterScenePrompts.Apply(VisionCompositionResponse().ToString(), scene, ModelFamily.Anima);
        Check(!normal.Contains("Dan (arctic fox)") && !normal.Contains("Oriana (snake)"), "existing game-profile anchor already supplies species without another label");
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            Check(VisionPrompts.Build(scene, family, "").Contains(ScenePrompts.SpeciesRules) && ScenePrompts.Build(scene, family, "").Contains(ScenePrompts.SpeciesRules),
                family + " both prompt routes receive explicit species resolution rules");
        }
    }

    private static void VisionAppearanceCases()
    {
        SceneSnapshot scene = CompositionScene();
        JObject response = VisionCompositionResponse();
        response["composition"] = "Dan has white fur; Oriana has white scales.";
        string[] blocks = CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out string notice).Split("\n\n");
        Check(blocks.Length == 4 && notice == "", "complete vision scene has cast, two character blocks and environment");
        Check(!blocks[0].Contains("white fur") && !blocks[0].Contains("white scales"), "vision cast prefix omits redundant appearance overview even if model supplies one");
        Check(blocks[1] == "Dan: " + (string)response["characters"]![1]!["prompt"]! &&
            blocks[2] == "Oriana: " + (string)response["characters"]![0]!["prompt"]!, "vision paragraphs retain one complete model description per ID without automatic gender or profile prose");
        Check(blocks[1].Contains("digitigrade") && blocks[1].Contains("Blue tunic", StringComparison.OrdinalIgnoreCase) && !blocks[1].Contains("scaled tail"),
            "complete prompt keeps anatomy, live outfit and own traits");
        ((JArray)response["characters"]!).RemoveAt(1);
        string missing = CharacterScenePrompts.ApplyVision(response.ToString(), scene, ModelFamily.Anima, out notice);
        Check(missing.Contains(SceneIdentity.Anchor(scene.Characters[0])) && missing.Contains("Blue tunic") && notice.Length > 0,
            "missing vision entry falls back to the saved profile and current clothing");
        Check(missing.Split("white scales").Length == 2, "fallback for one character does not duplicate another complete entry");
        Check(CharacterScenePrompts.ApplyVision("My edited full prompt.", scene, ModelFamily.Anima, out _) == "My edited full prompt.", "plain vision prompt not supplemented with anchors");
        Check(CharacterScenePrompts.ApplyVision("{}", scene, ModelFamily.Anima, out _).Contains("arctic fox"), "unusable structure retains reference fallback without rejecting generation");
        scene.Characters.RemoveAt(0);
        var tags = new JObject { ["composition"] = "sitting, full body", ["characters"] = new JArray(new JObject {
            ["character_id"] = "npc:Oriana", ["gender"] = "female", ["prompt"] = "anthro snake, white scales, golden eyes, digitigrade, cream robe, WHITE_SCALES" }), ["environment"] = "bedroom" };
        string tagged = CharacterScenePrompts.ApplyVision(tags.ToString(), scene, ModelFamily.Illustrious, out _);
        Check(tagged.Split("\n\n")[1] == "anthro snake, white scales, golden eyes, digitigrade, cream robe", "Illustrious vision uses unique supplied tags without another generated identity tag set");
        scene.Characters[0].PortraitObservation = "A snake with blue eyes in the portrait.";
        string request = VisionPrompts.Build(scene, ModelFamily.Illustrious, "watercolor");
        Check(request.Contains("golden eyes") && request.Contains("blue eyes") && request.Contains("takes priority over a conflicting portrait observation") &&
            request.Contains("current_clothing") && request.Contains("booru-style"), "LLM resolves saved-profile conflicts and clothing in a single complete Illustrious entry");
        JObject reference = ScenePrompts.CharacterReference(scene.Characters[0], includeIdentityAnchors: false);
        Check(reference["physical_identity_anchor"] == null && reference["physical_identity_tags"] == null && (string?)reference["visual_profile"] == scene.Characters[0].Description,
            "vision reference retains authoritative profile once without derived copies");
        Check(ScenePrompts.CharacterReference(scene.Characters[0])["physical_identity_anchor"] != null, "original game LLM retains its anchor-based contract");
    }

    private static async Task VisionResponseCases()
    {
        foreach (JArray? options in new JArray?[] { null, new("low", "medium", "high"), new("off", "on") })
        {
            using var api = new VisionApi(VisionProvider.LMStudio) { ReasoningOptions = options, RawReply = () => new JObject {
                ["output"] = new JArray(new JObject { ["type"] = "reasoning", ["content"] = "private thinking" },
                    new JObject { ["type"] = "message", ["content"] = "A fox." },
                    new JObject { ["type"] = "message", ["content"] = "Blue eyes." }) } };
            using var client = Vision(VisionOptions(VisionProvider.LMStudio), api);
            string answer = await client.RunAsync((v, c) => v.ChatAsync("s", "p", Png, 1600, c), _ => { }, CancellationToken.None);
            Check(answer == "A fox.\nBlue eyes.", "LM Studio includes message items only, never separate reasoning");
            Check(options?.Values<string>().Contains("off") == true ? (string?)api.Chats[0]["reasoning"] == "off" : api.Chats[0]["reasoning"] == null,
                "LM Studio reasoning control follows advertised capabilities");
        }
        foreach (VisionProvider provider in Enum.GetValues<VisionProvider>())
        {
            using var api = new VisionApi(provider) { RawReply = () => provider switch {
                VisionProvider.LMStudio => JObject.Parse("{\"output\":[{\"type\":\"reasoning\",\"content\":\"private thinking\"}],\"stats\":{\"total_output_tokens\":600,\"reasoning_output_tokens\":600}}"),
                VisionProvider.Ollama => JObject.Parse("{\"message\":{\"content\":\"\",\"thinking\":\"private thinking\"},\"eval_count\":600,\"done_reason\":\"length\"}"),
                _ => JObject.Parse("{\"choices\":[{\"message\":{\"content\":null,\"reasoning_content\":\"private thinking\"},\"finish_reason\":\"length\"}],\"usage\":{\"completion_tokens\":600,\"completion_tokens_details\":{\"reasoning_tokens\":600}}}") } };
            using var client = Vision(VisionOptions(provider), api);
            try { await client.RunAsync((v, c) => v.ChatAsync("s", "p", Png, 600, c), _ => { }, CancellationToken.None); throw new Exception("Expected no answer"); }
            catch (InvalidDataException ex)
            {
                Check(ex.Message.Contains("reasoning without an answer") && ex.Message.Contains("600 / 600") && ex.Message.Contains("limit was reached"), provider + " empty answer explains exhausted reasoning budget");
                Check(!ex.Message.Contains("private thinking"), "diagnostic never exposes reasoning text");
            }
            Check(api.Unloads > 0 && !api.Loaded, provider + " empty answer still unloads model");
        }
        foreach (string reply in new[] { "<think>unfinished private thinking", "<think>private thinking</think>" })
        {
            using var api = new VisionApi(VisionProvider.Ollama) { Reply = _ => reply };
            using var client = Vision(VisionOptions(VisionProvider.Ollama), api);
            await ThrowsAsync<InvalidDataException>(() => client.RunAsync((v, c) => v.ChatAsync("s", "p", null, 600, c), _ => { }, CancellationToken.None), "tagged reasoning alone cannot become an image prompt");
            Check(api.Unloads > 0 && !api.Loaded, "reasoning-only tagged response unloads");
        }
        using (var api = new VisionApi(VisionProvider.KoboldCpp) { Reply = _ => "<think>private thinking</think>A fox." })
        using (var client = Vision(VisionOptions(VisionProvider.KoboldCpp), api))
            Check(await client.RunAsync((v, c) => v.ChatAsync("s", "p", null, 600, c), _ => { }, CancellationToken.None) == "A fox.", "tagged reasoning removed while final answer preserved");
        var invalid = VisionOptions(VisionProvider.LMStudio); invalid.Temperature = 1.5;
        Throws<ArgumentException>(() => invalid.Validate(), "LM Studio native temperature range");
    }

    private static async Task MemoryPipelineCases()
    {
        var settings = Settings(); settings.ReleaseComfyMemory = true;
        using (var api = new MockApi { FailFree = true })
        using (var client = Client(api))
        {
            GenerationResult result = await GenerationPipeline.RunAsync(client, settings, DirectoryFor("cleanup-error"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
            Check(File.Exists(result.Images[0]) && settings.CleanupNotice.Contains("could not be confirmed"), "cleanup failure keeps image and reports warning");
        }
        using (var api = new MockApi { BusyQueueChecks = 3 })
        using (var client = Client(api))
        {
            string notice = await client.ReleaseMemoryAsync(_ => { }, CancellationToken.None);
            Check(api.FreeCalls == 1 && api.Routes.FindIndex(r => r.Contains("free")) >= 3 && notice == "", "wait for other queued work before server-wide cleanup");
        }
        using (var api = new MockApi { RetainVram = true })
        using (var client = Client(api))
        {
            settings.Vision.Enabled = true;
            int llmCalls = 0;
            await ThrowsAsync<TimeoutException>(() => GenerationPipeline.RunAsync(client, settings, DirectoryFor("vram-not-released"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None,
                (_, _) => { llmCalls++; return Task.FromResult("unused"); }, cleanupTimeout: TimeSpan.FromMilliseconds(30)), "Comfy release measured before loading vision");
            Check(llmCalls == 0 && api.Queued == 0, "no LLM or image started when Comfy retains VRAM");
            settings.Vision.Enabled = false;
        }
        using (var api = new MockApi { UnknownMemoryBackend = true })
        using (var client = Client(api))
            Check((await client.ReleaseMemoryAsync(_ => { }, CancellationToken.None)).Contains("could not be measured"), "MPS acknowledgement not reported as measured GPU release");
        using (var cancel = new CancellationTokenSource())
        using (var api = new MockApi { NeverComplete = true, OnHistory = cancel.Cancel })
        using (var client = Client(api))
        {
            await ThrowsAsync<OperationCanceledException>(() => GenerationPipeline.RunAsync(client, settings, DirectoryFor("pipeline-cancel"), TimeSpan.FromSeconds(2), _ => { }, cancel.Token), "image cancellation propagated");
            Check(api.Cancelled.Count == 1 && api.FreeCalls == 1, "scoped image cancellation followed by memory cleanup");
        }
        using (var api = new MockApi { FailHistory = true })
        using (var client = Client(api))
        {
            await ThrowsAsync<InvalidDataException>(() => GenerationPipeline.RunAsync(client, settings, DirectoryFor("pipeline-error"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None), "image errors propagated");
            Check(api.FreeCalls == 1, "execution error still cleans up");
        }
        using (var api = new MockApi())
        using (var client = Client(api))
        {
            settings.Positive = "User edited prompt, kept verbatim.";
            await GenerationPipeline.RunAsync(client, settings, DirectoryFor("manual-cleanup"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
            Check(settings.Positive == "User edited prompt, kept verbatim." && api.FreeCalls == 1, "manual regeneration no LLM rewrite, cleanup still applied");
        }
    }
}
