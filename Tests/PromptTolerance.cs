using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static SceneSnapshot PromptTestScene() => new()
    {
        Characters = new() { new() { Key = "npc:Oriana", Name = "Oriana", Description = "An anthro snake with white scales and yellow markings.", Clothing = new() { State = "clothed", EquipmentKnown = true, Equipped = new() { new() { Name = "Green tunic" } } } } },
        Environment = "An empty tavern lit by candles.", Dialogue = "Oriana smiles."
    };

    private static void PromptTolerance()
    {
        SceneSnapshot scene = PromptTestScene();
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            var response = new JObject { ["characters"] = new JArray(new JObject { ["character_id"] = "npc:Oriana", ["prompt"] = "A human woman standing near the fireplace." }), ["environment"] = "warm candlelight, wooden table" };
            string prompt = CharacterScenePrompts.Apply(response.ToString(), scene, family);
            Check(prompt.Contains("human woman"), family + " leaves wrong anatomy wording editable instead of declining generation");
            response["characters"]![0]!["character_id"] = " Oriana ";
            Check(CharacterScenePrompts.Apply(response.ToString(), scene, family).Contains("human woman"), family + " tolerates name instead of ID");
            response["characters"]![0]!["character_id"] = "NPC:ORIANA";
            Check(CharacterScenePrompts.Apply(response.ToString(), scene, family).Contains("human woman"), family + " tolerates ID casing");
            foreach (string malformed in new[] { "", "<think>unfinished", "{\"characters\":[", "{}", "{\"characters\":[{\"prompt\":null}],\"environment\":4}" })
            {
                string fallback = CharacterScenePrompts.Apply(malformed, scene, family, out string notice);
                Check(fallback.Contains("snake") && fallback.Contains("tunic", StringComparison.OrdinalIgnoreCase) && fallback.Contains("tavern"), family + " malformed/empty response uses selected reference plus clothing and environment");
                Check(notice.Length > 0, family + " fallback is disclosed for prompt editing");
            }
            string raw = "Human woman by a fireplace.\n\nWarm candlelight.";
            Check(CharacterScenePrompts.Apply(raw, scene, family) == (family == ModelFamily.Anima ? raw : raw.ToLowerInvariant()), family + " accepts plain prompt paragraphs");
            Check(CharacterScenePrompts.Apply("fox", scene, family) == "fox", family + " short usable prompt is not rejected");
            Check(CharacterScenePrompts.Apply("{\"positive_prompt\":\"human woman, seated\"}", scene, family).Contains("human woman"), family + " accepts alternate prompt wrapper");
            Check(CharacterScenePrompts.Apply("Here is the prompt:\n" + response + "\nDone.", scene, family).Contains("human woman"), family + " extracts JSON from explanatory text");
        }
        string legacy = "{\"Family\":0,\"Model\":\"illustrious.safetensors\",\"Positive\":\"solo fox\",\"AttentionCouple\":true,\"Regions\":[{}],\"Detailers\":[{\"Detector\":\"bbox/face.pt\",\"RegionId\":\"old-target\",\"Positive\":\"detailed eyes\"}]}";
        var migrated = JsonConvert.DeserializeObject<GenerationSettings>(legacy)!;
        JObject graph = Workflow.Build(migrated, 42);
        Check((string?)graph["detailer_0"]?["class_type"] == "FaceDetailer", "old saved region target cannot block ordinary detailing");
        Check(graph.Values().All(n => !((string)n["class_type"]!).Contains("Couple") && !((string)n["class_type"]!).Contains("SEGS") && !((string)n["class_type"]!).Contains("Mask")), "old enabled coupling data cannot create coupling or regional mask nodes");
        Check(!JsonConvert.SerializeObject(migrated).Contains("RegionId") && !JsonConvert.SerializeObject(migrated).Contains("AttentionCouple"), "removed settings disappear when serialized again");
        Workflow.ValidateServer(graph, Info(migrated));
    }

    private static async Task PermissivePromptApi()
    {
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            var settings = Settings(family);
            settings.Detailers.Add(new DetailerSetting { Detector = "bbox/face.pt" });
            settings.Positive = CharacterScenePrompts.Apply("{\"characters\":[{\"character_id\":\"npc:Oriana\",\"prompt\":\"human woman, smiling\"},{\"character_id\":\"npc:Extra\",\"prompt\":\"extra character\"}]}", PromptTestScene(), family);
            using var api = new MockApi { Nodes = Info(settings) };
            using var client = Client(api);
            var result = await client.GenerateAsync(settings, DirectoryFor("permissive-" + family), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
            Check(api.Queued == 1 && result.Images.Count == 1 && ((string?)api.SubmittedGraph?["4"]?["inputs"]?["text"])?.Contains("human woman") == true, family + " content previously vetoed is submitted and downloaded with a normal detailer");
            Check((string?)api.SubmittedGraph?["detailer_0"]?["class_type"] == "FaceDetailer", family + " ordinary detailing remains in submitted workflow");
        }
    }
}
