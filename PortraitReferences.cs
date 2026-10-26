using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SilverpineMods.ComfyImageGenerator;

public enum PortraitSource { Current, Clothed, Nude }

public sealed class PortraitReferenceSettings
{
    public bool Enabled { get; set; }
    public string Adapter { get; set; } = "";
    public string ClipVision { get; set; } = "clip-vision_vit-h.safetensors";
    public double Strength { get; set; } = .5;
    public int ReferenceSize { get; set; } = 512;
    public double Start { get; set; }
    public double End { get; set; } = 1;
    public static PortraitReferenceSettings Default(ModelFamily family) => new()
    {
        Adapter = family == ModelFamily.Anima ? "ip_adapter-Character_Reference-10.safetensors" : "ip-adapter_sdxl_vit-h.safetensors",
        Strength = family == ModelFamily.Anima ? .2 : .5
    };
    public void Validate(ModelFamily family)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(Adapter) || (family == ModelFamily.Illustrious && string.IsNullOrWhiteSpace(ClipVision)))
            throw new ArgumentException("Select the portrait adapter and its matching vision encoder in Portrait references.");
        if (!Finite(Strength, 0, family == ModelFamily.Anima ? 2 : 5))
            throw new ArgumentException("Portrait strength must be 0-2 for Anima or 0-5 for Illustrious.");
        if (family == ModelFamily.Anima && (ReferenceSize < 224 || ReferenceSize > 2048 || ReferenceSize % 16 != 0))
            throw new ArgumentException("Anima reference size must be 224-2048 pixels, in multiples of 16. Start at 512.");
        if (family == ModelFamily.Illustrious && (!Finite(Start, 0, 1) || !Finite(End, 0, 1) || Start >= End))
            throw new ArgumentException("Portrait timing needs 0 <= start < end <= 1.");
    }
    private static bool Finite(double n, double min, double max) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= min && n <= max;
}

public sealed class PortraitReference
{
    public string CharacterKey { get; set; } = "";
    [JsonIgnore] public byte[] Png { get; set; } = Array.Empty<byte>();
}

public static class PortraitRequirements
{
    public const string AnimaCompatibility = "Silverpine portrait compatibility v1";
    public static string[] Nodes(ModelFamily family) => new[]
    { "LoadImage", "GetImageSize", "EmptyImage", "InvertMask", "ImageCompositeMasked", "ResizeAndPadImage" }
        .Concat(family == ModelFamily.Anima ? new[] { "AnimaIPAdapterLoader", "AnimaIPAdapterApply" }
            : new[] { "IPAdapterModelLoader", "IPAdapterAdvanced", "CLIPVisionLoader" }).ToArray();
    public static string AnimaSupportIssue(JObject info)
    {
        if (!((string?)info["AnimaIPAdapterApply"]?["description"] ?? "").Contains(AnimaCompatibility))
            return "Install the included Anima compatibility update and restart ComfyUI. It supports the tested 28-to-40 block mapping and normal LoRA composition.";
        JToken? second = info["AnimaIPAdapterApply"]?["input"]?["optional"]?["ref_image_2"]
            ?? info["AnimaIPAdapterApply"]?["input"]?["required"]?["ref_image_2"];
        return second is JArray input && (string?)input.First == "IMAGE" ? ""
            : "Update the Anima node using the included installer, restart ComfyUI, and refresh lists. This version needs separate portrait inputs (ref_image_2).";
    }
    public static string Unavailable(JObject info, ModelFamily family, PortraitReferenceSettings settings)
    {
        string[] missing = Workflow.MissingNodes(info, Nodes(family));
        if (missing.Length > 0) return "Missing nodes: " + string.Join(", ", missing) + ".";
        if (family == ModelFamily.Anima && AnimaSupportIssue(info) is string issue && issue.Length > 0) return issue;
        string loader = family == ModelFamily.Anima ? "AnimaIPAdapterLoader" : "IPAdapterModelLoader";
        string input = family == ModelFamily.Anima ? "ip_adapter_name" : "ipadapter_file";
        if (!Workflow.Choices(info, loader, input).Contains(settings.Adapter)) return "Adapter file is not installed: " + settings.Adapter;
        if (family == ModelFamily.Illustrious && !Workflow.Choices(info, "CLIPVisionLoader", "clip_name").Contains(settings.ClipVision))
            return "Vision encoder is not installed: " + settings.ClipVision;
        return "";
    }
}

public static partial class Workflow
{
    private static JArray AppendPortraitReferences(JObject graph, GenerationSettings s, JArray model)
    {
        if (!s.PortraitSettings.Enabled || s.Portraits.Count == 0) return model;
        int maximum = s.Family == ModelFamily.Anima ? 2 : 1;
        if (s.Portraits.Count > maximum || s.Portraits.Select(p => p.CharacterKey).Distinct().Count() != s.Portraits.Count ||
            s.Portraits.Any(p => !s.SceneCharacterKeys.Contains(p.CharacterKey)))
            throw new ArgumentException("Portrait references must belong to the selected scene participants (Anima: up to two; Illustrious: one).");
        var images = new List<JArray>();
        for (int i = 0; i < s.Portraits.Count; i++)
        {
            string id = "portrait_" + i;
            graph[id] = Node("LoadImage", new JObject { ["image"] = "pending-portrait.png [temp]" });
            graph[id + "_size"] = Node("GetImageSize", new JObject { ["image"] = Link(id) });
            graph[id + "_background"] = Node("EmptyImage", new JObject { ["width"] = Link(id + "_size"), ["height"] = Link(id + "_size", 1), ["batch_size"] = 1, ["color"] = 3158064 });
            graph[id + "_alpha"] = Node("InvertMask", new JObject { ["mask"] = Link(id, 1) });
            graph[id + "_flatten"] = Node("ImageCompositeMasked", new JObject
            { ["destination"] = Link(id + "_background"), ["source"] = Link(id), ["mask"] = Link(id + "_alpha"), ["x"] = 0, ["y"] = 0, ["resize_source"] = false });
            int canvas = s.Family == ModelFamily.Anima ? Math.Max(1024, s.PortraitSettings.ReferenceSize) : 1024;
            graph[id + "_fit"] = Node("ResizeAndPadImage", new JObject
            { ["image"] = Link(id + "_flatten"), ["target_width"] = canvas, ["target_height"] = canvas, ["padding_color"] = "black", ["interpolation"] = "lanczos" });
            images.Add(Link(id + "_fit"));
        }
        JArray image = images[0];
        PortraitReferenceSettings r = s.PortraitSettings;
        if (s.Family == ModelFamily.Anima)
        {
            graph["portrait_adapter"] = Node("AnimaIPAdapterLoader", new JObject { ["ip_adapter_name"] = r.Adapter, ["auto_download"] = false });
            graph["portrait_apply"] = Node("AnimaIPAdapterApply", new JObject
            {
                ["model"] = model, ["ip_adapter"] = Link("portrait_adapter"), ["ref_image"] = image, ["strength"] = r.Strength,
                ["ref_image_size"] = r.ReferenceSize, ["siglip_layer"] = -1, ["ip_cfg_scale"] = 4.0, ["ip_cfg_separate"] = false, ["gray_null"] = false, ["use_lora"] = true
            });
            // Encode portraits independently; combine reference features without a shared left/right canvas.
            if (images.Count == 2) graph["portrait_apply"]!["inputs"]!["ref_image_2"] = images[1];
        }
        else
        {
            graph["portrait_adapter"] = Node("IPAdapterModelLoader", new JObject { ["ipadapter_file"] = r.Adapter });
            graph["portrait_vision"] = Node("CLIPVisionLoader", new JObject { ["clip_name"] = r.ClipVision });
            graph["portrait_apply"] = Node("IPAdapterAdvanced", new JObject
            {
                ["model"] = model, ["ipadapter"] = Link("portrait_adapter"), ["clip_vision"] = Link("portrait_vision"), ["image"] = image,
                ["weight"] = r.Strength, ["weight_type"] = "linear", ["combine_embeds"] = "concat", ["start_at"] = r.Start, ["end_at"] = r.End, ["embeds_scaling"] = "V only"
            });
        }
        return Link("portrait_apply");
    }
}
