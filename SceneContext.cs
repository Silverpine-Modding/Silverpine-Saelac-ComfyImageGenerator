using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SilverpineMods.ComfyImageGenerator;

public sealed class SceneClothing
{
    public string State { get; set; } = "unknown";
    public bool EquipmentKnown { get; set; }
    public bool? UpperBodyExposed { get; set; }
    public bool? LowerBodyExposed { get; set; }
    public List<SceneWornItem> Equipped { get; set; } = new();
    public string VisibleDetails { get; set; } = "";

    public JObject ToReference() => new()
    {
        ["state"] = State, ["equipment_list_known"] = EquipmentKnown,
        ["upper_body_exposed"] = UpperBodyExposed, ["lower_body_exposed"] = LowerBodyExposed,
        ["equipped_clothing_and_accessories"] = new JArray(Equipped.Select(item => new JObject
        {
            ["slot"] = item.Slot, ["item"] = ScenePrompts.Limit(item.Name, 120), ["description"] = ScenePrompts.Limit(item.Description, 240)
        })),
        ["current_visible_details"] = ScenePrompts.Limit(VisibleDetails, 2400)
    };
}

public sealed class SceneWornItem
{
    public string Slot { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public static class SceneReferences
{
    public static bool Mentions(string text, string name) => !string.IsNullOrWhiteSpace(name) &&
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(name.Trim()) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string PlainText(string text) => Regex.Replace(text ?? "", @"<[^>]+>", "");
}

public static class SceneIdentity
{
    // Copy physical sentences from the selected profile, not an LLM paraphrase.
    // Outfit/state sentences must stay out: live clothing takes precedence.
    private const string Physical = @"\b(?:anthro(?:pomorphic)?|human|male|female|species|body|build|physique|fur|furred|furry|scales|scaled|feathers|feathered|muzzle|snout|beak|ears|tail|wings|horns|claws|paws|digitigrade|plantigrade|legs|feet|heels|hocks|eyes|hair|skin|dragoness|dragonfolk)\b";
    private const string OutfitOrState = @"\b(?:wear(?:s|ing)?|wore|worn|dressed|outfit|attire|clothing|clothed|unclothed|nude|naked|undressed|uncovered|exposed|shirt|dress|trousers|pants|apron|tunic|armor|armour|boots|stockings|socks|hat|corset|skirt|jacket|gloves|bracers|accessories)\b";

    public static string Anchor(SceneCharacter character)
    {
        string description = SceneReferences.PlainText(character.Description).Trim();
        var sentences = Regex.Split(description, @"(?<=[.!?])\s+|[\r\n]+").Select(s =>
            Regex.Split(s, @"(?:,\s*|\s+)(?:(?:who|usually|typically)\s+)?(?:wears|wearing|dressed|clothed)\b", RegexOptions.IgnoreCase)[0].Trim());
        var physical = sentences.Where(s => Regex.IsMatch(s, Physical, RegexOptions.IgnoreCase) ||
                (!string.IsNullOrWhiteSpace(character.Species) && SceneReferences.Mentions(s, character.Species)))
            .Where(s => !Regex.IsMatch(s, OutfitOrState, RegexOptions.IgnoreCase)).ToList();
        // Do not cut a sentence midway or append an outdated game species to a
        // customized profile. A missing anchor is handled by the normal reference.
        var bounded = new List<string>();
        foreach (string sentence in physical)
        {
            if (sentence.Length + bounded.Sum(s => s.Length + 1) > 1200) continue;
            bounded.Add(Regex.IsMatch(sentence, @"[.!?]$") ? sentence : sentence + ".");
        }
        string anchor = string.Join(" ", bounded);
        if (anchor.Length == 0 && description.Length == 0 && !string.IsNullOrWhiteSpace(character.Species))
            anchor = character.Species.Trim() + ".";
        return Regex.Replace(anchor, @"\banthro\b", "anthropomorphic", RegexOptions.IgnoreCase);
    }

    public static bool IsExplicitlyAnthro(string anchor) => Regex.IsMatch(anchor,
        @"\b(?:anthropomorphic|dragoness|dragonfolk)\b", RegexOptions.IgnoreCase);

    public static string Tags(SceneCharacter character)
    {
        string anchor = Anchor(character).ToLowerInvariant();
        var tags = new List<string>();
        if (IsExplicitlyAnthro(anchor)) tags.Add("anthro");
        // Retain the species phrase stated immediately after 'anthro', including
        // custom species/breeds, without guessing from the character's name.
        Match species = Regex.Match(anchor, @"\banthropomorphic\s+([^.,;:!?]+)");
        if (species.Success)
        {
            string phrase = Regex.Split(species.Groups[1].Value, @"\s+\b(?:with|who|has|have|having|is|are|in|and|standing|wearing)\b")[0].Trim();
            if (phrase.Length > 0 && phrase.Length <= 60) tags.Add(phrase);
        }
        else if (Regex.IsMatch(anchor, @"\b(?:dragoness|dragonfolk)\b")) tags.Add("dragon");
        else if (!string.IsNullOrWhiteSpace(character.Species) && SceneReferences.Mentions(anchor, character.Species))
            tags.Add(character.Species.Trim().ToLowerInvariant());
        foreach (string trait in new[] { "male", "female", "human", "muzzle", "snout", "beak", "tail", "wings", "horns", "claws", "paws", "digitigrade", "fur", "scales", "feathers" })
            if (Regex.IsMatch(anchor, @"\b" + trait + @"\b") && !Regex.IsMatch(anchor, @"\b(?:no|without|lacks?|missing)\s+(?:visible\s+)?" + trait + @"\b")) tags.Add(trait);
        const string colors = @"(?:black|white|grey|gray|silver|orange|red|reddish|brown|blue|green|pink|rose|cream|golden|gold|amber|yellow|russet|beige|tawny|navy|purple)";
        const string features = @"(?:fur|scales|feathers|eyes|hair|skin|muzzle|tail|wings)";
        foreach (Match color in Regex.Matches(anchor, @"\b(" + colors + @"(?:-" + colors + @")?)\s+(" + features + @")\b")) tags.Add(color.Value);
        foreach (Match color in Regex.Matches(anchor, @"\b(" + colors + @")-furred\b")) tags.Add(color.Groups[1].Value + " fur");
        foreach (Match color in Regex.Matches(anchor, @"\b(" + features + @")\s+(?:is|are)\s+(?:(?:predominantly|bright|deep|striking|a|an)\s+)*?(" + colors + @"(?:-" + colors + @")?)\b"))
            tags.Add(color.Groups[2].Value + " " + color.Groups[1].Value);
        return string.Join(", ", tags.Distinct(StringComparer.Ordinal));
    }

    public static string AddTags(SceneCharacter character, string generated, bool solo = false)
    {
        string anchor = Anchor(character);
        if (IsExplicitlyAnthro(anchor) && Regex.IsMatch(generated, @"(?<!non-)(?<!non )\bhuman(?:s)?\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("The scene prompt replaced " + character.Name + " with a human.");
        return string.Join(", ", (solo ? new[] { "solo" } : Array.Empty<string>()).Concat(Tags(character).Split(','))
            .Concat(generated.Split(',')).Select(t => t.Trim()).Where(t => t.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string SubjectCount(int count) => count == 1 ? "solo" : count == 2 ? "duo, two distinct characters, separate bodies" : count + " distinct characters, separate bodies";
}
