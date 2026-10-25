# Comfy Image Generator 1.8.0

Attention Couple and prompt-content rejection have been removed. Generate images
from Silverpine dialogue with selectable characters, editable prompts and ordinary
full-image detailer passes.

## Changes

- Removed the entire Attention Couple setup: toggle, region maps/editors, masks,
  regional prompt generation, character targets, special detailer filters/reports,
  configuration bindings, PPM node requirements and obsolete tests.
- Human/species/anatomy wording no longer rejects a generation. Incorrect wording
  remains editable through Regenerate / edit prompt.
- Missing, duplicate or extra LLM character entries no longer reject the job.
  Available selected entries are kept, extras are skipped, and missing portions use
  selected character/clothing/scene references. Plain prompt text is accepted.
  Fallbacks are disclosed in the viewer; no extra corrective LLM request is made.
- Included the character picker introduced locally in 1.7.0: Anima supports one or
  two selected participants, Illustrious exactly one. The player is optional.
  Unselected conversation participants remain off-screen in the LLM instructions.
- Kept ordinary detailers, LoRAs, EasyCache, base styling, visual profiles/presets,
  dark scrollable editors, named image saves and cancellation on close.
- Removed unused legacy scene-reference fields and obsolete documentation.

Cast-selection limits and technical checks for server/node availability, valid
settings, request timeouts and cancellation remain. Generated image quality and
the LLM's free-form wording are not automatically judged.

## Upgrade

Extract `ComfyImageGenerator-1.8.0.zip` into `BepInEx/plugins/`, replacing the old
`ComfyImageGenerator/ComfyImageGenerator.dll`. Keep your existing configuration and
`CharacterProfiles` folder. Restart Silverpine to load the DLL.

Old coupling configuration is ignored. Detailer passes preserve their existing
enable states and settings; old region IDs no longer apply. Positive overrides
now replace the scene prompt for a full-image detailer pass. Review those overrides
if you previously used them as extra regional tags.

Requires Silverpine 1.7.3, BepInEx 5, Modding Tools 1.10.0 or newer, and a ComfyUI
server supporting job-specific cancellation. Ordinary detailers require Impact Pack,
Impact Subpack and compatible detector weights. This release includes no custom
ComfyUI node, modifies no server node files, and needs no ComfyUI restart.

## Validation

- Release build: zero warnings and errors.
- 286 assertions passed, including permissive prompts through a simulated ComfyUI
  submission, legacy coupling settings, ordinary detailer graphs, cast selection,
  profiles, save paths, cancellation and temporary-image cleanup.
- Live GPU image quality and the in-game UI were not exercised for this release.

Created by Saelac and ChatGPT.
