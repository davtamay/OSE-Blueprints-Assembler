#!/usr/bin/env python3
"""
build_hand_glb.py — Procedural low-poly hand mesh for tool_hand visualization.

Constructs a stylized right-hand pinch-grip pose from trimesh primitives
(palm + thumb + index + curled middle/ring/pinky) and exports as GLB to
the d3d_v18_10 package's tools assets folder.

The hand is centered on its bounding box; the pinch point (thumb tip +
index tip nearly touching) sits at +Z in the mesh's local frame so that
toolPose.tipPoint = (0, 0, ~0.04) lines up at the tip. The base of the
palm sits at -Z, so toolPose.gripPoint = (0, 0, ~-0.04) places the cursor
anchor at the wrist.

Approximate finished hand size: ~10 cm across, ~8 cm tall, ~3 cm thick.
"""

from pathlib import Path
import sys

import numpy as np
import trimesh

REPO_ROOT = Path(__file__).resolve().parent.parent
OUT_PATH  = REPO_ROOT / "Assets/_Project/Data/Packages/d3d_v18_10/assets/tools/tool_hand.glb"


def capsule(length: float, radius: float) -> trimesh.Trimesh:
    """A Y-axis capsule: cylinder of `length` with hemispherical caps."""
    cyl     = trimesh.creation.cylinder(radius=radius, height=length, sections=12)
    cap_top = trimesh.creation.icosphere(radius=radius, subdivisions=2)
    cap_top.apply_translation([0, length / 2, 0])
    cap_bot = trimesh.creation.icosphere(radius=radius, subdivisions=2)
    cap_bot.apply_translation([0, -length / 2, 0])
    return trimesh.util.concatenate([cyl, cap_top, cap_bot])


def main() -> None:
    parts = []

    # ── Palm: flattened ellipsoid (scaled sphere) ──
    palm = trimesh.creation.icosphere(radius=0.025, subdivisions=3)
    palm.apply_scale([1.4, 1.0, 0.6])  # wider × medium × thinner — flat hand
    parts.append(palm)

    # ── Thumb: angled capsule on the right side, tip pointing up-and-in ──
    thumb = capsule(length=0.035, radius=0.011)
    # Move base to right edge of palm, then rotate ~45° so tip aims toward
    # the index finger's tip → pinch grip.
    thumb_xform = trimesh.transformations.concatenate_matrices(
        trimesh.transformations.translation_matrix([0.030, 0.020, 0.0]),
        trimesh.transformations.rotation_matrix(np.radians(-45), [0, 0, 1]),
    )
    thumb.apply_transform(thumb_xform)
    parts.append(thumb)

    # ── Index finger: extended forward, tip curling toward thumb ──
    index = capsule(length=0.050, radius=0.009)
    index_xform = trimesh.transformations.concatenate_matrices(
        trimesh.transformations.translation_matrix([0.014, 0.045, 0.0]),
        trimesh.transformations.rotation_matrix(np.radians(15), [0, 0, 1]),
    )
    index.apply_transform(index_xform)
    parts.append(index)

    # ── Middle / ring / pinky: shorter, slight forward angle, curled down ──
    other_fingers = [
        # (x_offset_from_center, length, radius)
        (-0.005, 0.040, 0.0085),  # middle
        (-0.022, 0.034, 0.0080),  # ring
        (-0.038, 0.028, 0.0075),  # pinky
    ]
    for xoff, length, radius in other_fingers:
        f = capsule(length=length, radius=radius)
        xform = trimesh.transformations.concatenate_matrices(
            trimesh.transformations.translation_matrix([xoff, 0.030, 0.0]),
            trimesh.transformations.rotation_matrix(np.radians(25), [1, 0, 0]),  # curl forward (out of palm plane)
        )
        f.apply_transform(xform)
        parts.append(f)

    # ── Wrist stub: short cylinder hanging off the palm base ──
    wrist = capsule(length=0.030, radius=0.018)
    wrist.apply_scale([1.1, 1.0, 0.6])  # match palm flatness
    wrist.apply_translation([0.0, -0.030, 0.0])
    parts.append(wrist)

    hand = trimesh.util.concatenate(parts)

    # Center bounding box on the origin so the GLB's pivot is at the
    # geometric center. Scaling/positioning at runtime then aligns the
    # cursor anchor with the toolPose.gripPoint we set in shared.json.
    bbox_center = (hand.bounds[0] + hand.bounds[1]) / 2.0
    hand.apply_translation(-bbox_center)

    # Re-orient: the construction above used Y-forward (palm faces +Z,
    # fingers extend in +Y). Rotate so the tip direction is +Z (matches
    # GLTF/Unity forward convention) — pinch point ends up at +Z which
    # pairs with toolPose.tipPoint = (0, 0, +Z_extent).
    hand.apply_transform(trimesh.transformations.rotation_matrix(np.radians(-90), [1, 0, 0]))

    # Skin-tone vertex color so the placeholder reads as a hand even
    # without the editor's cyan tint in subsequent passes.
    hand.visual = trimesh.visual.ColorVisuals(
        mesh=hand,
        vertex_colors=np.tile([210, 180, 150, 255], (len(hand.vertices), 1)),
    )

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    hand.export(str(OUT_PATH))

    bounds = hand.bounds
    extents = bounds[1] - bounds[0]
    print(f"Wrote {OUT_PATH}")
    print(f"  vertices: {len(hand.vertices)}")
    print(f"  faces:    {len(hand.faces)}")
    print(f"  bounds:   {bounds.tolist()}")
    print(f"  extents:  {extents.tolist()}  (~m)")


if __name__ == "__main__":
    sys.exit(main() or 0)
