using System;
using System.Threading;
using System.Threading.Tasks;

namespace SilverpineMods.ComfyImageGenerator;

// The LLM stage runs on Unity's context; ComfyUI HTTP work uses a worker thread.
internal static class GenerationService
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? cancellation;
    private static string status = "Connect to ComfyUI, select your model, and enter a prompt.";
    private static GenerationResult? result;
    private static string prompt = "";
    private static string regionPrompts = "";
    private static int promptRevision;
    private static ModelFamily promptFamily;
    private static bool discard;
    internal static bool Busy { get { lock (Gate) return cancellation != null; } }
    internal static string Status { get { lock (Gate) return status; } }
    internal static GenerationResult? Result { get { lock (Gate) return result; } }
    internal static string Prompt { get { lock (Gate) return prompt; } }
    internal static string RegionPrompts { get { lock (Gate) return regionPrompts; } }
    internal static int PromptRevision { get { lock (Gate) return promptRevision; } }
    internal static ModelFamily PromptFamily { get { lock (Gate) return promptFamily; } }
    private static void SetStatus(string text) { lock (Gate) status = text; }

    internal static void Start(GenerationSettings settings, string address, string bearerToken, string directory, int minutes,
        Func<Action<string>, CancellationToken, Task<string>>? promptFactory = null)
    {
        settings.Validate();
        ComfyClient.NormalizeAddress(address);
        CancellationTokenSource source;
        lock (Gate)
        {
            if (cancellation != null) throw new InvalidOperationException("A generation is already in progress.");
            if (result != null) TemporaryImages.DeleteRun(directory, result.Directory);
            result = null;
            source = cancellation = new CancellationTokenSource();
            discard = false;
            status = "Starting generation...";
        }
        // Start on Unity's main thread: the game's LLM uses Unity APIs. Only the
        // ComfyUI HTTP/file work below moves to a worker after the prompt is ready.
        _ = Run();
        async Task Run()
        {
            try
            {
                using var client = new ComfyClient(address, bearerToken);
                if (promptFactory != null)
                {
                    SetStatus("Checking ComfyUI before asking the LLM...");
                    var serverInfo = await Task.Run(() => client.CheckConnectionAsync(source.Token));
                    await Task.Run(() => Workflow.ValidateServer(Workflow.Build(settings, settings.ResolveSeed()), serverInfo));
                    source.Token.ThrowIfCancellationRequested();
                }
                if (promptFactory != null) settings.Positive = await promptFactory(SetStatus, source.Token);
                source.Token.ThrowIfCancellationRequested();
                // Expose only editable scene text; Workflow adds the separately saved style prefix once.
                lock (Gate)
                {
                    prompt = settings.Positive;
                    regionPrompts = settings.AttentionCouple ? Newtonsoft.Json.JsonConvert.SerializeObject(settings.Regions) : "";
                    promptFamily = settings.Family;
                    promptRevision++;
                }
                GenerationResult generated = await Task.Run(() => client.GenerateAsync(settings, directory, TimeSpan.FromMinutes(minutes), SetStatus, source.Token)).ConfigureAwait(false);
                lock (Gate)
                {
                    if (discard) TemporaryImages.DeleteRun(directory, generated.Directory);
                    else
                    {
                        result = generated;
                        status = $"Generated image. Seed: {generated.Seed}. Use Save As to keep it.";
                        if (generated.DetailerReports.Count > 0) status += "\n" + string.Join("\n", generated.DetailerReports);
                    }
                }
            }
            catch (OperationCanceledException) { SetStatus("Generation cancelled. No image will be kept."); }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                // Avoid logging endpoints, credentials, and prompts from server errors.
                Plugin.Log.LogWarning(ex is ComfyCancellationException ? ex.Message : "Image generation failed (" + ex.GetType().Name + "). See the generator window for details.");
            }
            finally
            {
                lock (Gate) { cancellation = null; source.Dispose(); }
            }
        }
    }
    internal static void StopWaiting() { lock (Gate) cancellation?.Cancel(); }
    internal static void CloseViewer()
    {
        lock (Gate)
        {
            discard = true;
            cancellation?.Cancel();
            if (result != null) TemporaryImages.DeleteRun(Plugin.ImagesDirectory, result.Directory);
            result = null;
        }
    }
}
