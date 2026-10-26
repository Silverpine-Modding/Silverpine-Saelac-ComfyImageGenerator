using Newtonsoft.Json;
using System;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow
{
    private bool portraitEnabled;
    private string portraitAdapter = "", portraitVision = "", portraitStrength = "", portraitStart = "0", portraitEnd = "1", portraitSize = "512";
    private PortraitSource characterPortraitSource;
    private string characterPortraitFile = "";

    private void LoadPortraitSettings(Profile profile)
    {
        PortraitReferenceSettings s = PortraitReferenceSettings.Default(family);
        try { s = JsonConvert.DeserializeObject<PortraitReferenceSettings>(profile.PortraitJson.Value) ?? s; }
        catch (Exception) { status = "The saved portrait settings are invalid. Review Portrait references and Save settings."; }
        portraitEnabled = s.Enabled; portraitAdapter = s.Adapter; portraitVision = s.ClipVision;
        portraitStrength = s.Strength.ToString(Inv); portraitStart = s.Start.ToString(Inv); portraitEnd = s.End.ToString(Inv);
        portraitSize = s.ReferenceSize.ToString(Inv);
    }
    private PortraitReferenceSettings ReadPortraitSettings() => new()
    {
        Enabled = portraitEnabled, Adapter = portraitAdapter.Trim(), ClipVision = portraitVision.Trim(),
        Strength = portraitEnabled ? Number(portraitStrength, "Portrait strength") : double.TryParse(portraitStrength, System.Globalization.NumberStyles.Float, Inv, out double weight) ? weight : .5,
        ReferenceSize = portraitEnabled && family == ModelFamily.Anima ? Int(portraitSize, "Reference image size") : int.TryParse(portraitSize, out int size) ? size : 512,
        Start = portraitEnabled && family == ModelFamily.Illustrious ? Number(portraitStart, "Portrait start") : 0,
        End = portraitEnabled && family == ModelFamily.Illustrious ? Number(portraitEnd, "Portrait end") : 1
    };
    private void DrawPortraitSettings()
    {
        GUILayout.Label("Portrait references  /  " + family, title);
        GUILayout.Label("Optional IP-Adapter guidance from the selected dialogue participants only. Anima supports one or two; Illustrious supports one. The prompt controls poses and positions. References can also influence clothing, style and composition.", label);
        GUILayout.BeginHorizontal(); DrawFamilyButton(ModelFamily.Illustrious); DrawFamilyButton(ModelFamily.Anima); GUILayout.EndHorizontal();
        bool anima = family == ModelFamily.Anima;
        GUILayout.Label(anima
            ? "Requires ComfyUI-Anima_IP-Adapter, the included AnimaCompatibility update, an Anima adapter in models/ipadapter, and SigLIP2 in models/siglip2. See the bundled README for install commands. The update preserves ordinary LoRAs and maps the tested 28-block adapter into NovaFurry's 40-block model."
            : "Requires ComfyUI_IPAdapter_plus: IPAdapterModelLoader and IPAdapterAdvanced; core CLIPVisionLoader; an SDXL IP-Adapter in models/ipadapter and its matching encoder in models/clip_vision. Recommended pair: ip-adapter_sdxl_vit-h.safetensors + clip-vision_vit-h.safetensors. FaceID adapters are not supported by this workflow.", label);
        GUILayout.Label("Core image nodes: LoadImage, GetImageSize, EmptyImage, InvertMask, ImageCompositeMasked, ResizeAndPadImage. Install dependencies on the ComfyUI server, restart it, then refresh lists.", label);
        bool canConnect = GUI.enabled;
        GUI.enabled = canConnect && connectionTask == null;
        if (GUILayout.Button("Connect / refresh lists", button, GUILayout.Height(38))) Connect();
        GUI.enabled = canConnect;
        portraitAdapter = PickRow("IP-Adapter model", portraitAdapter, anima ? "AnimaIPAdapterLoader" : "IPAdapterModelLoader", anima ? "ip_adapter_name" : "ipadapter_file", v => portraitAdapter = v);
        if (!anima) portraitVision = PickRow("CLIP vision encoder", portraitVision, "CLIPVisionLoader", "clip_name", v => portraitVision = v);
        string unavailable = NodeAvailability(PortraitRequirements.Nodes(family));
        if (unavailable.Length == 0) unavailable = PortraitRequirements.Unavailable(info, family, new PortraitReferenceSettings { Adapter = portraitAdapter.Trim(), ClipVision = portraitVision.Trim() });
        if (unavailable.Length == 0 && !string.IsNullOrWhiteSpace(customWorkflow)) unavailable = "Clear the custom workflow path to use the built-in portrait option.";
        GUILayout.Label(unavailable.Length == 0 ? "Required nodes and selected adapter files are available." : unavailable, label);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && (portraitEnabled || unavailable.Length == 0);
        portraitEnabled = GUILayout.Toggle(portraitEnabled, " Use character portrait references (experimental)", GUILayout.Height(34));
        GUI.enabled = enabled;
        portraitStrength = Row("Reference strength", portraitStrength);
        if (anima)
        {
            portraitSize = Row("Reference image size (pixels)", portraitSize);
            GUILayout.Label("512 is the default encoder size. Try 768 or 1024 for more portrait detail; improvement varies and larger sizes use more GPU memory and time. Range: 224-2048, multiples of 16. This changes reference encoding, not the output image resolution.", label);
        }
        if (!anima)
        {
            portraitStart = Row("Start fraction (0-1)", portraitStart);
            portraitEnd = Row("End fraction (0-1)", portraitEnd);
        }
        GUILayout.Label(anima ? "Start at 0.2; 0.2-0.5 were tested locally. Each portrait is encoded separately, without a shared reference sheet, output regions or masks. The prompt controls positions. References can still mix smaller features; higher strength is not always better."
            : "Start at 0.5 and adjust for your checkpoint. Earlier end fractions can leave later sampling freer to follow the scene prompt. The adapter and vision encoder must be a matching SDXL pair.", label);
        GUILayout.Label("Select current, clothed or nude game art, or an image override, in Character profiles & presets, then Save character profile. Current uses the game's live dialogue sprite, including compatible portrait mods. Missing artwork falls back to text-only generation with a notice.", label);
        GUILayout.Label("Portrait PNGs are uploaded to two reusable slots in ComfyUI's temp folder. They are not saved in the input/output folders. Detailer crops use the normal model and LoRAs without portrait guidance.", label);
        if (GUILayout.Button("Save settings", button, GUILayout.Height(40))) Try(SaveSettings);
    }
    private void LoadCharacterPortrait(string key)
    {
        Plugin.Profiles.Profiles.TryGetValue(key, out CharacterProfile? profile);
        characterPortraitSource = profile?.PortraitSource ?? PortraitSource.Current;
        characterPortraitFile = profile?.PortraitFile ?? "";
    }
    private void DrawCharacterPortrait()
    {
        GUILayout.Label("Portrait source for optional IP-Adapter guidance", title);
        GUILayout.BeginHorizontal();
        foreach (PortraitSource source in Enum.GetValues(typeof(PortraitSource)))
            if (GUILayout.Button((source == characterPortraitSource ? "Selected: " : "") + source, button, GUILayout.Height(34))) characterPortraitSource = source;
        GUILayout.EndHorizontal();
        characterPortraitFile = Row("Image override (PNG/JPEG path)", characterPortraitFile);
        GUILayout.Label("Leave blank to use game art. A relative path is resolved inside the plugin folder. Portrait choices and file paths are saved with this profile and its presets; presets do not embed image files. Save character profile to apply.", label);
    }
}
