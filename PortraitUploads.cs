using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SilverpineMods.ComfyImageGenerator;

public sealed partial class ComfyClient
{
    private async Task UploadPortraitsAsync(GenerationSettings settings, JObject graph, CancellationToken cancel)
    {
        if (!Guid.TryParse(settings.PortraitUploadNamespace, out Guid owner))
            throw new ArgumentException("Portrait upload identity is missing. Reopen the generator window.");
        string subfolder = "Silverpine-References/" + owner.ToString("N");
        for (int i = 0; i < settings.Portraits.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            byte[] png = settings.Portraits[i].Png;
            if (png.Length > 8 * 1024 * 1024 || ImageExtension(png) != ".png")
                throw new InvalidDataException("A portrait must be a PNG no larger than 8 MB.");
            // Only two reusable slots per installation. Never accumulate permanent input files.
            string filename = "portrait-" + i + ".png";
            // SendContentAsync transfers ownership to HttpRequestMessage. Do not
            // dispose this a second time: Unity Mono's MultipartContent.Dispose
            // is not idempotent and throws after its parts list has been cleared.
            var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(png);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, "image", filename);
            form.Add(new StringContent("temp"), "type");
            form.Add(new StringContent("true"), "overwrite");
            form.Add(new StringContent(subfolder), "subfolder");
            byte[] answer = await SendContentAsync(HttpMethod.Post, "upload/image", form, 65536, cancel).ConfigureAwait(false);
            JObject uploaded = JObject.Parse(Encoding.UTF8.GetString(answer));
            if ((string?)uploaded["name"] != filename || (string?)uploaded["subfolder"] != subfolder || (string?)uploaded["type"] != "temp")
                throw new InvalidDataException("ComfyUI did not confirm the requested temporary portrait path. Check the upload endpoint.");
            graph["portrait_" + i]!["inputs"]!["image"] = subfolder + "/" + filename + " [temp]";
        }
    }
}
