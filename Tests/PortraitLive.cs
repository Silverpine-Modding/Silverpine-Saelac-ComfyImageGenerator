using Newtonsoft.Json.Linq;
using SilverpineMods.ComfyImageGenerator;

internal static partial class Program
{
    // Explicit local integration mode: uses the production upload/client/graph code.
    private static async Task<int> LivePortraits(string workspace, bool referenceSizes = false)
    {
        string output = Path.Combine(workspace, "artifacts", referenceSizes ? "ComfySeparatePortraits-1.10.0/reference-sizes" : "PortraitReferenceIntegration-20261026");
        string prior = Path.Combine(workspace, "artifacts", "AnimaReferenceExperiment-20261026");
        Directory.CreateDirectory(output);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        foreach (int test in referenceSizes ? new[] { 512, 768, 1024 } : new[] { 0, 1 })
        {
            ModelFamily family = referenceSizes || test == 1 ? ModelFamily.Anima : ModelFamily.Illustrious;
            string name = referenceSizes ? "Anima-ref-" + test : family.ToString();
            var s = ReferencedSettings(family);
            if (referenceSizes) s.PortraitSettings.ReferenceSize = test;
            bool anima = family == ModelFamily.Anima;
            s.Model = anima ? "novaFurryAM_v5529B.safetensors" : "novaFurryXL_ilV135.safetensors";
            s.Width = anima ? 1152 : 832; s.Height = anima ? 896 : 1152;
            s.Seed = 2026102601; s.Steps = 25; s.Cfg = 5; s.Sampler = "euler_ancestral"; s.Scheduler = "sgm_uniform";
            s.Positive = anima ? (string)JObject.Parse(File.ReadAllText(Path.Combine(prior, "remap-40", "silverpine-stand-bed-bound-ip020-style100.api.json")))["4"]!["inputs"]!["text"]!
                : "masterpiece, best quality, detailed illustration, solo, 1boy, male anthro arctic fox, white fur, blue eyes, pointed fox muzzle, triangular fox ears, fluffy white tail, digitigrade, raised heels, paw feet, full body, standing beside a bed, dark blue tunic, brown leather bracers, brown trousers, looking at viewer, timber bedroom, wooden floor, warm lamplight";
            s.Negative = "low quality, blurry, watermark, text, human, human skin, extra person, crowd, merged bodies, duplicate character, extra limbs, nudity, exposed genitals";
            s.Portraits[0].Png = File.ReadAllBytes(Path.Combine(prior, "references", "Dan-Miyo-dialogue.png"));
            if (anima)
            {
                s.Portraits[1].Png = File.ReadAllBytes(Path.Combine(prior, "references", "Oriana-original-dialogue.png"));
                s.Loras.Add(new LoraSetting { Name = "SilverpineAnimav2.safetensors", ModelStrength = 1 });
            }
            using var handler = new RecordingHandler(Path.Combine(output, name + ".submitted.api.json"));
            using var client = new ComfyClient("http://127.0.0.1:8000", handler: handler, requestTimeout: TimeSpan.FromSeconds(60));
            Console.WriteLine("Starting " + name);
            GenerationResult result = await client.GenerateAsync(s, Path.Combine(output, name), TimeSpan.FromMinutes(15), Console.WriteLine, cancel.Token);
            File.WriteAllText(Path.Combine(output, name + ".result.json"), JObject.FromObject(new { result.PromptId, result.Seed, result.Images, Model = s.Model, Strength = s.PortraitSettings.Strength, s.PortraitSettings.ReferenceSize }).ToString());
            Console.WriteLine("Completed " + name + ": " + result.Images[0]);
        }
        return 0;
    }
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly string file;
        internal RecordingHandler(string file) : base(new HttpClientHandler { AllowAutoRedirect = false }) { this.file = file; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (request.RequestUri!.AbsolutePath == "/prompt")
            {
                JObject data = JObject.Parse(await request.Content!.ReadAsStringAsync(cancel));
                File.WriteAllText(file, data["prompt"]!.ToString());
            }
            return await base.SendAsync(request, cancel);
        }
    }
}
