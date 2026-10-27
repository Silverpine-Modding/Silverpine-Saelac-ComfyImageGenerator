# Comfy Image Generator

BepInEx 5 plugin for **Silverpine 1.7.3 on Windows**, version **1.10.1**.

[Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-ComfyImageGenerator/releases/latest)

Adds **Generate scene** immediately to the left of the conversation's `...`
button. It opens a dark, scrollable character picker before making any LLM request.
Choose **one or two participants for Anima**, or **exactly one for Illustrious**,
then click **Generate selected characters**. These limits also apply to group
dialogues. The player is optional: Anima can depict two NPCs, and Illustrious can
depict any single NPC or the player. The header's **Choose characters** button
reopens this picker. Changing model families resets the selection on reopening.

Silverpine's configured LLM reads a snapshot of the current dialogue,
environment and character profiles, then writes an image prompt for ComfyUI:

- **Illustrious:** comma-separated booru-style visual tags.
- **Anima:** descriptive English prose with character placement and interaction.

The LLM is instructed to write each visible character in a separate paragraph,
followed by a separate environment paragraph. Illustrious uses comma-separated
tags within each paragraph; Anima uses sentences. Blank lines are preserved
through cleanup, editing and the ComfyUI API. This keeps descriptions organized;
the image model can still mix traits or misrender anatomy.

The plugin does not add image instructions to the ongoing NPC conversation or
modify its history. It makes separate LLM requests using the game's existing
connection, including compatible endpoint plugins.

Scene references bind each exact character name and profile key to its physical
description, a fallback game species hint, and its current clothing state. The
LLM is instructed to lead with species and supported anatomy instead of turning
an anthro character into a generic human. Saved visual profiles retain priority
over game species hints when you intentionally customize a character.

Both model paths request separate character entries keyed to exact profile IDs.
For Anima, the plugin places physical sentences from each selected profile at
the start of its own paragraph, expanding `anthro` to `anthropomorphic`.
For Illustrious, it prepends supported species, anatomy and color tags extracted
from that same physical reference. The LLM supplies current clothing, placement,
expression and action.
The physical excerpt excludes outfit/state sentences so usual clothing does not
override the live scene. Unknown traits are not invented, and an empty excerpt
leaves appearance extraction to the LLM's full profile reference.
**Prompt wording no longer vetoes a generation.** Human/species/anatomy wording
is accepted for user correction. Duplicate and extra structured entries are skipped;
missing selected entries use that character's physical and current clothing references.
Plain prompt text is accepted even when the LLM does not return the requested JSON.
Empty or unreadable structured replies fall back to the selected references and
scene context. The viewer reports when these fallbacks were used. No corrective
LLM retries are made. Review or edit the resulting prompt through **Regenerate /
edit prompt**; profile anchors remain part of normal structured prompt assembly.

**Only user-selected current conversation participants may appear in generated scenes.**
The picker includes the player, the current NPC and NPCs in the active group conversation.
Only selected characters' profiles and clothing reach the LLM or first-use profile
initialization. Unselected participants are explicitly kept off-screen, even when
they speak or interact in the dialogue. Dialogue and environment remain context.
The selected IDs and count are checked again against the live conversation when
you confirm generation. A character leaving the conversation requires refreshing
the picker. These are input limits; the image checkpoint can still invent or omit
subjects. Manual prompt edits, style prefixes and custom workflows remain editable.
Nearby NPCs, people named in dialogue, and people described as visibly present
in the environment are excluded unless they are actual conversation participants.
For example, mentioning Gareth behind the bar does not add him to the image.
Scene capture does not look up or supply bystander profiles. Extra structured
character entries are omitted without rejecting the image job. The LLM is instructed
to keep unselected people out of character and scenery paragraphs. Plain text and
manual edits are not inspected for cast or anatomy mistakes. The environment remains
useful for scenery and lighting.
After updating, use **Generate current dialogue** to rebuild an old scene prompt;
manual regeneration continues to use the text you chose to edit.

The player's currently equipped clothing/accessories, item descriptions and
upper/lower body coverage are captured from the live game. The game's visible
description is also included, allowing installed clothing/coverage plugins to
contribute their details. Held weapons are not listed as clothing. Loaded NPCs
supply their live clothed/nude flag; the game does not expose an NPC outfit
inventory through this integration. Current clothing overrides the profile's
usual outfit. Unknown clothing state remains unknown rather than inferred nude.
Clothing is captured when **Generate selected characters** is confirmed. Regenerate uses
the existing editable prompts; generate the current dialogue again after an
outfit change. After upgrading, use **Generate current dialogue** to create a new
prompt with the updated cast and identity rules; Regenerate reuses existing
text. Temporary clothing state is not written into visual profiles.

## Install and configure

Requirements:

- Silverpine 1.7.3, BepInEx 5, and Modding Tools **1.10.0 or newer**.
- The shared `Newtonsoft.Json.dll` already supplied in this installation's
  `BepInEx/plugins/ModdingTools` folder.
- A running self-hosted ComfyUI server with **job-specific cancellation**:
  `POST /api/jobs/{job_id}/cancel`. The plugin checks this before submitting.
  Update ComfyUI if the connection test reports this API is missing.
- A configured, loaded game LLM for scene prompts and profile initialization.
  Manually entered prompts do not need the LLM.
- Compatible model files installed on the ComfyUI server. No model weights or
  custom nodes are downloaded by this plugin.

Copy `ComfyImageGenerator.dll` and this README into
`BepInEx/plugins/ComfyImageGenerator`, then restart Silverpine.
Do not copy game/Unity/BepInEx assemblies or duplicate ModdingTools.dll.

1. Open **Modding Tools > Comfy Image Generator** from the main menu, or
   **Mods > Comfy Image Generator** in-game.
2. Enter the ComfyUI server's **IP / hostname** and **port**, default
   `127.0.0.1:8188`. For another computer, enter its LAN IP; ComfyUI must listen
   on that interface and its port must be reachable. HTTPS and a reverse-proxy
   base path are also supported.
3. Click **Connect / refresh lists**. This reads installed nodes, models, LoRAs,
   detectors, samplers and schedulers, and verifies scoped cancellation.
4. Select **Illustrious** or **Anima**, then choose the actual server filenames.
   The Illustrious preset loads an SDXL-style checkpoint with its embedded
   encoder/VAE. Anima uses a diffusion model, Qwen text encoder and Qwen Image
   VAE. Defaults for Anima are `anima-base-v1.0.safetensors`,
   `qwen_3_06b_base.safetensors`, and `qwen_image_vae.safetensors`.
5. Set width, height, **steps**, **CFG**, sampler, scheduler, and negative prompt.
   Set **Base positive prompt** for reusable styling at the start of every prompt.
   Add any desired LoRAs and scene direction / trigger words. Optional detailer
   passes are under **Settings > Detailers**. **Save settings**.
6. Load a game, finish a dialogue response, then click **Generate scene** next
   to `...`. Choose up to two participants for Anima or one for Illustrious,
   then click **Generate selected characters**.

The connection is saved in
`BepInEx/config/Saelac.Silverpine.ComfyImageGenerator.cfg`.
An optional `Connection.BearerToken` supports authenticated reverse proxies.
Credentials are not included in image files or logged by this plugin.

## Updating from 1.6.x or 1.7.0

Attention Couple has been removed, including the region map, masks, regional
prompts, character-targeted detailers and PPM dependency. Ordinary detailer passes
remain available. Old coupling settings and detailer region IDs are ignored;
detailer settings save without region IDs. Existing enable states, models, numeric
settings and prompt overrides are preserved. Review old positive overrides because
they now replace the scene prompt for the full-image detailer pass.

Keep your configuration and `CharacterProfiles` folder when updating. Existing
ComfyUI custom nodes are not modified or uninstalled. This update requires a game
restart to load the new plugin DLL; no ComfyUI restart is needed for the removal.

## Viewer and image lifetime

The viewer fills most of the screen and fits the whole image without cropping
or stretching, preserving portrait, square and landscape aspect ratios.
Controls scale with the display resolution. All plugin in-game panels, buttons,
fields, selectors, Save As form and scrollbars use dark backgrounds and light text.

- **Regenerate / edit prompt** exposes the exact positive and negative prompts
  sent to ComfyUI in a scrollable page below the image. The image keeps the same
  display size and aspect ratio while editing. Opening the editor scrolls down
  to its controls; scroll the page up to view the full image again. Close, Save As
  and Cancel generation remain accessible outside the scrolling page. **Hide
  prompt editor** or Escape returns to the image, as does starting regeneration.
  The editor shows the reusable base positive prefix separately from the scene
  text so both can be edited without accumulating duplicate prefixes. Changing
  the prefix here saves it for the selected model family when you generate.
  All prompt boxes wrap text and scroll vertically with a
  visible scrollbar; use the mouse wheel over a box or drag its scrollbar.
  Edit them and choose **Regenerate with edited prompt**, a full-height button
  below the editor that stays visible regardless of prompt length. This
  combines the base positive prefix with your scene text directly, without
  another LLM rewrite. Seed `-1` randomizes;
  enter a fixed seed to reproduce the same workflow settings.
- **Save As...** opens a dark in-game folder-and-filename form. It replaces the
  native Windows dialog path that was reported to cause a stack overflow.
  Enter a full folder path or use Pictures, Documents or Desktop, then name
  the image and choose **Save permanent copy**. The default is a `Silverpine`
  folder under Pictures. Missing folders are created on save; the correct PNG
  or JPG extension is added if omitted. Existing files require **Replace
  existing file** confirmation. Changing the path clears that confirmation.
  Back or Escape cancels the save form without closing the image viewer.
  Saving returns to the viewer and preserves the permanent copy after Close.
- **Close**, Escape, scene destruction, and disabling the viewer cancel its
  active generation and delete its temporary local image.
- **Cancel generation** cancels the current request without closing the viewer.

The plugin keeps **one temporary local image** in `TemporaryImages`. Starting a
regeneration deletes the previous temporary image. Closing deletes the current
one. Explicit Save As copies are outside that folder and are preserved. Leftover
plugin temporary images from an abnormal shutdown are cleaned on startup.
Custom workflows with multiple output images display/download only the first
image, preferring an output image over a preview.

Built-in workflows use ComfyUI's **PreviewImage** node, so the server writes to
its own temporary area rather than its permanent output directory. Server-side
temporary files are managed by ComfyUI; this plugin does not have a remote file
deletion API. Custom workflows containing SaveImage can also save files on the
server according to that workflow.

Cancellation is sent for this job's ID only; it never clears the queue or sends
a global interrupt. A submission handshake already in flight is allowed to
return its ID, then that job is cancelled. If the server becomes unreachable,
cancellation cannot be confirmed; the plugin logs this with the job ID, discards
late results, and removes local temporary files. During profile/prompt creation,
Close stops the plugin's pipeline and prevents any ComfyUI submission. The
game's LLM API has no per-request abort, so an already-running LLM request can
finish in the background and its result is discarded.

## Character profiles and presets

Open **Settings > Character profiles & presets** after loading a game.

- On first scene generation, the LLM attempts to initialize missing visual
  profiles for that scene's selected participants only. It extracts stable appearance from
  the game's descriptions. Existing profiles are preserved. Disable this
  behavior with the first-use checkbox if desired.
- Choose any loaded character, including the player, and edit its generation
  description. **Save character profile** stores your override. These changes
  affect image generation only.
- **Ask LLM for profile draft** puts a suggested description into the editor.
  Review/edit it and save it when ready. A failed request leaves existing saved
  profiles intact. First-use extraction errors fall back to game descriptions;
  a timed-out LLM request stops the generation to avoid piling up LLM calls.
- **Reset to game description** removes that character's override. First-use
  initialization can run again if enabled.
- **Save all saved profiles as preset** writes a named set. Unsaved editor text
  is not included. Saving an existing preset name replaces that preset.
- **Choose and load preset** replaces the active set. The previous set is
  retained in `CharacterProfiles/before-preset-load.json` for recovery.

Active profiles are stored in `CharacterProfiles/active.json`. Named presets
are shareable JSON files in `CharacterProfiles/Presets`. Copy a preset into
that directory to make it available in the load list. Profile identities use
exact character names, with separate keys for players and NPCs; renamed
characters receive separate entries.

## Optional portrait references (IP-Adapter)

Open **Settings > Portrait references**, select the model family, and **Connect /
refresh lists**. Choose the adapter files, enable **Use character portrait
references**, then **Save settings**. Each family has independent settings and
defaults to disabled. This option is for the built-in workflows.

- **Anima:** one or two selected participants, using `AnimaIPAdapterLoader` and
  `AnimaIPAdapterApply`. Start at strength **0.2**; **0.2-0.5** worked well in the
  local NovaFurry tests. Each portrait is encoded separately, then their reference
  tokens are combined. **Reference image size defaults to 512**, including older
  configurations. Optional sizes are 224–2048 in multiples of 16; 768 or 1024 may
  help with detail but use more memory/time and do not guarantee better results.
  This setting changes reference encoding, not output resolution. The scene prompt
  controls positions and poses; there are no output regions or attention masks.
- **Illustrious:** exactly one selected participant, using `IPAdapterModelLoader`,
  `IPAdapterAdvanced` and `CLIPVisionLoader`. Start at strength **0.5**. Start/end
  fractions control when the reference affects sampling (default 0 to 1).

In **Character profiles & presets**, select **Current**, **Clothed** or **Nude**
game art. Current uses the live dialogue portrait, including compatible portrait
mods. An optional PNG/JPEG **Image override** path takes priority. Absolute paths
are supported; relative paths resolve inside the plugin folder. Click **Save
character profile** to apply. These choices are saved in `CharacterProfiles/active.json`
and named presets alongside the visual description. Presets contain image paths,
not the image files; include those files separately when sharing a preset.

The existing LLM still turns selected profiles, clothing and scene context into
the text prompt. It is not sent the portrait images. The adapter receives those
images separately. References can influence appearance, clothing and style, and
can still mix traits or miss anatomy; generated content is not automatically
judged or vetoed. Missing or unreadable artwork falls back to text-only generation
with a viewer notice. A manual prompt with no selected participants uses text only.

Portraits are encoded on Unity's main thread, bounded to 1536 pixels on their
longest side, and uploaded as PNGs to **two reusable slots in the server's temp
folder**. Regeneration overwrites the slots. They are not written to permanent
ComfyUI input/output folders. Closing the viewer cancels uploads or the queued
job; uploaded temp references remain until the server clears its temp directory.
Generated images retain the existing one-image viewer/save/cleanup behavior.
Detailer crops use the usual model and LoRAs without portrait conditioning.

### Anima server setup

Run the included **[Anima portrait support installer](AnimaCompatibility/README.md)**
on the ComfyUI server. Windows, macOS and Linux launchers use the same Python helper.
It downloads pinned original node files from GitHub, applies the modifications,
and downloads the adapter and SigLIP2 weights from Hugging Face after acknowledgement
of their terms (about 2.01 GB for a fresh install). Hashes are checked, matching files
are reused and recognized existing node versions are backed up. Unknown edits are
left untouched. No Git installation is needed. The separate server support ZIP
can be transferred to a different computer.

Restart ComfyUI after installing it. The mod checks the compatibility marker and
second-portrait input before enabling Anima references. The update maps the tested 28-block
adapter into NovaFurry's 40-block layout and preserves normal LoRAs; it does not
make it a natively trained 40-block adapter. It replaces one existing custom-node
file and does not alter ComfyUI core. The mod does not install it automatically.

### Illustrious server setup

Install [ComfyUI_IPAdapter_plus](https://github.com/cubiq/ComfyUI_IPAdapter_plus)
on the ComfyUI server. For the tested SDXL pairing, download:

- [`ip-adapter_sdxl_vit-h.safetensors`](https://huggingface.co/h94/IP-Adapter/resolve/main/sdxl_models/ip-adapter_sdxl_vit-h.safetensors)
  into `models/ipadapter/`.
- [ViT-H vision encoder](https://huggingface.co/h94/IP-Adapter/resolve/main/models/image_encoder/model.safetensors)
  into `models/clip_vision/`, renamed to `clip-vision_vit-h.safetensors`.

Select those exact filenames in Portrait references. Other SDXL adapters need
their matching encoder; SD1.5, Anima and FaceID files are not interchangeable.
This workflow does not use FaceID. Restart after installing the nodes and refresh
lists after adding models. Missing nodes or selected model files disable activation.
File listings cannot verify the contents or architecture of renamed weights.

Both families also require core `LoadImage`, `GetImageSize`, `EmptyImage`,
`InvertMask`, `ImageCompositeMasked` and `ResizeAndPadImage`.
The settings page lists missing node names. Neither path requires `ImageStitch`.

## Base positive prompt / reusable styling

In **Settings > Connection, model & LoRAs**, edit **Base positive prompt (always
prepended)** and **Save settings**. Use it for recurring style tags, quality tags,
art direction or trigger words, for example `watercolor, soft lighting, fine
linework`. It defaults to blank, which disables the prefix. Illustrious and Anima
each save their own prefix in their `BasePositivePrompt` config entry.

Every positive prompt is assembled as the saved prefix, a blank line, then the
scene or manually edited prompt. The prefix is applied after the LLM returns;
its spelling, casing and internal line breaks are preserved (outer whitespace
is trimmed). This differs from **Scene direction / LoRA triggers**, which gives
instructions to the LLM. Repeated regeneration does not copy the prefix into
the scene editor or accumulate extra copies. To disable styling, clear the
prefix field rather than removing text from the scene prompt.

The prefix also applies to detailer positive overrides and the custom workflow
`{{positive}}` placeholder. Detailers that inherit the scene use its already
combined conditioning. Negative prompts are unaffected. The combined positive
prompt, including the prefix and separator, must fit within 32,000 characters.

## LoRAs, Turbo and EasyCache

Each model-family profile stores its own settings and ordered LoRA list. Add
multiple LoRAs with **+ Add LoRA**. Each has a model strength; Illustrious also
exposes an independent CLIP strength. Anima uses model-only LoRA nodes. Trigger
words can be entered in **Scene direction / LoRA triggers** or directly in the
regeneration prompt. Use LoRAs compatible with the selected checkpoint.

Turbo is not a cache setting. Anima has standalone Turbo models and Turbo
LoRAs. Select the appropriate installed model or add its Turbo LoRA, then set
steps and CFG for that specific release. The plugin never silently overwrites
those values when you change a checkpoint or LoRA.

**Enable native EasyCache** inserts the server's experimental EasyCache node
after the LoRA chain. Threshold, start fraction and end fraction are editable.
It defaults off. Availability is checked on the server; compatibility and
quality depend on the model and ComfyUI version. Other cache/custom-node
workflows can be used through the custom API workflow option.

## Detailer passes

Open **Settings > Detailers**, then **+ Add detailer**. Add as many passes as
needed, such as faces followed by hands. **Up / Down** changes their execution
order; **Enabled** bypasses a pass without removing its settings. Each pass
receives the previous pass's full image. Only the final image goes to the viewer,
and closing the viewer cancels the entire workflow, including detailer passes.

Each pass has its own detector model, steps, CFG, denoise, crop guide/max size,
detection threshold, dilation, crop factor, feathering and cycle count. Sampler
and scheduler inherit the base settings when blank. Positive/negative prompt
overrides replace the corresponding scene prompt for that pass. Blank fields inherit.
Detectors scan the full image; each detection uses its complete mask and padded crop.
The base positive styling prefix is also prepended to each
positive override. Use a compatible face, hand or other detector from the list.
Segmentation detectors also supply their segmentation masks. SAM refinement and
other detailer implementations can be configured in a custom API workflow.

Each pass also has a **Wildcard** box passed unchanged to Impact Pack's
`FaceDetailer.wildcard`. Wildcard, positive override and negative override boxes
have independent vertical scrolling and dark backgrounds. Save settings to keep
their contents; older settings default to an empty wildcard.

A blank wildcard keeps the pass's positive conditioning. A nonempty wildcard
normally replaces it, including the base style. Start with `[CONCAT]` to append
to the inherited scene or styled positive override instead. Negative conditioning
is unchanged. Wildcard files (`__name__`), choices (`{a|b}`) and inline LoRAs use
Impact Pack's server-side implementation and installed files.

For separate detected regions, for example:

```text
[CONCAT][ASC] clear amber eyes [SEP] clear blue eyes
```

`[ASC]` orders detections by left edge, then top edge; `[DSC]` reverses that.
`[ASC-SIZE]` / `[DSC-SIZE]` sort by area; `[RND]` shuffles the prompt choices.
`[SEP]` separates prompts assigned to successive regions. This orders detailing
of existing detections; it does not place characters in the base image or identify
them by name. Impact Pack also handles its native seed and skip/stop directives.

For label-specific detailing, use the exact labels your detector outputs:

```text
[CONCAT][LAB]
[ALL] fine texture,
[face] clear eyes, detailed muzzle
[hand] well-defined fingers
```

These are example labels, not a list guaranteed by every detector. Matching is
case-sensitive. `[ALL]` prefixes each selected label prompt; include a comma or
other separator at its end. Without `[ALL]`, unmatched labels keep the pass's
positive conditioning. `[LAB]` chooses prompts for detections, not which labels
the detector scans. Multi-class SEGM models retain their class labels through
the normal detector/mask connections.

Directives must start at the first character. Keep combined prefixes adjacent,
such as `[CONCAT][LAB]` or `[CONCAT][ASC]`, without whitespace between them.
No extra nodes or modifications to Impact Pack are required. See the
[Impact Pack wildcard guide](https://github.com/ltdrdata/ComfyUI-extension-tutorials/blob/Main/ComfyUI-Impact-Pack/tutorial/ImpactWildcard.md)
for the syntax supported by your installed version.

Passes use the selected base model, encoder, VAE, LoRAs and resolved seed.
EasyCache is applied only to base sampling; detailer crops use the model without
that cache patch. Settings, order and enable states are saved separately for
Illustrious and Anima. Detailers require an Impact Pack version that supports
the chosen model's sampling/encoding; live Anima detailer compatibility has not
been verified here.

## Required nodes and unavailable options

The window displays requirements beside optional features. Connect to your
server before activating them. Missing nodes disable **Enable EasyCache**,
**+ Add LoRA**, **+ Add detailer**, detailer activation, and selection of a model
family whose required core nodes are missing. Detailers also need at least one
installed detector, and a disabled pass cannot be enabled with an unavailable
detector. **Choose** buttons require an installed choice reported by the server.
Changing the server address locks these controls until that server is checked.

| Feature | Required server nodes / files |
| --- | --- |
| Illustrious | Core `CheckpointLoaderSimple` and an SDXL-compatible checkpoint |
| Anima | Core `UNETLoader`, `CLIPLoader`, `VAELoader` and compatible diffusion model, text encoder and VAE |
| Both base workflows | Core `CLIPTextEncode`, `EmptyLatentImage`, `KSampler`, `VAEDecode`, `PreviewImage` |
| Illustrious LoRAs | Core `LoraLoader` and compatible LoRA files |
| Anima LoRAs | Core `LoraLoaderModelOnly` and compatible LoRA files |
| EasyCache | Native `EasyCache`; update ComfyUI if missing |
| Detailers | `FaceDetailer` from [ComfyUI-Impact-Pack](https://github.com/ltdrdata/ComfyUI-Impact-Pack) and `UltralyticsDetectorProvider` from [ComfyUI-Impact-Subpack](https://github.com/ltdrdata/ComfyUI-Impact-Subpack) |
| Detector models | Compatible detector weights under `ComfyUI/models/ultralytics/bbox` or `ComfyUI/models/ultralytics/segm` |

Install required nodes and weights on the **ComfyUI server**, restart ComfyUI,
then **Connect / refresh lists**. The plugin does not install these automatically.
Saved options remain editable/removable if they become unavailable. Every job
checks fresh server metadata before submission; scene generation also checks
the workflow before asking the LLM. Missing nodes, missing selected files and
unsupported required detailer inputs stop the job with an explanation.

## Custom API workflows

Export a working workflow in ComfyUI's **API format**, not normal canvas JSON.
Enter its path in settings (absolute or relative to the plugin directory).
Replace selected **whole JSON input values** with these placeholders:

| Placeholder | Value |
| --- | --- |
| `{{positive}}` | Base positive prefix plus scene or edited prompt; required at least once |
| `{{negative}}` | Negative prompt |
| `{{seed}}` | Resolved integer seed |
| `{{width}}`, `{{height}}` | Integer dimensions |
| `{{steps}}`, `{{cfg}}` | Sampling parameters |
| `{{model}}`, `{{text_encoder}}`, `{{vae}}` | Selected filenames |
| `{{sampler}}`, `{{scheduler}}` | Selected names |

Example: `"text": "{{positive}}"`, `"seed": "{{seed}}"`. Numeric placeholders
become JSON numbers. Prompts are inserted as JSON values, so quotes and
backslashes remain safe. Unmarked inputs retain their workflow values.
Put LoRA, cache and detailer nodes directly inside a custom workflow, and
clear/disable the built-in LoRA/cache/detailer/portrait controls to avoid ambiguous settings. Include a
PreviewImage or SaveImage output returning PNG or JPEG.

## Limits and validation

Version 1.9.1 fixes a portrait-upload `NullReferenceException` specific to Unity
Mono: the multipart form now has a single disposal owner. BepInEx logs include
exception types and stack traces, including inner exceptions, while omitting
exception messages that could contain prompts, credentials or endpoint details.
The generator window still shows the error message.

Dimensions must be multiples of 64, from 256 to 2048. Steps: 1-150. CFG: 0-30.
LoRA strengths: -10 to 10. One local job at a time. Default overall image-job
timeout: 20 minutes; configurable to 1-120 minutes. Default LLM request timeout:
120 seconds. Requests are asynchronous and Unity texture creation stays on the
main thread. Downloads are bounded to 32 MB; previews are limited to 16 MP and
8192 pixels per side, and to the GPU texture limit.

Build and regression checks, from the game directory:

```powershell
dotnet build "Plugin Development/ComfyImageGenerator/ComfyImageGenerator.csproj" -c Release
dotnet run --project "Plugin Development/ComfyImageGenerator/Tests/ComfyImageGenerator.Tests.csproj" -c Release
```

For a standalone GitHub checkout, install the game and dependencies separately,
then build from the repository root with their location supplied explicitly:

```powershell
dotnet build ./ComfyImageGenerator.csproj -c Release "-p:SilverpineGameDir=C:\Games\Silverpine"
dotnet run --project ./Tests/ComfyImageGenerator.Tests.csproj -c Release "-p:SilverpineGameDir=C:\Games\Silverpine"
```

The test runner requires .NET 9. Game, Unity, BepInEx and Modding Tools assemblies
are referenced from that installation and are not redistributed in this repository.
The additional multipart regression checks run inside the game's actual Mono
runtime and cover both adapter families, successful uploads, cancellation, server
errors and diagnostic redaction. They use a simulated HTTP handler and submit no
real image jobs. With the .NET Framework 4.8 targeting pack installed, run:

```powershell
& "Plugin Development/ComfyImageGenerator/Tests/MonoRuntime/Run-Checks.ps1"
```

For a standalone source checkout, use `./Tests/MonoRuntime/Run-Checks.ps1
-GameDirectory "C:\Games\Silverpine"`.

The checks use a mock HTTP handler and the installed game assemblies. They
cover workflow wiring, LoRA chains/cache, ordered detailer chains, permissive
LLM parsing and reference fallbacks, removed legacy settings, node and model
requirements, separated prompt blocks, reusable positive prefixes and repeated
generation, typed templates, character presets, user-selected cast limits,
per-character clothing context, permanent save paths and overwrite protection,
LLM cancellation, job errors/timeouts, submission races, job-ID cancellation,
and temporary-image cleanup. A real ComfyUI generation and in-game visual
inspection are still required on the target server/display. Automated checks
use a simulated ComfyUI API and do not verify GPU output quality or live GUI layout.

Optional detailer integration checks run against your installed, unmodified
Impact Pack using ComfyUI's Python environment:

```sh
python Tests/ImpactRuntime/test_detailer_wildcards.py --comfy-dir /path/to/ComfyUI --impact-dir /path/to/custom_nodes/ComfyUI-Impact-Pack
```

These use synthetic detections and a sampling stub on CPU. They verify native
label matching through segmentation masks, `[CONCAT]`, ordered region prompts,
skipping and blank fallback without loading checkpoints or contacting the server.

The server installer regression tests need only Python's standard library:

```sh
python -m unittest discover -s Tests/Installer -v
```

The node tests need a working ComfyUI Python environment. Set `COMFYUI_PATH`
to the ComfyUI core folder containing `comfy/` and `folder_paths.py`, then run
`python -m unittest discover -s Tests/AnimaNode -v`. These use tiny tensors and
do not download models. GitHub Actions runs installer tests on Windows,
macOS and Linux; that does not establish GPU compatibility on every platform.

## Licensing

The included Anima node modifications retain the upstream Apache 2.0 license,
attribution and a full modification diff. Adapter weights have separate terms;
they are downloaded from the author and are not bundled. See
[Third-party notices](THIRD_PARTY_NOTICES.md) and the server installer guide for
the documented upstream weight-license version discrepancy.

## Implementation references

- [ComfyUI server routes](https://docs.comfy.org/development/comfyui-server/comms_routes)
- [ComfyUI server implementation, including job-ID cancellation](https://github.com/Comfy-Org/ComfyUI/blob/master/server.py)
- [Official Anima workflow](https://github.com/Comfy-Org/workflow_templates/blob/main/templates/image_anima_base_v1.json)
- [Anima model variants](https://huggingface.co/circlestone-labs/Anima)
- [Native EasyCache implementation](https://github.com/Comfy-Org/ComfyUI/blob/master/comfy_extras/nodes_easycache.py)

Created by Saelac and ChatGPT.
