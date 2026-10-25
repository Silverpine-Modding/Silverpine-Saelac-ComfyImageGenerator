using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SilverpineMods.ComfyImageGenerator;

public enum ModelFamily { Illustrious, Anima }

public sealed class LoraSetting
{
    public string Name { get; set; } = "";
    public double ModelStrength { get; set; } = 1;
    public double ClipStrength { get; set; } = 1;
}

public sealed class GenerationSettings
{
    public ModelFamily Family { get; set; }
    public string Model { get; set; } = "";
    public string TextEncoder { get; set; } = "qwen_3_06b_base.safetensors";
    public string Vae { get; set; } = "qwen_image_vae.safetensors";
    public string Positive { get; set; } = "";
    public string BasePositive { get; set; } = "";
    public string Negative { get; set; } = "";
    // Runtime prompt ownership, not a saved profile or ComfyUI node input.
    public List<string> SceneCharacterKeys { get; set; } = new();
    public string PromptNotice { get; set; } = "";
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public int Steps { get; set; } = 30;
    public double Cfg { get; set; } = 5;
    public long Seed { get; set; } = -1;
    public string Sampler { get; set; } = "euler";
    public string Scheduler { get; set; } = "normal";
    public string WorkflowFile { get; set; } = "";
    public List<LoraSetting> Loras { get; set; } = new();
    public List<DetailerSetting> Detailers { get; set; } = new();
    public bool EasyCache { get; set; }
    public double CacheThreshold { get; set; } = 0.2;
    public double CacheStart { get; set; } = 0.15;
    public double CacheEnd { get; set; } = 0.95;

    public void Validate()
    {
        if (!Enum.IsDefined(typeof(ModelFamily), Family)) throw new ArgumentException("Choose Illustrious or Anima.");
        string combinedPositive = ComposePositive(Positive);
        if (string.IsNullOrWhiteSpace(combinedPositive)) throw new ArgumentException("Enter a positive prompt or base positive prompt.");
        if (Positive.Length > 32000 || BasePositive.Length > 32000 || combinedPositive.Length > 32000 || Negative.Length > 32000)
            throw new ArgumentException("Prompts are limited to 32,000 characters, including the base positive prefix.");
        if (Width < 256 || Width > 2048 || Height < 256 || Height > 2048 || Width % 64 != 0 || Height % 64 != 0)
            throw new ArgumentException("Width and height must be multiples of 64 between 256 and 2048.");
        if (Steps < 1 || Steps > 150) throw new ArgumentException("Steps must be between 1 and 150.");
        if (double.IsNaN(Cfg) || double.IsInfinity(Cfg) || Cfg < 0 || Cfg > 30) throw new ArgumentException("CFG must be between 0 and 30.");
        if (Seed < -1) throw new ArgumentException("Seed must be -1 (random) or a nonnegative integer.");
        if (EasyCache && (!FiniteRange(CacheThreshold, 0, 3) || !FiniteRange(CacheStart, 0, 1) || !FiniteRange(CacheEnd, 0, 1) || CacheStart >= CacheEnd))
            throw new ArgumentException("EasyCache needs threshold 0-3 and 0 <= start < end <= 1.");
        foreach (LoraSetting lora in Loras)
            if (string.IsNullOrWhiteSpace(lora.Name) || !ValidStrength(lora.ModelStrength) || !ValidStrength(lora.ClipStrength))
                throw new ArgumentException("Each LoRA needs a filename and finite strengths between -10 and 10.");
        foreach (DetailerSetting detailer in Detailers)
        {
            if (detailer == null) throw new ArgumentException("A saved detailer is invalid. Remove it and add a new pass.");
            detailer.Validate();
            if (detailer.Enabled && !string.IsNullOrWhiteSpace(detailer.Positive) && ComposePositive(detailer.Positive).Length > 32000)
                throw new ArgumentException("Detailer '" + detailer.Name + "': positive prompt plus base positive prefix exceeds 32,000 characters.");
        }
        if (!string.IsNullOrWhiteSpace(WorkflowFile) && (Loras.Count != 0 || EasyCache || Detailers.Any(d => d.Enabled)))
            throw new ArgumentException("For a custom workflow, put LoRA/cache/detailer nodes in that workflow and disable the built-in LoRA/cache/detailer settings.");
        if (string.IsNullOrWhiteSpace(WorkflowFile))
        {
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("Select the model filename from your ComfyUI server.");
            if (Family == ModelFamily.Anima && (string.IsNullOrWhiteSpace(TextEncoder) || string.IsNullOrWhiteSpace(Vae)))
                throw new ArgumentException("Anima requires a text encoder and VAE.");
            if (string.IsNullOrWhiteSpace(Sampler) || string.IsNullOrWhiteSpace(Scheduler))
                throw new ArgumentException("Select a sampler and scheduler.");
        }
    }

    private static bool ValidStrength(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= -10 && value <= 10;
    private static bool FiniteRange(double value, double min, double max) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;

    // Keep scene text separate from the reusable prefix. Building/submitting the
    // same settings repeatedly must never add another copy to the editable text.
    public string ComposePositive(string prompt)
    {
        string prefix = BasePositive.Trim();
        if (prefix.Length == 0) return prompt;
        return string.IsNullOrWhiteSpace(prompt) ? prefix : prefix + "\n\n" + prompt;
    }

    public long ResolveSeed()
    {
        if (Seed >= 0) return Seed;
        byte[] bytes = new byte[8];
        using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        // Stay within the exact integer range supported by JavaScript workflow editors.
        return (long)(BitConverter.ToUInt64(bytes, 0) & 0x001fffffffffffffUL);
    }
}

public static partial class Workflow
{
    public static JObject Build(GenerationSettings s, long seed)
    {
        s.Validate();
        if (!string.IsNullOrWhiteSpace(s.WorkflowFile))
        {
            FileInfo file = new(s.WorkflowFile);
            if (!file.Exists || file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Custom API workflow must exist and be no larger than 4 MB.");
            return FromTemplate(File.ReadAllText(file.FullName), s, seed);
        }
        bool anima = s.Family == ModelFamily.Anima;
        JObject graph = new();
        graph["1"] = anima
            ? Node("UNETLoader", new JObject { ["unet_name"] = s.Model, ["weight_dtype"] = "default" })
            : Node("CheckpointLoaderSimple", new JObject { ["ckpt_name"] = s.Model });
        if (anima)
        {
            // Matches the official Comfy-Org Anima Base v1 workflow. Anima's loader
            // recognizes the Qwen encoder with type=stable_diffusion.
            graph["2"] = Node("CLIPLoader", new JObject { ["clip_name"] = s.TextEncoder, ["type"] = "stable_diffusion", ["device"] = "default" });
            graph["3"] = Node("VAELoader", new JObject { ["vae_name"] = s.Vae });
        }
        JArray modelLink = Link("1"), clipLink = Link(anima ? "2" : "1", anima ? 0 : 1);
        for (int i = 0; i < s.Loras.Count; i++)
        {
            LoraSetting lora = s.Loras[i];
            string id = "lora_" + i;
            var inputs = new JObject { ["model"] = modelLink, ["lora_name"] = lora.Name, ["strength_model"] = lora.ModelStrength };
            if (!anima) { inputs["clip"] = clipLink; inputs["strength_clip"] = lora.ClipStrength; }
            graph[id] = Node(anima ? "LoraLoaderModelOnly" : "LoraLoader", inputs);
            modelLink = Link(id);
            if (!anima) clipLink = Link(id, 1);
        }
        graph["4"] = Node("CLIPTextEncode", new JObject { ["text"] = s.ComposePositive(s.Positive), ["clip"] = clipLink });
        graph["5"] = Node("CLIPTextEncode", new JObject { ["text"] = s.Negative, ["clip"] = clipLink.DeepClone() });
        graph["6"] = Node("EmptyLatentImage", new JObject { ["width"] = s.Width, ["height"] = s.Height, ["batch_size"] = 1 });
        // Detailer crops use the LoRA-patched model without EasyCache's sampling state.
        JArray detailerModelLink = (JArray)modelLink.DeepClone();
        if (s.EasyCache)
        {
            graph["cache"] = Node("EasyCache", new JObject { ["model"] = modelLink, ["reuse_threshold"] = s.CacheThreshold,
                ["start_percent"] = s.CacheStart, ["end_percent"] = s.CacheEnd, ["verbose"] = false });
            modelLink = Link("cache");
        }
        graph["7"] = Node("KSampler", new JObject
        {
            ["model"] = modelLink, ["positive"] = Link("4"), ["negative"] = Link("5"), ["latent_image"] = Link("6"),
            ["seed"] = seed, ["steps"] = s.Steps, ["cfg"] = s.Cfg, ["sampler_name"] = s.Sampler,
            ["scheduler"] = s.Scheduler, ["denoise"] = 1.0
        });
        JArray vaeLink = Link(anima ? "3" : "1", anima ? 0 : 2);
        graph["8"] = Node("VAEDecode", new JObject { ["samples"] = Link("7"), ["vae"] = vaeLink });
        JArray finalImage = AppendDetailers(graph, s, seed, detailerModelLink, clipLink, vaeLink);
        graph["9"] = Node("PreviewImage", new JObject { ["images"] = finalImage });
        return graph;
    }

    private static JObject Node(string type, JObject inputs) => new() { ["class_type"] = type, ["inputs"] = inputs };
    private static JArray Link(string id, int slot = 0) => new(id, slot);

    public static JObject FromTemplate(string json, GenerationSettings s, long seed)
    {
        JObject graph = JObject.Parse(json);
        if (graph["prompt"] is JObject wrapped) graph = (JObject)wrapped.DeepClone();
        if (graph["nodes"] is JArray || graph.Count == 0 || graph.Properties().Any(p => !(p.Value is JObject node) || node["class_type"]?.Type != JTokenType.String || !(node["inputs"] is JObject)))
            throw new InvalidDataException("Export the workflow using ComfyUI's API format, not its normal canvas JSON.");
        var replacements = new Dictionary<string, JToken>(StringComparer.Ordinal)
        {
            ["{{positive}}"] = new JValue(s.ComposePositive(s.Positive)), ["{{negative}}"] = new JValue(s.Negative),
            ["{{seed}}"] = new JValue(seed), ["{{width}}"] = new JValue(s.Width), ["{{height}}"] = new JValue(s.Height),
            ["{{steps}}"] = new JValue(s.Steps), ["{{cfg}}"] = new JValue(s.Cfg), ["{{model}}"] = new JValue(s.Model),
            ["{{text_encoder}}"] = new JValue(s.TextEncoder), ["{{vae}}"] = new JValue(s.Vae),
            ["{{sampler}}"] = new JValue(s.Sampler), ["{{scheduler}}"] = new JValue(s.Scheduler)
        };
        // Replace whole JSON input values only. Never interpolate raw JSON or alter node links.
        bool foundPrompt = false;
        foreach (JObject node in graph.Properties().Select(p => (JObject)p.Value))
        foreach (JProperty input in ((JObject)node["inputs"]!).Properties())
        {
            if (input.Value.Type != JTokenType.String) continue;
            string value = (string)input.Value!;
            if (replacements.TryGetValue(value, out JToken? replacement))
            {
                foundPrompt |= value == "{{positive}}";
                input.Value = replacement.DeepClone();
            }
            else if (value.Contains("{{") || value.Contains("}}"))
                throw new InvalidDataException("Workflow placeholders must be supported whole input values, e.g. \"text\": \"{{positive}}\".");
        }
        if (!foundPrompt) throw new InvalidDataException("Put {{positive}} in the API workflow's positive prompt text input so the window's prompt is used.");
        return graph;
    }

    public static string[] Choices(JObject info, string nodeType, string input)
    {
        JToken? definition = info[nodeType]?["input"]?["required"]?[input] ?? info[nodeType]?["input"]?["optional"]?[input];
        return definition is JArray d && d.Count > 0 && d[0] is JArray values
            ? values.Values<string>().OfType<string>().ToArray() : Array.Empty<string>();
    }

    public static string[] MissingNodes(JObject info, params string[] types) => types.Where(type => !(info[type] is JObject)).ToArray();

    public static void ValidateServer(JObject graph, JObject info)
    {
        foreach (JProperty entry in graph.Properties())
        {
            string type = (string)entry.Value["class_type"]!;
            if (!(info[type] is JObject))
            {
                string help = type == "FaceDetailer" ? " Install ComfyUI-Impact-Pack on the server."
                    : type == "UltralyticsDetectorProvider" ? " Install ComfyUI-Impact-Subpack and a compatible detector model on the server."
                    : " Update ComfyUI or install the workflow's required nodes.";
                throw new InvalidDataException($"ComfyUI is missing node '{type}'." + help);
            }
            // Catch changed custom-node schemas before an expensive LLM call or image job.
            if (type == "FaceDetailer" && info[type]?["input"]?["required"] is JObject required)
                foreach (JProperty input in required.Properties())
                    if (entry.Value["inputs"]?[input.Name] == null)
                        throw new InvalidDataException($"{type} requires unsupported input '{input.Name}'. Use a matching custom API workflow for this server version.");
            foreach (JProperty input in ((JObject)entry.Value["inputs"]!).Properties())
            {
                if (input.Value.Type != JTokenType.String) continue;
                string[] options = Choices(info, type, input.Name);
                JToken? definition = info[type]?["input"]?["required"]?[input.Name] ?? info[type]?["input"]?["optional"]?[input.Name];
                bool isChoice = definition is JArray d && d.Count > 0 && d[0] is JArray;
                if (isChoice && !options.Contains((string)input.Value!))
                    throw new InvalidDataException($"{type}.{input.Name}: '{input.Value}' is unavailable on the server. Refresh the lists and select an installed value.");
            }
        }
    }
}
