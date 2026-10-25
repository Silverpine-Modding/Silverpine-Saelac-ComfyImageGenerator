using Newtonsoft.Json.Linq;
using System;

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
            string id = "detailer_" + i, detectorId = id + "_detector";
            graph[detectorId] = Node("UltralyticsDetectorProvider", new JObject { ["model_name"] = pass.Detector });
            bool segmentation = pass.Detector.StartsWith("segm/", StringComparison.Ordinal);
            JArray positive = Link("4"), negative = Link("5");
            if (!string.IsNullOrWhiteSpace(pass.Positive))
            {
                graph[id + "_positive"] = Node("CLIPTextEncode", new JObject { ["text"] = settings.ComposePositive(pass.Positive), ["clip"] = clip.DeepClone() });
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
            inputs["bbox_threshold"] = pass.DetectionThreshold; inputs["bbox_dilation"] = pass.Dilation;
            inputs["bbox_crop_factor"] = pass.CropFactor; inputs["bbox_detector"] = Link(detectorId); inputs["drop_size"] = 10;
            inputs["sam_detection_hint"] = "center-1"; inputs["sam_dilation"] = 0; inputs["sam_threshold"] = 0.93;
            inputs["sam_bbox_expansion"] = 0; inputs["sam_mask_hint_threshold"] = 0.7; inputs["sam_mask_hint_use_negative"] = "False";
            if (segmentation) inputs["segm_detector_opt"] = Link(detectorId, 1);
            graph[id] = Node("FaceDetailer", inputs);
            image = Link(id);
        }
        return image;
    }

}
