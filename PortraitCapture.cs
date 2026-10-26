using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal static class PortraitCapture
{
    private static readonly System.Reflection.FieldInfo? NpcClothedSprite = HarmonyLib.AccessTools.Field(typeof(NeuralNPC), "dialogSprite");
    // Called only on Unity's main thread. The HTTP worker receives PNG bytes, never Unity objects.
    internal static void Capture(GenerationSettings settings)
    {
        if (!settings.PortraitSettings.Enabled) return;
        settings.Portraits.Clear();
        settings.PortraitUploadNamespace = Plugin.PortraitUploadId.Value;
        if (settings.SceneCharacterKeys.Count == 0)
        {
            settings.PortraitNotice = "No scene participants are selected for this manual prompt; generated with text only.";
            return;
        }
        foreach (string key in settings.SceneCharacterKeys)
        {
            try
            {
                Plugin.Profiles.Profiles.TryGetValue(key, out CharacterProfile? profile);
                byte[] png;
                if (!string.IsNullOrWhiteSpace(profile?.PortraitFile)) png = ReadFile(profile!.PortraitFile);
                else
                {
                    Sprite? sprite = FindSprite(key, profile?.PortraitSource ?? PortraitSource.Current);
                    if (sprite == null) throw new InvalidOperationException("No portrait is available");
                    png = Encode(sprite.texture, sprite.textureRect);
                }
                if (png.Length > 8 * 1024 * 1024) throw new InvalidDataException("Portrait PNG exceeds 8 MB");
                settings.Portraits.Add(new PortraitReference { CharacterKey = key, Png = png });
            }
            catch (Exception ex)
            {
                // Missing artwork is not a reason to veto a user's generation. Avoid a partial
                // pair reference that would preferentially imprint the remaining character.
                settings.Portraits.Clear();
                settings.PortraitNotice = "Portrait reference unavailable for " + key + " (" + ex.GetType().Name + "). Generated with text only. Check the character's portrait source or file override.";
                return;
            }
        }
        settings.PortraitNotice = "Used " + settings.Portraits.Count + " selected character portrait reference(s).";
    }

    private static Sprite? FindSprite(string key, PortraitSource source)
    {
        Player player = Player.Instance;
        if (player != null && key == CharacterProfileStore.Key(player.playerName, true))
            return source == PortraitSource.Nude ? player.nudeDialogSprite : source == PortraitSource.Clothed ? player.clothedDialogSprite : player.GetDialogSprite();
        NeuralNPC? npc = NeuralNPC.neuralNPCs.Values.FirstOrDefault(n => n != null && CharacterProfileStore.Key(n.GetFinalName(), false) == key);
        if (npc == null) return null;
        return source == PortraitSource.Nude ? npc.dialogNudeSprite : source == PortraitSource.Clothed ? NpcClothedSprite?.GetValue(npc) as Sprite : npc.GetDialogSprite();
    }

    private static byte[] ReadFile(string path)
    {
        string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Plugin.PluginDirectory, path));
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length > 16 * 1024 * 1024) throw new InvalidDataException("Portrait file must exist and be no larger than 16 MB");
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            ComfyClient.ImageExtension(bytes); // Check PNG/JPEG dimensions before allocating the decoded image.
            if (!ImageConversion.LoadImage(image, bytes)) throw new InvalidDataException("Portrait is not a supported PNG or JPEG");
            return Encode(image, new Rect(0, 0, image.width, image.height));
        }
        finally { UnityEngine.Object.Destroy(image); }
    }

    private static byte[] Encode(Texture texture, Rect rect)
    {
        float ratio = Math.Min(1f, 1536f / Math.Max(rect.width, rect.height));
        int width = Math.Max(1, Mathf.RoundToInt(rect.width * ratio)), height = Math.Max(1, Mathf.RoundToInt(rect.height * ratio));
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D? readable = null;
        try
        {
            // Blit reads non-readable game textures and crops sprites from their texture atlas.
            Graphics.Blit(texture, target, new Vector2(rect.width / texture.width, rect.height / texture.height), new Vector2(rect.x / texture.width, rect.y / texture.height));
            RenderTexture.active = target;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readable.Apply(false, false);
            return ImageConversion.EncodeToPNG(readable);
        }
        finally
        {
            RenderTexture.active = previous;
            if (readable != null) UnityEngine.Object.Destroy(readable);
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
