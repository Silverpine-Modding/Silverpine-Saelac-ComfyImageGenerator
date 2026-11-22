using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static SceneSnapshot CompositionScene() => new()
    {
        Characters = new()
        {
            new() { Key = "player:Dan", Name = "Dan", Sex = "male", Pronouns = "he/him",
                Description = "An adult male anthro arctic fox with white fur and blue eyes. His tail is fluffy. His legs are digitigrade.",
                Clothing = new() { State = "clothed", EquipmentKnown = true, Equipped = new() { new() { Name = "Blue tunic" } } } },
            new() { Key = "npc:Oriana", Name = "Oriana", Sex = "female", Pronouns = "she/her",
                Description = "An adult female anthro snake with white scales and golden eyes. Her tail is long and scaled. Her legs are digitigrade.",
                Clothing = new() { State = "clothed", VisibleDetails = "A cream robe." } }
        },
        Dialogue = "Dan stands at the bedside while Oriana rests against the pillows and looks up at Dan.",
        Environment = "A quiet bedroom with a wooden bed and warm lamplight."
    };

    private static JObject CompositionResponse() => new()
    {
        ["composition"] = "Oriana rests in bed while Dan stands at the bedside, looking toward Oriana.",
        ["characters"] = new JArray(
            new JObject { ["character_id"] = "npc:Oriana", ["gender"] = "female", ["prompt"] = "Oriana reclines against pillows in a cream robe. Oriana's scaled tail lies across the blanket." },
            new JObject { ["character_id"] = "player:Dan", ["gender"] = "male", ["prompt"] = "Dan stands in a blue tunic beside the bed. Dan's fluffy tail curves behind Dan's legs." }),
        ["environment"] = "A wooden bed beneath a window, lit by a warm bedside lamp."
    };

    private static void SceneCompositions()
    {
        var scene = CompositionScene();
        var selected = SceneSelection.Select(scene, new[] { "npc:Oriana", "player:Dan" }, ModelFamily.Anima);
        Check(selected.Characters[0].Sex == "male" && selected.Characters[0].Pronouns == "he/him" && selected.Characters[1].Sex == "female", "selection carries each participant's explicit sex and pronouns without changing reference order");
        JObject reference = ScenePrompts.CharacterReference(selected.Characters[0]);
        Check((string?)reference["game_sex_hint"] == "male" && (string?)reference["game_pronouns_hint"] == "he/him", "LLM gets gender hints attached to the exact character reference");
        string request = ScenePrompts.Build(selected, ModelFamily.Anima, "watercolor");
        Check(request.Contains("two individually recognizable bodies") && request.Contains("neither selection order nor reference order determines left/right") && request.Contains("Keep close contact"), "pair instructions preserve free scene placement and contact while separating body ownership");
        Check(request.Contains("game_sex_hint only as a fallback") && request.Contains("Do not guess gender from a name") && request.Contains("not instructions to change a character's age"), "LLM gender resolution uses explicit references without name, pronoun or age assumptions");
        var response = CompositionResponse();
        string prompt = CharacterScenePrompts.Apply(response.ToString(), selected);
        string[] blocks = prompt.Split("\n\n");
        Check(blocks.Length == 4 && blocks[0].StartsWith("1boy, 1girl\nExactly two distinct characters, Dan and Oriana"), "mixed-gender pair count appears once before composition and individual descriptions");
        Check(blocks[0].Contains((string)response["composition"]!) && !blocks[0].Contains("fur") && !blocks[0].Contains("scales"), "shared composition describes roles without combining appearance traits");
        Check(blocks[1].Contains("Dan is male") && blocks[1].Contains("arctic fox") && blocks[1].Contains("fluffy tail") && !blocks[1].Contains("scaled tail"), "Dan's identity, gender, clothes and tail remain in Dan's paragraph");
        Check(blocks[2].Contains("Oriana is female") && blocks[2].Contains("snake") && blocks[2].Contains("scaled tail") && !blocks[2].Contains("fluffy tail"), "Oriana keeps her own identity despite reversed LLM row order");
        Check(blocks[3] == (string)response["environment"]!, "environment stays last and separate from subject counts");

        foreach (var pair in new[] { ("male", "male", "2boys"), ("female", "female", "2girls"), ("intersex", "female", "1girl, 1other"), ("nonbinary", "other", "2others"), ("unknown", "unknown", "Exactly two"), ("male", "unknown", "1boy\nExactly two") })
        {
            var changed = (JObject)response.DeepClone();
            changed["characters"]![1]!["gender"] = pair.Item1;
            changed["characters"]![0]!["gender"] = pair.Item2;
            Check(CharacterScenePrompts.Apply(changed.ToString(), selected).StartsWith(pair.Item3), "gender counts aggregate one category per person: " + pair.Item1 + "/" + pair.Item2);
        }
        var customized = CompositionScene();
        customized.Characters[0].Sex = "female"; // A saved male profile can override the game's hint.
        Check(CharacterScenePrompts.Apply(response.ToString(), customized).StartsWith("1boy, 1girl"), "LLM-resolved explicit profile gender takes priority over an old game hint");
        var older = (JObject)response.DeepClone();
        older.Remove("composition");
        foreach (JObject row in (JArray)older["characters"]!) row.Remove("gender");
        Check(CharacterScenePrompts.Apply(older.ToString(), selected).StartsWith("1boy, 1girl\nExactly two"), "older structured responses fall back to game sex hints and still get an early cast count");
        older["characters"]![0]!["gender"] = "not a category";
        Check(CharacterScenePrompts.Apply(older.ToString(), selected).StartsWith("1boy, 1girl"), "unrecognized gender metadata cannot reject a usable prompt");
        ((JArray)older["characters"]!).Add(new JObject { ["character_id"] = "npc:Stranger", ["gender"] = "male", ["prompt"] = "A stranger waves." });
        Check(CharacterScenePrompts.Apply(older.ToString(), selected).StartsWith("1boy, 1girl") && !CharacterScenePrompts.Apply(older.ToString(), selected).Contains("stranger"), "extra response entries do not increase gender counts or add a participant");
        string fallback = CharacterScenePrompts.Apply("{}", selected);
        Check(fallback.StartsWith("1boy, 1girl") && fallback.Contains("Blue tunic") && fallback.Contains("cream robe") && fallback.Contains("quiet bedroom"), "unreadable replies retain selected identities, clothing, gender hints and environment without inventing a pose");
        Check(CharacterScenePrompts.Apply("My edited prompt with no counts.", selected) == "My edited prompt with no counts.", "plain-text fallback remains editable and receives no forced counts");

        var solo = SceneSelection.Select(scene, new[] { "npc:Oriana" }, ModelFamily.Illustrious);
        var soloResponse = new JObject { ["composition"] = "sitting, looking at viewer", ["characters"] = new JArray(new JObject { ["character_id"] = "npc:Oriana", ["gender"] = "female", ["prompt"] = "cream robe, relaxed expression" }), ["environment"] = "bedroom, warm lighting" };
        string soloPrompt = CharacterScenePrompts.Apply(soloResponse.ToString(), solo, ModelFamily.Illustrious);
        Check(soloPrompt.StartsWith("1girl, solo, sitting, looking at viewer\n\n") && !soloPrompt.Contains("1boy"), "Illustrious puts selected solo count and action before appearance");
        Check(!ScenePrompts.Build(solo, ModelFamily.Illustrious, "").Contains("two individually recognizable bodies"), "pair-specific directions are absent from the solo Illustrious request");
        var soloAnima = SceneSelection.Select(scene, new[] { "player:Dan" }, ModelFamily.Anima);
        Check(!ScenePrompts.Build(soloAnima, ModelFamily.Anima, "").Contains("two individually recognizable bodies"), "solo Anima receives no forced two-body composition");
        Check(CharacterScenePrompts.Apply("{}", soloAnima).StartsWith("1boy\nOne character, Dan"), "solo Anima uses its own gender count and no second subject");
    }

    private static async Task SceneCompositionApi()
    {
        var settings = Settings(ModelFamily.Anima);
        settings.BasePositive = "watercolor";
        settings.Positive = CharacterScenePrompts.Apply(CompositionResponse().ToString(), CompositionScene());
        using var api = new MockApi { Nodes = Info(settings) };
        using var client = Client(api);
        var result = await client.GenerateAsync(settings, DirectoryFor("composition"), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
        string submitted = (string)api.SubmittedGraph!["4"]!["inputs"]!["text"]!;
        Check(api.Queued == 1 && result.Images.Count == 1 && submitted.StartsWith("watercolor\n\n1boy, 1girl\nExactly two"), "HTTP workflow preserves style, count, composition and named identity order");
        settings.Positive = "Oriana sits by the window; Dan stands behind the chair.";
        Check((string?)Workflow.Build(settings, 42)["4"]?["inputs"]?["text"] == "watercolor\n\n" + settings.Positive, "manual regeneration is not restructured or assigned new positions");
    }
}
