# Third-party components

The included Anima compatibility code is derived from LuciferTC9527's
[ComfyUI-Anima_IP-Adapter](https://github.com/LuciferTC9527/ComfyUI-Anima_IP-Adapter)
at commit `6b77cd0c367d76402174ace2be50d3cb6aa77855` and distributed under Apache 2.0.
See [LICENSE](AnimaCompatibility/LICENSE), [NOTICE](AnimaCompatibility/NOTICE),
and the [complete modification diff](AnimaCompatibility/compatibility.patch).
Modified files carry prominent change notices. Keep these documents with redistributed copies.

Model weights are downloaded separately by the optional server installer:

| Component | Source | Terms |
| --- | --- | --- |
| Anima character-reference adapter | [LuciferTC/Anima-IP-Adapter](https://huggingface.co/LuciferTC/Anima-IP-Adapter) | The author's pinned README names CircleStone Labs Non-Commercial v1.0; its linked document is headed v1.2. See the note below. |
| SigLIP2 base patch16 512 | [Google model card](https://huggingface.co/google/siglip2-base-patch16-512) | Apache 2.0, as declared in the model card. |

The adapter's license-version discrepancy is recorded in
[install-manifest.json](AnimaCompatibility/install-manifest.json), with the
author's statement and immutable license URL/hash. The full linked document is
included as [WEIGHTS-LICENSE.reference.txt](AnimaCompatibility/WEIGHTS-LICENSE.reference.txt).
This preserves the reference, not a new grant of rights or a resolution of the
upstream discrepancy. Review the upstream model terms for your intended use;
the node's Apache license does not relicense the adapter weights.

The installer asks users to acknowledge those terms before downloading weights.
No weights, game portraits, game assemblies, Unity/BepInEx assemblies or third-party
DLL dependencies are bundled in the release. Existing checkpoint, LoRA and
Illustrious adapter choices retain their own upstream licenses.
