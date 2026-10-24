using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SilverpineMods.ComfyImageGenerator;

public sealed class CharacterProfile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "Manual";
}

public sealed class CharacterProfileStore
{
    private readonly string root;
    private Dictionary<string, CharacterProfile> profiles = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CharacterProfile> Profiles => profiles;
    public CharacterProfileStore(string root) { this.root = Path.GetFullPath(root); }
    public static string Key(string name, bool player) => (player ? "player:" : "npc:") + name;
    public string? Description(string key) => profiles.TryGetValue(key, out CharacterProfile? profile) ? profile.Description : null;
    public void LoadActive()
    {
        string path = Path.Combine(root, "active.json");
        if (File.Exists(path)) profiles = Read(path);
    }
    public void Set(string key, string name, string description, string source = "Manual")
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 8000) throw new ArgumentException("A character description must contain 1-8000 characters.");
        var updated = new Dictionary<string, CharacterProfile>(profiles, StringComparer.Ordinal)
        { [key] = new CharacterProfile { Name = name, Description = description.Trim(), Source = source } };
        WriteActive(updated);
        profiles = updated;
    }
    public void Reset(string key)
    {
        var updated = new Dictionary<string, CharacterProfile>(profiles, StringComparer.Ordinal);
        updated.Remove(key);
        WriteActive(updated);
        profiles = updated;
    }
    public string[] Presets() => Directory.Exists(Path.Combine(root, "Presets"))
        ? Directory.GetFiles(Path.Combine(root, "Presets"), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n).ToArray()
        : Array.Empty<string>();
    public void SavePreset(string name) => AtomicWrite(PresetPath(name), Serialize(profiles));
    public void LoadPreset(string name)
    {
        Dictionary<string, CharacterProfile> loaded = Read(PresetPath(name));
        // Keep the previous active set recoverable after an intentional preset load.
        AtomicWrite(Path.Combine(root, "before-preset-load.json"), Serialize(profiles));
        WriteActive(loaded);
        profiles = loaded;
    }
    private void WriteActive(Dictionary<string, CharacterProfile> values) => AtomicWrite(Path.Combine(root, "active.json"), Serialize(values));
    private static string Serialize(Dictionary<string, CharacterProfile> values) => new JObject
    { ["version"] = 1, ["characters"] = JObject.FromObject(values) }.ToString(Formatting.Indented);
    private string PresetPath(string name)
    {
        name = name.Trim();
        if (name.Length < 1 || name.Length > 80 || name.EndsWith(".") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name == "." || name == ".." || name.Contains("/") || name.Contains("\\"))
            throw new ArgumentException("Use a preset name of 1-80 characters without path separators or invalid filename characters.");
        string device = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(device))
            throw new ArgumentException("That preset name is reserved by Windows.");
        return Path.Combine(root, "Presets", name + ".json");
    }
    private static Dictionary<string, CharacterProfile> Read(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Character preset exceeds 4 MB.");
        JObject json = JObject.Parse(File.ReadAllText(path));
        if ((int?)json["version"] != 1 || !(json["characters"] is JObject characters)) throw new InvalidDataException("Unsupported character preset format.");
        var result = new Dictionary<string, CharacterProfile>(StringComparer.Ordinal);
        foreach (JProperty property in characters.Properties())
        {
            CharacterProfile? value = property.Value.ToObject<CharacterProfile>();
            if ((!property.Name.StartsWith("npc:", StringComparison.Ordinal) && !property.Name.StartsWith("player:", StringComparison.Ordinal)) ||
                value == null || string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Description) || value.Description.Length > 8000)
                throw new InvalidDataException("Invalid character profile: " + property.Name);
            result.Add(property.Name, value);
        }
        return result;
    }
    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
