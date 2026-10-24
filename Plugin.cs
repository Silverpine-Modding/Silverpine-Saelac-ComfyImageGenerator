using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;
using System;
using System.IO;

namespace SilverpineMods.ComfyImageGenerator;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Silverpine.ModdingTools.Plugin.PluginGuid, "1.10.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "Saelac.Silverpine.ComfyImageGenerator";
    public const string PluginName = "Comfy Image Generator";
    public const string PluginVersion = "1.6.1";
    internal static ManualLogSource Log = null!;
    internal static ConfigFile Settings = null!;
    internal static ConfigEntry<string> Server = null!, BearerToken = null!;
    internal static ConfigEntry<int> TimeoutMinutes = null!;
    internal static ConfigEntry<ModelFamily> Family = null!;
    internal static ConfigEntry<bool> InitializeProfiles = null!;
    internal static ConfigEntry<int> LlmTimeoutSeconds = null!;
    internal static ConfigEntry<string> SceneGuidance = null!;
    internal static CharacterProfileStore Profiles = null!;
    internal static Profile Illustrious = null!, Anima = null!;
    internal static string PluginDirectory = "", ImagesDirectory = "";
    internal static Profile CurrentProfile => Family.Value == ModelFamily.Anima ? Anima : Illustrious;

    private void Awake()
    {
        Log = Logger;
        Settings = Config;
        PluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;
        ImagesDirectory = Path.Combine(PluginDirectory, "TemporaryImages");
        TemporaryImages.Clear(ImagesDirectory);
        Server = Config.Bind("Connection", "ServerUrl", "http://127.0.0.1:8188", "ComfyUI HTTP(S) base URL. Reverse-proxy subpaths are supported.");
        BearerToken = Config.Bind("Connection", "BearerToken", "", "Optional token for a self-hosted reverse proxy. Stored in this config file; never included in saved workflow metadata.");
        TimeoutMinutes = Config.Bind("Connection", "GenerationTimeoutMinutes", 20, new ConfigDescription("Overall wait limit, including queue time.", new AcceptableValueRange<int>(1, 120)));
        Family = Config.Bind("Generation", "ModelFamily", ModelFamily.Illustrious, "Selected generation profile.");
        InitializeProfiles = Config.Bind("Characters", "InitializeMissingProfilesWithLlm", true, "On scene generation, attempt to generate missing visual profiles for scene participants. Existing profiles are preserved.");
        LlmTimeoutSeconds = Config.Bind("Characters", "LlmTimeoutSeconds", 120, new ConfigDescription("Timeout per profile or scene-prompt LLM request.", new AcceptableValueRange<int>(10, 600)));
        SceneGuidance = Config.Bind("Generation", "SceneGuidance", "", "Optional art direction appended to scene prompt instructions, including LoRA trigger words if needed.");
        Profiles = new CharacterProfileStore(Path.Combine(PluginDirectory, "CharacterProfiles"));
        try { Profiles.LoadActive(); }
        catch (Exception ex) { Logger.LogWarning("Character profiles could not be loaded: " + ex.Message); }
        Illustrious = new Profile(Config, ModelFamily.Illustrious);
        Anima = new Profile(Config, ModelFamily.Anima);
        ModdingToolsMenu.RegisterSession(PluginGuid + ".main", PluginName, (_, session) => GeneratorWindow.Open(session), order: 335);
        InventoryModTools.RegisterSession(PluginGuid + ".inventory", PluginName, (_, session) => GeneratorWindow.Open(session), order: 335);
        new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"{PluginName} {PluginVersion} registered. Images: {ImagesDirectory}");
    }
    // Static menu registrations and managed jobs survive Silverpine's bootstrap-host destruction.
}

internal sealed class Profile
{
    internal readonly ModelFamily Family;
    internal readonly ConfigEntry<string> Model, Encoder, Vae, Sampler, Scheduler, WorkflowFile, LorasJson, DetailersJson, BasePositive, Negative;
    internal readonly ConfigEntry<int> Width, Height, Steps;
    internal readonly ConfigEntry<double> Cfg;
    internal readonly ConfigEntry<bool> EasyCache;
    internal readonly ConfigEntry<double> CacheThreshold, CacheStart, CacheEnd;
    internal readonly ConfigEntry<bool> AttentionCouple;
    internal readonly ConfigEntry<string> RegionsJson;
    internal readonly ConfigEntry<double> GlobalRegionStrength;
    internal Profile(ConfigFile config, ModelFamily family)
    {
        Family = family;
        string section = family.ToString();
        bool anima = family == ModelFamily.Anima;
        Model = config.Bind(section, "Model", anima ? "anima-base-v1.0.safetensors" : "", "Exact ComfyUI model filename (including a subfolder if present).");
        Encoder = config.Bind(section, "TextEncoder", "qwen_3_06b_base.safetensors", "Anima CLIPLoader filename.");
        Vae = config.Bind(section, "Vae", "qwen_image_vae.safetensors", "Anima VAELoader filename.");
        Width = config.Bind(section, "Width", 1024, "Image width, a multiple of 64 from 256 to 2048.");
        Height = config.Bind(section, "Height", 1024, "Image height, a multiple of 64 from 256 to 2048.");
        Steps = config.Bind(section, "Steps", anima ? 30 : 28, "Sampling steps, 1-150. Use settings appropriate to your model variant.");
        Cfg = config.Bind(section, "Cfg", anima ? 4.0 : 5.0, "Guidance scale, 0-30.");
        Sampler = config.Bind(section, "Sampler", "euler", "ComfyUI sampler name.");
        Scheduler = config.Bind(section, "Scheduler", anima ? "simple" : "normal", "ComfyUI scheduler name.");
        WorkflowFile = config.Bind(section, "WorkflowFile", "", "Optional API-format workflow template path, absolute or relative to the plugin directory. See README for placeholders.");
        LorasJson = config.Bind(section, "Loras", "[]", "Ordered list of LoRAs, with Name, ModelStrength and ClipStrength. Edit using the generator window.");
        DetailersJson = config.Bind(section, "Detailers", "[]", "Ordered optional Impact Pack detailer passes. Configure detectors, sampling and prompts in the Detailers tab.");
        AttentionCouple = config.Bind(section, "AttentionCouple", false, "Optional Illustrious regional prompts. Requires ComfyUI-ppm; ignored for Anima.");
        RegionsJson = config.Bind(section, "CharacterRegions", "[]", "Character rectangles, dialogue indices and editable prompts. Detailer targets use stable region IDs.");
        GlobalRegionStrength = config.Bind(section, "GlobalRegionStrength", .3, "Shared background mask strength in gaps outside character rectangles, 0.01-1. Keep identity traits out of the shared prompt and style prefix.");
        BasePositive = config.Bind(section, "BasePositivePrompt", "", "Reusable styling prefix prepended to every positive prompt, including regeneration and detailer overrides. Applied directly without an LLM rewrite. Blank disables it.");
        Negative = config.Bind(section, "NegativePrompt", "low quality, blurry, text, watermark", "Negative prompt used for scene generation.");
        EasyCache = config.Bind(section, "EasyCache", false, "Enable the server's native experimental EasyCache node. Model compatibility/quality varies.");
        CacheThreshold = config.Bind(section, "CacheThreshold", 0.2, "EasyCache reuse threshold, 0-3.");
        CacheStart = config.Bind(section, "CacheStart", 0.15, "Sampling fraction at which EasyCache starts, 0-1.");
        CacheEnd = config.Bind(section, "CacheEnd", 0.95, "Sampling fraction at which EasyCache ends, 0-1.");
    }
}
