import unittest
import torch
import types
from unittest.mock import patch
import test_remap as base

adapter = base.adapter

class ColorVision:
    def to(self, *args, **kwargs): return self
    def __call__(self, images, **kwargs):
        colors=images.mean(dim=(2,3))
        extra=colors[:, :1] * .25
        tokens=torch.cat((colors,extra),dim=-1).unsqueeze(1).repeat(1,4,1)
        return types.SimpleNamespace(last_hidden_state=tokens,hidden_states=[tokens])

class SeparateTests(unittest.TestCase):
    def test_comfy_device_selection_supports_cpu(self):
        with patch.object(adapter.model_management, 'get_torch_device', return_value=torch.device('cpu')):
            _, handler = self.apply()
        self.assertEqual(handler.ip_tokens.device.type, 'cpu')
        self.assertEqual(tuple(handler.ip_tokens.shape), (1,8,4))

    def apply(self, reverse=False, gray=False, checkpoint_null=False):
        package=base.weights(28)
        package['siglip_encoder']=ColorVision()
        if checkpoint_null: package['null_tokens']=torch.full((1,4,4),.125)
        first=torch.zeros(1,16,16,3); first[:,:,:,0]=1
        second=torch.zeros(1,16,16,3); second[:,:,:,2]=1
        if reverse: first,second=second,first
        result,=adapter.AnimaIPAdapterApply().apply(base.TinyModel(40),package,first,strength=.2,ref_image_2=second,gray_null=gray)
        return result, result.model_options['model_function_wrapper']

    def test_both_references_reach_conditional_and_unconditional_is_null(self):
        _,handler=self.apply()
        self.assertEqual(tuple(handler.ip_tokens.shape),(1,8,4))
        self.assertFalse(torch.equal(handler.ip_tokens[:,:4],handler.ip_tokens[:,4:]))
        captured=[]
        def capture(x,t,**kwargs):
            captured.append(kwargs['transformer_options']['anima_ip_tokens'].clone())
            return x
        handler(capture,{'input':torch.zeros(2,1),'timestep':torch.zeros(2),'c':{},'cond_or_uncond':[0,1]})
        torch.testing.assert_close(captured[0][0],handler.ip_tokens[0].to(captured[0]))
        self.assertEqual(captured[0][1].abs().sum().item(),0)
        captured.clear()
        handler(capture,{'input':torch.zeros(1,1),'timestep':torch.zeros(1),'c':{},'cond_or_uncond':[0]})
        self.assertEqual(tuple(captured[0].shape),(1,8,4))

    def test_order_is_only_a_token_permutation(self):
        _,a=self.apply(); _,b=self.apply(reverse=True)
        torch.testing.assert_close(a.ip_tokens[:,:4],b.ip_tokens[:,4:])
        torch.testing.assert_close(a.ip_tokens[:,4:],b.ip_tokens[:,:4])
        torch.manual_seed(145)
        q=torch.randn(1,2,7,4,device=a.ip_tokens.device)
        ka=a.ip_tokens.unsqueeze(1).expand(-1,2,-1,-1)
        kb=b.ip_tokens.unsqueeze(1).expand(-1,2,-1,-1)
        torch.testing.assert_close(torch.nn.functional.scaled_dot_product_attention(q,ka,ka),torch.nn.functional.scaled_dot_product_attention(q,kb,kb),rtol=1e-5,atol=1e-6)

    def test_checkpoint_nulls_repeat_per_reference(self):
        _,handler=self.apply(checkpoint_null=True)
        self.assertEqual(tuple(handler.null_tokens.shape),(1,8,4))
        torch.testing.assert_close(handler.null_tokens,torch.full_like(handler.null_tokens,.125))

    def test_gray_null_has_full_sequence(self):
        _,handler=self.apply(gray=True)
        self.assertEqual(handler.null_tokens.shape,handler.ip_tokens.shape)
        torch.testing.assert_close(handler.null_tokens[:,:4],handler.null_tokens[:,4:])

if __name__=='__main__': unittest.main(verbosity=2)
