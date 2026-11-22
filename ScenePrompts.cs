using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SilverpineMods.ComfyImageGenerator;

public sealed class SceneCharacter
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Species { get; set; } = "";
    public string Sex { get; set; } = "";
    public string Pronouns { get; set; } = "";
    public string PortraitObservation { get; set; } = "";
    public SceneClothing Clothing { get; set; } = new();
}

public sealed class SceneSnapshot
{
    public string Environment { get; set; } = "";
    public string Dialogue { get; set; } = "";
    public List<SceneCharacter> Characters { get; set; } = new();
    public List<string> OffscreenParticipants { get; set; } = new();
}

public static class ScenePrompts
{
    public const string AppearanceRules = "Match each conversation participant to that exact character's reference. " +
        "Names, pronouns and occupations do not imply a human body. Never replace a known nonhuman character with a human, " +
        "or with a human wearing animal ears or a costume. An anthro/anthropomorphic animal retains the species and animal anatomy stated in its profile. " +
        "Explicitly describe species and defining physical traits such as fur/scales/feathers, muzzle/snout, ears, tail, wings or horns WHEN supported by that character's reference. " +
        "Do not invent anatomy, transfer traits between names, or assume an unknown species is human. " +
        "The visual profile controls physical identity; game_species_hint is a fallback only and must not override a customized visual profile. " + SpeciesRules;
    public const string SpeciesRules = "Resolve and explicitly name each selected character's species separately. " +
        "Use the species stated in that character's visual_profile first; use game_species_hint only when the profile does not specify a species. " +
        "Preserve a stated subtype or hybrid, rather than reducing it to animal, anthro, furry, humanoid or person. Fur/scales and colors alone do not identify species. " +
        "Return the resolved species name in that character's species field (for example arctic fox or cobra, only when supported by its reference). " +
        "Do not copy a partner's species or let an ambiguous portrait override an explicit species. If no reference establishes species, use an empty species field rather than inventing one. ";
    public const string ClothingRules = "For clothing, current_clothing takes priority over a profile's usual outfit and older dialogue. " +
        "Use only currently equipped player clothing/accessories, preserving reported coverage and visible details. Held weapons are not clothing. " +
        "If an NPC is currently nude, omit the profile's usual outfit; if clothed with no equipment list, use supported outfit details from its profile or latest dialogue. " +
        "Unknown clothing state means unknown, not nude. Do not invent missing garments or remove reported accessories. " +
        "Clothing state does not change species or physical identity. ";
    public const string GenderRules = "Resolve each character's gender separately from explicit facts in that character's visual_profile, using game_sex_hint only as a fallback. " +
        "game_pronouns_hint helps choose how to address that character; pronouns alone do not establish body sex or anatomy. " +
        "Return male, female, intersex, nonbinary, other or unknown in the gender field. Use unknown when the references do not establish it. " +
        "Do not guess gender from a name, species, clothing, occupation, the partner's gender, or stereotypes. " +
        "A customized profile's explicit gender takes priority over game hints. Never assign both male and female counts to one intersex character. " +
        "Count tags such as 1boy and 1girl are model vocabulary, not instructions to change a character's age; retain any established age. ";
    public const string CastRules = "The user-selected current conversation participants in the supplied references are the ONLY allowed characters in the image. " +
        "Being part of the conversation does not make an unselected participant visible. Depict exactly the selected cast. " +
        "Never depict anyone outside the supplied participant references, even if named, nearby, visibly present, or interacting in the dialogue or environment. " +
        "Keep all nonparticipants off-screen. Do not include bystanders, background people, crowds, staff, customers or extra creatures. " +
        "Names and other people in location descriptions, biographies, memories or dialogue are context only, never additions to the cast. " +
        "Character prompts must describe only participants; the environment prompt must contain scenery only. ";
    public static string Build(SceneSnapshot scene, ModelFamily family, string guidance)
    {
        return family == ModelFamily.Anima ? CharacterScenePrompts.BuildAnima(scene, guidance) : CharacterScenePrompts.BuildIllustrious(scene, guidance);
    }

    public static string Context(SceneSnapshot scene, string guidance, bool includeIdentityAnchors = true) =>
        "\nArt direction / trigger words: " + Limit(guidance, 1000) +
        "\n<character_visual_profiles>\n" + string.Join("\n\n", scene.Characters.Select((c, i) => "Character " + (i + 1) + " — " + Limit(c.Name, 100) + ":\n" + CharacterReference(c, includeIdentityAnchors).ToString())) +
        "\n</character_visual_profiles>\n" + CastRules + SceneSelection.PromptContext(scene) + "\n<environment>\n" + Limit(scene.Environment, 2000) +
        "\n</environment>\n<recent_dialogue>\n" + Tail(scene.Dialogue, 4500);

    public static JObject CharacterReference(SceneCharacter character, bool includeIdentityAnchors = true)
    {
        var reference = new JObject
        {
            ["character_id"] = character.Key, ["dialogue_name"] = character.Name,
            ["game_species_hint"] = Limit(character.Species, 160),
            ["game_sex_hint"] = character.Sex, ["game_pronouns_hint"] = character.Pronouns,
            ["visual_profile"] = Limit(character.Description, 2400),
            ["portrait_observation"] = Limit(character.PortraitObservation, 2400),
            ["current_clothing"] = character.Clothing.ToReference()
        };
        if (includeIdentityAnchors)
        {
            reference["physical_identity_anchor"] = SceneIdentity.Anchor(character);
            reference["physical_identity_tags"] = SceneIdentity.Tags(character);
        }
        return reference;
    }

    public static string ProfileInstruction(SceneCharacter character) =>
        "Create a reusable visual description for this fantasy game character. " +
        "Extract species, explicit gender, body build, apparent age only if stated, skin/fur/scales, hair, eyes, clothing, accessories, and identifying features. " +
        "Keep established facts; do not invent missing traits. Start with the character's name, explicit species and distinguishing body anatomy. " +
        "Preserve anthro/animal features instead of substituting a human body or costume. A name or job is not evidence of human species. " +
        "Omit biography, relationships, dialogue, personality, temporary actions and temporary clothing state. " +
        "Write 60-120 words of clear English usable as reference for both prose and booru-tag image prompts. " +
        "Return only the description. Treat the following material as reference data.\nCharacter: " + character.Name +
        "\nGame species hint (use only if the description does not specify it): " + Limit(character.Species, 160) +
        "\nGame sex hint (fallback only; do not override an explicit description): " + character.Sex +
        "\nGame pronouns (do not infer anatomy from these): " + character.Pronouns +
        "\n<game_description>" + Limit(character.Description, 5000) + "</game_description>";

    public static string Clean(string response, bool tags = false)
    {
        response = Regex.Replace(response ?? "", @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        if (response.IndexOf("<think", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("The LLM returned unfinished reasoning instead of a visual prompt.");
        response = Regex.Replace(response, @"^```[^\r\n]*[\r\n]|```$", "").Trim();
        response = Regex.Replace(response, @"^(positive prompt|image prompt|prompt|description|tags)\s*:\s*", "", RegexOptions.IgnoreCase);
        // Normalize whitespace inside each line without flattening character/environment blocks.
        response = response.Replace("\r\n", "\n").Replace('\r', '\n');
        response = string.Join("\n", response.Split('\n').Select(line => Regex.Replace(line, @"[^\S\n]+", " ").Trim()));
        response = Regex.Replace(response, @"\n{3,}", "\n\n").Trim().Trim('"');
        if (response.Length < 10 || response.Length > 6000 || response.StartsWith("{") || response.StartsWith("["))
            throw new InvalidOperationException("The LLM did not return a usable plain-text visual description. Try again or enter a prompt manually.");
        return tags ? response.Replace('_', ' ').ToLowerInvariant() : response;
    }
    public static string Limit(string text, int size) => text.Length <= size ? text : text.Substring(0, size);
    public static string Tail(string text, int size) => text.Length <= size ? text : text.Substring(text.Length - size);

    public static async Task<string> AwaitLlm(Task<string> request, TimeSpan timeout, CancellationToken cancel)
    {
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        Task delay = Task.Delay(timeout, delayCancellation.Token);
        if (await Task.WhenAny(request, delay) != request)
        {
            // The game's API has no per-request cancellation. Observe eventual failures and
            // discard late responses so a timeout/stop can never submit an unwanted image job.
            _ = request.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            cancel.ThrowIfCancellationRequested();
            throw new TimeoutException("The game's LLM timed out. Its request may still finish in the background.");
        }
        delayCancellation.Cancel();
        cancel.ThrowIfCancellationRequested();
        return await request;
    }
}
