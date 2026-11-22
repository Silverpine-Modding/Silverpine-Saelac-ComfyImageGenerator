using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow
{
    private bool visionEnabled, releaseComfyMemory = true, visionListing;
    private VisionProvider visionProvider;
    private string visionAddress = "http://127.0.0.1:11434", visionModel = "", visionKey = "";
    private string visionTimeout = "300", visionTokens = "1600", visionContext = "8192", visionTemperature = "0.25";

    private void LoadVisionSettings()
    {
        try
        {
            VisionSettings s = JsonConvert.DeserializeObject<VisionSettings>(Plugin.VisionJson.Value) ?? new();
            visionEnabled = s.Enabled; visionProvider = s.Provider; visionAddress = s.Address; visionModel = s.Model;
            visionTimeout = s.TimeoutSeconds.ToString(Inv); visionTokens = s.MaxTokens.ToString(Inv);
            visionContext = s.ContextLength.ToString(Inv); visionTemperature = s.Temperature.ToString(Inv);
        }
        catch (Exception) { status = "Saved vision settings could not be read. Reconfigure the Vision LLM beta tab."; }
        visionKey = Plugin.VisionApiKey.Value;
        releaseComfyMemory = Plugin.ReleaseComfyMemory.Value;
    }

    private VisionSettings ReadVisionSettings() => new()
    {
        Enabled = visionEnabled, Provider = visionProvider, Address = visionAddress.Trim(), Model = visionModel.Trim(), ApiKey = visionKey,
        TimeoutSeconds = Int(visionTimeout, "Vision timeout"), MaxTokens = Int(visionTokens, "Vision output tokens"),
        ContextLength = Int(visionContext, "Vision context length"), Temperature = Number(visionTemperature, "Vision temperature")
    };

    private void DrawVisionSettings()
    {
        GUILayout.Label("Portrait-based scene prompting — beta", title);
        visionEnabled = GUILayout.Toggle(visionEnabled, " Use a separate local vision LLM for new scene prompts");
        GUILayout.Label("Reads each selected portrait separately, then writes the scene using dialogue, profiles and current clothing. Works with or without IP-Adapter. Regenerate keeps your edited prompt and skips the LLM.", label);
        GUILayout.BeginHorizontal();
        foreach (VisionProvider provider in Enum.GetValues(typeof(VisionProvider)))
        {
            string name = provider == VisionProvider.KoboldCpp ? "KoboldCPP" : provider == VisionProvider.LMStudio ? "LM Studio" : "Ollama";
            if (GUILayout.Button((visionProvider == provider ? "✓ " : "") + name, button, GUILayout.Height(40)) && visionProvider != provider)
            {
                visionProvider = provider; visionAddress = VisionSettings.DefaultAddress(provider);
                visionModel = provider == VisionProvider.KoboldCpp ? "initial_model" : "";
                visionKey = ""; // Credentials are never carried to a different provider's endpoint.
            }
        }
        GUILayout.EndHorizontal();
        visionAddress = Row("LLM server URL / IP and port", visionAddress);
        visionModel = Row(visionProvider == VisionProvider.KoboldCpp ? "Vision .kcpps config / initial_model" : "Vision model name", visionModel);
        GUILayout.BeginHorizontal();
        GUILayout.Label(visionProvider == VisionProvider.KoboldCpp ? "Admin password" : "API token (optional)", label, GUILayout.Width(300));
        visionKey = GUILayout.PasswordField(visionKey, '•', field);
        GUILayout.EndHorizontal();
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && !visionListing;
        if (GUILayout.Button(visionListing ? "Reading models..." : "Connect / choose model or config", button, GUILayout.Height(40))) ListVisionModels();
        GUI.enabled = enabled;
        if (visionProvider == VisionProvider.KoboldCpp)
            GUILayout.Label("Requires Enable Model Administration, a config directory, and its admin password. Use a saved vision config containing both the model and its matching MMProj, or initial_model when started with those files. Model switching restarts KoboldCPP's worker. The mod waits for an inactive worker before loading and after unloading.", label);
        else if (visionProvider == VisionProvider.LMStudio)
            GUILayout.Label("Requires LM Studio 0.4.0+ with its local server and native v1 model-management API enabled. Download a vision-capable model first. The mod loads/reuses an instance and unloads that model's instances after prompting.", label);
        else GUILayout.Label("Requires an installed vision-capable Ollama model and a running Ollama server. The mod loads on demand, explicitly unloads after prompting, and checks the running-model list.", label);
        visionTimeout = Row("Request/load timeout (seconds)", visionTimeout);
        visionTokens = Row("Output token limit per request", visionTokens);
        visionContext = Row("Context tokens (Ollama / LM Studio)", visionContext);
        visionTemperature = Row(visionProvider == VisionProvider.LMStudio ? "Temperature (0–1)" : "Temperature (0–2)", visionTemperature);
        GUILayout.Label("The output limit applies to each portrait description and the final scene prompt. LM Studio reasoning is disabled when the model reports support for turning it off.", label);
        GUILayout.Label("KoboldCPP context size comes from its saved config. Portrait images consume context tokens too; increase context if the server reports overflow. More context uses more VRAM.", label);
        GUILayout.Space(12);
        GUILayout.Label("Model memory lifecycle", title);
        releaseComfyMemory = GUILayout.Toggle(releaseComfyMemory, " Release ComfyUI models/cache after every image (also with the game's LLM)");
        GUILayout.Label("Vision mode always releases ComfyUI models before loading the LLM and after generating the image. Image submission waits for confirmed LLM unloading. Cancellation and errors also trigger cleanup. Reloading models adds time; server processes remain available.", label);
        GUILayout.Label("Use a dedicated LLM endpoint/config if the game's dialogue LLM must stay loaded. Other Ollama/LM Studio models are not unloaded. KoboldCPP admin switching affects its whole worker. ComfyUI cleanup affects its server cache and waits for its entire queue to become idle. CUDA-style allocator usage is checked; CPU/MPS backends report cleanup acknowledgement only.", label);
        GUILayout.Label("Portrait source/file overrides are in Character profiles. Explicit saved descriptions and live clothing take priority. First-use profile initialization uses this vision LLM when enabled; the manual profile-draft button still uses the game's LLM. No extra ComfyUI nodes are required.", label);
        if (GUILayout.Button("Save vision and generation settings", button, GUILayout.Height(44))) Try(() =>
        {
            ReadVisionSettings().Validate(visionEnabled);
            SaveSettings();
        });
    }

    private async void ListVisionModels()
    {
        if (visionListing) return;
        visionListing = true;
        try
        {
            VisionSettings snapshot = ReadVisionSettings(); snapshot.Validate(false);
            using var client = new VisionClient(snapshot);
            string[] models = await client.ListModelsAsync(lifetime.Token);
            if (this == null || closed) return;
            if (visionProvider != snapshot.Provider || visionAddress.Trim() != snapshot.Address) return;
            if (models.Length == 0) { status = "No models/configs found. Install a vision model in that server first."; return; }
            ShowChoices("Vision model / config (select one with image support)", models, value => visionModel = value);
            status = "Model list retrieved. Loading and unloading are checked when generating a scene.";
        }
        catch (Exception ex) { if (this != null && !closed) status = "Vision connection: " + ex.Message; }
        finally { visionListing = false; }
    }
}
