using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static GenerationSettings ReferencedSettings(ModelFamily family)
    {
        var s = Settings(family);
        s.PortraitSettings = PortraitReferenceSettings.Default(family);
        s.PortraitSettings.Enabled = true;
        s.PortraitUploadNamespace = "246a3af246aa4315b81a996cae51c555";
        s.SceneCharacterKeys.Add("player:Dan");
        if (family == ModelFamily.Anima) s.SceneCharacterKeys.Add("npc:Oriana");
        s.Portraits = s.SceneCharacterKeys.Select(key => new PortraitReference { CharacterKey = key, Png = Png }).ToList();
        return s;
    }
    private static void PortraitWorkflows()
    {
        var s = ReferencedSettings(ModelFamily.Anima);
        s.Loras.Add(new LoraSetting { Name = "style.safetensors" }); s.EasyCache = true;
        s.Detailers.Add(new DetailerSetting { Enabled = true, Detector = "bbox/face.pt" });
        var graph = Workflow.Build(s, 42);
        Check((string?)graph["portrait_apply"]?["inputs"]?["model"]?[0] == "lora_0", "normal LoRAs feed adapter");
        Check((string?)graph["cache"]?["inputs"]?["model"]?[0] == "portrait_apply", "cache follows adapter");
        Check((string?)graph["detailer_0"]?["inputs"]?["model"]?[0] == "lora_0", "detailer does not reapply pair portrait to each face");
        Check(graph.Values().All(n => (string?)n["class_type"] != "ImageStitch"), "Anima never merges portraits into a reference sheet");
        Check((string?)graph["portrait_apply"]?["inputs"]?["ref_image"]?[0] == "portrait_0_fit" && (string?)graph["portrait_apply"]?["inputs"]?["ref_image_2"]?[0] == "portrait_1_fit", "distinct portrait inputs reach encoder");
        Check((int?)graph["portrait_0_fit"]?["inputs"]?["target_width"] == 1024 && (int?)graph["portrait_1_fit"]?["inputs"]?["target_width"] == 1024, "each full portrait fits its own square without cropping");
        Check((int?)graph["portrait_apply"]?["inputs"]?["ref_image_size"] == 512, "existing settings default to 512 encoder resolution");
        foreach (int size in new[] { 224, 512, 768, 1024, 1536, 2048 })
        {
            s.PortraitSettings.ReferenceSize = size; s.Validate();
            var sized = Workflow.Build(s, 42);
            Check((int?)sized["portrait_apply"]?["inputs"]?["ref_image_size"] == size, "reference size reaches encoder: " + size);
            Check((int?)sized["portrait_0_fit"]?["inputs"]?["target_width"] >= size, "preparation does not reduce requested reference resolution: " + size);
            Check(sized["6"]!.ToString() == graph["6"]!.ToString(), "reference size does not change output resolution");
        }
        foreach (int size in new[] { 0, 225, 4096 })
        {
            s.PortraitSettings.ReferenceSize = size;
            Throws<ArgumentException>(s.Validate, "invalid reference size diagnosed before submitting");
        }
        s.PortraitSettings.ReferenceSize = 768;
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<PortraitReferenceSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s.PortraitSettings))!;
        Check(restored.ReferenceSize == 768, "reference resolution survives settings roundtrip");
        Check(Newtonsoft.Json.JsonConvert.DeserializeObject<PortraitReferenceSettings>("{\"Enabled\":true,\"Strength\":0.2}")!.ReferenceSize == 512, "old settings migrate with a usable reference-size default");
        s.PortraitSettings.ReferenceSize = 512;
        Check(graph.Properties().All(p => !((string?)p.Value["class_type"] ?? "").Contains("AttentionCouple")), "no attention coupling");
        var info = Info(s);
        Workflow.ValidateServer(graph, info);
        info["AnimaIPAdapterApply"]!["input"]!["optional"] = new JObject();
        Throws<InvalidDataException>(() => Workflow.ValidateServer(graph, info), "older node cannot silently drop the second portrait");
        Check(PortraitRequirements.AnimaSupportIssue(info).Contains("ref_image_2"), "settings identify the missing separate-reference capability");
        info = Info(s);
        info["AnimaIPAdapterApply"]!["description"] = "";
        Throws<InvalidDataException>(() => Workflow.ValidateServer(graph, info), "unpatched Anima requires compatibility update");
        s.Portraits.RemoveAt(1); graph = Workflow.Build(s, 42);
        Check(graph["portrait_apply"]?["inputs"]?["ref_image_2"] == null && (int?)graph["portrait_0_fit"]?["inputs"]?["target_width"] == 1024, "single Anima portrait omits second input");
        s.Portraits[0].CharacterKey = "npc:Bystander";
        Throws<ArgumentException>(() => Workflow.Build(s, 42), "unselected portrait cannot enter graph");
        s = ReferencedSettings(ModelFamily.Illustrious); s.PortraitSettings.Strength = .7; s.PortraitSettings.End = .8;
        graph = Workflow.Build(s, 42);
        Check((string?)graph["portrait_apply"]?["class_type"] == "IPAdapterAdvanced", "Illustrious uses SDXL IP-Adapter");
        Check(graph["portrait_pair"] == null && graph["portrait_1"] == null, "Illustrious only one reference");
        Check((double?)graph["portrait_apply"]?["inputs"]?["weight"] == .7 && (double?)graph["portrait_apply"]?["inputs"]?["end_at"] == .8, "strength and timing reach SDXL node");
        info = Info(s);
        info["IPAdapterModelLoader"]!["input"]!["required"]!["ipadapter_file"] = new JArray { new JArray(s.PortraitSettings.Adapter) };
        info["CLIPVisionLoader"]!["input"]!["required"]!["clip_name"] = new JArray { new JArray(s.PortraitSettings.ClipVision) };
        Check(PortraitRequirements.Unavailable(info, s.Family, s.PortraitSettings) == "", "installed matching dependencies available");
        info.Remove("IPAdapterAdvanced");
        Check(PortraitRequirements.Unavailable(info, s.Family, s.PortraitSettings).Contains("IPAdapterAdvanced"), "missing node disables option with exact name");
        info = Info(s); info["LoadImage"]!["input"]!["required"]!["image"] = new JArray { new JArray("input-only.png") };
        Workflow.ValidateServer(graph, info); assertions++;
        s.Portraits.Add(new PortraitReference { CharacterKey = "npc:Oriana", Png = Png });
        Throws<ArgumentException>(() => Workflow.Build(s, 42), "Illustrious rejects a second portrait");
        s.Portraits.Clear(); graph = Workflow.Build(s, 42);
        Check(graph["portrait_apply"] == null, "missing artwork leaves text-only workflow");
        s.WorkflowFile = "custom.json"; Throws<ArgumentException>(s.Validate, "custom template cannot silently ignore portrait option");
        s.WorkflowFile = ""; s.PortraitSettings.Start = .9; s.PortraitSettings.End = .8;
        Throws<ArgumentException>(s.Validate, "invalid adapter timing diagnosed");
        var store = new CharacterProfileStore(DirectoryFor("portrait-profiles"));
        store.Set("player:Dan", "Dan", "Fox", "Manual", PortraitSource.Nude, "Portraits/Dan.png");
        store.SavePreset("Portraits"); store.Set("player:Dan", "Dan", "Updated by LLM", "LLM");
        Check(store.Profiles["player:Dan"].PortraitFile == "Portraits/Dan.png", "description update preserves portrait override");
        store.LoadPreset("Portraits");
        Check(store.Profiles["player:Dan"].PortraitSource == PortraitSource.Nude && store.Profiles["player:Dan"].PortraitFile == "Portraits/Dan.png", "portrait choice survives preset roundtrip");
    }
    private static async Task PortraitApi()
    {
        var s = ReferencedSettings(ModelFamily.Anima);
        using var api = new MockApi { Nodes = Info(s) }; using var client = Client(api);
        await client.GenerateAsync(s, DirectoryFor("portraits"), TimeSpan.FromSeconds(3), _ => { }, CancellationToken.None);
        Check(api.Uploads == 2 && api.Queued == 1, "two portraits uploaded before one generation");
        Check((string?)api.SubmittedGraph?["portrait_0"]?["inputs"]?["image"] == api.UploadedPaths[0] + " [temp]", "graph uses confirmed annotated temp path");
        await client.GenerateAsync(s, DirectoryFor("portraits"), TimeSpan.FromSeconds(3), _ => { }, CancellationToken.None);
        Check(api.UploadedPaths.Take(2).SequenceEqual(api.UploadedPaths.Skip(2)), "regeneration reuses bounded server upload slots");
        using var cancel = new CancellationTokenSource();
        using var cancelling = new MockApi { Nodes = Info(s), OnUpload = cancel.Cancel }; using var c = Client(cancelling);
        await ThrowsAsync<OperationCanceledException>(() => c.GenerateAsync(s, DirectoryFor("upload-cancel"), TimeSpan.FromSeconds(3), _ => { }, cancel.Token), "closing viewer interrupts upload");
        Check(cancelling.Queued == 0 && cancelling.Uploads == 1 && cancelling.Cancelled.Count == 0, "upload cancellation submits no job or global cancel");
        using var invalid = new MockApi { Nodes = Info(s), BadUpload = true }; using var c2 = Client(invalid);
        await ThrowsAsync<InvalidDataException>(() => c2.GenerateAsync(s, DirectoryFor("bad-upload"), TimeSpan.FromSeconds(3), _ => { }, CancellationToken.None), "unexpected server upload path rejected");
        Check(invalid.Queued == 0, "bad upload never queues");
    }
}
