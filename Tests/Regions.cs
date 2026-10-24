using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static GenerationSettings RegionalSettings()
    {
        var s = Settings(); s.AttentionCouple = true; s.Regions = RegionalPrompts.DefaultRegions();
        s.Positive = "two people, candlelit tavern, medium shot"; s.BasePositive = "watercolor, soft light";
        s.Regions[0].Name = "Mara"; s.Regions[0].Prompt = "red fox, silver glasses, green shirt";
        s.Regions[1].Name = "Robin"; s.Regions[1].Prompt = "grey wolf, blue coat, short hair";
        s.Detailers.Add(new DetailerSetting { Detector = "bbox/face.pt", RegionId = s.Regions[0].Id });
        s.Detailers.Add(new DetailerSetting { Detector = "bbox/face.pt", RegionId = s.Regions[1].Id });
        return s;
    }
    private static void RegionalWorkflows()
    {
        GenerationSettings s = RegionalSettings();
        s.EasyCache = true; s.Loras.Add(new LoraSetting { Name = "style.safetensors" });
        JObject g = Workflow.Build(s, 42);
        Check((string?)g["attention_couple"]?["class_type"] == "AttentionCouplePPM", "real PPM attention coupling node");
        Check((string?)g["attention_couple"]?["inputs"]?["model"]?[0] == "cache" && (string?)g["7"]?["inputs"]?["model"]?[0] == "attention_couple", "coupling applied after cache and before sampling");
        Check(JToken.DeepEquals(g["attention_couple"]?["inputs"]?["base_cond"], g["7"]?["inputs"]?["positive"]), "PPM and sampler share identical base conditioning link");
        Check(((string)g["4"]!["inputs"]!["text"]!).Contains("two shot") && ((string)g["4"]!["inputs"]!["text"]!).Contains(s.Positive) && !((string)g["4"]!["inputs"]!["text"]!).Contains("red fox"), "shared prompt keeps composition without character identities");
        string leftPrompt = (string)g["region_0_positive"]!["inputs"]!["text"]!, rightPrompt = (string)g["region_1_positive"]!["inputs"]!["text"]!;
        Check(leftPrompt.StartsWith(s.BasePositive + "\n\n" + s.Regions[0].Prompt) && leftPrompt.IndexOf("red fox", StringComparison.Ordinal) < leftPrompt.IndexOf("candlelit tavern", StringComparison.Ordinal), "saved style stays first and character identity precedes shared scenery");
        Check(RegionalPrompts.DistinctTags("duo, (fox, red fur:1.2), duo, wide shot, WIDE SHOT, fox\\,wolf, fox\\,wolf") == "duo, (fox, red fur:1.2), wide shot, fox\\,wolf", "tag compaction preserves weighted groups and escaped commas");
        var duplicate = RegionalSettings(); duplicate.Positive = "duo, two distinct characters, separate bodies, two shot, wide shot, tavern, wide shot";
        string compact = RegionalPrompts.SharedComposition(duplicate);
        Check(compact.Split("duo").Length == 2 && compact.Split("wide shot").Length == 2 && compact.Split("two distinct characters").Length == 2, "LLM and application composition tags do not accumulate duplicates");
        Check(leftPrompt.Contains("two distinct characters") && leftPrompt.Contains("character on left") && leftPrompt.Contains(s.Positive) && leftPrompt.Contains(s.Regions[0].Prompt) && !leftPrompt.Contains("grey wolf"), "left character gets shared layout and own identity without right identity");
        Check(rightPrompt.Contains("two distinct characters") && rightPrompt.Contains("character on right") && rightPrompt.Contains(s.Positive) && rightPrompt.Contains(s.Regions[1].Prompt) && !rightPrompt.Contains("red fox"), "right character gets shared layout and own identity without left identity");
        Check((string?)g["region_1_positive"]?["inputs"]?["clip"]?[0] == "lora_0", "regional prompts use LoRA-patched CLIP");
        Check((string?)g["attention_couple"]?["inputs"]?["cond_1"]?[0] == "region_0_conditioning" && (string?)g["attention_couple"]?["inputs"]?["mask_2"]?[0] == "region_1_mask", "PPM dynamic inputs numbered from one");
        Check((string?)g["region_0_conditioning"]?["inputs"]?["conditioning"]?[0] == "region_0_positive" && (double?)g["region_0_conditioning"]?["inputs"]?["strength"] == 1, "default conditioning strength retains existing PPM behavior");
        s.Regions[0].PromptStrength = 1.2; g = Workflow.Build(s, 42);
        Check((double?)g["region_0_conditioning"]?["inputs"]?["strength"] == 1.2 && (double?)g["region_0_mask"]?["inputs"]?["value"] == 1 && (double?)g["region_1_conditioning"]?["inputs"]?["strength"] == 1, "prompt strength scales only the selected character conditioning, not mask weights");
        Check((double?)g["attention_base_mask"]?["inputs"]?["w"] == 1 && (double?)g["attention_base_mask"]?["inputs"]?["value"] == .3, "shared mask covers gaps with positive strength");
        Check((string?)g["attention_couple"]?["inputs"]?["base_mask"]?[0] == "region_1_background_mask" &&
            (double?)g["region_0_background_cutout"]?["inputs"]?["value"] == 0 && (double?)g["region_0_background_cutout"]?["inputs"]?["outer_value"] == 1,
            "shared conditioning is cut out of every character rectangle before entering PPM");
        Check((string?)g["region_1_background_mask"]?["inputs"]?["destination"]?[0] == "region_0_background_mask" &&
            (string?)g["region_0_background_mask"]?["inputs"]?["operation"] == "multiply", "background cutouts accumulate without subtracting low-strength region masks");
        Check((double?)g["region_1_mask"]?["inputs"]?["x"] == .5 && (double?)g["region_1_mask"]?["inputs"]?["w"] == .5, "second character receives right-half attention mask");
        Check((string?)g["detailer_0"]?["inputs"]?["image"]?[0] == "8", "detector receives the full decoded image, never a chopped face");
        Check((string?)g["region_1_detail_outside"]?["inputs"]?["mask"]?[0] == "region_1_background_cutout" &&
            (bool?)g["region_1_detail_outside"]?["inputs"]?["bbox_fill"] == false, "selection uses the exact inverted region rectangle without filling its hole");
        Check((string?)g["detailer_1"]?["inputs"]?["positive"]?[0] == "region_1_detail_positive" && (string?)g["region_1_detail_positive"]?["inputs"]?["text"] == s.ComposePositive(s.Regions[1].Prompt), "single-face detailing does not inherit the base generation's multiple-character layout");
        Check((string?)g["detailer_1"]?["inputs"]?["model"]?[0] == "lora_0", "crop sampling bypasses global attention and cache state");
        Check((string?)g["detailer_1"]?["inputs"]?["image"]?[0] == "detailer_0", "second detailer starts with previous full image");
        Check(!g.Values().Any(n => (string?)n["class_type"] is "ImageCrop" or "ImageCompositeMasked" or "SEGSRangeFilterDetailerHookProvider"), "no hard rectangle crops, pastes or padded-crop rejection filters");
        Check((string?)g["9"]?["inputs"]?["images"]?[0] == "detailer_1" && g.Values().Count(n => (string?)n["class_type"] == "PreviewImage") == 1, "only final full-size image is previewed");
        s.Detailers[1].Positive = "grey wolf, detailed amber eyes";
        g = Workflow.Build(s, 42);
        Check((string?)g["detailer_1_positive"]?["inputs"]?["text"] == s.ComposePositive(s.Regions[1].Prompt + ", " + s.Detailers[1].Positive) && (string?)g["detailer_1"]?["inputs"]?["segs"]?[0] == "detailer_1_region_1_assignment", "character detail tags supplement identity while keeping unique detection assignment");
        s.Regions.Reverse(); g = Workflow.Build(s, 42);
        Check((string?)g["detailer_0"]?["inputs"]?["positive"]?[0] == "region_1_detail_positive", "detailer targets stable IDs after region reordering");
        s = RegionalSettings(); s.Width = 1216; s.Height = 832;
        s.Regions[1].X = .337; s.Regions[1].Y = .13; s.Regions[1].Width = .61; s.Regions[1].Height = .72;
        g = Workflow.Build(s, 42);
        PixelRegion p = s.Regions[1].Pixels(s.Width, s.Height);
        Check(p.X % 8 == 0 && p.Width % 8 == 0 && p.Y % 8 == 0 && p.Height % 8 == 0, "arbitrary bounds align to VAE grid");
        Check(Math.Abs((double)g["region_1_mask"]!["inputs"]!["x"]! * s.Width - p.X) < .00001 &&
            Math.Abs((double)g["region_1_background_cutout"]!["inputs"]!["y"]! * s.Height - p.Y) < .00001, "preview, attention masks and whole-face assignment share snapped pixel bounds");
        string saved = JsonConvert.SerializeObject(s);
        GenerationSettings reload = JsonConvert.DeserializeObject<GenerationSettings>(saved)!;
        Check(reload.AttentionCouple && reload.Regions[1].Id == s.Regions[1].Id && reload.Detailers[1].RegionId == reload.Regions[1].Id && reload.Regions[1].X == .337, "region settings, prompts and stable detailer references survive JSON persistence");
        Check(JsonConvert.DeserializeObject<CharacterRegion>("{\"Strength\":0.5}")!.PromptStrength == 1 && JsonConvert.DeserializeObject<CharacterRegion>(JsonConvert.SerializeObject(new CharacterRegion { PromptStrength = 1.2 }))!.PromptStrength == 1.2, "older saved regions default to unchanged prompt strength and new values persist");
        JObject info = RegionalInfo(s); Workflow.ValidateServer(g, info);
        JObject changedAssignment = (JObject)info.DeepClone(); changedAssignment["ImpactSEGSIntersectionFilter"]!["input"]!["required"]!["future_option"] = new JArray("STRING");
        Throws<InvalidDataException>(() => Workflow.ValidateServer(g, changedAssignment), "unsupported Impact-node schemas fail preflight");
        Check(Workflow.MissingNodes(info, Workflow.AttentionNodes).Length == 0, "availability recognizes installed PPM requirements");
        foreach (string type in Workflow.AttentionNodes.Concat(Workflow.RegionDetailerNodes).Concat(new[] { "BboxDetectorSEGS" }))
        {
            JObject missing = (JObject)info.DeepClone(); missing.Remove(type);
            Check(Workflow.MissingNodes(missing, type).Single() == type, "UI gate detects missing " + type);
            Throws<InvalidDataException>(() => Workflow.ValidateServer(g, missing), "preflight rejects missing " + type);
        }
        info["AttentionCouplePPM"]!["input"]!["required"]!["future_input"] = new JArray("MODEL");
        Throws<InvalidDataException>(() => Workflow.ValidateServer(g, info), "unsupported PPM schema rejected before LLM or submission");
        foreach (Action<GenerationSettings> invalidate in new Action<GenerationSettings>[] {
            v => v.Family = ModelFamily.Anima, v => v.WorkflowFile = "custom.json", v => v.GlobalRegionStrength = 0,
            v => v.Regions[0].X = double.NaN, v => v.Regions[0].Width = .001, v => v.Regions[1].Width = .7,
            v => v.Regions[0].Height = double.PositiveInfinity, v => v.Regions[0].Strength = 0,
            v => v.Regions[0].PromptStrength = double.NaN, v => v.Regions[0].PromptStrength = 2.1,
            v => v.Regions[0].Prompt = "", v => v.Regions[1].Id = v.Regions[0].Id,
            v => v.Regions[1].CharacterIndex = v.Regions[0].CharacterIndex, v => v.Regions.Clear(),
            v => v.Detailers[0].RegionId = "removed", v => v.AttentionCouple = false })
        {
            var bad = RegionalSettings(); invalidate(bad); Throws<ArgumentException>(bad.Validate, "invalid region configuration blocked");
        }
        s = RegionalSettings(); s.Regions.ForEach(r => r.Prompt = ""); s.Validate(false);
        Check(true, "settings can save empty region prompts before first LLM use");
        s = RegionalSettings(); s.AttentionCouple = false; s.Detailers.ForEach(d => d.Enabled = false);
        g = Workflow.Build(s, 42);
        Check(g["attention_couple"] == null && g["region_0_mask"] == null, "off mode has no regional dependencies");
        s = RegionalSettings(); s.Detailers[0].RegionId = ""; g = Workflow.Build(s, 42);
        Check(g["detailer_0"] == null && (string?)g["detailer_0_region_0"]?["inputs"]?["positive"]?[0] == "region_0_detail_positive" &&
            (string?)g["detailer_0_region_1"]?["inputs"]?["positive"]?[0] == "region_1_detail_positive", "whole-image detailer becomes separate identity-preserving region passes in coupled mode");
        Check((string?)g["detailer_0_region_1"]?["inputs"]?["image"]?[0] == "detailer_0_region_0" &&
            (string?)g["detailer_0_region_1"]?["inputs"]?["segs"]?[0] == "detailer_0_region_1_assignment", "automatic regional detailing chains full images and whole detections");
        Check((string?)g["detailer_0_region_0_assignment"]?["inputs"]?["segs1"]?[0] == "detailer_0_detection" &&
            (string?)g["detailer_0_region_1_assignment"]?["inputs"]?["segs1"]?[0] == "detailer_0_region_0_remaining" &&
            (string?)g["detailer_0_detection"]?["inputs"]?["image"]?[0] == "8", "all regions share one immutable detection snapshot per detailer pass");
        Check((double?)g["detailer_0_region_0_assignment"]?["inputs"]?["ioa_threshold"] == .5 &&
            (string?)g["detailer_0_region_0_remaining"]?["inputs"]?["segs2"]?[0] == "detailer_0_region_0_assignment" &&
            (double?)g["detailer_0_region_0_remaining"]?["inputs"]?["ioa_threshold"] == .99, "majority overlap preserves whole masks and removes assigned detections before the next region");
        Check(g.Values().All(n => (string?)n["class_type"] != "SilverpineRegionSEGS") &&
            (string?)g["detailer_0_region_0_selected_report"]?["class_type"] == "PreviewAny", "workflow and diagnostics use only existing public nodes");
        Check((string?)g["detailer_1_detection"]?["inputs"]?["image"]?[0] == "detailer_0_region_1", "a later detailer pass detects on the prior pass's final image");
        Check((double?)g["detailer_0_detection"]?["inputs"]?["crop_factor"] == s.Detailers[0].CropFactor &&
            (string?)g["detailer_0_region_0"]?["class_type"] == "DetailerForEach", "padding is retained for sampling, never used for face eligibility");
        s.Detailers[0].Detector = "segm/furry-face.pt"; g = Workflow.Build(s, 42);
        Check((string?)g["detailer_0_detection"]?["class_type"] == "SegmDetectorSEGS" && (int?)g["detailer_0_detection"]?["inputs"]?["segm_detector"]?[1] == 1, "segmentation detector preserves its actual masks in regional detailing");
        s = RegionalSettings(); s.Positive = "solo, tavern, two people";
        s.Regions[0].Prompt = "solo, red fox, (solo:1.2), solo focus, green shirt";
        s.Regions[0].Width = .2; s.Regions[1].X = .8; s.Regions[1].Width = .2;
        g = Workflow.Build(s, 42);
        Check(!((string)g["4"]!["inputs"]!["text"]!).Contains("solo"), "uncovered background cannot retain a conflicting solo prompt");
        foreach (int index in new[] { 0, 1 })
        {
            string prompt = (string)g["region_" + index + "_positive"]!["inputs"]!["text"]!;
            Check(!prompt.Contains("solo") && prompt.Contains("two distinct characters") && prompt.Contains("two shot"), "old saved solo tags cannot override two-character composition in narrow regions");
            Check(prompt.Split("watercolor").Length == 2, "shared layout adds the base style only once");
        }
        Check(s.Regions[0].Prompt.StartsWith("solo") && s.Positive.StartsWith("solo"), "workflow normalization does not overwrite editable saved prompts");
        s.Regions.RemoveAt(1); s.Detailers.Clear(); s.Positive = "solo, tavern";
        g = Workflow.Build(s, 42);
        Check(((string)g["region_0_positive"]!["inputs"]!["text"]!).Contains("solo") && !((string)g["region_0_positive"]!["inputs"]!["text"]!).Contains("two shot"), "single-character regional scenes retain solo framing");
    }

    // Explicit public input schemas from upstream PPM and ComfyUI, independent
    // of the graph builder. Dynamic cond_N/mask_N are additional PPM inputs.
    private static JObject RegionalInfo(GenerationSettings s)
    {
        JObject info = Info(s);
        foreach (var schema in new Dictionary<string, string[]>
        {
            ["AttentionCouplePPM"] = new[] { "model", "base_cond", "base_mask" },
            ["LatentToMaskBB"] = new[] { "latent", "x", "y", "w", "h", "value" },
            ["MaskComposite"] = new[] { "destination", "source", "x", "y", "operation" },
            ["ConditioningSetAreaStrength"] = new[] { "conditioning", "strength" },
            ["MaskToSEGS"] = new[] { "mask", "combined", "crop_factor", "bbox_fill", "drop_size", "contour_fill" },
            ["ImpactSEGSIntersectionFilter"] = new[] { "segs1", "segs2", "ioa_threshold" },
            ["ImpactCount_Elts_in_SEGS"] = new[] { "segs" },
            ["PreviewAny"] = new[] { "source" },
            ["BboxDetectorSEGS"] = new[] { "bbox_detector", "image", "threshold", "dilation", "crop_factor", "drop_size", "labels" },
            ["SegmDetectorSEGS"] = new[] { "segm_detector", "image", "threshold", "dilation", "crop_factor", "drop_size", "labels" },
            ["DetailerForEach"] = new[] { "image", "segs", "model", "clip", "vae", "guide_size", "guide_size_for", "max_size", "seed", "steps", "cfg", "sampler_name", "scheduler", "positive", "negative", "denoise", "feather", "noise_mask", "force_inpaint", "wildcard", "cycle" }
        })
            info[schema.Key] = new JObject { ["input"] = new JObject { ["required"] = new JObject(schema.Value.Select(name => new JProperty(name, new JArray("TYPE")))) } };
        return info;
    }

    private static void RegionalLlmPrompts()
    {
        var scene = new SceneSnapshot { Dialogue = "Mara hands Robin a cup.", Environment = "A candlelit tavern.", Characters = new() {
            new SceneCharacter { Name = "Mara", Key = "a", Description = "A red fox with silver glasses." },
            new SceneCharacter { Name = "Robin", Key = "b", Description = "A grey wolf in a blue coat." } } };
        var regions = RegionalPrompts.DefaultRegions();
        string prompt = RegionalPrompts.Build(scene, regions, "soft shadows");
        Check(prompt.Contains(scene.Dialogue) && prompt.Contains(scene.Environment) && prompt.Contains(scene.Characters[0].Description), "regional LLM gets current scene and saved visual profiles");
        Check(prompt.Contains(regions[0].Id) && prompt.Contains("bounds") && prompt.Contains("NEVER a character's hair"), "LLM receives stable IDs, bounds and global trait exclusion");
        Check(regions[1].Name == "Robin" && regions[1].CharacterKey == "b", "scene binding maps dialogue order to real identities");
        JObject response = new() { ["global"] = "TAVERN, candle_light", ["characters"] = new JArray(
            new JObject { ["region"] = regions[1].Id, ["prompt"] = "GREY_WOLF, blue_coat" },
            new JObject { ["region"] = regions[0].Id, ["prompt"] = "RED_FOX, silver_glasses" }) };
        string global = RegionalPrompts.Apply("<think>hidden</think>\n```json\n" + response + "\n```", regions);
        Check(global == "tavern, candle light" && regions[0].Prompt == "red fox, silver glasses" && regions[1].Prompt == "grey wolf, blue coat", "out-of-order JSON maps prompts by ID and cleans each independently");
        foreach (Action<JObject> damage in new Action<JObject>[] {
            o => ((JArray)o["characters"]!).RemoveAt(1), o => o["characters"]![1]!["region"] = regions[1].Id,
            o => o["characters"]![0]!["region"] = "invented", o => o["characters"]![1]!["prompt"] = "x",
            o => o["global"] = "", o => o["characters"] = "broken" })
        {
            JObject broken = (JObject)response.DeepClone(); damage(broken);
            Throws<InvalidOperationException>(() => RegionalPrompts.Apply(broken.ToString(), regions), "malformed character assignment rejected");
            Check(regions[0].Prompt == "red fox, silver glasses" && regions[1].Prompt == "grey wolf, blue coat", "failed LLM response cannot partially replace prompts");
        }
        Throws<InvalidOperationException>(() => RegionalPrompts.Apply("red fox, blue coat, tavern", regions), "unstructured text cannot leak into shared prompt");
        regions[1].CharacterIndex = 3;
        Throws<ArgumentException>(() => RegionalPrompts.Bind(scene, regions), "missing dialogue character does not silently misassign a region");
    }

    private static async Task RegionalApi()
    {
        var s = RegionalSettings();
        using (var api = new MockApi { Nodes = RegionalInfo(s), ExtraOutputs = new JObject {
            ["detailer_0_detected_report"] = CountOutput(2), ["detailer_0_region_0_selected_report"] = CountOutput(1) } })
        using (var client = Client(api))
        {
            string dir = DirectoryFor("regional-api");
            var result = await client.GenerateAsync(s, dir, TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
            Check((string?)api.SubmittedGraph?["attention_couple"]?["class_type"] == "AttentionCouplePPM" && (string?)api.SubmittedGraph?["9"]?["inputs"]?["images"]?[0] == "detailer_1", "HTTP submits complete coupled and region-detailed job");
            Check(api.Queued == 1 && result.Images.Count == 1, "regional workflow retains one image for one job");
            Check(result.DetailerReports.Count == 1 && result.DetailerReports[0].Contains("2 detected, 1 selected"), "detector counts survive HTTP history and reach the viewer result");
            TemporaryImages.DeleteRun(dir, result.Directory);
        }
        foreach (string missing in Workflow.AttentionNodes.Concat(Workflow.RegionDetailerNodes).Concat(new[] { "BboxDetectorSEGS" }))
        {
            using var api = new MockApi { Nodes = RegionalInfo(s) }; using var client = Client(api); api.Nodes.Remove(missing);
            await ThrowsAsync<InvalidDataException>(() => client.GenerateAsync(s, DirectoryFor("regional-missing-" + missing), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None), "regional preflight blocks missing " + missing);
            Check(api.Queued == 0, "no HTTP job queued without " + missing);
        }
        using var cancel = new CancellationTokenSource();
        using var running = new MockApi { Nodes = RegionalInfo(s), OnHistory = cancel.Cancel }; using var cancelClient = Client(running);
        string cancelledDir = DirectoryFor("regional-cancel");
        await ThrowsAsync<OperationCanceledException>(() => cancelClient.GenerateAsync(s, cancelledDir, TimeSpan.FromSeconds(2), _ => { }, cancel.Token), "coupling and all detailer passes cancel together");
        Check(running.Cancelled.Single() == running.Id && ImageCount(cancelledDir) == 0, "regional cancellation scoped to own job and leaves no image");
    }
}
