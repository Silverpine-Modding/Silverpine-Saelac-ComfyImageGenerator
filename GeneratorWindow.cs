using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silverpine.ModdingTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow : ModToolBehaviour
{
    private static GeneratorWindow? instance;
    internal static bool IsOpen => instance != null && !instance.closed;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const float ViewerImageHeight = 670, PromptEditorTop = 686, PromptEditorHeight = 440;
    private int page = 1;
    private string status = "Set your ComfyUI server, connect, and choose the installed model.";
    private string host = "127.0.0.1", port = "8188", basePath = "", positive = "", basePositive = "", negative = "", guidance = "";
    private bool https, editingPrompt, easyCache, closed;
    private string model = "", encoder = "", vae = "", sampler = "", scheduler = "", customWorkflow = "";
    private string width = "1024", height = "1024", steps = "30", cfg = "4", seed = "-1", threshold = "0.2", cacheStart = "0.15", cacheEnd = "0.95";
    private ModelFamily family;
    private readonly List<LoraDraft> loras = new();
    private sealed class LoraDraft { internal string Name = "", Model = "1", Clip = "1"; }
    private JObject info = new();
    private string infoAddress = "";
    private Task<JObject>? connectionTask;
    private CancellationTokenSource lifetime = new();
    private bool profileBusy;
    private string[] choices = Array.Empty<string>();
    private Action<string>? choose;
    private string choiceSearch = "", choiceTitle = "";
    private Vector2 scroll, listScroll, statusScroll, viewerScroll, promptScroll, basePositiveScroll, basePositiveSettingsScroll, negativePromptScroll;
    private Texture2D? preview, background;
    private GUISkin? darkSkin;
    private readonly List<Texture2D> surfaces = new();
    private string previewPath = "";
    private int lastPromptRevision;
    private GUIStyle label = null!, title = null!, button = null!, field = null!, promptField = null!, panel = null!;
    private Player? inputOwner;
    private CanvasGroup? blockedDialogue;
    private bool originalDialogueInteractable;
    private DialogBox? blockedDialogOwner;
    private List<SceneCharacter> characters = new();
    private int selectedCharacter;
    private string characterText = "", presetName = "My character profiles";
    private string saveFolder = "", saveName = "", saveSource = "", overwriteDestination = "";

    internal static void Open(ModToolSession session) { Create(session).page = 1; }
    internal static void OpenScene(NeuralNPC npc)
    {
        GeneratorWindow window = Create(null);
        window.OpenCastPicker(npc);
    }
    private static GeneratorWindow Create(ModToolSession? session)
    {
        if (instance != null && !instance.closed)
        {
            if (session != null) instance.AttachSession(session);
            return instance;
        }
        GameObject? root = null;
        try
        {
            root = ModUi.CreateOverlayCanvas("Comfy Scene Viewer", 12000);
            Image shield = root.AddComponent<Image>();
            shield.color = new Color(0, 0, 0, 0.82f);
            shield.raycastTarget = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var window = root.AddComponent<GeneratorWindow>();
            instance = window;
            if (session != null) window.AttachSession(session);
            else if (Player.Instance != null)
            {
                window.inputOwner = Player.Instance;
                window.inputOwner.SetInputBlock(true);
            }
            // Also block keyboard submission to the underlying dialogue while editing.
            if (DialogBox.Instance != null && DialogBox.Instance.isOpen)
            {
                var group = HarmonyLib.AccessTools.Field(typeof(DialogBox), "globalCanvasGroup").GetValue(DialogBox.Instance) as CanvasGroup;
                if (group != null)
                {
                    window.blockedDialogue = group;
                    window.originalDialogueInteractable = group.interactable;
                    window.blockedDialogOwner = DialogBox.Instance;
                    HarmonyLib.AccessTools.Method(typeof(DialogBox), "SetGlobalCanvasInputBlock").Invoke(window.blockedDialogOwner, new object[] { true });
                    group.interactable = false;
                }
            }
            window.LoadConnection();
            window.family = Plugin.Family.Value;
            window.LoadProfile();
            window.guidance = Plugin.SceneGuidance.Value;
            return window;
        }
        catch
        {
            if (root != null) Destroy(root);
            session?.Close();
            throw;
        }
    }

    private void LoadConnection()
    {
        if (Uri.TryCreate(Plugin.Server.Value, UriKind.Absolute, out Uri? address))
        { host = address.Host; port = address.Port.ToString(Inv); https = address.Scheme == "https"; basePath = address.AbsolutePath.Trim('/'); }
    }
    private string Address()
    {
        int number = Int(port, "Port");
        if (number < 1 || number > 65535) throw new ArgumentException("Port must be from 1 to 65535.");
        if (string.IsNullOrWhiteSpace(host) || host.Contains("/") || host.Contains("@")) throw new ArgumentException("Enter the server IP or hostname without http:// or a path.");
        return ComfyClient.NormalizeAddress(new UriBuilder(https ? "https" : "http", host.Trim(), number, basePath.Trim('/')).Uri.AbsoluteUri).AbsoluteUri;
    }
    private void LoadProfile()
    {
        positive = "";
        promptCharacterKeys.Clear();
        Profile p = family == ModelFamily.Anima ? Plugin.Anima : Plugin.Illustrious;
        model = p.Model.Value; encoder = p.Encoder.Value; vae = p.Vae.Value;
        width = p.Width.Value.ToString(Inv); height = p.Height.Value.ToString(Inv); steps = p.Steps.Value.ToString(Inv); cfg = p.Cfg.Value.ToString(Inv);
        sampler = p.Sampler.Value; scheduler = p.Scheduler.Value; customWorkflow = p.WorkflowFile.Value; negative = p.Negative.Value;
        basePositive = p.BasePositive.Value;
        basePositiveScroll = basePositiveSettingsScroll = Vector2.zero;
        easyCache = p.EasyCache.Value; threshold = p.CacheThreshold.Value.ToString(Inv); cacheStart = p.CacheStart.Value.ToString(Inv); cacheEnd = p.CacheEnd.Value.ToString(Inv);
        loras.Clear();
        try
        {
            foreach (LoraSetting lora in JsonConvert.DeserializeObject<List<LoraSetting>>(p.LorasJson.Value) ?? new())
                loras.Add(new LoraDraft { Name = lora.Name, Model = lora.ModelStrength.ToString(Inv), Clip = lora.ClipStrength.ToString(Inv) });
        }
        catch (Exception) { status = "The saved LoRA list is invalid. Rebuild it in settings and Save settings."; }
        LoadDetailers(p);
        lastPromptRevision = 0;
    }
    private GenerationSettings Snapshot(string prompt) => new()
    {
        Family = family, Model = model.Trim(), TextEncoder = encoder.Trim(), Vae = vae.Trim(), Positive = prompt, BasePositive = basePositive, Negative = negative,
        SceneCharacterKeys = promptCharacterKeys.ToList(),
        Width = Int(width, "Width"), Height = Int(height, "Height"), Steps = Int(steps, "Steps"), Cfg = Number(cfg, "CFG"),
        Seed = long.TryParse(seed, NumberStyles.Integer, Inv, out long value) ? value : throw new ArgumentException("Seed must be an integer or -1 for random."),
        Sampler = sampler.Trim(), Scheduler = scheduler.Trim(),
        WorkflowFile = string.IsNullOrWhiteSpace(customWorkflow) ? "" : Path.GetFullPath(Path.IsPathRooted(customWorkflow) ? customWorkflow : Path.Combine(Plugin.PluginDirectory, customWorkflow)),
        Loras = loras.Select(l => new LoraSetting { Name = l.Name, ModelStrength = Number(l.Model, "LoRA model strength"), ClipStrength = Number(l.Clip, "LoRA CLIP strength") }).ToList(),
        Detailers = detailers.Select(d => d.Read()).ToList(),
        EasyCache = easyCache, CacheThreshold = Number(threshold, "Cache threshold"), CacheStart = Number(cacheStart, "Cache start"), CacheEnd = Number(cacheEnd, "Cache end")
    };
    private static int Int(string text, string name) => int.TryParse(text, NumberStyles.Integer, Inv, out int value) ? value : throw new ArgumentException(name + " must be an integer.");
    private static double Number(string text, string name) => double.TryParse(text, NumberStyles.Float, Inv, out double value) && !double.IsNaN(value) && !double.IsInfinity(value)
        ? value : throw new ArgumentException(name + " must be a finite number (use a decimal point).");
    private void SaveSettings()
    {
        GenerationSettings s = Snapshot(string.IsNullOrWhiteSpace(basePositive) ? "settings validation" : "");
        // Allow the initial connection to be saved before the model has been selected.
        if (s.Model.Length == 0) s.Model = "unselected";
        s.Validate();
        string address = Address();
        Profile p = family == ModelFamily.Anima ? Plugin.Anima : Plugin.Illustrious;
        bool save = Plugin.Settings.SaveOnConfigSet;
        Plugin.Settings.SaveOnConfigSet = false;
        try
        {
            Plugin.Server.Value = address; Plugin.Family.Value = family; Plugin.SceneGuidance.Value = guidance;
            p.Model.Value = model.Trim(); p.Encoder.Value = encoder.Trim(); p.Vae.Value = vae.Trim();
            p.Width.Value = s.Width; p.Height.Value = s.Height; p.Steps.Value = s.Steps; p.Cfg.Value = s.Cfg;
            p.Sampler.Value = s.Sampler; p.Scheduler.Value = s.Scheduler; p.WorkflowFile.Value = customWorkflow.Trim(); p.Negative.Value = negative;
            p.BasePositive.Value = basePositive;
            p.LorasJson.Value = JsonConvert.SerializeObject(s.Loras); p.EasyCache.Value = easyCache;
            p.DetailersJson.Value = JsonConvert.SerializeObject(s.Detailers);
            p.CacheThreshold.Value = s.CacheThreshold; p.CacheStart.Value = s.CacheStart; p.CacheEnd.Value = s.CacheEnd;
        }
        finally { Plugin.Settings.SaveOnConfigSet = save; }
        Plugin.Settings.Save();
        status = "Settings saved.";
    }

    private void BeginScene()
    {
        Try(() =>
        {
            if (GenerationService.Busy || profileBusy) throw new InvalidOperationException("Wait for the current request to finish.");
            SceneSnapshot scene = SelectedLiveScene();
            GenerationSettings settings = Snapshot("Scene prompt pending");
            settings.SceneCharacterKeys = scene.Characters.Select(c => c.Key).ToList();
            SceneSelection.ValidateGeneration(settings);
            // Preflight the exact graph before spending an LLM request. Prompts
            // are temporary only in this private generation snapshot.
            settings.Validate();
            SaveSettings();
            ReleasePreview();
            string sceneGuidance = guidance;
            GenerationService.Start(settings, Address(), Plugin.BearerToken.Value, Plugin.ImagesDirectory, Plugin.TimeoutMinutes.Value,
                (progress, cancel) => SceneCapture.GeneratePrompt(scene, settings.Family, sceneGuidance, progress, cancel, notice => settings.PromptNotice = notice));
            page = 0; SetPromptEditing(false); status = "";
        });
        if (!GenerationService.Busy && model.Length == 0) page = 1;
    }
    private void Regenerate()
    {
        Try(() =>
        {
            GenerationSettings settings = Snapshot(positive);
            SceneSelection.ValidateGeneration(settings);
            settings.Validate();
            SaveSettings();
            ReleasePreview();
            GenerationService.Start(settings, Address(), Plugin.BearerToken.Value, Plugin.ImagesDirectory, Plugin.TimeoutMinutes.Value);
            SetPromptEditing(false); page = 0; status = "";
        });
    }
    private void Connect()
    {
        Try(() =>
        {
            string address = Address();
            infoAddress = address;
            info = new JObject();
            string token = Plugin.BearerToken.Value;
            CancellationToken cancel = lifetime.Token;
            connectionTask = Task.Run(async () => { using var client = new ComfyClient(address, token); return await client.CheckConnectionAsync(cancel); });
            status = "Connecting and checking nodes, models, LoRAs and detectors...";
        });
    }
    private void Update()
    {
        if (closed) return;
        if (connectionTask != null && connectionTask.IsCompleted)
        {
            try { info = connectionTask.GetAwaiter().GetResult(); status = "Connected. Installed nodes, models, LoRAs and detectors checked. Optional features show their requirements below."; }
            catch (Exception ex) { status = "Connection failed: " + ex.Message; }
            connectionTask = null;
        }
        int generatedRevision = GenerationService.PromptRevision;
        if (generatedRevision != lastPromptRevision)
        {
            lastPromptRevision = generatedRevision;
            if (GenerationService.PromptFamily == family)
            {
                positive = GenerationService.Prompt; promptScroll = Vector2.zero;
                promptCharacterKeys = GenerationService.PromptCharacterKeys.ToList();
            }
        }
        GenerationResult? result = GenerationService.Result;
        if (result != null && result.Images.Count > 0 && previewPath != result.Images[0])
        {
            ReleasePreview();
            previewPath = result.Images[0];
            Try(() =>
            {
                byte[] bytes = File.ReadAllBytes(previewPath);
                ComfyClient.ImageExtension(bytes);
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(texture, bytes, true)) throw new InvalidDataException("Unity could not decode the returned image.");
                    if (texture.width > SystemInfo.maxTextureSize || texture.height > SystemInfo.maxTextureSize) throw new InvalidDataException("Image is larger than the GPU's texture limit.");
                    texture.filterMode = FilterMode.Bilinear;
                    preview = texture;
                }
                catch { Destroy(texture); throw; }
            });
        }
    }

    private void OnGUI()
    {
        if (closed) return;
        using ModGuiScope scope = ModGui.BeginScaled(1600, 900);
        GUI.depth = -2000;
        GUI.enabled = true; GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
        if (background == null) Styles();
        GUISkin previousSkin = GUI.skin;
        GUI.skin = darkSkin;
        try { DrawGui(); }
        finally { GUI.skin = previousSkin; }
    }
    private void DrawGui()
    {
        GUI.Box(new Rect(12, 12, 1576, 876), GUIContent.none, panel);
        GUI.Label(new Rect(32, 24, 640, 40), "Comfy scene generator  /  " + family, title);
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        if (GUI.Button(new Rect(755, 24, 275, 38), "Choose characters", button)) OpenCastPicker(NeuralNPC.currentActiveDialogNeuralNPC);
        GUI.enabled = true;
        if (GUI.Button(new Rect(1040, 24, 165, 38), "Image viewer", button)) { page = 0; choose = null; }
        if (GUI.Button(new Rect(1215, 24, 165, 38), "Settings", button)) { page = 1; choose = null; }
        if (GUI.Button(new Rect(1390, 24, 178, 38), "Close", button)) Close();
        if (closed) return;
        if (choose != null) DrawChoices();
        else if (page == 0) DrawViewer();
        else if (page == 5) DrawSaveImage();
        else if (page == 7) DrawCastPicker();
        else DrawSettings();
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        { if (choose != null) choose = null; else if (page == 5) { page = 0; overwriteDestination = ""; } else if (editingPrompt) SetPromptEditing(false); else Close(); Event.current.Use(); }
    }
    private void DrawViewer()
    {
        // Keep exactly the same image rectangle when editing. The editor extends
        // the page below the image instead of taking space away from it.
        float contentHeight = editingPrompt ? PromptEditorTop + PromptEditorHeight : ViewerImageHeight;
        viewerScroll = GUI.BeginScrollView(new Rect(20, 78, 1568, ViewerImageHeight), viewerScroll,
            new Rect(0, 0, 1536, contentHeight), false, editingPrompt);
        Rect imageArea = new(0, 0, 1536, ViewerImageHeight);
        GUI.Box(imageArea, GUIContent.none, panel);
        if (preview != null)
        {
            GUI.DrawTexture(imageArea, preview, ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(8, 2, 400, 28), $"{preview.width} × {preview.height}", label);
        }
        else GUI.Label(new Rect(38, 202, 1460, 100), GenerationService.Busy ? GenerationService.Status :
            "Generate a scene from the dialogue, or enter a prompt below. The temporary image is deleted when this viewer closes.", title);
        if (editingPrompt)
        {
            GUI.BeginGroup(new Rect(0, PromptEditorTop, 1536, PromptEditorHeight));
            DrawPromptEditor();
            GUI.EndGroup();
        }
        GUI.EndScrollView();
        // These controls remain reachable while the image/editor page scrolls.
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        if (GUI.Button(new Rect(32, 758, 290, 42), "Generate current dialogue", button)) OpenCastPicker(NeuralNPC.currentActiveDialogNeuralNPC);
        if (GUI.Button(new Rect(335, 758, 290, 42), editingPrompt ? "Hide prompt editor" : "Regenerate / edit prompt", button)) SetPromptEditing(!editingPrompt);
        GUI.enabled = !GenerationService.Busy && preview != null;
        if (GUI.Button(new Rect(638, 758, 290, 42), "Save As...", button)) SaveImage();
        GUI.enabled = GenerationService.Busy;
        if (GUI.Button(new Rect(941, 758, 290, 42), "Cancel generation", button)) GenerationService.StopWaiting();
        GUI.enabled = true;
        DrawStatus(new Rect(32, 812, 1536, 62));
    }

    private void SetPromptEditing(bool enabled)
    {
        editingPrompt = enabled;
        // Reveal all editor controls on opening; return to the complete image on closing/generating.
        viewerScroll = enabled ? new Vector2(0, PromptEditorTop + PromptEditorHeight - ViewerImageHeight) : Vector2.zero;
    }

    private void DrawPromptEditor()
    {
        // Reserve explicit space for every control: long text must never squeeze the action button.
        GUI.Label(new Rect(0, 0, 1536, 28), "Base positive prompt — styling prepended to every prompt; saved for " + family + " when you generate.", label);
        basePositive = ScrollablePrompt(new Rect(0, 34, 1536, 72), basePositive, ref basePositiveScroll);
        GUI.Label(new Rect(0, 114, 1536, 28), "Scene prompt — sent after the base positive prompt above. Scroll the page up for the full-size image.", label);
        positive = ScrollablePrompt(new Rect(0, 148, 1536, 176), positive, ref promptScroll);
        GUI.Label(new Rect(0, 334, 105, 28), "Negative", label);
        negative = ScrollablePrompt(new Rect(110, 332, 1054, 58), negative, ref negativePromptScroll);
        GUI.Label(new Rect(1188, 330, 348, 26), "Seed (-1 = random)", label);
        seed = GUI.TextField(new Rect(1188, 358, 348, 32), seed, 32, field);
        float generateY = 398;
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        if (GUI.Button(new Rect(0, generateY, 420, 42), "Regenerate with edited prompt", button)) Regenerate();
        GUI.enabled = true;
    }

    private string ScrollablePrompt(Rect viewport, string text, ref Vector2 position)
    {
        // Word wrapping gives a vertical-only editor. Measure the full text, including
        // blank character/environment paragraphs and space for the final caret line.
        float textWidth = Mathf.Max(1, viewport.width - GUI.skin.verticalScrollbar.fixedWidth - Mathf.Max(0, GUI.skin.verticalScrollbar.margin.left) - 4);
        float textHeight = Mathf.Max(viewport.height - 1, promptField.CalcHeight(new GUIContent(text + "\n"), textWidth));
        position.x = 0;
        position = GUI.BeginScrollView(viewport, position, new Rect(0, 0, textWidth, textHeight), false, true);
        text = GUI.TextArea(new Rect(0, 0, textWidth, textHeight), text, 32000, promptField);
        GUI.EndScrollView();
        return text;
    }
    private void SaveImage() => Try(() =>
    {
        if (preview == null || !File.Exists(previewPath)) throw new IOException("Generate an image before saving.");
        saveSource = previewPath;
        if (saveFolder.Length == 0) saveFolder = SaveImageDialog.DefaultFolder();
        saveName = SaveImageDialog.DefaultName(Path.GetExtension(saveSource));
        overwriteDestination = ""; page = 5; status = "Choose a folder and name for your permanent copy.";
    });

    private void DrawSaveImage()
    {
        GUILayout.BeginArea(new Rect(60, 95, 1480, 665));
        GUILayout.Label("Save image permanently", title);
        GUILayout.Label("Choose a folder and file name on this computer. New folders are created when you save. The temporary image stays in the viewer until you close it.", label);
        string oldFolder = saveFolder, oldName = saveName;
        saveFolder = Row("Folder", saveFolder);
        saveName = Row("File name (" + Path.GetExtension(saveSource) + ")", saveName);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Pictures", button, GUILayout.Height(38))) saveFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (GUILayout.Button("Documents", button, GUILayout.Height(38))) saveFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (GUILayout.Button("Desktop", button, GUILayout.Height(38))) saveFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        GUILayout.EndHorizontal();
        if (oldFolder != saveFolder || oldName != saveName) overwriteDestination = "";
        GUILayout.Space(20);
        if (overwriteDestination.Length != 0)
        {
            GUILayout.Label("A file already exists at: " + overwriteDestination, label);
            GUILayout.Label("Replace it with this image, or change the name to keep both files.", label);
            if (GUILayout.Button("Replace existing file", button, GUILayout.Height(44))) WriteSavedImage(true);
        }
        else if (GUILayout.Button("Save permanent copy", button, GUILayout.Height(44))) WriteSavedImage(false);
        if (GUILayout.Button("Back to image / cancel", button, GUILayout.Height(40))) { page = 0; overwriteDestination = ""; }
        GUILayout.EndArea();
        DrawStatus(new Rect(60, 790, 1480, 84));
    }

    private void WriteSavedImage(bool replace) => Try(() =>
    {
        if (!File.Exists(saveSource)) throw new IOException("The temporary image is no longer available. Generate an image and choose Save As again.");
        string destination = SaveImageDialog.Destination(saveFolder, saveName, Path.GetExtension(saveSource), Plugin.ImagesDirectory);
        bool confirmed = replace && string.Equals(destination, overwriteDestination, StringComparison.OrdinalIgnoreCase);
        if (File.Exists(destination) && !confirmed) { overwriteDestination = destination; return; }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        TemporaryImages.SavePermanent(Plugin.ImagesDirectory, saveSource, destination, confirmed);
        overwriteDestination = ""; page = 0;
        status = "Saved permanently: " + destination;
    });

    private void DrawSettings()
    {
        GUILayout.BeginArea(new Rect(32, 80, 1536, 708));
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Connection, model & LoRAs", button, GUILayout.Height(36))) page = 1;
        if (GUILayout.Button("Detailers", button, GUILayout.Height(36))) page = 3;
        if (GUILayout.Button("Character profiles & presets", button, GUILayout.Height(36))) { page = 2; Try(RefreshCharacters); }
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll);
        if (page == 2) DrawCharacters(); else if (page == 3) DrawDetailers(); else DrawModelSettings();
        GUILayout.EndScrollView();
        GUI.enabled = true;
        GUILayout.EndArea();
        DrawStatus(new Rect(32, 800, 1536, 74));
    }
    private void DrawModelSettings()
    {
        GUILayout.Label("ComfyUI server", title);
        GUILayout.BeginHorizontal();
        GUILayout.Label("IP / hostname", label, GUILayout.Width(160)); host = GUILayout.TextField(host, field, GUILayout.Width(400));
        GUILayout.Label("Port", label, GUILayout.Width(60)); port = GUILayout.TextField(port, field, GUILayout.Width(110));
        https = GUILayout.Toggle(https, " HTTPS", GUILayout.Width(110));
        GUI.enabled &= connectionTask == null;
        if (GUILayout.Button("Connect / refresh lists", button, GUILayout.Width(280))) Connect();
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        GUILayout.EndHorizontal();
        basePath = Row("Proxy base path (optional)", basePath);
        GUILayout.Space(12);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Model family", label, GUILayout.Width(250));
        DrawFamilyButton(ModelFamily.Illustrious);
        DrawFamilyButton(ModelFamily.Anima);
        GUILayout.EndHorizontal();
        GUILayout.Label("Illustrious requires CheckpointLoaderSimple. Anima requires UNETLoader, CLIPLoader and VAELoader. Both need core text encoding, latent, sampler, decode and preview nodes. Update ComfyUI if these are missing.", label);
        string modelUnavailable = NodeAvailability(ModelNodes(family));
        if (modelUnavailable.Length != 0) GUILayout.Label(modelUnavailable, label);
        GUILayout.Label("Choose buttons unlock after connection when the server reports installed choices. Refresh lists after adding models or nodes.", label);
        model = PickRow("Checkpoint / diffusion model", model, family == ModelFamily.Anima ? "UNETLoader" : "CheckpointLoaderSimple", family == ModelFamily.Anima ? "unet_name" : "ckpt_name", v => model = v);
        if (family == ModelFamily.Anima)
        {
            encoder = PickRow("Text encoder", encoder, "CLIPLoader", "clip_name", v => encoder = v);
            vae = PickRow("VAE", vae, "VAELoader", "vae_name", v => vae = v);
        }
        GUILayout.BeginHorizontal();
        Small("Width", ref width); Small("Height", ref height); Small("Steps", ref steps); Small("CFG", ref cfg); Small("Seed", ref seed);
        GUILayout.EndHorizontal();
        sampler = PickRow("Sampler", sampler, "KSampler", "sampler_name", v => sampler = v);
        scheduler = PickRow("Scheduler", scheduler, "KSampler", "scheduler", v => scheduler = v);
        GUILayout.Label("Base positive prompt (always prepended)", title);
        basePositive = ScrollablePrompt(GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(96), GUILayout.ExpandWidth(true)), basePositive, ref basePositiveSettingsScroll);
        GUILayout.Label("Reusable style tags or wording, added directly before every scene, regeneration and detailer positive prompt. Saved separately for Illustrious and Anima. Leave blank to disable.", label);
        negative = Row("Negative prompt", negative);
        guidance = Row("Scene direction / LoRA triggers", guidance);
        GUILayout.Label("LoRAs (applied in list order; use files compatible with the selected model)", title);
        GUILayout.Label("An SDXL / Illustrious LoRA cannot guide Anima. The filename list does not verify model architecture; check the LoRA's training base. LoRAs apply to the entire image.", label);
        string loraNode = family == ModelFamily.Anima ? "LoraLoaderModelOnly" : "LoraLoader";
        string loraUnavailable = NodeAvailability(loraNode);
        if (loraUnavailable.Length == 0 && Workflow.Choices(info, loraNode, "lora_name").Length == 0)
            loraUnavailable = "No LoRA files reported by this server. Add compatible files and refresh lists.";
        if (loraUnavailable.Length == 0 && !string.IsNullOrWhiteSpace(customWorkflow))
            loraUnavailable = "Put LoRAs inside the custom workflow, or clear its path to add them here.";
        GUILayout.Label("Requires " + loraNode + " (ComfyUI core). " + (loraUnavailable.Length == 0 ? "Available on the connected server." : loraUnavailable), label);
        for (int i = 0; i < loras.Count; i++)
        {
            LoraDraft lora = loras[i];
            GUILayout.BeginHorizontal();
            lora.Name = GUILayout.TextField(lora.Name, field, GUILayout.MinWidth(380));
            bool canEdit = GUI.enabled;
            GUI.enabled = canEdit && loraUnavailable.Length == 0;
            if (GUILayout.Button("Choose", button, GUILayout.Width(100))) Pick("LoRA", family == ModelFamily.Anima ? "LoraLoaderModelOnly" : "LoraLoader", "lora_name", v => lora.Name = v);
            GUI.enabled = canEdit;
            GUILayout.Label("Model", label, GUILayout.Width(70)); lora.Model = GUILayout.TextField(lora.Model, field, GUILayout.Width(90));
            if (family != ModelFamily.Anima) { GUILayout.Label("CLIP", label, GUILayout.Width(60)); lora.Clip = GUILayout.TextField(lora.Clip, field, GUILayout.Width(90)); }
            if (GUILayout.Button("Remove", button, GUILayout.Width(100))) { loras.RemoveAt(i); i--; }
            GUILayout.EndHorizontal();
        }
        bool controlsEnabled = GUI.enabled;
        GUI.enabled = controlsEnabled && loraUnavailable.Length == 0;
        if (GUILayout.Button("+ Add LoRA", button, GUILayout.Width(220))) loras.Add(new LoraDraft());
        GUI.enabled = controlsEnabled;
        GUILayout.Label("Turbo checkpoints and Turbo LoRAs use this same model/LoRA list. Set steps and CFG for the exact variant you installed.", label);
        string cacheUnavailable = NodeAvailability("EasyCache");
        if (cacheUnavailable.Length == 0 && !string.IsNullOrWhiteSpace(customWorkflow)) cacheUnavailable = "Use an EasyCache node inside the custom workflow, or clear its path to enable this option.";
        GUILayout.Label("EasyCache requires the native EasyCache node (current ComfyUI core). " + (cacheUnavailable.Length == 0 ? "Available on the connected server." : cacheUnavailable), label);
        GUI.enabled = controlsEnabled && (easyCache || cacheUnavailable.Length == 0);
        easyCache = GUILayout.Toggle(easyCache, " Enable native EasyCache (experimental)");
        GUI.enabled = controlsEnabled;
        if (easyCache)
        {
            GUILayout.BeginHorizontal(); Small("Threshold", ref threshold); Small("Start fraction", ref cacheStart); Small("End fraction", ref cacheEnd); GUILayout.EndHorizontal();
        }
        customWorkflow = Row("Custom API workflow (optional)", customWorkflow);
        GUILayout.Label("Leave custom workflow blank for the built-in preset. Custom API workflows must contain their own LoRA/cache/detailer nodes. All required node types are checked before generation.", label);
        if (GUILayout.Button("Save settings", button, GUILayout.Height(38))) Try(SaveSettings);
    }

    private string Row(string caption, string value)
    {
        GUILayout.BeginHorizontal(); GUILayout.Label(caption, label, GUILayout.Width(300));
        value = GUILayout.TextField(value, field); GUILayout.EndHorizontal(); return value;
    }
    private void Small(string caption, ref string value)
    { GUILayout.Label(caption, label, GUILayout.Width(125)); value = GUILayout.TextField(value, field, GUILayout.Width(130)); }
    private string PickRow(string caption, string value, string node, string input, Action<string> selected)
    {
        GUILayout.BeginHorizontal(); GUILayout.Label(caption, label, GUILayout.Width(300)); value = GUILayout.TextField(value, field);
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && NodeAvailability(node).Length == 0 && Workflow.Choices(info, node, input).Length > 0;
        if (GUILayout.Button("Choose", button, GUILayout.Width(110))) Pick(caption, node, input, selected);
        GUI.enabled = wasEnabled;
        GUILayout.EndHorizontal(); return value;
    }
    private void Pick(string caption, string node, string input, Action<string> selected) => Try(() =>
    {
        if (Address() != infoAddress) throw new InvalidOperationException("Connect to this server first to load its lists.");
        string[] values = Workflow.Choices(info, node, input);
        if (values.Length == 0) throw new InvalidOperationException("No choices returned for " + caption + ". Check installed files/nodes and refresh the connection.");
        ShowChoices(caption, values, selected);
    });
    private void ShowChoices(string caption, string[] values, Action<string> selected)
    { choiceTitle = caption; choices = values; choose = selected; choiceSearch = ""; listScroll = Vector2.zero; }
    private void DrawChoices()
    {
        GUILayout.BeginArea(new Rect(60, 90, 1480, 760));
        GUILayout.Label(choiceTitle, title);
        choiceSearch = GUILayout.TextField(choiceSearch, field);
        listScroll = GUILayout.BeginScrollView(listScroll);
        foreach (string choice in choices.Where(c => c.IndexOf(choiceSearch, StringComparison.OrdinalIgnoreCase) >= 0))
            if (GUILayout.Button(choice, button, GUILayout.Height(36))) { Action<string>? callback = choose; choose = null; Try(() => callback?.Invoke(choice)); break; }
        GUILayout.EndScrollView();
        if (GUILayout.Button("Back", button, GUILayout.Height(38))) choose = null;
        GUILayout.EndArea();
    }

    private void RefreshCharacters()
    {
        characters = SceneCapture.AvailableCharacters();
        foreach (var saved in Plugin.Profiles.Profiles)
            if (!characters.Any(c => c.Key == saved.Key)) characters.Add(new SceneCharacter { Key = saved.Key, Name = saved.Value.Name, Description = saved.Value.Description });
        selectedCharacter = Mathf.Clamp(selectedCharacter, 0, Math.Max(0, characters.Count - 1));
        LoadCharacter();
    }
    private void LoadCharacter()
    {
        if (characters.Count == 0) { characterText = ""; return; }
        SceneCharacter character = characters[selectedCharacter];
        characterText = Plugin.Profiles.Description(character.Key) ?? character.Description;
    }
    private void DrawCharacters()
    {
        GUILayout.Label("Character visual profiles", title);
        GUILayout.Label("Profiles control appearance in generated scenes and do not change the game's character data. The latest dialogue supplies actions and expressions.", label);
        bool initialize = GUILayout.Toggle(Plugin.InitializeProfiles.Value, " On first scene use, ask the LLM to initialize missing participant profiles");
        if (initialize != Plugin.InitializeProfiles.Value) Plugin.InitializeProfiles.Value = initialize;
        if (characters.Count == 0) GUILayout.Label("Load a game to edit character profiles, or load a saved preset below.", label);
        else
        {
            SceneCharacter character = characters[selectedCharacter];
            GUILayout.BeginHorizontal();
            GUILayout.Label(character.Name, title);
            if (GUILayout.Button("Choose character", button, GUILayout.Width(250)))
                ShowChoices("Character", characters.Select(c => c.Name + "  [" + c.Key + "]").ToArray(), v => { selectedCharacter = Array.IndexOf(characters.Select(c => c.Name + "  [" + c.Key + "]").ToArray(), v); LoadCharacter(); });
            GUILayout.EndHorizontal();
            characterText = GUILayout.TextArea(characterText, 8000, field, GUILayout.Height(245));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save character profile", button, GUILayout.Height(38))) Try(() => { Plugin.Profiles.Set(character.Key, character.Name, characterText); status = "Saved profile for " + character.Name + "."; });
            if (GUILayout.Button("Ask LLM for profile draft", button, GUILayout.Height(38))) GenerateProfileDraft(character);
            if (GUILayout.Button("Reset to game description", button, GUILayout.Height(38))) Try(() => { Plugin.Profiles.Reset(character.Key); characterText = character.Description; status = "Using game description; first-use initialization may run again."; });
            GUILayout.EndHorizontal();
            GUILayout.Label("LLM drafts appear above for editing. Save character profile to keep the draft. Existing saved descriptions are never automatically overwritten.", label);
        }
        GUILayout.Space(18);
        GUILayout.Label("Profile presets", title);
        presetName = Row("Preset name", presetName);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save all saved profiles as preset", button, GUILayout.Height(38))) Try(() => { Plugin.Profiles.SavePreset(presetName); status = "Saved preset: " + presetName + ". Unsaved editor text is not included."; });
        if (GUILayout.Button("Choose and load preset", button, GUILayout.Height(38))) Try(() =>
        {
            string[] presets = Plugin.Profiles.Presets();
            if (presets.Length == 0) throw new InvalidOperationException("No saved presets yet.");
            ShowChoices("Load profile preset (replaces active profiles)", presets, name => { Plugin.Profiles.LoadPreset(name); presetName = name; RefreshCharacters(); status = "Loaded " + name + ". Previous profiles kept in before-preset-load.json."; });
        });
        GUILayout.EndHorizontal();
        GUILayout.Label("Presets are shareable JSON files in the plugin's CharacterProfiles/Presets folder. Put another preset there, then choose and load it.", label);
    }
    private async void GenerateProfileDraft(SceneCharacter character)
    {
        if (profileBusy || GenerationService.Busy) return;
        profileBusy = true; status = "Generating a visual profile for " + character.Name + "...";
        try
        {
            string description = await SceneCapture.GenerateProfile(character, lifetime.Token);
            if (this == null || closed) return;
            characterText = description; status = "Profile draft ready. Edit it and Save character profile.";
        }
        catch (Exception ex) { if (this != null && !closed) status = "Profile generation failed: " + ex.GetBaseException().Message; }
        finally { profileBusy = false; }
    }
    private void DrawStatus(Rect rect)
    {
        GUILayout.BeginArea(rect);
        statusScroll = GUILayout.BeginScrollView(statusScroll);
        GUILayout.Label((GenerationService.Busy ? GenerationService.Status : status.Length > 0 ? status : GenerationService.Status), label);
        GUILayout.EndScrollView(); GUILayout.EndArea();
    }
    private void Try(Action action)
    { try { action(); } catch (Exception ex) { status = ex.Message; } }
    private void Styles()
    {
        background = Solid(new Color(0.065f, 0.075f, 0.09f));
        Texture2D control = Solid(new Color(0.16f, 0.18f, 0.22f));
        Texture2D hover = Solid(new Color(0.24f, 0.28f, 0.34f));
        Texture2D input = Solid(new Color(0.035f, 0.045f, 0.06f));
        Texture2D selected = Solid(new Color(0.17f, 0.30f, 0.36f));
        darkSkin = Instantiate(GUI.skin);
        darkSkin.hideFlags = HideFlags.HideAndDontSave;
        foreach (GUIStyle style in new[] { darkSkin.button, darkSkin.toggle, darkSkin.horizontalScrollbarThumb,
            darkSkin.verticalScrollbarThumb, darkSkin.horizontalScrollbarLeftButton, darkSkin.horizontalScrollbarRightButton,
            darkSkin.verticalScrollbarUpButton, darkSkin.verticalScrollbarDownButton }) Darken(style, control, hover, selected);
        foreach (GUIStyle style in new[] { darkSkin.box, darkSkin.window, darkSkin.scrollView, darkSkin.horizontalScrollbar, darkSkin.verticalScrollbar })
            Darken(style, background, background, background);
        Darken(darkSkin.textField, input, input, control);
        Darken(darkSkin.textArea, input, input, control);
        darkSkin.verticalScrollbar.fixedWidth = 22;
        darkSkin.verticalScrollbar.margin = new RectOffset(4, 0, 0, 0);
        darkSkin.verticalScrollbarThumb.fixedWidth = 22;
        darkSkin.toggle.fontSize = 20;
        darkSkin.toggle.padding = new RectOffset(12, 12, 7, 7);
        panel = new GUIStyle(darkSkin.box);
        label = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true }; label.normal.textColor = Color.white;
        title = new GUIStyle(label) { fontSize = 25, fontStyle = FontStyle.Bold };
        button = new GUIStyle(darkSkin.button) { fontSize = 20, wordWrap = true, padding = new RectOffset(10, 10, 5, 5) };
        field = new GUIStyle(darkSkin.textArea) { fontSize = 20, wordWrap = true, padding = new RectOffset(8, 8, 5, 5) };
        promptField = new GUIStyle(field) { fixedWidth = 0, fixedHeight = 0 };
    }
    private Texture2D Solid(Color color)
    {
        Texture2D texture = new(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color); texture.Apply(false, true); surfaces.Add(texture); return texture;
    }
    private static void Darken(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D selected)
    {
        style.border = new RectOffset();
        foreach (GUIStyleState state in new[] { style.normal, style.hover, style.focused, style.active, style.onNormal, style.onHover, style.onFocused, style.onActive })
        { state.background = normal; state.textColor = new Color(0.94f, 0.95f, 0.97f); }
        style.hover.background = style.focused.background = hover;
        style.active.background = style.onNormal.background = style.onHover.background = style.onFocused.background = style.onActive.background = selected;
    }
    private void ReleasePreview() { if (preview != null) Destroy(preview); preview = null; previewPath = ""; }
    private void Close()
    {
        if (closed) return;
        closed = true;
        lifetime.Cancel();
        Try(GenerationService.CloseViewer);
        ReleasePreview();
        if (blockedDialogue != null) blockedDialogue.interactable = originalDialogueInteractable;
        blockedDialogue = null;
        if (blockedDialogOwner != null)
            HarmonyLib.AccessTools.Method(typeof(DialogBox), "SetGlobalCanvasInputBlock").Invoke(blockedDialogOwner, new object[] { false });
        blockedDialogOwner = null;
        if (inputOwner != null) inputOwner.SetInputBlock(false);
        inputOwner = null;
        ReleaseSession();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
    protected override void OnFrameworkSessionClosed() => Close();
    protected override void OnDisable() { if (!closed) Close(); base.OnDisable(); }
    protected override void OnDestroy()
    {
        if (!closed) Close();
        if (darkSkin != null) Destroy(darkSkin);
        foreach (Texture2D texture in surfaces) if (texture != null) Destroy(texture);
        surfaces.Clear();
        lifetime.Dispose();
        if (instance == this) instance = null;
        base.OnDestroy();
    }
}
