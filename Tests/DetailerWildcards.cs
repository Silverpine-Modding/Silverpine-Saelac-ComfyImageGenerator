using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private const string LabelWildcard = "[CONCAT][LAB]\n[ALL] fine texture,\n[face] clear amber eyes, __details/eyes__\n[hand] {relaxed|open} fingers\n[tail] white fur <lora:fur:0.5>";
    private const string RegionWildcard = "[ASC] amber eyes [SEP:123] blue eyes [SEP] [SKIP]";

    private static void DetailerWildcards()
    {
        var legacy = JsonConvert.DeserializeObject<DetailerSetting>("{\"Detector\":\"bbox/face.pt\",\"Positive\":\"clear eyes\"}")!;
        Check(legacy.Wildcard == "" && legacy.Positive == "clear eyes", "old detailer settings keep their override and default to an empty wildcard");

        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            var settings = DetailedSettings(family);
            settings.BasePositive = "painted illustration";
            settings.Detailers[0].Wildcard = RegionWildcard;
            settings.Detailers[2].Wildcard = LabelWildcard;
            settings.Detailers[1].Wildcard = "disabled text";
            settings.Validate();
            JObject graph = Workflow.Build(settings, 42);
            Check((string?)graph["detailer_0"]?["inputs"]?["wildcard"] == RegionWildcard, family + " ordered region prompts and seed/skip directives pass through exactly");
            Check((string?)graph["detailer_2"]?["inputs"]?["wildcard"] == LabelWildcard, family + " label directives stay at the start, with files, choices and LoRAs unexpanded");
            Check((string?)graph["detailer_0"]?["inputs"]?["positive"]?[0] == "4", family + " wildcard pass retains scene conditioning for native CONCAT");
            Check((string?)graph["detailer_2_positive"]?["inputs"]?["text"] == settings.ComposePositive(settings.Detailers[2].Positive), family + " CONCAT can use the styled positive override");
            Check((string?)graph["detailer_2_negative"]?["inputs"]?["text"] == settings.Detailers[2].Negative, family + " wildcard does not change negative override");
            Check((string?)graph["detailer_2"]?["inputs"]?["bbox_detector"]?[0] == "detailer_2_detector"
                && (string?)graph["detailer_2"]?["inputs"]?["segm_detector_opt"]?[0] == "detailer_2_detector"
                && (int?)graph["detailer_2"]?["inputs"]?["segm_detector_opt"]?[1] == 1, family + " label detailer receives both outputs of the same segmentation model");
            Check(graph["detailer_1"] == null, family + " disabled wildcard pass is skipped");

            settings.Detailers = JsonConvert.DeserializeObject<List<DetailerSetting>>(JsonConvert.SerializeObject(settings.Detailers))!;
            Check(settings.Detailers[0].Wildcard == RegionWildcard && settings.Detailers[2].Wildcard == LabelWildcard, family + " separate multiline wildcards survive saving and loading");
            settings.Detailers.Reverse();
            graph = Workflow.Build(settings, 42);
            Check((string?)graph["detailer_0"]?["inputs"]?["wildcard"] == LabelWildcard
                && (string?)graph["detailer_2"]?["inputs"]?["wildcard"] == RegionWildcard, family + " wildcards stay attached to their passes when reordered");

            foreach (string raw in new[] { "", "[CONCAT] extra detail", "[LAB][unknown_label] user text", "  [LAB]\n[face] intentionally uncorrected", "[DSC-SIZE] first [SEP] second", "[RND] one [SEP] two" })
            {
                settings.Detailers[0].Wildcard = raw;
                settings.Validate();
                Check((string?)Workflow.Build(settings, 42)["detailer_0"]?["inputs"]?["wildcard"] == raw, family + " wildcard text is neither rewritten nor semantically vetoed");
            }
        }
    }

    private static async Task DetailerWildcardApi()
    {
        foreach (ModelFamily family in Enum.GetValues<ModelFamily>())
        {
            var settings = DetailedSettings(family);
            settings.Detailers[0].Wildcard = RegionWildcard;
            settings.Detailers[2].Wildcard = LabelWildcard;
            using var api = new MockApi { Nodes = Info(settings) };
            using var client = Client(api);
            var result = await client.GenerateAsync(settings, DirectoryFor("wildcards-" + family), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None);
            Check(api.Queued == 1 && result.Images.Count == 1, family + " wildcard workflow completes through the HTTP client");
            Check((string?)api.SubmittedGraph?["detailer_0"]?["inputs"]?["wildcard"] == RegionWildcard
                && (string?)api.SubmittedGraph?["detailer_2"]?["inputs"]?["wildcard"] == LabelWildcard, family + " submitted API graph preserves both independent wildcard strings");
        }
    }
}
