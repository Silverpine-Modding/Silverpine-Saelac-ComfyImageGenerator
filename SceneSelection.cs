using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SilverpineMods.ComfyImageGenerator;

// Cast selection is resolved before profile initialization or any LLM request.
// Free-form prompt edits remain the user's responsibility; no anatomy or prose
// heuristics are used to infer membership.
public static class SceneSelection
{
    public static int Maximum(ModelFamily family) => family switch
    {
        ModelFamily.Anima => 2,
        ModelFamily.Illustrious => 1,
        _ => throw new ArgumentException("Choose Illustrious or Anima.")
    };

    public static SceneSnapshot Select(SceneSnapshot dialogue, IEnumerable<string> selectedKeys, ModelFamily family)
    {
        var keys = selectedKeys.ToList();
        ValidateKeys(keys, family, false);
        var participants = dialogue.Characters.GroupBy(c => c.Key, StringComparer.Ordinal).Select(g => g.First()).ToList();
        if (keys.Any(key => !participants.Any(c => c.Key == key)))
            throw new ArgumentException("A selected character is no longer in this conversation. Refresh the character list and choose again.");
        return new SceneSnapshot
        {
            Environment = dialogue.Environment, Dialogue = dialogue.Dialogue,
            // Keep reference order stable, independent of the order of clicks.
            Characters = participants.Where(c => keys.Contains(c.Key)).Select(c => new SceneCharacter
            {
                Key = c.Key, Name = c.Name, Description = c.Description, Species = c.Species,
                Clothing = new SceneClothing
                {
                    State = c.Clothing.State, EquipmentKnown = c.Clothing.EquipmentKnown,
                    UpperBodyExposed = c.Clothing.UpperBodyExposed, LowerBodyExposed = c.Clothing.LowerBodyExposed,
                    VisibleDetails = c.Clothing.VisibleDetails,
                    Equipped = c.Clothing.Equipped.Select(w => new SceneWornItem { Slot = w.Slot, Name = w.Name, Description = w.Description }).ToList()
                }
            }).ToList(),
            OffscreenParticipants = participants.Where(c => !keys.Contains(c.Key)).Select(c => c.Name).ToList()
        };
    }

    public static void Validate(SceneSnapshot scene, ModelFamily family) =>
        ValidateKeys(scene.Characters.Select(c => c.Key).ToList(), family, false);

    private static void ValidateKeys(IList<string> keys, ModelFamily family, bool allowManual)
    {
        int max = Maximum(family);
        if ((!allowManual && keys.Count == 0) || keys.Count > max)
            throw new ArgumentException(family == ModelFamily.Anima ? "Choose one or two conversation participants for Anima." : "Choose exactly one conversation participant for Illustrious.");
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new ArgumentException("Each selected character must have a unique profile ID.");
    }

    public static void ValidateGeneration(GenerationSettings settings)
    {
        ValidateKeys(settings.SceneCharacterKeys, settings.Family, true);
    }

    public static string PromptContext(SceneSnapshot scene) =>
        "\nVisible cast: exactly " + scene.Characters.Count + " selected character(s): " + JsonConvert.SerializeObject(scene.Characters.Select(c => c.Name)) + ". " +
        "Depict every selected character once, with one complete body each. Frame the image around only this cast. " +
        "Other conversation participants must stay entirely outside the image, including their faces, limbs, silhouettes and reflections. " +
        "For dialogue interactions with an unselected person, depict only the selected character's side of the interaction; keep the partner off-screen. " +
        "Do not copy an unselected person's appearance into a selected character's prompt. " +
        "\nOff-screen conversation participants (names are context only): " + JsonConvert.SerializeObject(scene.OffscreenParticipants) + "\n";

}
