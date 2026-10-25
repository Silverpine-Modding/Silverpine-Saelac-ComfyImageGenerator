using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static void SceneUnderstanding()
    {
        var player = new SceneCharacter { Key = "player:Rowan", Name = "Rowan", Description = "Rowan is an anthro arctic fox with white fur. Usually wears a blue tunic.",
            Clothing = new SceneClothing { State = "partly clothed", EquipmentKnown = true, UpperBodyExposed = true, LowerBodyExposed = false,
                Equipped = new() { new SceneWornItem { Slot = "Legs", Name = "Green trousers", Description = "Dark green linen trousers." },
                    new SceneWornItem { Slot = "Ring", Name = "Silver ring" } }, VisibleDetails = "Rowan's upper body is uncovered; trousers cover the lower body." } };
        var speaker = new SceneCharacter { Key = "npc:Lilian", Name = "Lilian", Species = "fox", Description = "Lilian is an orange anthro fox with green eyes. Usually wears a black dress.",
            Clothing = new SceneClothing { State = "nude", EquipmentKnown = false } };
        var gareth = new SceneCharacter { Key = "npc:Gareth", Name = "Gareth", Species = "wolf", Description = "Gareth is a black-furred anthro wolf with amber eyes and a muzzle. Usually wears a tavern keeper's apron.",
            Clothing = new SceneClothing { State = "clothed", EquipmentKnown = false, VisibleDetails = "Gareth dries a glass behind the bar." } };
        var absent = new SceneCharacter { Key = "npc:Robin", Name = "Robin", Description = "Robin is a grey wolf." };
        var scene = new SceneSnapshot { Characters = new() { player, speaker }, Dialogue = "Lilian: Gareth is polishing glasses behind the bar. Robin is away in another town.", Environment = "Gareth's candlelit tavern." };
        Check(SceneReferences.Mentions("GARETH's tavern", "Gareth") && SceneReferences.Mentions("Gareth’s tavern", "Gareth"), "case-insensitive names with both possessive apostrophes resolve");
        Check(!SceneReferences.Mentions("There is danger on the road.", "Dan") && !SceneReferences.Mentions("Garethson enters.", "Gareth"), "partial names do not attach another character's identity");
        Check(SceneReferences.Mentions("Dr. A+B waits.", "Dr. A+B"), "punctuation in a character name is treated literally");
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            string prompt = ScenePrompts.Build(scene, family, "soft light");
            Check(!prompt.Contains(gareth.Description) && !prompt.Contains(absent.Description) && !prompt.Contains("\"character_id\": \"npc:Gareth\"") && !prompt.Contains("named_bystander_references"), family + " never receives nonparticipant profiles even when mentioned and visibly present");
            Check(prompt.Contains("Green trousers") && prompt.Contains("upper_body_exposed") && prompt.Contains("\"state\": \"nude\""), family + " receives actual player gear/coverage and NPC clothing state");
            Check(prompt.Contains(ScenePrompts.CastRules) && prompt.Contains("ONLY allowed characters"), family + " receives the strict participant-only cast rule");
            Check(prompt.Contains(scene.Dialogue) && prompt.Contains(scene.Environment), family + " retains dialogue and location context without promoting their names into the cast");
        }
        JObject playerReference = ScenePrompts.CharacterReference(player), npcReference = ScenePrompts.CharacterReference(speaker);
        Check((string?)playerReference["visual_profile"] == player.Description && (string?)playerReference["current_clothing"]?["equipped_clothing_and_accessories"]?[0]?["item"] == "Green trousers", "saved physical appearance stays separate from live worn outfit");
        Check((bool?)playerReference["current_clothing"]?["upper_body_exposed"] == true && (bool?)playerReference["current_clothing"]?["lower_body_exposed"] == false, "partial coverage is not flattened into fully clothed or nude");
        Check((string?)npcReference["current_clothing"]?["state"] == "nude" && (bool?)npcReference["current_clothing"]?["equipment_list_known"] == false, "NPC flag is supplied without inventing an equipment inventory");
        Check(CharacterScenePrompts.AnimaSystemInstruction.Contains("current_clothing takes priority") && CharacterScenePrompts.AnimaSystemInstruction.Contains("Never replace a known nonhuman"), "ordinary generation prioritizes live clothing and established nonhuman identity");
        string profileRequest = ScenePrompts.ProfileInstruction(player);
        Check(!profileRequest.Contains("Green trousers") && !profileRequest.Contains("upper_body_exposed") && profileRequest.Contains("temporary clothing state"), "profile initialization does not persist transient equipment or undressing");
        Check(profileRequest.Contains(player.Name) && ScenePrompts.ProfileInstruction(gareth).Contains("Game species hint"), "first-use profile extraction binds name and species instead of human assumptions");
        Check(SceneReferences.PlainText("A <color=red>red</color> coat.") == "A red coat.", "equipment display markup removed without losing appearance text");
        var customized = new SceneCharacter { Name = "Gareth", Key = "npc:Gareth", Species = "wolf", Description = "An anthro raven with black feathers." };
        Check((string?)ScenePrompts.CharacterReference(customized)["visual_profile"] == customized.Description && ScenePrompts.AppearanceRules.Contains("must not override a customized visual profile"), "game species remains a fallback so user appearance overrides keep priority");
    }
}
