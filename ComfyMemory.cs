using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SilverpineMods.ComfyImageGenerator;

public sealed partial class ComfyClient
{
    // /free only acknowledges queue flags. Wait for idle plus low allocator use;
    // never treat its HTTP 200 as proof that the worker has released VRAM.
    public async Task<string> ReleaseMemoryAsync(Action<string> progress, CancellationToken cancel)
    {
        bool requested = false;
        int idleSamples = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            JObject queue = await JsonAsync(HttpMethod.Get, "queue", null, cancel).ConfigureAwait(false);
            if (queue["queue_running"] is not JArray running || queue["queue_pending"] is not JArray pending)
                throw new InvalidDataException("ComfyUI did not return its queue state for memory cleanup.");
            if (running.Count != 0 || pending.Count != 0)
            {
                progress("Waiting for ComfyUI's queue to become idle before releasing models...");
                idleSamples = 0;
                requested = false;
            }
            else
            {
                if (!requested)
                {
                    progress("Releasing ComfyUI models and execution cache...");
                    // Core /free returns an empty body, not JSON.
                    await SendAsync(HttpMethod.Post, "free", new JObject { ["unload_models"] = true, ["free_memory"] = true }, 65536, cancel).ConfigureAwait(false);
                    requested = true;
                    await Task.Delay(pollInterval, cancel).ConfigureAwait(false);
                }
                JObject stats = await JsonAsync(HttpMethod.Get, "system_stats", null, cancel).ConfigureAwait(false);
                JArray devices = stats["devices"] as JArray ?? throw new InvalidDataException("ComfyUI did not report device memory after cleanup.");
                if (devices.Count == 0) throw new InvalidDataException("ComfyUI returned no devices for the memory check.");
                bool measurable = devices.OfType<JObject>().All(d => IsAllocatorDevice((string?)d["type"] ?? ""));
                bool released = measurable && devices.OfType<JObject>().All(d => d["torch_vram_total"]?.Type == JTokenType.Integer &&
                    d["torch_vram_total"]!.Value<long>() >= 0 && d["torch_vram_total"]!.Value<long>() <= 64L * 1024 * 1024);
                if (!measurable || released) idleSamples++; else idleSamples = 0;
                if (idleSamples >= 3)
                    return measurable ? "" : "ComfyUI accepted model/cache cleanup. This device backend does not expose separate GPU allocator usage, so VRAM release could not be measured.";
            }
            await Task.Delay(pollInterval, cancel).ConfigureAwait(false);
        }
    }
    private static bool IsAllocatorDevice(string type) => type == "cuda" || type == "xpu" || type == "npu" || type == "mlu";
}
