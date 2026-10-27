"""Optional CPU integration checks against an installed, unmodified Impact Pack.

Run with ComfyUI's Python:
  python test_detailer_wildcards.py --comfy-dir /path/to/ComfyUI \
    --impact-dir /path/to/custom_nodes/ComfyUI-Impact-Pack

Synthetic detections and a stub for diffusion sampling avoid loading weights or
contacting a running server. Impact Pack performs mask combination, label/region
prompt selection, CONCAT handling and image compositing itself.
"""
import argparse
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

parser = argparse.ArgumentParser()
parser.add_argument("--comfy-dir", required=True, type=Path)
parser.add_argument("--impact-dir", required=True, type=Path)
options = parser.parse_args()
sys.argv = [sys.argv[0]]
sys.path[:0] = [str(options.comfy_dir), str(options.impact_dir / "modules")]

from comfy.cli_args import args
args.cpu = True
from aiohttp import web
from server import PromptServer
# Only collect node routes in this test process; do not start a server.
PromptServer.instance = SimpleNamespace(routes=web.RouteTableDef(), add_on_prompt_handler=lambda handler: None)
# Match the package's initialization order to avoid its circular imports.
import impact.impact_server
from impact import core
from impact.impact_pack import FaceDetailer
import numpy as np
import torch


class Detector:
    def __init__(self, segs):
        self.segs = segs
        self.calls = 0

    def setAux(self, value):
        pass

    def detect(self, *args, **kwargs):
        self.calls += 1
        return self.segs


class DetailerWildcardTests(unittest.TestCase):
    def run_detailer(self, wildcard):
        # Deliberately put the right-hand region first to distinguish label
        # matching from list order and to exercise native ASC/DSC sorting.
        right = core.SEG(None, np.ones((16, 16), dtype=np.float32), .99,
                         (32, 0, 48, 16), (32, 0, 48, 16), "hand", None)
        left = core.SEG(None, np.ones((16, 16), dtype=np.float32), .99,
                        (0, 0, 16, 16), (0, 0, 16, 16), "face", None)
        segs = ((32, 64), [right, left])
        bbox, segm = Detector(segs), Detector(segs)
        calls = []

        def sample(image, *args, **kwargs):
            calls.append((args[6], kwargs["wildcard_opt"], kwargs["wildcard_opt_concat_mode"], args[12], args[13]))
            return image, None

        with patch.object(core, "enhance_detail", side_effect=sample):
            result = FaceDetailer().doit(
                image=torch.zeros((1, 32, 64, 3)), model=object(), clip=None, vae=None,
                guide_size=64, guide_size_for=True, max_size=128, seed=42, steps=1,
                cfg=1, sampler_name="euler", scheduler="normal", positive="styled positive",
                negative="negative override", denoise=.2, feather=0, noise_mask=True,
                force_inpaint=True, bbox_threshold=.5, bbox_dilation=0, bbox_crop_factor=1,
                sam_detection_hint="center-1", sam_dilation=0, sam_threshold=.93,
                sam_bbox_expansion=0, sam_mask_hint_threshold=.7,
                sam_mask_hint_use_negative="False", drop_size=1,
                bbox_detector=bbox, segm_detector_opt=segm, wildcard=wildcard)
        self.assertEqual(bbox.calls, 1)
        self.assertEqual(segm.calls, 1)
        self.assertEqual(tuple(result[0].shape), (1, 32, 64, 3))
        for _, _, _, positive, negative in calls:
            self.assertEqual(positive, "styled positive")
            self.assertEqual(negative, "negative override")
        return calls

    def test_concat_labels_with_common_prefix(self):
        calls = self.run_detailer("[CONCAT][LAB]\n[ALL] fine texture,\n[face] amber eyes\n[hand] relaxed fingers")
        self.assertEqual([c[1] for c in calls], ["fine texture,relaxed fingers", "fine texture,amber eyes"])
        self.assertEqual([c[2] for c in calls], ["concat", "concat"])

    def test_unmatched_label_keeps_positive(self):
        calls = self.run_detailer("[LAB][face] amber eyes[HAND] wrong case")
        self.assertEqual([c[1] for c in calls], ["", "amber eyes"])
        self.assertEqual([c[2] for c in calls], [None, None])

    def test_ordered_region_prompts(self):
        for mode, expected_x in [("ASC", [0, 32]), ("DSC", [32, 0])]:
            with self.subTest(mode=mode):
                calls = self.run_detailer(f"[CONCAT][{mode}]first[SEP]second")
                self.assertEqual([c[0][0] for c in calls], expected_x)
                self.assertEqual([c[1] for c in calls], ["first", "second"])
                self.assertEqual([c[2] for c in calls], ["concat", "concat"])

    def test_skip_region(self):
        calls = self.run_detailer("[ASC][SKIP][SEP]right only")
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][0][0], 32)
        self.assertEqual(calls[0][1], "right only")

    def test_blank_inherits(self):
        calls = self.run_detailer("")
        self.assertEqual([c[1] for c in calls], ["", ""])
        self.assertEqual([c[2] for c in calls], [None, None])


if __name__ == "__main__":
    unittest.main(verbosity=2)
