using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static void SceneSelections()
    {
        var group = new SceneSnapshot
        {
            Environment = "A tavern at sunset.", Dialogue = "Dan talks to Oriana and Mara.",
            Characters = new()
            {
                new() { Key = "player:Dan", Name = "Dan", Description = "Arctic fox with a unique blue coat.", Clothing = new() { State = "clothed", EquipmentKnown = true, Equipped = new() { new() { Slot = "Chest", Name = "Red cloak" } } } },
                new() { Key = "npc:Oriana", Name = "Oriana", Description = "White snake with yellow scales.", Clothing = new() { State = "nude" } },
                new() { Key = "npc:Mara", Name = "Mara", Description = "A fox with a distinctive green tail." }
            }
        };
        Throws<ArgumentException>(() => SceneSelection.Select(group, Array.Empty<string>(), ModelFamily.Anima), "empty cast cannot generate");
        Throws<ArgumentException>(() => SceneSelection.Select(group, group.Characters.Select(c => c.Key), ModelFamily.Anima), "Anima rejects three participants");
        Throws<ArgumentException>(() => SceneSelection.Select(group, new[] { "player:Dan", "npc:Oriana" }, ModelFamily.Illustrious), "Illustrious rejects two participants");
        Throws<ArgumentException>(() => SceneSelection.Select(group, new[] { "npc:Gareth" }, ModelFamily.Illustrious), "legacy bystanders cannot be selected");
        Throws<ArgumentException>(() => SceneSelection.Select(group, new[] { "npc:Missing" }, ModelFamily.Anima), "participant leaving dialogue rejects stale selection");
        Throws<ArgumentException>(() => SceneSelection.Select(group, new[] { "npc:Oriana", "npc:Oriana" }, ModelFamily.Anima), "duplicate IDs do not count as two characters");
        var pair = SceneSelection.Select(group, new[] { "npc:Mara", "npc:Oriana" }, ModelFamily.Anima);
        Check(pair.Characters.Select(c => c.Name).SequenceEqual(new[] { "Oriana", "Mara" }), "player optional and selected NPC order stable regardless of clicks");
        Check(pair.OffscreenParticipants.SequenceEqual(new[] { "Dan" }), "unselected player stays off-screen and bystander references discarded");
        Check(pair.Characters[0].Clothing.State == "nude" && pair.Dialogue == group.Dialogue && pair.Environment == group.Environment, "selected live clothing plus dialogue and environment retained");
        string animaRequest = ScenePrompts.Build(pair, ModelFamily.Anima, "watercolor");
        Check(animaRequest.Contains("exactly 2 selected") && animaRequest.Contains("White snake with yellow scales") && animaRequest.Contains("distinctive green tail"), "Anima request contains selected profiles and exact count");
        Check(!animaRequest.Contains("unique blue coat") && !animaRequest.Contains("Red cloak") && !animaRequest.Contains("Bystander reference"), "unselected visual profile and clothing not sent to LLM");
        Check(animaRequest.Contains("partner off-screen") && animaRequest.Contains("Off-screen conversation participants") && animaRequest.Contains("unselected participant"), "unselected dialogue speakers explicitly offscreen even during interactions");
        var response = new JObject
        {
            ["characters"] = new JArray(pair.Characters.Select(c => new JObject { ["character_id"] = c.Key, ["prompt"] = "Standing beside the window and looking outside." })),
            ["environment"] = "A quiet tavern with warm sunset lighting."
        };
        string generated = CharacterScenePrompts.Apply(response.ToString(), pair, ModelFamily.Anima);
        Check(generated.Contains("Oriana:") && generated.Contains("Mara:") && !generated.Contains("Dan:"), "Anima LLM response produces only selected NPC paragraphs");
        ((JArray)response["characters"]!).Add(new JObject { ["character_id"] = "player:Dan", ["prompt"] = "Standing beside the others in the tavern." });
        Check(!CharacterScenePrompts.Apply(response.ToString(), pair, ModelFamily.Anima).Contains("Dan:"), "extra unselected player entry ignored without rejecting the job");

        var solo = SceneSelection.Select(group, new[] { "npc:Oriana" }, ModelFamily.Illustrious);
        string illustriousRequest = ScenePrompts.Build(solo, ModelFamily.Illustrious, "oil painting");
        Check(illustriousRequest.Contains("exactly 1 selected") && !illustriousRequest.Contains("unique blue coat") && !illustriousRequest.Contains("distinctive green tail"), "Illustrious LLM gets only chosen profile even during group conversation");
        string soloResponse = new JObject { ["characters"] = new JArray(new JObject { ["character_id"] = "npc:Oriana", ["prompt"] = "standing, looking outside, full body" }), ["environment"] = "tavern, sunset, warm lighting" }.ToString();
        Check(CharacterScenePrompts.Apply(soloResponse, solo, ModelFamily.Illustrious).Contains("solo"), "solo Illustrious conditioning count");
        var selectedPlayer = SceneSelection.Select(group, new[] { "player:Dan" }, ModelFamily.Anima);
        selectedPlayer.Characters[0].Description = "LLM initialized profile";
        selectedPlayer.Characters[0].Clothing.Equipped[0].Name = "Edited cloak";
        Check(group.Characters[0].Description.Contains("unique blue coat") && group.Characters[0].Clothing.Equipped[0].Name == "Red cloak", "profile initialization and later edits do not mutate capture or other selections");
        for (int i = 0; i < 10; i++) group.Characters.Add(new() { Key = "npc:Extra" + i, Name = "Extra" + i });
        Check(SceneSelection.Select(group, new[] { "npc:Extra9" }, ModelFamily.Illustrious).Characters[0].Name == "Extra9", "picker can choose late participant instead of limiting eligible cast to first entries");

        var settings = Settings(ModelFamily.Anima);
        settings.SceneCharacterKeys = new() { "npc:Oriana", "npc:Mara" };
        SceneSelection.ValidateGeneration(settings);
        settings.Family = ModelFamily.Illustrious;
        Throws<ArgumentException>(() => SceneSelection.ValidateGeneration(settings), "two-character regeneration cannot switch to Illustrious");
        settings.SceneCharacterKeys = new() { "npc:Oriana" };
        SceneSelection.ValidateGeneration(settings);
        settings.SceneCharacterKeys.Clear();
        SceneSelection.ValidateGeneration(settings); // Free-form manual prompts do not require a live conversation.
    }
}
