"""Exercise actual Apply routing with small real torch modules, not model mocks."""
import importlib.util
import os
import sys
import types
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, os.environ['COMFYUI_PATH'])
import torch
from torch import nn
from comfy.model_patcher import ModelPatcher
from comfy.lora import calculate_weight

spec = importlib.util.spec_from_file_location('adapter_under_test', ROOT.parents[1]/'AnimaCompatibility/nodes.py')
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)
# Original layer indices in the published Anima-2.9B expansion manifest.
# https://huggingface.co/Gazingstars123/Anima-2.9B/blob/9f9cb50/expand_manifest.json
TARGETS = [0, 1, 3, 4, 6, 7, 9, 10, 12, 13, 15, 16, 18, 19, 20, 22, 23, 25, 26, 28, 29, 31, 32, 34, 35, 37, 38, 39]

class TinyAttention(nn.Module):
    def __init__(self):
        super().__init__()
        for name in ('q_proj', 'k_proj', 'v_proj', 'output_proj'):
            setattr(self, name, nn.Linear(8, 8))
        self.n_heads = 2
        self.head_dim = 4
        self.q_norm = nn.Identity()

    def forward(self, x):
        return self.output_proj(self.q_proj(x))

class TinyBlock(nn.Module):
    def __init__(self):
        super().__init__()
        self.cross_attn = TinyAttention()

    def forward(self, x, emb, cross, **kwargs):
        flat = self.cross_attn(x.flatten(1, 3))
        return x + flat.reshape_as(x)

def TinyModel(count):
    dit = nn.Module()
    dit.blocks = nn.ModuleList(TinyBlock() for _ in range(count))
    base = nn.Module()
    base.diffusion_model = dit
    return ModelPatcher(base, torch.device('cpu'), torch.device('cpu'))

class TinyVision(nn.Module):
    def forward(self, images, **kwargs):
        return types.SimpleNamespace(last_hidden_state=torch.ones(images.shape[0], 4, 4, device=images.device))

def weights(count, shared=False):
    ip = {}
    lora = {}
    for source in range(count):
        tag = (source + 1) / 1000
        for name, shape in [('adaln_ip.1.weight',(8,8)),('adaln_ip.1.bias',(8,))]:
            ip[f'blocks.{source}.{name}'] = torch.full(shape, tag)
        if not shared:
            for proj in ('ip_k_proj','ip_v_proj'):
                ip[f'blocks.{source}.{proj}.weight'] = torch.full((8,4), tag)
                ip[f'blocks.{source}.{proj}.bias'] = torch.full((8,), tag)
        lora[source] = {'q_proj':{'A':torch.full((2,8),tag),'B':torch.ones(8,2)}}
    if shared:
        for proj in ('shared_ip_k_proj','shared_ip_v_proj'):
            for name, shape in [('expand.weight',(8,4)),('expand.bias',(8,)),('project.weight',(8,8)),('project.bias',(8,))]:
                ip[f'{proj}.{name}'] = torch.full(shape, 0.01)
    return dict(ip_weights=ip, num_blocks=count, ip_embed_dim=4, inner_dim=8,
                siglip_encoder=TinyVision(), use_shared_projection=shared,
                lora_weights=lora, lora_rank=2)

class RemapTest(unittest.TestCase):
    def check_routing(self, source_count, model_count, targets, shared=False):
        model = TinyModel(model_count)
        package = weights(source_count, shared)
        image = torch.zeros(1,16,16,3)
        apply = adapter.AnimaIPAdapterApply()
        result, = apply.apply(model, package, image, strength=0.2, use_lora=True)
        blocks = result.model.diffusion_model.blocks
        for source, target in enumerate(targets):
            block = blocks[target]
            self.assertTrue(torch.equal(block.adaln_ip[1].weight, package['ip_weights'][f'blocks.{source}.adaln_ip.1.weight']))
            key=f'diffusion_model.blocks.{target}.cross_attn.q_proj.weight'
            original=block.cross_attn.q_proj.weight.detach().clone()
            expected=original+package['lora_weights'][source]['q_proj']['B']@package['lora_weights'][source]['q_proj']['A']
            actual=calculate_weight(result.patches[key],original.clone(),key)
            torch.testing.assert_close(actual,expected)
            self.assertIsInstance(block.cross_attn.q_proj,nn.Linear)
            self.assertTrue(block._ip_fwd_patched)
            self.assertEqual(len(block.cross_attn._forward_pre_hooks),1)
            if not shared:
                self.assertTrue(torch.equal(block.ip_k_proj.weight, package['ip_weights'][f'blocks.{source}.ip_k_proj.weight']))
            out = block(torch.ones(1,1,1,1,8), torch.ones(1,1,8), None,
                        transformer_options={'anima_ip_tokens':torch.ones(1,4,4)})
            self.assertEqual(tuple(out.shape),(1,1,1,1,8))
            self.assertTrue(torch.isfinite(out).all())
        for target in set(range(model_count))-set(targets):
            self.assertFalse(hasattr(blocks[target],'ip_k_proj'))
            self.assertFalse(hasattr(blocks[target],'_ip_fwd_patched'))
            self.assertIsInstance(blocks[target].cross_attn.q_proj, nn.Linear)
        # Native patches belong to the returned clone, with no permanent LoRA
        # modules or accumulated patches on the source model.
        self.assertEqual(model.patches,{})
        repeated,=apply.apply(model,package,image,strength=0.2,use_lora=True)
        self.assertTrue(all(len(p)==1 for p in repeated.patches.values()))
        disabled,=apply.apply(model, package, image, strength=0.2, use_lora=False)
        self.assertEqual(disabled.patches,{})
        for target in targets:
            self.assertIsInstance(blocks[target].cross_attn.q_proj,nn.Linear)
            self.assertEqual(len(blocks[target].cross_attn._forward_pre_hooks),1)

    def test_expanded_per_block(self):
        self.check_routing(28,40,TARGETS)

    def test_expanded_shared_projection(self):
        self.check_routing(28,40,TARGETS,shared=True)

    def test_native_28_unchanged(self):
        self.check_routing(28,28,list(range(28)))

    def test_native_40_unchanged(self):
        self.check_routing(40,40,list(range(40)))

    def test_external_lora_composes_and_preserves_weight_names(self):
        model=TinyModel(40)
        package=weights(28)
        key='diffusion_model.blocks.3.cross_attn.q_proj.weight'
        original=model.model.state_dict()[key].clone()
        external=torch.full_like(original,0.125)
        model.add_patches({key:('diff',(external,))},0.8)
        result,=adapter.AnimaIPAdapterApply().apply(model,package,torch.zeros(1,16,16,3),strength=0.5,use_lora=True)
        self.assertEqual(len(model.patches[key]),1)
        self.assertEqual(len(result.patches[key]),2)
        expected=original+external*0.8+package['lora_weights'][2]['q_proj']['B']@package['lora_weights'][2]['q_proj']['A']
        torch.testing.assert_close(calculate_weight(result.patches[key],original.clone(),key),expected)
        # A later standard LoRA loader still sees the original parameter paths.
        self.assertIn(key,result.model.state_dict())
        self.assertFalse(any('.base.' in k for k in result.model.state_dict()))

if __name__=='__main__':
    unittest.main(verbosity=2)
