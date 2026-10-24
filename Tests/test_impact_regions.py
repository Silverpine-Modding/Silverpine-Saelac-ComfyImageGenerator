"""Exercise installed, unmodified Impact algorithms on masks; no GPU/models.
Run with the ComfyUI Python environment and the Impact Pack directory as argv[1].
AST extraction avoids importing ComfyUI's server and GPU initialization.
"""
import ast
from collections import namedtuple
import logging
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
import cv2
import numpy as np
import torch

impact = Path(sys.argv.pop(1)) / "modules/impact"
SEG = namedtuple("SEG", "cropped_image cropped_mask confidence crop_region bbox label control_net_wrapper")
scope = dict(np=np, torch=torch, cv2=cv2, logging=logging, SEG=SEG)


def load(filename, names):
    path = impact / filename
    tree = ast.parse(path.read_text(encoding="utf-8"))
    definitions = [n for n in tree.body if isinstance(n, (ast.FunctionDef, ast.ClassDef)) and n.name in names]
    assert len(definitions) == len(names)
    exec(compile(ast.Module(body=definitions, type_ignores=[]), str(path), "exec"), scope)


load("utils.py", ["make_crop_region", "normalize_region", "bitwise_and_masks"])
scope["utils"] = SimpleNamespace(**scope)
load("core.py", ["mask_to_segs", "segs_to_combined_mask"])
scope["core"] = SimpleNamespace(**scope)
load("segs_nodes.py", ["SEGSIntersectionFilter"])
filter_node = scope["SEGSIntersectionFilter"]()


def rectangle(bounds, size=128):
    mask = np.zeros((size, size), dtype=np.float32)
    x1, y1, x2, y2 = bounds
    mask[y1:y2, x1:x2] = 1
    return mask


def face(bounds, size=128):
    return scope["mask_to_segs"](rectangle(bounds, size), True, 3, False)[1][0]


def assign(segments, boxes, size=128):
    available = ((size, size), segments)
    results = []
    for box in boxes:
        outside = scope["mask_to_segs"](1 - rectangle(box, size), True, 1, False)
        selected = filter_node.doit(available, outside, .5)[0]
        results.append(selected[1])
        available = filter_node.doit(available, selected, .99)[0]
    return results


class ImpactWorkflowTests(unittest.TestCase):
    def test_context_crops_cross_boxes_without_rejection_or_mutation(self):
        faces = [face((30, 20, 54, 48)), face((76, 20, 100, 48))]
        masks = [f.cropped_mask.copy() for f in faces]
        self.assertGreater(faces[0].crop_region[2], 60)
        selected = assign(faces, [(0, 0, 60, 128), (68, 0, 128, 128)])
        self.assertEqual(list(map(len, selected)), [1, 1])
        for i, original in enumerate(faces):
            self.assertIs(selected[i][0], original)
            np.testing.assert_array_equal(selected[i][0].cropped_mask, masks[i])

    def test_exact_boundary_face_is_kept_whole_once(self):
        original = face((48, 20, 80, 48))  # 32 mask pixels: 16 on each side
        selected = assign([original], [(0, 0, 64, 128), (64, 0, 128, 128)])
        self.assertEqual(list(map(len, selected)), [1, 0])
        self.assertIs(selected[0][0], original)

    def test_overlapping_regions_have_list_order_priority(self):
        original = face((55, 20, 75, 48))
        selected = assign([original], [(0, 0, 96, 128), (32, 0, 128, 128)])
        self.assertEqual(list(map(len, selected)), [1, 0])

    def test_empty_and_outside_detections(self):
        boxes = [(0, 0, 40, 128), (88, 0, 128, 128)]
        self.assertEqual(list(map(len, assign([], boxes))), [0, 0])
        self.assertEqual(list(map(len, assign([face((55, 20, 75, 48))], boxes))), [0, 0])

    def test_full_frame_box_and_three_regions(self):
        faces = [face((10, 20, 25, 40)), face((52, 20, 68, 40)), face((98, 20, 113, 40))]
        self.assertEqual(list(map(len, assign(faces, [(0, 0, 128, 128)]))), [3])
        self.assertEqual(list(map(len, assign(faces, [(0, 0, 40, 128), (40, 0, 88, 128), (88, 0, 128, 128)]))), [1, 1, 1])

    def test_face_mask_not_bbox_or_padding_decides_assignment(self):
        original = face((30, 20, 100, 48))
        # Asymmetric segmentation: bbox spans both columns but visible face is right.
        mask = original.cropped_mask.copy()
        mask[:, :70 - original.crop_region[0]] = 0
        original = original._replace(cropped_mask=mask)
        selected = assign([original], [(0, 0, 64, 128), (64, 0, 128, 128)])
        self.assertEqual(list(map(len, selected)), [0, 1])
        self.assertIs(selected[1][0], original)


if __name__ == "__main__":
    unittest.main()
