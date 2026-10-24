using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow
{
    private bool attentionCouple;
    private string globalRegionStrength = "0.3";
    private readonly List<RegionDraft> regions = new();
    private float EditorHeight => PromptEditorHeight + (attentionCouple ? regions.Count * 164 : 0);

    private sealed class RegionDraft
    {
        internal string Id, CharacterNumber, Name, CharacterKey, Prompt, X, Y, Width, Height, Strength, PromptStrength;
        internal Vector2 PromptScroll;
        internal RegionDraft(CharacterRegion r)
        {
            Id = r.Id; CharacterNumber = (r.CharacterIndex + 1).ToString(Inv); Name = r.Name; CharacterKey = r.CharacterKey; Prompt = r.Prompt;
            X = (r.X * 100).ToString(Inv); Y = (r.Y * 100).ToString(Inv); Width = (r.Width * 100).ToString(Inv); Height = (r.Height * 100).ToString(Inv); Strength = r.Strength.ToString(Inv);
            PromptStrength = r.PromptStrength.ToString(Inv);
        }
        internal string Caption => "Character " + CharacterNumber + (string.IsNullOrWhiteSpace(Name) ? "" : ": " + Name);
        internal CharacterRegion Read() => new()
        {
            Id = Id, CharacterIndex = Int(CharacterNumber, "Dialogue character number") - 1, Name = Name, CharacterKey = CharacterKey, Prompt = Prompt,
            X = Number(X, "Region left") / 100, Y = Number(Y, "Region top") / 100,
            Width = Number(Width, "Region width") / 100, Height = Number(Height, "Region height") / 100, Strength = Number(Strength, "Region strength"),
            PromptStrength = Number(PromptStrength, "Character prompt strength")
        };
    }

    private void LoadRegions(Profile profile)
    {
        attentionCouple = family == ModelFamily.Illustrious && profile.AttentionCouple.Value;
        globalRegionStrength = profile.GlobalRegionStrength.Value.ToString(Inv);
        regions.Clear();
        try
        {
            foreach (CharacterRegion region in JsonConvert.DeserializeObject<List<CharacterRegion>>(profile.RegionsJson.Value) ?? new()) regions.Add(new RegionDraft(region));
        }
        catch (Exception) { status = "The saved character regions are invalid. Rebuild them in Attention Couple settings."; }
        if (regions.Count == 0) regions.AddRange(RegionalPrompts.DefaultRegions().Select(r => new RegionDraft(r)));
        // Recover the last in-session prompt set as one unit when reopening the
        // viewer; a model-family switch must not import another family's prompts.
        lastPromptRevision = 0;
    }

    private string AttentionAvailability()
    {
        if (family != ModelFamily.Illustrious) return "Available only for Illustrious. Switch model family to use this mode.";
        if (!string.IsNullOrWhiteSpace(customWorkflow)) return "Clear the custom API workflow path to use built-in Attention Couple.";
        return NodeAvailability(Workflow.AttentionNodes);
    }

    private string RegionDetailerAvailability()
    {
        if (!attentionCouple) return "Enable Attention Couple to choose character targets.";
        string reason = AttentionAvailability();
        return reason.Length != 0 ? reason : NodeAvailability(Workflow.RegionDetailerNodes);
    }

    private void BindRegions(SceneSnapshot scene)
    {
        var values = regions.Select(r => r.Read()).ToList();
        RegionalPrompts.Bind(scene, values);
        for (int i = 0; i < values.Count; i++)
        {
            // Avoid showing a previous conversation's appearance under a new name.
            if (regions[i].CharacterKey != values[i].CharacterKey) regions[i].Prompt = "";
            regions[i].Name = values[i].Name; regions[i].CharacterKey = values[i].CharacterKey;
        }
    }

    private void UseDialogueRegions()
    {
        SceneSnapshot scene = SceneCapture.Capture(NeuralNPC.currentActiveDialogNeuralNPC);
        var old = regions.Select(r => r.Read()).ToList();
        var next = RegionalPrompts.DefaultRegions(scene.Characters.Count);
        for (int i = 0; i < next.Count; i++)
        {
            CharacterRegion? previous = old.FirstOrDefault(r => r.CharacterIndex == i);
            if (previous != null)
            {
                next[i].Id = previous.Id;
                if (old.Count == next.Count) { next[i].X = previous.X; next[i].Y = previous.Y; next[i].Width = previous.Width; next[i].Height = previous.Height; }
                next[i].Strength = previous.Strength;
                next[i].PromptStrength = previous.PromptStrength;
                if (previous.CharacterKey == scene.Characters[i].Key) next[i].Prompt = previous.Prompt;
            }
        }
        RegionalPrompts.Bind(scene, next);
        regions.Clear(); regions.AddRange(next.Select(r => new RegionDraft(r)));
        status = "Loaded current dialogue characters. " + (old.Count == next.Count ? "Kept existing rectangles." : "Arranged equal columns for the new character count.") + " Review detailer targets after removing characters.";
    }

    private void DrawRegions()
    {
        GUILayout.Label("Attention Couple / character regions", title);
        GUILayout.Label("Requires AttentionCouplePPM and LatentToMaskBB from ComfyUI-ppm, plus MaskComposite and ConditioningSetAreaStrength from ComfyUI core. Install on the ComfyUI server, restart it, then Connect / refresh lists.", label);
        string unavailable = AttentionAvailability();
        GUILayout.Label(unavailable.Length == 0 ? "Required Attention Couple nodes are installed." : unavailable, label);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && (attentionCouple || unavailable.Length == 0);
        bool wasCoupled = attentionCouple;
        attentionCouple = GUILayout.Toggle(attentionCouple, " Enable Attention Couple for Illustrious");
        if (wasCoupled && !attentionCouple)
        {
            foreach (DetailerDraft d in detailers.Where(d => d.RegionId.Length != 0)) d.Enabled = false;
            status = "Attention Couple off. Character-targeted detailer passes were disabled; their settings are preserved.";
        }
        GUI.enabled = enabled;
        DrawRegionPreview();
        GUILayout.Label("Shared prompt = environment, lighting, subject counts and composition. Each rectangle receives that shared context plus its own character and base style. Keep character traits out of the shared prompt and base style.", label);
        GUILayout.Label("Put traits intended for only one character in that character's prompt. The base style applies to EVERY region. Character identity is encoded before shared scenery; duplicate tags are removed. LoRAs also affect every region.", label);
        GUILayout.Label("Character numbers follow the current dialogue's reference order (player first, then NPCs). They are reassigned when generating a new dialogue scene. Only listed regions are included; use the button below to include every participant.", label);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Use current dialogue characters", button, GUILayout.Height(38))) Try(UseDialogueRegions);
        if (GUILayout.Button("Arrange equal columns", button, GUILayout.Height(38)))
            for (int i = 0; i < regions.Count; i++) { regions[i].X = (100.0 * i / regions.Count).ToString(Inv); regions[i].Y = "0"; regions[i].Width = (100.0 / regions.Count).ToString(Inv); regions[i].Height = "100"; }
        GUILayout.EndHorizontal();
        globalRegionStrength = Row("Background scene strength (0.01-1)", globalRegionStrength);
        GUILayout.Label("Each region receives its own identity plus the scene's full character count and layout. The background mask fills uncovered gaps. Whole-image detailer passes automatically process each character region separately in this mode.", label);
        GUILayout.Label("Rectangles use percentages from the top left. Bounds snap to 8 pixels and must cover at least 64 x 64 pixels. Strength controls relative influence where regions overlap; disjoint regions each control their own area. Bounds guide composition and cannot guarantee separate bodies.", label);
        GUILayout.Label("Mask weight only balances overlapping regions; raising it does not strengthen an isolated box. Prompt strength changes the actual regional conditioning (1 = unchanged, range 0.1-2). Try small changes with the same seed; high values can distort anatomy. It does not change detailer strength.", label);
        int remove = -1;
        for (int i = 0; i < regions.Count; i++)
        {
            RegionDraft r = regions[i];
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((selectedRegionId == r.Id ? "> " : "") + r.Caption, button)) selectedRegionId = r.Id;
            if (GUILayout.Button("Remove region", button, GUILayout.Width(180))) remove = i;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); Small("Character #", ref r.CharacterNumber); Small("Mask weight", ref r.Strength); Small("Prompt strength", ref r.PromptStrength); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); Small("Left %", ref r.X); Small("Top %", ref r.Y); Small("Width %", ref r.Width); Small("Height %", ref r.Height); GUILayout.EndHorizontal();
            GUILayout.Label("Character prompt (filled by scene generation; directly editable for regeneration)", label);
            r.Prompt = ScrollablePrompt(GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(105), GUILayout.ExpandWidth(true)), r.Prompt, ref r.PromptScroll);
        }
        if (remove >= 0) regions.RemoveAt(remove);
        GUI.enabled = enabled && regions.Count < 9;
        if (GUILayout.Button("+ Add character region", button, GUILayout.Height(38)))
        {
            int index = Enumerable.Range(1, 9).First(n => !regions.Any(r => r.CharacterNumber == n.ToString(Inv))) - 1;
            regions.Add(new RegionDraft(new CharacterRegion { CharacterIndex = index, X = .5 }));
        }
        GUI.enabled = enabled;
        GUILayout.Label("Character-specific detailing needs Impact Pack (DetailerForEach, BboxDetectorSEGS / SegmDetectorSEGS, MaskToSEGS, ImpactSEGSIntersectionFilter, ImpactCount_Elts_in_SEGS), Impact Subpack (UltralyticsDetectorProvider), a detector model and core PreviewAny. No additional Silverpine node or server files are required. Refresh lists after installing missing dependencies.", label);
        string detailing = RegionDetailerAvailability();
        GUILayout.Label(detailing.Length == 0 ? "Whole-face assignment nodes installed. Detector availability is shown in Detailers." : detailing, label);
        if (GUILayout.Button("Save settings", button, GUILayout.Height(38))) Try(SaveSettings);
    }

    private void DrawDetailerTarget(DetailerDraft d)
    {
        RegionDraft? selected = regions.FirstOrDefault(r => r.Id == d.RegionId);
        GUILayout.Label("Target: " + (d.RegionId.Length == 0 ? (attentionCouple ? "All character regions (separate passes)" : "Whole image (all detections)") : selected?.Caption ?? "Missing region — select another target"), label);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(attentionCouple ? "All character regions" : "Whole image", button, GUILayout.Width(260))) d.RegionId = "";
        bool enabled = GUI.enabled;
        string unavailable = RegionDetailerAvailability();
        GUI.enabled = enabled && unavailable.Length == 0 && regions.Count != 0;
        if (GUILayout.Button("Choose character region", button, GUILayout.Width(330)))
        {
            RegionDraft[] targets = regions.ToArray();
            string[] labels = targets.Select((r, i) => (i + 1) + ". " + r.Caption).ToArray();
            ShowChoices("Detailer character region", labels, value => d.RegionId = targets[Array.IndexOf(labels, value)].Id);
        }
        GUI.enabled = enabled;
        GUILayout.EndHorizontal();
        if (unavailable.Length != 0) GUILayout.Label(unavailable, label);
        GUILayout.Label("Each pass detects once on the full image. At least half a detection's mask must be inside a character box; earlier characters take priority when boxes overlap or a face lies exactly on a boundary. Selected detections are removed before the next character, preventing repeated detailing. The entire face mask and padded crop stay intact. Keep faces mostly inside their boxes. Status shows detected/selected counts; assignment uses position, not identity recognition.", label);
    }
}
