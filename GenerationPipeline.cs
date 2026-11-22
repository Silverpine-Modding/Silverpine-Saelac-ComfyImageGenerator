using System;
using System.Threading;
using System.Threading.Tasks;

namespace SilverpineMods.ComfyImageGenerator;

public static class GenerationPipeline
{
    // Retain the caller's Unity context around promptFactory; it may use game APIs.
    public static async Task<GenerationResult> RunAsync(ComfyClient client, GenerationSettings settings, string directory,
        TimeSpan imageTimeout, Action<string> progress, CancellationToken cancel,
        Func<Action<string>, CancellationToken, Task<string>>? promptFactory = null, Action<string>? promptReady = null,
        TimeSpan? cleanupTimeout = null)
    {
        bool release = settings.ReleaseComfyMemory || settings.Vision.Enabled;
        try
        {
            if (promptFactory != null)
            {
                progress("Checking ComfyUI before asking the LLM...");
                var info = await Task.Run(() => client.CheckConnectionAsync(cancel));
                Workflow.ValidateServer(Workflow.Build(settings, settings.ResolveSeed()), info);
                if (settings.Vision.Enabled)
                {
                    using var preflight = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    preflight.CancelAfter(cleanupTimeout ?? TimeSpan.FromSeconds(120));
                    try { settings.CleanupNotice = await client.ReleaseMemoryAsync(progress, preflight.Token); }
                    catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                    { throw new TimeoutException("ComfyUI did not finish releasing its models in time. The vision LLM was not loaded; check the queue and free memory before retrying."); }
                }
                cancel.ThrowIfCancellationRequested();
                settings.Positive = await promptFactory(progress, cancel);
            }
            cancel.ThrowIfCancellationRequested();
            promptReady?.Invoke(settings.Positive);
            return await Task.Run(() => client.GenerateAsync(settings, directory, imageTimeout, progress, cancel));
        }
        finally
        {
            if (release)
            {
                // A cancelled viewer token must not cancel model cleanup. Keep failures
                // visible without discarding a successfully downloaded image.
                try
                {
                    using var cleanup = new CancellationTokenSource(cleanupTimeout ?? TimeSpan.FromSeconds(120));
                    settings.CleanupNotice = await client.ReleaseMemoryAsync(progress, cleanup.Token);
                }
                catch (Exception)
                {
                    settings.CleanupNotice = "ComfyUI memory cleanup could not be confirmed. Its server may still have models loaded; use ComfyUI's unload/free-memory control.";
                }
            }
        }
    }
}
