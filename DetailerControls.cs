using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow
{
    private readonly List<DetailerDraft> detailers = new();

    private sealed class DetailerDraft
    {
        internal bool Enabled, NoiseMask, ForceInpaint;
        internal string Name, Detector, Steps, Cfg, Denoise, Sampler, Scheduler, GuideSize, MaxSize,
            DetectionThreshold, Dilation, CropFactor, Feather, Cycles, Positive, Negative;
        internal bool Expanded = true;

        internal DetailerDraft(DetailerSetting s)
        {
            Name = s.Name; Enabled = s.Enabled; Detector = s.Detector;
            Steps = s.Steps.ToString(Inv); Cfg = s.Cfg.ToString(Inv); Denoise = s.Denoise.ToString(Inv);
            Sampler = s.Sampler; Scheduler = s.Scheduler; GuideSize = s.GuideSize.ToString(Inv); MaxSize = s.MaxSize.ToString(Inv);
            DetectionThreshold = s.DetectionThreshold.ToString(Inv); Dilation = s.Dilation.ToString(Inv); CropFactor = s.CropFactor.ToString(Inv);
            Feather = s.Feather.ToString(Inv); Cycles = s.Cycles.ToString(Inv); NoiseMask = s.NoiseMask; ForceInpaint = s.ForceInpaint;
            Positive = s.Positive; Negative = s.Negative;
        }

        internal DetailerSetting Read() => new()
        {
            Name = Name, Enabled = Enabled, Detector = Detector.Trim(), Steps = Int(Steps, Name + " steps"), Cfg = Number(Cfg, Name + " CFG"),
            Denoise = Number(Denoise, Name + " denoise"), Sampler = Sampler.Trim(), Scheduler = Scheduler.Trim(),
            GuideSize = Int(GuideSize, Name + " guide size"), MaxSize = Int(MaxSize, Name + " max size"),
            DetectionThreshold = Number(DetectionThreshold, Name + " detection threshold"), Dilation = Int(Dilation, Name + " dilation"),
            CropFactor = Number(CropFactor, Name + " crop factor"), Feather = Int(Feather, Name + " feather"), Cycles = Int(Cycles, Name + " cycles"),
            NoiseMask = NoiseMask, ForceInpaint = ForceInpaint, Positive = Positive, Negative = Negative
        };
    }

    private void LoadDetailers(Profile profile)
    {
        detailers.Clear();
        try
        {
            foreach (DetailerSetting s in JsonConvert.DeserializeObject<List<DetailerSetting>>(profile.DetailersJson.Value) ?? new())
                detailers.Add(new DetailerDraft(s));
        }
        catch (Exception) { status = "The saved detailer list is invalid. Rebuild it in the Detailers tab and Save settings."; }
    }

    // Metadata is tied to the exact address that returned it; never unlock features using another server's lists.
    private string NodeAvailability(params string[] nodes)
    {
        try
        {
            if (connectionTask != null || Address() != infoAddress || info.Count == 0)
                return "Connect / refresh lists for this server to check availability and unlock this option.";
        }
        catch (Exception) { return "Enter a valid server address, then Connect / refresh lists to unlock this option."; }
        string[] missing = Workflow.MissingNodes(info, nodes);
        return missing.Length == 0 ? "" : "Unavailable: missing " + string.Join(", ", missing) + ". Install the required nodes on the server, restart ComfyUI, then refresh lists.";
    }

    private string DetailerAvailability()
    {
        string unavailable = NodeAvailability("FaceDetailer", "UltralyticsDetectorProvider");
        if (unavailable.Length != 0) return unavailable;
        if (Workflow.Choices(info, "UltralyticsDetectorProvider", "model_name").Length == 0)
            return "Unavailable: no detector models installed. Add a compatible model under ComfyUI/models/ultralytics/bbox or segm, then refresh lists.";
        if (!string.IsNullOrWhiteSpace(customWorkflow))
            return "Custom API workflow selected: put detailers inside that workflow, or clear its path to use these passes.";
        return "";
    }

    private static string[] ModelNodes(ModelFamily modelFamily) =>
        (modelFamily == ModelFamily.Anima ? new[] { "UNETLoader", "CLIPLoader", "VAELoader" } : new[] { "CheckpointLoaderSimple" })
        .Concat(new[] { "CLIPTextEncode", "EmptyLatentImage", "KSampler", "VAEDecode", "PreviewImage" }).ToArray();

    private void DrawFamilyButton(ModelFamily target)
    {
        bool wasEnabled = GUI.enabled;
        string unavailable = NodeAvailability(ModelNodes(target));
        GUI.enabled = wasEnabled && (family == target || unavailable.Length == 0);
        if (GUILayout.Button((family == target ? "Selected: " : "") + target, button, GUILayout.Width(260), GUILayout.Height(34)) && family != target)
            Try(() => { SaveSettings(); family = target; Plugin.Family.Value = family; LoadProfile(); });
        GUI.enabled = wasEnabled;
    }

    private void DrawDetailers()
    {
        GUILayout.Label("Detailer passes  /  " + family, title);
        GUILayout.Label("Passes run from top to bottom on the previous pass's image. Choose face, hand, or other detectors installed on your server. Only the final image is shown and kept temporarily.", label);
        GUILayout.Label("Requires FaceDetailer from ComfyUI-Impact-Pack, UltralyticsDetectorProvider from ComfyUI-Impact-Subpack, and a compatible detector model. Install these on the ComfyUI server, then Connect / refresh lists.", label);
        string unavailable = DetailerAvailability();
        GUILayout.Label(unavailable.Length == 0 ? "Required nodes and detector models are available on the connected server." : unavailable, label);
        GUILayout.Label("Each pass uses the selected model, encoder, VAE and LoRAs. EasyCache applies only to base generation. Detectors scan the full image; each detected face or object is detailed with its complete mask and crop. Anima needs a compatible Impact Pack version.", label);
        GUILayout.Label("Multi-class detector models process every detected label. Use a face-only detector for a pass intended only for faces.", label);
        bool controlsEnabled = GUI.enabled;
        int moveFrom = -1, moveTo = -1, remove = -1;
        string[] detectors = Workflow.Choices(info, "UltralyticsDetectorProvider", "model_name");
        for (int i = 0; i < detailers.Count; i++)
        {
            DetailerDraft d = detailers[i];
            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(d.Expanded ? "Hide" : "Edit", button, GUILayout.Width(85))) d.Expanded = !d.Expanded;
            GUILayout.Label("Pass " + (i + 1), title, GUILayout.Width(110));
            d.Name = GUILayout.TextField(d.Name, 128, field, GUILayout.MinWidth(250));
            bool detectorAvailable = detectors.Contains(d.Detector.Trim());
            GUI.enabled = controlsEnabled && (d.Enabled || (unavailable.Length == 0 && detectorAvailable));
            d.Enabled = GUILayout.Toggle(d.Enabled, " Enabled", GUILayout.Width(150));
            GUI.enabled = controlsEnabled && i > 0;
            if (GUILayout.Button("Up", button, GUILayout.Width(80))) { moveFrom = i; moveTo = i - 1; }
            GUI.enabled = controlsEnabled && i < detailers.Count - 1;
            if (GUILayout.Button("Down", button, GUILayout.Width(85))) { moveFrom = i; moveTo = i + 1; }
            GUI.enabled = controlsEnabled;
            if (GUILayout.Button("Remove", button, GUILayout.Width(110))) remove = i;
            GUILayout.EndHorizontal();
            if (unavailable.Length == 0 && !detectorAvailable)
                GUILayout.Label("This pass's detector is unavailable. Choose an installed detector before enabling or generating.", label);
            if (!d.Expanded) continue;
            d.Detector = PickRow("Detector model", d.Detector, "UltralyticsDetectorProvider", "model_name", v => d.Detector = v);
            GUILayout.BeginHorizontal(); Small("Steps", ref d.Steps); Small("CFG", ref d.Cfg); Small("Denoise", ref d.Denoise); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); Small("Guide size", ref d.GuideSize); Small("Max size", ref d.MaxSize); Small("Cycles", ref d.Cycles); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); Small("Detection", ref d.DetectionThreshold); Small("Dilation", ref d.Dilation); Small("Crop factor", ref d.CropFactor); Small("Feather", ref d.Feather); GUILayout.EndHorizontal();
            GUILayout.Label("Guide/max size control the crop's processing resolution. Detection is the confidence threshold; dilation and feather are pixels.", label);
            d.Sampler = PickRow("Sampler (blank = base setting)", d.Sampler, "FaceDetailer", "sampler_name", v => d.Sampler = v);
            d.Scheduler = PickRow("Scheduler (blank = base setting)", d.Scheduler, "FaceDetailer", "scheduler", v => d.Scheduler = v);
            GUILayout.BeginHorizontal();
            d.NoiseMask = GUILayout.Toggle(d.NoiseMask, " Limit noise to detected mask", GUILayout.Width(400));
            d.ForceInpaint = GUILayout.Toggle(d.ForceInpaint, " Force inpaint even without upscaling", GUILayout.Width(500));
            GUILayout.EndHorizontal();
            GUILayout.Label("Positive override (blank inherits the scene prompt; base style is always added)", label);
            d.Positive = GUILayout.TextArea(d.Positive, 32000, field, GUILayout.MinHeight(80));
            GUILayout.Label("Negative prompt override (blank inherits the base negative prompt)", label);
            d.Negative = GUILayout.TextArea(d.Negative, 32000, field, GUILayout.MinHeight(60));
        }
        // Mutate only after drawing all rows to keep controls and callbacks attached to the correct pass.
        if (remove >= 0) detailers.RemoveAt(remove);
        else if (moveFrom >= 0)
        {
            DetailerDraft pass = detailers[moveFrom]; detailers.RemoveAt(moveFrom); detailers.Insert(moveTo, pass);
        }
        GUILayout.Space(16);
        GUI.enabled = controlsEnabled && unavailable.Length == 0;
        if (GUILayout.Button("+ Add detailer", button, GUILayout.Width(260), GUILayout.Height(38)))
            detailers.Add(new DetailerDraft(new DetailerSetting
            {
                Name = "Detailer " + (detailers.Count + 1), Detector = detectors[0],
                Steps = int.TryParse(steps, out int baseSteps) ? baseSteps : 20,
                Cfg = double.TryParse(cfg, System.Globalization.NumberStyles.Float, Inv, out double baseCfg) ? baseCfg : 5
            }));
        GUI.enabled = controlsEnabled;
        if (GUILayout.Button("Save settings", button, GUILayout.Height(38))) Try(SaveSettings);
    }
}
