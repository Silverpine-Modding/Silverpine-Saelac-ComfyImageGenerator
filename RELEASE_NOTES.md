# Comfy Image Generator 1.10.1

- Each detailer now has a native Impact Pack wildcard box supporting per-region
  prompts, `[CONCAT]`, and `[LAB]` / `[ALL]` detector-label prompts, along with
  server-side wildcard files, choices and inline LoRAs.
- Positive override, negative override and wildcard editors scroll independently
  within dark, fixed-height boxes. Each pass includes expandable syntax help.
- Wildcard text is saved per pass and model family and passed through unchanged.
  Existing settings retain their overrides and start with an empty wildcard.
- Label prompts use the detector's exact class names, including segmentation
  labels; they do not identify characters by name.

Replace the plugin DLL and **restart Silverpine**. This update makes no changes
to ComfyUI nodes and requires no ComfyUI restart or Anima installer update from
1.10.0. The existing Anima support package and its licensing remain unchanged.

Validation: release build has zero warnings/errors; all 404 mod assertions and
five CPU integration tests against the installed, unmodified Impact Pack pass.
The integration tests use synthetic detections and stub diffusion sampling;
they verify prompt routing, not generated image quality. In-game layout still
needs visual verification after restarting Silverpine.

## Previous release: 1.10.0

Anima portrait references now encode each selected character separately instead
of placing both portraits on a shared left/right sheet. The scene prompt controls
positions and poses. This reduces the reference sheet's spatial bias, while
smaller traits, clothing and anatomy can still blend.

**Reference image size defaults to 512**, for both fresh settings and older saved
configurations. Change it under **Settings > Portrait references**. Optional values
are 224–2048 in multiples of 16; larger values cost more memory/time and do not
guarantee better results. Output image resolution is independent.

## Server installation

The release includes a complete **AnimaCompatibility** installer and a separate
**AnimaSupport-1.10.0.zip** for copying to another ComfyUI computer.

- Windows, macOS and Linux launchers use the same Python installer.
- Downloads original node files from a pinned GitHub revision, verifies them,
  and installs the included modified node.
- Downloads the adapter and SigLIP2 weights from pinned Hugging Face revisions
  in the same process, after acknowledgement of their upstream terms.
- About 2.01 GB on a fresh setup. Matching model files are verified and reused;
  partial downloads can resume.
- Recognized existing node versions are backed up. Unknown edits and different
  existing model files are preserved and reported.
- Detects standard ComfyUI Python environments, with an explicit path option.
  The node follows ComfyUI's selected compute device.

Node code retains its Apache 2.0 license, attribution, prominent modification
notice and full diff. The adapter weights have separate terms. The author labels
their license v1.0 while linking a document headed v1.2; this discrepancy and the
referenced text are included without relicensing the weights. No weights or game
portraits are bundled. See **THIRD_PARTY_NOTICES.md**.

## Included changes since the last GitHub release (1.8.0)

- Optional IP-Adapter references for up to two selected Anima participants or one
  selected Illustrious participant.
- Per-character current/clothed/nude portraits and PNG/JPEG overrides, saved with
  visual profiles and presets. Existing LLM scene prompting remains in place.
- Reference strength controls, plus Illustrious start/end fractions.
- Dark, scrollable settings with required-node/model information.
- Missing portraits fall back to text-only generation with a viewer notice.
- Reusable temporary portrait uploads and job-specific cancellation.
- Fix for Unity Mono's portrait-upload NullReferenceException after the node
  check, caused by disposing a multipart form twice.
- BepInEx diagnostics include exception types and stack/inner-exception traces
  without logging potentially private exception messages.

Anima's compatibility mapping supports the tested 28-block adapter with NovaFurry's
40-block Anima-2.9B expansion while keeping normal LoRAs composable. Native 28-block
models remain supported. This mapping is not a natively trained 40-block adapter.
Attention Couple remains removed. No prompt/anatomy vetoes are added. Detailer
crops use the ordinary model and LoRAs without portrait conditioning.

## Upgrade

Replace the plugin DLL and keep your configuration and CharacterProfiles folder.
**Restart Silverpine** to load the new version. Run the Anima server installer,
**restart ComfyUI**, then **Connect / refresh lists** before enabling Anima
references. The mod checks for the updated second-portrait input.

Illustrious uses its existing ComfyUI_IPAdapter_plus / SDXL adapter / matching
vision encoder setup. The Anima helper does not install Illustrious models, game
checkpoints, style LoRAs, the Anima text encoder or VAE.

## 1.10.0 validation and limits

- Release build: zero warnings/errors; 367 mod assertions and 28 Unity Mono
  assertions pass.
- Ten node tests cover block mapping, normal LoRA composition, independent
  reference tokens, matching null sequences, reference order and CPU routing.
- Fourteen installer tests cover verified reuse, resume, corrupt downloads,
  fresh installs, upgrades, preservation of edits, paths and model acknowledgement.
- Local installer verified the original downloads, reused both existing weight
  sets and upgraded the recognized experimental node with a backup.
- Production mod workflow completed GPU generations at reference sizes 512, 768
  and 1024 with Dan/Miyo and Oriana, NovaFurry AM v5529B, SilverpineAnimaV2 at 1.0
  and adapter strength 0.2. All three retained the two characters and standing/bed
  composition. This small comparison does not establish a superior size.
- Actual GPU inference is tested on Windows/CUDA. Native Linux GPU and macOS/MPS
  inference remain unverified. The portable installer test matrix covers all
  three operating systems separately from GPU execution.
- New in-game controls still need visual verification after restarting Silverpine.

References remain guidance: identity, anatomy, clothing and character count are
not guaranteed. Larger reference sizes are optional experiments; 512 is the
normal starting point.
