using System;
using Newtonsoft.Json;

namespace SilverpineMods.ComfyImageGenerator;

public enum VisionProvider { Ollama, KoboldCpp, LMStudio }

public sealed class VisionSettings
{
    public bool Enabled { get; set; }
    public VisionProvider Provider { get; set; }
    public string Address { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "";
    [JsonIgnore] public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxTokens { get; set; } = 1600;
    public int ContextLength { get; set; } = 8192;
    public double Temperature { get; set; } = 0.25;

    public void Validate(bool requireModel = true)
    {
        if (!Enum.IsDefined(typeof(VisionProvider), Provider)) throw new ArgumentException("Choose a supported vision LLM server.");
        NormalizeAddress(Address);
        if (requireModel && string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("Choose a vision model (or KoboldCPP vision config) in Vision LLM settings.");
        if (Model == "unload_model") throw new ArgumentException("Choose a KoboldCPP vision config, not unload_model.");
        if (TimeoutSeconds < 30 || TimeoutSeconds > 1800) throw new ArgumentException("Vision request timeout must be 30–1800 seconds.");
        if (MaxTokens < 256 || MaxTokens > 8192) throw new ArgumentException("Vision output limit must be 256–8192 tokens.");
        if (ContextLength < 4096 || ContextLength > 131072) throw new ArgumentException("Vision context length must be 4096–131072 tokens.");
        if (double.IsNaN(Temperature) || double.IsInfinity(Temperature) || Temperature < 0 || Temperature > 2)
            throw new ArgumentException("Vision temperature must be 0–2.");
        if (Provider == VisionProvider.LMStudio && Temperature > 1)
            throw new ArgumentException("LM Studio native chat temperature must be 0–1.");
    }

    public static Uri NormalizeAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter the vision server's HTTP(S) base URL, without credentials, query, or fragment.");
        string path = uri.AbsoluteUri.TrimEnd('/');
        // Accept commonly copied OpenAI/Ollama API bases without doubling their paths.
        foreach (string suffix in new[] { "/api/v1", "/v1", "/api" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { path = path.Substring(0, path.Length - suffix.Length); break; }
        return new Uri(path + "/");
    }

    public static string DefaultAddress(VisionProvider provider) => provider switch
    {
        VisionProvider.KoboldCpp => "http://127.0.0.1:5001",
        VisionProvider.LMStudio => "http://127.0.0.1:1234",
        _ => "http://127.0.0.1:11434"
    };
}
