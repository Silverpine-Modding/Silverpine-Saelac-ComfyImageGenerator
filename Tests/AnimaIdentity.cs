using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static void AnimaIdentity()
    {
        var player = new SceneCharacter { Key = "player:Rowan", Name = "Rowan", Description = "An anthro arctic fox with white fur. His face has a pointed muzzle and blue eyes. He usually wears a blue tunic. His tail is fluffy." };
        var npc = new SceneCharacter { Key = "npc:Lilian", Name = "Lilian", Species = "fox", Description = "Lilian is an orange anthro fox. Her eyes are green. She wears a black dress." };
        var side = new SceneCharacter { Key = "npc:Gareth", Name = "Gareth", ObservedNearby = true, Description = "A black-furred anthro wolf. His eyes are amber." };
        var scene = new SceneSnapshot { Characters = new() { player, npc }, ReferencedCharacters = new() { side }, Dialogue = "Rowan: Hello, Lilian.", Environment = "A quiet tavern." };
        var response = new JObject { ["characters"] = new JArray(
            new JObject { ["character_id"] = npc.Key, ["prompt"] = "She stands on the right, smiling and waving." },
            new JObject { ["character_id"] = player.Key, ["prompt"] = "He sits on the left in green trousers, leaning forward." }),
            ["environment"] = "Warm candlelight fills the tavern, with a stone hearth behind the table." };
        string prompt = CharacterScenePrompts.Apply(response.ToString(), scene);
        string[] blocks = prompt.Split("\n\n");
        Check(blocks.Length == 3 && blocks[0].StartsWith("Rowan:") && blocks[1].StartsWith("Lilian:"), "structured Anima response is assembled in reference order even when LLM rows are reversed");
        Check(blocks[0].Contains("anthropomorphic arctic fox") && blocks[0].Contains("pointed muzzle") && blocks[0].Contains("tail is fluffy"), "Anima retains physical identity even when LLM only returns pronouns and actions");
        Check(blocks[1].Contains("orange anthropomorphic fox") && blocks[1].Contains("green"), "Anima anchors each character to its own species and appearance");
        Check(!prompt.Contains("blue tunic") && !prompt.Contains("black dress") && prompt.Contains("green trousers"), "fixed identity excludes usual outfits while preserving current generated clothing");
        Check(!prompt.Contains("Gareth") && !CharacterScenePrompts.BuildAnima(scene, "").Contains(side.Description), "unmentioned nearby character cannot enter Anima through identity anchors or lookup context");
        string illustrious = CharacterScenePrompts.Apply(response.ToString(), scene, ModelFamily.Illustrious);
        string[] tagBlocks = illustrious.Split("\n\n");
        Check(tagBlocks[0].StartsWith("anthro, arctic fox") && tagBlocks[0].Contains("white fur") && tagBlocks[0].Contains("muzzle"), "Illustrious forcibly retains per-character species, surface and anatomy tags");
        Check(tagBlocks[1].StartsWith("anthro, fox") && !tagBlocks[1].Contains("arctic fox") && !illustrious.Contains("blue tunic"), "Illustrious keeps identities isolated and excludes profile outfits from fixed tags");
        Check(tagBlocks[2].StartsWith("duo, two distinct characters, separate bodies") && !illustrious.Contains("Gareth"), "Illustrious composition counts only selected characters");
        var regions = RegionalPrompts.DefaultRegions(); RegionalPrompts.Bind(scene, regions);
        var regional = new JObject { ["global"] = "tavern, warm light", ["characters"] = new JArray(
            new JObject { ["region"] = regions[1].Id, ["prompt"] = "waving, standing on right" },
            new JObject { ["region"] = regions[0].Id, ["prompt"] = "sitting on left, green trousers" }) };
        string global = RegionalPrompts.Apply(regional.ToString(), regions, scene);
        Check(regions[0].Prompt.StartsWith("anthro, arctic fox") && regions[1].Prompt.StartsWith("anthro, fox") && global.StartsWith("duo"), "Attention Couple receives fixed identity tags without conflicting solo instructions");
        var invented = (JObject)response.DeepClone();
        ((JArray)invented["characters"]!).Add(new JObject { ["character_id"] = side.Key, ["prompt"] = "He stands behind the bar polishing a glass." });
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(invented.ToString(), scene), "Anima rejects even a real nearby NPC ID if the scene did not mention it");
        scene.Dialogue += " Gareth is polishing a glass behind the bar.";
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(invented.ToString(), scene, family), family + " rejects a named present bystander even when a legacy profile is supplied");
            var replacement = (JObject)response.DeepClone(); replacement["characters"]![0]!["character_id"] = side.Key;
            Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(replacement.ToString(), scene, family), family + " rejects replacing a participant with a mentioned bystander at the same cast count");
            Check(!CharacterScenePrompts.Apply(response.ToString(), scene, family).Contains("Gareth"), family + " assembles only the conversation participants");
        }
        var human = (JObject)response.DeepClone();
        human["characters"]![0]!["prompt"] = "A human woman stands on the right, smiling.";
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(human.ToString(), scene), "Anima rejects an explicit human replacement for an anthro character before submitting an image");
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(human.ToString(), scene, ModelFamily.Illustrious), "Illustrious rejects the same human substitution");
        regional["characters"]![0]!["prompt"] = "human woman, smiling";
        string preservedRegion = regions[0].Prompt;
        Throws<InvalidOperationException>(() => RegionalPrompts.Apply(regional.ToString(), regions, scene), "Attention Couple rejects human substitutions before committing any region prompts");
        Check(regions[0].Prompt == preservedRegion, "failed regional identity check preserves existing prompts atomically");
        var missing = (JObject)response.DeepClone(); ((JArray)missing["characters"]!).RemoveAt(0);
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(missing.ToString(), scene), "Anima rejects missing primary character IDs");
        var duplicate = (JObject)response.DeepClone(); ((JArray)duplicate["characters"]!).Add(duplicate["characters"]![0]!.DeepClone());
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(duplicate.ToString(), scene), "Anima rejects duplicate identities");
        var unknown = (JObject)response.DeepClone(); unknown["characters"]![0]!["character_id"] = "npc:Invented";
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply(unknown.ToString(), scene), "Anima rejects invented identities");
        Throws<InvalidOperationException>(() => CharacterScenePrompts.Apply("A human woman walks through a tavern.", scene), "unstructured Anima response cannot bypass identity assembly");
        Check(CharacterScenePrompts.Apply("```json\n" + response + "\n```", scene) == prompt, "fenced structured output cleans without altering final paragraphs");
        var custom = new SceneCharacter { Key = npc.Key, Name = npc.Name, Species = "fox", Description = "Lilian is a human woman with brown eyes. She wears a red coat." };
        Check(SceneIdentity.Anchor(custom).Contains("human woman") && !SceneIdentity.Anchor(custom).Contains("fox"), "intentional human profile override is not replaced with the game's animal species");
        scene.Characters[1] = custom;
        Check(CharacterScenePrompts.Apply(human.ToString(), scene).Contains("human woman"), "manual human profile remains supported by Anima response validation");
        var combined = new SceneCharacter { Description = "An anthro wolf wearing a leather vest. His muzzle is grey." };
        Check(SceneIdentity.Anchor(combined).Contains("anthropomorphic wolf.") && !SceneIdentity.Anchor(combined).Contains("vest"), "mixed identity and outfit sentence retains identity without stale garments");
        Check(SceneIdentity.Anchor(new SceneCharacter { Description = "A human wearing a fur hat." }) == "A human.", "fur clothing does not turn a human profile into an animal anchor");
        Check(SceneIdentity.Tags(new SceneCharacter { Description = "An anthro crystal wyvern with purple scales. No wings." }).Contains("crystal wyvern") &&
            !SceneIdentity.Tags(new SceneCharacter { Description = "An anthro crystal wyvern with purple scales. No wings." }).Split(", ").Contains("wings"), "identity tags support custom species and do not add explicitly absent anatomy");
        var settings = Settings(); settings.Family = ModelFamily.Anima; settings.TextEncoder = "anima.safetensors"; settings.Vae = "vae.safetensors"; settings.Positive = prompt;
        var legs = new SceneCharacter { Description = "An anthro fox. His fur is white. His eyes are blue. His tail is bushy. He has digitigrade legs. His heels remain raised above the ground. His hocks are pronounced. He wears boots." };
        Check(SceneIdentity.Anchor(legs).Contains("digitigrade legs") && SceneIdentity.Anchor(legs).Contains("heels remain raised") && SceneIdentity.Anchor(legs).Contains("hocks are pronounced"), "physical identity retains leg anatomy beyond the fourth profile sentence");
        Check(SceneIdentity.Tags(legs).Split(", ").Contains("digitigrade") && !SceneIdentity.Anchor(legs).Contains("boots"), "Illustrious receives late digitigrade profile tags without copying old clothing");
        Check((string?)Workflow.Build(settings, 1)["4"]?["inputs"]?["text"] == prompt, "Anima identity paragraphs survive the final ComfyUI workflow unchanged");
    }
}
