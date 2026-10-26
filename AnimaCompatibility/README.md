# Anima portrait support installer

This package installs the modified [ComfyUI-Anima_IP-Adapter](https://github.com/LuciferTC9527/ComfyUI-Anima_IP-Adapter) node and its required reference models on the **ComfyUI server**. The installer runs on Windows, macOS and Linux using Python 3.8 or newer. The Silverpine game plugin is separately built for the tested Windows game installation.

## Quick installation

1. Extract the whole package. Keep all files together.
2. Run the launcher for your server:
   - **Windows:** double-click `Install-AnimaSupport.cmd`.
   - **macOS:** open `Install-AnimaSupport.command`. If executable permissions were lost during extraction, run `sh Install-AnimaSupport.command` in Terminal.
   - **Linux:** run `sh Install-AnimaSupport.sh`.
3. Choose the ComfyUI **data folder containing custom_nodes and models**. Desktop's data folder can differ from its application folder. Portable parent folders and the old node-folder argument are also accepted.
4. Review the displayed adapter model terms and type `YES` to download the weights. The helper checks the Python used by ComfyUI; if it cannot locate it, supply that executable when prompted.
5. Restart ComfyUI after installation. In Silverpine, choose Anima, open **Settings > Portrait references**, click **Connect / refresh lists**, select the adapter and enable references. Save settings. Restart Silverpine separately after replacing its DLL.

The installer downloads about **2.01 GB** on a fresh installation:

- `models/ipadapter/ip_adapter-Character_Reference-10.safetensors`
- SigLIP2 `config.json`, `model.safetensors` and `preprocessor_config.json` under `models/siglip2/siglip2-base-patch16-512/`

It downloads original node files from **GitHub**, checks their SHA-256 hashes, then installs this package's modified `nodes.py`. Models come directly from their authors' **Hugging Face** repositories. Every download uses an immutable revision and the size/hash recorded in `install-manifest.json`; this is not an automatic update to whatever upstream publishes next. Matching existing files are reused. Partial downloads can resume, and mismatching existing model files are left untouched.

This helper does not install the game checkpoint, Silverpine style LoRA, Anima text encoder or VAE, or the separate Illustrious adapter. Those remain configured through their existing setup instructions.

## Command line

Windows PowerShell:

```powershell
& "C:\ComfyUI\.venv\Scripts\python.exe" .\install.py --comfy-dir "C:\ComfyUI"
```

macOS / Linux:

```sh
python3 install.py --comfy-dir "$HOME/ComfyUI" --python "$HOME/ComfyUI/.venv/bin/python"
```

Options:

- `--python PATH`: use a specific ComfyUI Python environment. Standard venv and Windows portable environments are detected automatically. Dependencies must be installed in the environment that actually runs ComfyUI.
- `--node-only`: install node code alone; skip models and dependency changes.
- `--skip-dependencies`: download/install without changing Python dependencies.
- `--non-interactive --accept-model-license`: explicit acknowledgement for unattended model installation; also supply `--comfy-dir`.
- A positional data-folder or existing node-folder path remains accepted.

The helper checks ComfyUI's core dependencies and installs `transformers==4.57.3` only if transformers is absent. It checks an existing version for SigLIP support without upgrading it. Network access is needed for uncached files. Verified originals and a receipt are kept in `user/silverpine-anima-installer/`. It does not stop or restart the server.

## What changed

The included code is based on upstream commit `6b77cd0c367d76402174ace2be50d3cb6aa77855`. See `compatibility.patch` for the full diff.

- Encode each selected portrait independently, then combine their reference tokens. The second portrait is connected through optional `ref_image_2`. This avoids putting both characters on a single left/right reference sheet.
- Match unconditional/null reference tokens to the combined sequence length.
- Map a 28-block adapter into the original layers of the tested NovaFurry Anima-2.9B 40-block expansion. Insertions are `2,5,8,11,14,17,21,24,27,30,33,36`, as recorded in the [expansion manifest](https://huggingface.co/Gazingstars123/Anima-2.9B/blob/9f9cb50/expand_manifest.json). Native 28-to-28 and 40-to-40 mappings remain unchanged. This does not make a natively trained 40-block adapter, and unrelated expansion layouts are not established by this mapping.
- Compose the adapter's bundled LoRA with native ComfyUI patches so regular style LoRAs retain their parameter names.
- Use ComfyUI's selected compute device instead of assuming CUDA.
- Expose a capability marker; the mod also checks for the second-reference input before enabling this workflow.

No new node types or ComfyUI core changes are required. Windows/CUDA and a small CPU routing test are checked locally; native macOS/MPS and Linux GPU execution have not been tested here.

## Reference settings

**Reference image size defaults to 512**, including existing settings saved before this option existed. Optional values range from 224 to 2048 in multiples of 16. Try 768 or 1024 if more portrait detail helps, but larger input does not guarantee better identity or anatomy and costs memory/time. Output image resolution is configured separately. Game portrait uploads remain bounded to 1536 pixels on their longest side, so enlarging a smaller source cannot create missing detail.

Start at strength **0.2**. The scene prompt controls placement and poses; no fixed output regions are assigned. Independent references can still blend smaller traits or copy clothing. Detailer crops use the ordinary model/LoRAs without portrait conditioning.

## Updates and rollback

Recognized upstream and earlier Silverpine versions are backed up as `nodes.py.before-silverpine-<hash>.bak` before replacement. Unknown versions or custom edits are refused without replacing the node. Review/port `compatibility.patch` for those versions instead of discarding changes. Re-running on the installed version is safe.

Updating this node through another manager may overwrite the modifications. Stop ComfyUI before manually restoring a backup, then restart it. To undo the patch, replace `nodes.py` with the desired backup. Ordinary text generation remains available; Anima portrait mode requires the compatibility update. Model downloads are retained for reuse.

## Licensing

- **Node code:** Apache License 2.0, included in `LICENSE`; attribution and modification details are in `NOTICE`.
- **Adapter weights:** the [author's pinned README](https://github.com/LuciferTC9527/ComfyUI-Anima_IP-Adapter/blob/6b77cd0c367d76402174ace2be50d3cb6aa77855/README.md) labels them CircleStone Labs Non-Commercial License **v1.0**, but its linked license currently identifies **v1.2**. `WEIGHTS-LICENSE.reference.txt` preserves the linked document and its origin/hash is recorded in the manifest. This package documents that discrepancy; it does not resolve it or relicense the weights. Review the upstream terms for your intended use.
- **SigLIP2:** Google's [model card](https://huggingface.co/google/siglip2-base-patch16-512) identifies Apache 2.0.

No model weights or game portraits are bundled. The code license does not replace the separate model terms. Pass along the license, notice, modification diff and model-term reference when redistributing this package.
