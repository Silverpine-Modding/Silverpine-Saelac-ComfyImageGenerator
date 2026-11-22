# Comfy Image Generator 1.11.0-beta.6

- Illustrious vision prompts request arrays of individual booru-style tags for
  identity, pose, appearance and clothing instead of one long caption string.
  Scenery is supplied as a separate tag array. Join these into comma-separated
  text in a consistent order and remove exact duplicates across groups.
- Ask for one visual concept per item, usually 1–4 words, and about 20–30 useful
  character tags. Prioritize defining traits, current pose and clothing; separate
  color/feature combinations and individual garments instead of prose clauses.
  Quality/score tags stay in the user's base positive prompt.
- Request physical features as concepts with modifiers, and garments with
  separate color/material/pattern fields and detail tags. Assemble those fields
  without inferring or rewriting appearance; sleeves and necklines become their
  own tags instead of extending a long outfit caption.
- Preserve older text replies, flat tag arrays, mixed string/list groups and
  reference fallbacks. Malformed nontext items do not appear as JSON in the prompt.
  No word-count rejection, automatic truncation or new generation vetoes. Anima's
  sentence format, saved profiles, presets and manual Regenerate prompts retain
  their behavior.

Validation: release build with zero warnings/errors, 948 regression assertions
and 40 Unity Mono checks. A live NSFWVision/LM Studio test using Dahlia's saved
profile and clothed portrait returned 28 character tags, each 1–4 words, with
outfit details separated. Model unloading was verified. The text-only coyote
case still produced some long phrases: model adherence varies, and these are
prompt-format tests rather than generated-image quality tests. All output remains
editable; no additional conversion pass or automatic retries are introduced.

**Restart Silverpine and generate a new conversation scene** to use the revised
vision instructions. Regenerate keeps the existing prompt. No ComfyUI/LM Studio
restart or additional nodes are needed.

## Previous beta: 1.11.0-beta.5

- Anima vision scene replies now separate identity, overall body colors, local
  features, current action and clothing into individual fields. The mod joins
  their short sentences into one paragraph per character, preserving the existing
  cast and environment blocks. This avoids asking for one long appearance sentence.
- Remove the vision portrait/character word-count targets. Request short factual
  sentences, keeping all established coat colors and binding local colors to their
  body parts. Do not invent a dominant color or marking placement when unspecified.
  Overall color names, covering and texture are supplied as data; the mod formats
  them into a separate sentence naming that character before local features.
- Follow Anima's natural-language guidance: descriptive scenes with multiple
  sentences, names followed by basic appearance, and separate descriptions for
  each selected character. Quality/score tags remain in the user's base prompt;
  the vision LLM is instructed not to add them.
- Retain older string replies, partial usable replies and existing reference
  fallbacks. No word-count rejection, automatic prose truncation or new generation
  vetoes. Illustrious scene prompts still use booru tags. Saved profiles, presets,
  manual Regenerate prompts and model cleanup retain their behavior.

Validation: release build with zero warnings/errors, 934 regression assertions
and 39 Unity Mono checks. Live NSFWVision/LM Studio tests separated Dahlia's brown
and white fur from her white neck fur using her saved profile and clothed portrait.
Separate rabbit/otter text cases and the two-character Dan/Oriana case also kept
overall colors apart from local features. Model unloading was verified. These are
prompt tests, not image-quality benchmarks; the LLM can still omit or misdescribe
details and occasionally produce longer sentences, particularly for outfits.

**Restart Silverpine and generate a new conversation scene** to use the revised
vision instructions. Regenerate keeps the existing prompt. No ComfyUI/LM Studio
restart or additional nodes are needed.

## Previous beta: 1.11.0-beta.4

- Explicitly request the specific species for every selected character in both
  Anima and Illustrious prompts. Resolve it from the customized profile first,
  with the game's species as a fallback. Keep stated subtypes and hybrids.
- Add a species field to each structured LLM character entry. When that field
  contains a species omitted from the character's prompt, include it as a short
  Anima character label or an Illustrious species tag. Avoid adding it again when
  already present, and do not prepend another full appearance description.
- Vision portrait descriptions also explicitly request the character's name and
  known species. Generic anthro/furry/humanoid wording is not a substitute.
- Missing species fields and unknown species remain usable. No species guesses,
  automatic generation vetoes or new model calls are introduced. Manual edited
  prompts, saved profiles/presets and model cleanup retain their behavior.

Validation: release build with zero warnings/errors, 927 regression assertions
and 38 Unity Mono checks. Live NSFWVision/LM Studio tests included each selected
character's species in the final Anima and Illustrious prompts and confirmed
model unloading. This verifies prompt assembly, not generated-image anatomy or
complete model compliance with every reference detail.

**Restart Silverpine and generate a new conversation scene** to request the new
species field. Regenerate keeps the existing prompt. No ComfyUI/LM Studio restart
or additional nodes are needed.

## Previous beta: 1.11.0-beta.3

- Fix doubled appearance descriptions in vision-generated scene prompts. The
  vision model now combines the saved profile, compatible portrait observations,
  current clothing and action into one complete character paragraph or tag list.
  The mod uses it directly instead of prepending the saved physical description
  and derived identity tags a second time.
- Put each character's current scene action first in its complete vision block.
  Omit the separate composition overview from this path, preventing appearances
  from being repeated there. Cast counts and the scenery block remain separate.
- Vision requests omit redundant derived identity copies and explicitly ask for
  each physical trait once. Saved profile facts remain authoritative in the LLM
  instructions, and current clothing takes precedence over the portrait outfit.
- Collapse exact duplicate Illustrious tags within each returned character block.
  Keep reference fallbacks for missing entries and editable plain-text replies.
  No new anatomy/content checks or generation vetoes.
- Original game-LLM prompting, saved profiles/presets, manual Regenerate prompts
  and model-memory cleanup retain their behavior.

Validation: release build with zero warnings/errors, 914 regression assertions,
and 37 checks using Unity's Mono runtime. Live NSFWVision tests in LM Studio
returned a two-character Anima prompt and a single-character Illustrious prompt
without the extra profile/overview descriptions, and confirmed model unloading.
These checks cover prompt assembly and the memory lifecycle, not image quality
or complete compliance with every character/clothing instruction.

**Restart Silverpine**, then generate a new scene from the conversation to use
the revised vision instructions. Regenerate reuses the existing prompt; it does
not ask the LLM to rewrite it. No ComfyUI/LM Studio restart or node update is needed.

## Previous beta: 1.11.0-beta.2

- Fix empty portrait answers with reasoning models in LM Studio, including
  **NSFWVision v5 Qwen3.5-9B**. Use native chat and turn reasoning off when the
  selected model reports support; read only final message text. Other models
  retain their supported defaults. Requests do not persist chat history.
- Remove the hidden 600-token portrait cap. The configured output-token limit
  now applies to each portrait and the final scene prompt. The settings label
  makes that scope explicit; LM Studio's native temperature range is 0–1.
- Empty-answer errors report available output/reasoning token counts and whether
  the token limit was reached, without exposing prompts or reasoning. Incomplete
  thinking blocks are not passed to ComfyUI as image prompts.
- Model loading/unloading, cancellation, character selection and saved profiles
  retain the beta.1 behavior. No ComfyUI node changes are needed.

Live testing reproduced beta.1's failure with NSFWVision in LM Studio: all 600
output tokens were reasoning, with zero answer text. The updated requests returned
both portrait descriptions and an Anima scene prompt, plus a separate portrait
and Illustrious prompt, with zero reasoning tokens. Both runs confirmed the
selected model had no loaded instances. Validation also passed 897 regression
assertions and 35 checks using Unity's Mono runtime; the release build has zero
warnings/errors. Live tests validate responses and unloading, not prompt accuracy
or image quality. Prompts remain editable.

Replace the DLL and **restart Silverpine**. No ComfyUI or LM Studio restart is
needed. Existing settings and the current enabled/disabled state are preserved.

## Previous beta: 1.11.0-beta.1

- Optional portrait-based scene prompting through **Ollama, KoboldCPP or LM Studio**.
  New dark, scrollable **Vision LLM (beta)** settings provide endpoint/IP/port,
  model/config selection, masked credentials, timeout, context/output limits and
  temperature. Disabled by default so existing users retain the game's LLM.
- Each portrait is read independently with its character identity. The final
  prompt uses those observations, saved profiles, live clothing, dialogue and
  environment. Anima remains limited to two selected participants; Illustrious
  to one. No new anatomy or prompt-content vetoes.
- New scenes in vision mode release ComfyUI models/cache first, then load the
  LLM, create the prompt, explicitly unload it and verify the reported unloaded
  state before submitting an image. ComfyUI models/cache are released again after
  image download, including adapters/detailer execution.
- Cancellation and errors trigger bounded cleanup independent of the cancelled
  request. An unconfirmed LLM unload prevents image submission. A failed ComfyUI
  cleanup preserves a completed image and reports a warning.
- ComfyUI cleanup is also available for the original game-LLM path (default on
  in this beta). Regenerate skips the LLM, keeps edits and still performs cleanup.
- Vision uses existing character portrait-source/file settings, independently
  of IP-Adapter. First-use initialization can save missing visual profiles from
  vision drafts; existing profiles and preset formats are preserved.

**Requirements:** Ollama with an installed vision model; LM Studio **0.4.0+** with
native v1 model-management endpoints; or KoboldCPP with **Model Administration**,
its admin password/config directory, and a vision config containing the matching
MMProj. See README for setup and shared-server behavior. No new ComfyUI nodes,
weight downloads, node modifications or installer changes are required.

**Beta limitations:** model reloads add latency. Separate game/LLM processes can
still occupy VRAM. CUDA-style ComfyUI allocator cleanup is checked; CPU/MPS and
other backends without separate counters report acknowledgement, not measured
release. This beta rereads portraits per new scene and does not promise improved
anatomy or removal of identity blending. The manual profile-draft button still
uses the game's LLM. See README for cancellation during model loading.

Replace the DLL and **restart Silverpine**. Enable the feature in **Settings →
Vision LLM (beta)**, configure the server/model and generate a new conversation
scene. Keep the previous DLL for rollback. No ComfyUI restart is needed.

Validation: release build, protocol/lifecycle regression tests for all three
providers and image cleanup, plus actual Unity Mono HTTP/serialization checks.
These are simulated API tests, not a successful end-to-end live model benchmark.
The local Ollama server answered its model list but rejected the listed model's
metadata request; other default local server ports did not respond during testing.

## Previous version: 1.10.2

- Structured scene prompts lead with the selected cast count and the main action,
  followed by each character's own appearance/action paragraph and the environment.
- Two-character Anima instructions emphasize named ownership of features,
  clothing and touching limbs, consistent roles/poses, and similar detail for
  both participants. Scene placement remains free, including side changes,
  close contact and shared furniture.
- Scene capture supplies player and NPC sex/pronoun hints. The LLM resolves
  explicit custom-profile gender first; matching selected entries produce count
  tags such as `1boy, 1girl` or `2girls`. Explicit intersex/nonbinary/other entries
  count as `other`; unknown gender stays unspecified. Count tags do not set age.
- Illustrious retains its one-character limit and places solo/gender counts and
  action before appearance. No new content vetoes or automatic weights are added.
- Existing styling prefixes and manual prompt edits remain under user control.

**Restart Silverpine**, then generate the current dialogue again to use the new
LLM instructions. Regenerate reuses the existing edited prompt. No ComfyUI node
changes, server restart or adapter reinstall are needed.

Validation: clean release build and 432 mod assertions, including mixed/same/unknown
gender cases, profile overrides, selection order, fallback responses, and the
submitted API prompt order. These checks verify prompt construction, not a
measured reduction in blending or image quality across checkpoints.

## Previous release: 1.10.1

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
