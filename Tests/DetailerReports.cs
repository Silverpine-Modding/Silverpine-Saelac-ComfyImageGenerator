using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    private static JObject CountOutput(int count) => new() { ["text"] = new JArray(count.ToString()) };

    private static void DetailerReports()
    {
        var settings = RegionalSettings(); settings.Detailers[0].RegionId = "";
        var outputs = new JObject {
            ["detailer_0_detected_report"] = CountOutput(2),
            ["detailer_0_region_0_selected_report"] = CountOutput(1),
            ["detailer_0_region_1_selected_report"] = CountOutput(0),
            ["detailer_1_detected_report"] = CountOutput(0),
            ["detailer_1_region_0_selected_report"] = CountOutput(0),
            ["detailer_1_region_1_selected_report"] = CountOutput(0) };
        var record = new JObject { ["outputs"] = outputs };
        var reports = ComfyClient.ReadDetailerReports(record, settings);
        Check(reports.Count == 3 && reports[0].Contains("2 detected, 1 selected for detailing"), "counts distinguish detector hits from regional assignment and omit untargeted regions");
        Check(reports[1].Contains("No detections assigned") && reports[1].Contains("at least half"), "empty selection explains overlap and priority rules");
        Check(reports[2].Contains("check the detector model and threshold"), "empty detection results give actionable status");
        Check(ComfyClient.ImageDescriptors(record).Count == 0, "text diagnostics never create extra saved images");
        foreach (JToken bad in new JToken[] { new JValue("broken"), new JArray("-1"), new JArray("10001"), new JArray(1), new JArray("3"), new JArray("1", "2") })
        {
            outputs["detailer_0_region_0_selected_report"] = new JObject { ["text"] = bad };
            Check(ComfyClient.ReadDetailerReports(record, settings).Count == 2, "malformed or inconsistent optional diagnostics cannot break a completed image");
        }
        Check(ComfyClient.ReadDetailerReports(new JObject(), settings).Count == 0, "older workflows without diagnostics remain readable");
        settings.Detailers[1].Enabled = false;
        Check(ComfyClient.ReadDetailerReports(record, settings).Count == 1, "disabled passes have no reports");
        settings.WorkflowFile = "custom.json";
        Check(ComfyClient.ReadDetailerReports(record, settings).Count == 0, "custom workflows do not inherit built-in count interpretation");
    }
}
