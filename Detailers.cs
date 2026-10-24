using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace SilverpineMods.ComfyImageGenerator;

public sealed class DetailerSetting
{
    public string Name { get; set; } = "Detailer";
    public bool Enabled { get; set; } = true;
    public string Detector { get; set; } = "";
    public int Steps { get; set; } = 20;
    public double Cfg { get; set; } = 5;
    public double Denoise { get; set; } = 0.35;
    public string Sampler { get; set; } = "";
    public string Scheduler { get; set; } = "";
    public int GuideSize { get; set; } = 512;
    public int MaxSize { get; set; } = 1024;
    public double DetectionThreshold { get; set; } = 0.5;
    public int Dilation { get; set; } = 10;
    public double CropFactor { get; set; } = 3;
    public int Feather { get; set; } = 5;
    public int Cycles { get; set; } = 1;
    public bool NoiseMask { get; set; } = true;
    public bool ForceInpaint { get; set; } = true;
    public string Positive { get; set; } = "";
    public string Negative { get; set; } = "";
    public string RegionId { get; set; } = "";

    public void Validate()
    {
        if (!Enabled) return;
        string prefix = "Detailer '" + Name + "': ";
        if (string.IsNullOrWhiteSpace(Detector)) throw new ArgumentException(prefix + "select an installed detector model.");
        if (Steps < 1 || Steps > 150 || !Range(Cfg, 0, 30)) throw new ArgumentException(prefix + "steps must be 1-150 and CFG 0-30.");
        if (!Range(Denoise, 0.0001, 1)) throw new ArgumentException(prefix + "denoise must be 0.0001-1.");
        if (GuideSize < 64 || MaxSize > 2048 || GuideSize > MaxSize || GuideSize % 8 != 0 || MaxSize % 8 != 0)
            throw new ArgumentException(prefix + "guide/max size must be multiples of 8, with 64 <= guide <= max <= 2048.");
        if (!Range(DetectionThreshold, 0, 1) || !Range(CropFactor, 1, 10) || Dilation < -512 || Dilation > 512)
            throw new ArgumentException(prefix + "detector threshold must be 0-1, crop factor 1-10, and dilation -512 to 512.");
        if (Feather < 0 || Feather > 100 || Cycles < 1 || Cycles > 10)
            throw new ArgumentException(prefix + "feather must be 0-100 and cycles 1-10.");
        if (Positive == null || Negative == null || Positive.Length > 32000 || Negative.Length > 32000)
            throw new ArgumentException(prefix + "prompts must be text no longer than 32,000 characters.");
    }

    private static bool Range(double v, double min, double max) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max;
}

public static partial class Workflow
{
    private static JArray AppendDetailers(JObject graph, GenerationSettings settings, long seed, JArray model, JArray clip, JArray vae)
    {
        JArray image = Link("8");
        for (int i = 0; i < settings.Detailers.Count; i++)
        {
            DetailerSetting pass = settings.Detailers[i];
            if (!pass.Enabled) continue;
            string baseId = "detailer_" + i, detectorId = baseId + "_detector";
            graph[detectorId] = Node("UltralyticsDetectorProvider", new JObject { ["model_name"] = pass.Detector });
            bool segmentation = pass.Detector.StartsWith("segm/", StringComparison.Ordinal);
            if (settings.AttentionCouple)
            {
                // One detection snapshot per pass, before any character is redrawn.
                // All assignments use these same bboxes, so a face cannot move into
                // another region and get processed again during this pass.
                graph[baseId + "_detection"] = Node(segmentation ? "SegmDetectorSEGS" : "BboxDetectorSEGS", new JObject {
                    [segmentation ? "segm_detector" : "bbox_detector"] = Link(detectorId, segmentation ? 1 : 0),
                    ["image"] = image.DeepClone(), ["threshold"] = pass.DetectionThreshold, ["dilation"] = pass.Dilation,
                    ["crop_factor"] = pass.CropFactor, ["drop_size"] = 10, ["labels"] = "all" });
                AppendRegionAssignments(graph, settings, baseId);
            }
            bool allRegions = settings.AttentionCouple && string.IsNullOrEmpty(pass.RegionId);
            var targets = allRegions ? settings.Regions.Cast<CharacterRegion?>() : new CharacterRegion?[] {
                string.IsNullOrEmpty(pass.RegionId) ? null : settings.Regions.Single(r => r.Id == pass.RegionId) };
            foreach (CharacterRegion? region in targets)
            {
                string id = allRegions ? baseId + "_region_" + settings.Regions.IndexOf(region!) : baseId;
                JArray positive = Link("4"), negative = Link("5");
                if (region != null)
                {
                    positive = Link("region_" + settings.Regions.IndexOf(region) + "_detail_positive");
                }
                if (!string.IsNullOrWhiteSpace(pass.Positive))
                {
                    string detailPrompt = region == null ? pass.Positive : region.Prompt + ", " + pass.Positive;
                    graph[id + "_positive"] = Node("CLIPTextEncode", new JObject { ["text"] = settings.ComposePositive(detailPrompt), ["clip"] = clip.DeepClone() });
                    positive = Link(id + "_positive");
                }
                if (!string.IsNullOrWhiteSpace(pass.Negative))
                {
                    graph[id + "_negative"] = Node("CLIPTextEncode", new JObject { ["text"] = pass.Negative, ["clip"] = clip.DeepClone() });
                    negative = Link(id + "_negative");
                }
                JObject inputs = new()
                {
                    ["image"] = image.DeepClone(), ["model"] = model.DeepClone(), ["clip"] = clip.DeepClone(), ["vae"] = vae.DeepClone(),
                    ["positive"] = positive, ["negative"] = negative, ["seed"] = seed,
                    ["steps"] = pass.Steps, ["cfg"] = pass.Cfg, ["denoise"] = pass.Denoise,
                    ["sampler_name"] = string.IsNullOrWhiteSpace(pass.Sampler) ? settings.Sampler : pass.Sampler,
                    ["scheduler"] = string.IsNullOrWhiteSpace(pass.Scheduler) ? settings.Scheduler : pass.Scheduler,
                    ["guide_size"] = pass.GuideSize, ["guide_size_for"] = true, ["max_size"] = pass.MaxSize,
                    ["feather"] = pass.Feather, ["noise_mask"] = pass.NoiseMask, ["force_inpaint"] = pass.ForceInpaint,
                    ["cycle"] = pass.Cycles, ["wildcard"] = ""
                };
                if (region != null) inputs["segs"] = Link(baseId + "_region_" + settings.Regions.IndexOf(region) + "_assignment");
                else
                {
                    inputs["bbox_threshold"] = pass.DetectionThreshold; inputs["bbox_dilation"] = pass.Dilation;
                    inputs["bbox_crop_factor"] = pass.CropFactor; inputs["bbox_detector"] = Link(detectorId); inputs["drop_size"] = 10;
                    inputs["sam_detection_hint"] = "center-1"; inputs["sam_dilation"] = 0; inputs["sam_threshold"] = 0.93;
                    inputs["sam_bbox_expansion"] = 0; inputs["sam_mask_hint_threshold"] = 0.7; inputs["sam_mask_hint_use_negative"] = "False";
                    if (segmentation) inputs["segm_detector_opt"] = Link(detectorId, 1);
                }
                graph[id] = Node(region == null ? "FaceDetailer" : "DetailerForEach", inputs);
                image = Link(id);
            }
        }
        return image;
    }

    private static void AppendRegionAssignments(JObject graph, GenerationSettings settings, string passId)
    {
        JArray available = Link(passId + "_detection");
        AppendDetectionCount(graph, passId + "_detected", available);
        // Existing Impact Pack filters return the original SEG objects. Filtering
        // by mask overlap leaves both the full face mask and padded crop intact.
        // Assign all regions in list order, even for a single targeted character,
        // so that ownership does not change with the detailer target setting.
        for (int i = 0; i < settings.Regions.Count; i++)
        {
            string outsideId = "region_" + i + "_detail_outside";
            if (graph[outsideId] == null)
                graph[outsideId] = Node("MaskToSEGS", new JObject {
                    ["mask"] = Link("region_" + i + "_background_cutout"), ["combined"] = true,
                    ["crop_factor"] = 1, ["bbox_fill"] = false, ["drop_size"] = 1, ["contour_fill"] = false });
            string id = passId + "_region_" + i;
            // The intersection filter REMOVES masks above the threshold. With
            // the inverse rectangle, this keeps faces at least half inside it.
            graph[id + "_assignment"] = Node("ImpactSEGSIntersectionFilter", new JObject {
                ["segs1"] = available.DeepClone(), ["segs2"] = Link(outsideId), ["ioa_threshold"] = .5 });
            AppendDetectionCount(graph, id + "_selected", Link(id + "_assignment"));
            // Remove selected detections before considering the next region.
            // A detection overlaps itself by 100%, including exact 50/50 ties.
            // Near-identical duplicate detections are removed as well.
            graph[id + "_remaining"] = Node("ImpactSEGSIntersectionFilter", new JObject {
                ["segs1"] = available.DeepClone(), ["segs2"] = Link(id + "_assignment"), ["ioa_threshold"] = .99 });
            available = Link(id + "_remaining");
        }
    }

    private static void AppendDetectionCount(JObject graph, string id, JArray segs)
    {
        graph[id + "_count"] = Node("ImpactCount_Elts_in_SEGS", new JObject { ["segs"] = segs.DeepClone() });
        graph[id + "_report"] = Node("PreviewAny", new JObject { ["source"] = Link(id + "_count") });
    }
}
