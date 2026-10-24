using System;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow
{
    private bool showRegionImage = true;
    private string selectedRegionId = "";
    private Vector2 regionLegendScroll;

    private void DrawRegionPreview()
    {
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        Small("Image width", ref width); Small("Image height", ref height);
        showRegionImage = GUILayout.Toggle(showRegionImage, " Overlay current image", GUILayout.Width(265));
        if (GUILayout.Button("Enlarge region map", button, GUILayout.Width(240), GUILayout.Height(38))) page = 6;
        GUILayout.EndHorizontal();
        DrawRegionCanvas(GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(430), GUILayout.ExpandWidth(true)));
        GUILayout.Label("The outlined frame is the entire output image. Colored boxes are attention areas, not separate image generations. Click a box or name to highlight it; edit its percentages below. Keep dividing lines away from faces. Narrow boxes do not force extra characters to appear.", label);
    }

    private void DrawExpandedRegions()
    {
        if (GUI.Button(new Rect(32, 80, 280, 40), "Back to region settings", button)) page = 4;
        showRegionImage = GUI.Toggle(new Rect(340, 86, 390, 32), showRegionImage, " Overlay current image");
        GUI.Label(new Rect(760, 84, 790, 36), "Region map — selected output resolution, origin at top left", label);
        DrawRegionCanvas(new Rect(32, 134, 1536, 640));
        RegionDraft? selected = regions.Find(r => r.Id == selectedRegionId);
        if (selected == null && regions.Count > 0) { selected = regions[0]; selectedRegionId = selected.Id; }
        GUILayout.BeginArea(new Rect(32, 790, 1536, 90));
        if (selected != null)
        {
            GUI.enabled = !GenerationService.Busy && !profileBusy;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Edit #" + selected.CharacterNumber, label, GUILayout.Width(130));
            Small("Left %", ref selected.X); Small("Top %", ref selected.Y);
            Small("Width %", ref selected.Width); Small("Height %", ref selected.Height);
            if (GUILayout.Button("Save settings", button, GUILayout.Width(240), GUILayout.Height(38))) Try(SaveSettings);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }
        GUILayout.Label("Click a box or name to edit its bounds here. Keep base attention boundaries away from faces. Detailing assigns each whole detection to one box and preserves surrounding context. Boxes cannot guarantee a second body.", label);
        GUILayout.EndArea();
    }

    private void DrawRegionCanvas(Rect area)
    {
        GUI.Box(area, GUIContent.none, panel);
        if (!int.TryParse(width, out int w) || !int.TryParse(height, out int h) || w < 64 || h < 64 || w > 8192 || h > 8192)
        { GUI.Label(new Rect(area.x + 12, area.y + 12, area.width - 24, 64), "Enter a valid image width and height to preview the regions.", label); return; }
        var small = new GUIStyle(label) { fontSize = 16, wordWrap = true };
        var caption = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
        float legendWidth = 340;
        Rect viewport = new(area.x + 54, area.y + 46, area.width - legendWidth - 80, area.height - 76);
        float scale = Math.Min(viewport.width / w, viewport.height / h);
        Rect canvas = new(viewport.x + (viewport.width - w * scale) / 2, viewport.y + (viewport.height - h * scale) / 2, w * scale, h * scale);
        FillRegionRect(canvas, new Color(.075f, .085f, .10f));
        bool matchingAspect = preview != null && (long)preview.width * h == (long)preview.height * w;
        if (showRegionImage && matchingAspect) GUI.DrawTexture(canvas, preview!, ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(area.x + 12, area.y + 8, area.width - legendWidth - 24, 30), $"Output: {w} × {h} px  |  top left = (0, 0)", small);
        for (int line = 1; line < 4; line++)
        {
            float x = canvas.x + canvas.width * line / 4, y = canvas.y + canvas.height * line / 4;
            FillRegionRect(new Rect(x, canvas.y, 1, canvas.height), new Color(1, 1, 1, .23f));
            FillRegionRect(new Rect(canvas.x, y, canvas.width, 1), new Color(1, 1, 1, .23f));
            GUI.Label(new Rect(x - 32, canvas.y - 24, 64, 22), (w * line / 4).ToString(Inv), caption);
            GUI.Label(new Rect(canvas.x - 48, y - 11, 46, 22), (h * line / 4).ToString(Inv), caption);
        }
        OutlineRegion(canvas, new Color(.8f, .85f, .9f), 2);
        GUI.Label(new Rect(canvas.xMax - 108, canvas.yMax + 3, 112, 24), $"({w}, {h})", caption);
        for (int i = 0; i < regions.Count; i++)
        {
            try
            {
                CharacterRegion r = regions[i].Read(); r.Validate(w, h, false);
                PixelRegion p = r.Pixels(w, h);
                Rect box = new(canvas.x + p.X * scale, canvas.y + p.Y * scale, p.Width * scale, p.Height * scale);
                Color color = RegionColor(i);
                FillRegionRect(box, new Color(color.r, color.g, color.b, showRegionImage && matchingAspect ? .09f : .2f));
                OutlineRegion(box, color, selectedRegionId == r.Id ? 5 : 2);
                Rect chip = new(box.x + 3, box.y + 3, Math.Min(box.width - 6, 55), 25);
                if (chip.width > 10) { FillRegionRect(chip, new Color(.035f, .045f, .06f, .96f)); GUI.Label(chip, "#" + regions[i].CharacterNumber, caption); }
                if (GUI.enabled && Event.current.type == EventType.MouseDown && Event.current.button == 0 && box.Contains(Event.current.mousePosition))
                { selectedRegionId = r.Id; Event.current.Use(); }
            }
            catch (ArgumentException) { /* Partial numeric edits are described in the legend. */ }
        }
        Rect legend = new(area.xMax - legendWidth, area.y + 8, legendWidth - 8, area.height - 16);
        GUI.BeginGroup(legend);
        string imageNote = !showRegionImage ? "Image overlay off" : preview == null ? "No generated image yet" : !matchingAspect
            ? $"Image is {preview.width} × {preview.height}; aspect differs. Showing blank frame to avoid stretching."
            : $"Overlay: {preview!.width} × {preview.height} image";
        GUI.Label(new Rect(8, 0, legend.width - 16, 62), imageNote, small);
        regionLegendScroll = GUI.BeginScrollView(new Rect(0, 66, legend.width, legend.height - 66), regionLegendScroll,
            new Rect(0, 0, legend.width - 24, Math.Max(legend.height - 70, regions.Count * 98)));
        for (int i = 0; i < regions.Count; i++)
        {
            RegionDraft draft = regions[i]; float y = i * 98;
            FillRegionRect(new Rect(4, y + 6, 6, 78), RegionColor(i));
            if (GUI.Button(new Rect(16, y, legend.width - 44, 30), (draft.Id == selectedRegionId ? "> " : "") + draft.Caption, button)) selectedRegionId = draft.Id;
            string description;
            try
            {
                CharacterRegion r = draft.Read(); r.Validate(w, h, false); PixelRegion p = r.Pixels(w, h);
                description = $"({p.X}, {p.Y}) → ({p.X + p.Width}, {p.Y + p.Height})\n{p.Width} × {p.Height} px";
                if (regions.Count > 1 && (r.Width < .3 || r.Height < .3)) description += " — narrow region";
            }
            catch (ArgumentException ex) { description = ex.Message; }
            GUI.Label(new Rect(16, y + 34, legend.width - 44, 60), description, small);
        }
        GUI.EndScrollView(); GUI.EndGroup();
    }

    private static Color RegionColor(int index) => Color.HSVToRGB((index * .29f + .52f) % 1, .62f, 1);
    private static void FillRegionRect(Rect rect, Color color)
    {
        Color previous = GUI.color; GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
    }
    private static void OutlineRegion(Rect rect, Color color, float thickness)
    {
        FillRegionRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        FillRegionRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        FillRegionRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        FillRegionRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}
