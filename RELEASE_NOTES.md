# Comfy Image Generator 1.6.1

Generate images from Silverpine conversations with a BepInEx 5 plugin connected
to your own ComfyUI server. Supports Illustrious tag prompts and Anima prose
prompts, using the game's configured LLM and editable character visual profiles.

## Attention Couple advisory

**Attention Couple remains experimental. It can still fail to follow character
prompts even when the character boxes are separated.** Known failures include
characters merging or being omitted, traits/species/clothing crossing between
characters, ignored anatomy such as digitigrade legs, and visible artifacts at
region boundaries. Small boxes and higher prompt strength are not reliable fixes.

Review the LLM-generated character prompts before regenerating. Conflicting
anatomy descriptions can affect results independently of the coupler. Controlled
same-seed tests showed improved identity/outfit separation in some coupled
samples, but inconsistent leg anatomy and color fidelity. This release does not
claim to solve those remaining limitations, and adds no automatic anatomy
validators or corrective retries. Attention Couple is optional and can be disabled.

## Included

- Generate scene button beside the conversation's `...` button; scene references
  include conversation participants, physical descriptions and current clothing.
- Dark image viewer with resolution-aware display, scrolling prompt editing,
  regeneration, named permanent saves, and cancellation when the viewer closes.
- Editable character visual profiles, first-use LLM initialization, and profile presets.
- ComfyUI address/IP and port, checkpoint/model selection, user-configurable
  steps/CFG/resolution/seed, multiple LoRAs, shared positive styling prefix,
  optional EasyCache, and multiple detailer passes.
- Optional Illustrious Attention Couple with a resolution-aware region map,
  separate character prompts, mask weights and prompt strengths.
- Required-node checks and disabled controls when dependencies are unavailable.

## Regional detailer fix

An earlier filter could detect faces but reject every one because their padded
sampling crops crossed character boundaries. Version 1.6.1 selects whole
detections through existing Impact Pack workflow nodes: at least half the mask
must lie inside a character region, and earlier regions take priority for
overlaps/ties. Original masks and padded crops remain intact. Each configured
pass detects once before assigning its detections, and reports detected/selected
counts. Subsequent configured passes may intentionally detail the same face again.

**No Silverpine companion node is required or included.** The plugin does not
modify existing ComfyUI custom-node files. If the required nodes are already
loaded, this update does not need a ComfyUI restart. Whole-face detailing does
not guarantee reconstruction of malformed bodies or missing characters.

## Install

1. Install Silverpine 1.7.3, BepInEx 5, and
   [Modding Tools 1.10.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases).
2. Extract `ComfyImageGenerator-1.6.1.zip` into `BepInEx/plugins/` so the DLL is at
   `BepInEx/plugins/ComfyImageGenerator/ComfyImageGenerator.dll`. Replace an older
   DLL; keep your existing configuration and character profiles.
3. Start/restart the game, open the generator settings, enter your ComfyUI server
   address and port, then connect and refresh the available models/nodes.

The server must support job-specific cancellation. Install compatible model
weights separately. Optional Attention Couple requires ComfyUI-ppm; detailers
require Impact Pack, Impact Subpack and detector weights. The README lists exact
node requirements. The ZIP includes only this plugin DLL and documentation.

## Validation

- Release build: 0 warnings, 0 errors.
- C# regression suite: 378 assertions passed.
- Six tests against installed, unmodified Impact mask/filter algorithms passed.
- Live comparison on the earlier failed image: old filter selected 0 of 2 faces;
  workflow-only replacement selected both, one per region.
- Live generation with clarified LLM prompts requesting digitigrade Dan and
  Oriana: both faces detected and selected once in the detailer pass. Anatomy
  consistency remains unresolved. These are controlled single-seed comparisons,
  not a guarantee across checkpoints, scenes, or displays.

Created by Saelac and ChatGPT.
