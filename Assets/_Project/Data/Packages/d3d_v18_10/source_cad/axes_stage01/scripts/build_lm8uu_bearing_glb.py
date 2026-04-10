"""
build_lm8uu_bearing_glb.py
--------------------------
Procedural LM8UU linear bearing for the D3D 8mm rod axes.

LM8UU standard dimensions:
  Bore (inner diameter):   8 mm
  Outer diameter:         15 mm
  Length:                 24 mm
  Flange:                 none (UU = double-sealed, no flange)

No FreeCAD source file exists on the OSE wiki for this part.
Geometry is dimension-authored from the standard LM8UU callout.

Modelled as a hollow cylinder (outer housing) with a visible inner bore
and two end-cap grooves — sufficient visual fidelity for XR assembly training.

Usage (Blender CLI):
  blender --background --python build_lm8uu_bearing_glb.py -- \
      --output <path/to/d3d_x_axis_lm8uu_bearing_approved.glb> \
      [--report <path/to/report.json>]
"""

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


# LM8UU locked dimensions (metres)
BORE_D   = 0.008
OUTER_D  = 0.015
LENGTH   = 0.024
WALL_T   = (OUTER_D - BORE_D) * 0.5   # = 0.0035 m
GROOVE_W = 0.001   # retaining groove width
GROOVE_D = 0.0005  # retaining groove depth


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Build an LM8UU linear bearing GLB.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=False)
    parser.add_argument("--base-color", default="0.72,0.74,0.78,1.0")
    return parser.parse_args(argv)


def parse_color(value):
    parts = [float(v.strip()) for v in value.split(",") if v.strip()]
    if len(parts) == 3:
        parts.append(1.0)
    return parts


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def compute_bounds(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    xs, ys, zs = [c.x for c in corners], [c.y for c in corners], [c.z for c in corners]
    return {
        "xmin_m": min(xs), "xmax_m": max(xs), "xlen_m": max(xs) - min(xs),
        "ymin_m": min(ys), "ymax_m": max(ys), "ylen_m": max(ys) - min(ys),
        "zmin_m": min(zs), "zmax_m": max(zs), "zlen_m": max(zs) - min(zs),
    }


def main():
    args = parse_args()
    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    color = parse_color(args.base_color)
    mat = bpy.data.materials.new(name="LM8UU Linear Bearing Steel")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.85
    bsdf.inputs["Roughness"].default_value  = 0.25

    outer_r = OUTER_D * 0.5
    bore_r  = BORE_D  * 0.5

    # --- Main housing tube ---
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=64, radius=outer_r, depth=LENGTH,
        align="WORLD", location=(0.0, 0.0, 0.0),
    )
    housing = bpy.context.active_object
    housing.name = "Housing"

    # --- Bore ---
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=64, radius=bore_r, depth=LENGTH + 0.0002,
        align="WORLD", location=(0.0, 0.0, 0.0),
    )
    bore = bpy.context.active_object
    bore.name = "Bore"

    mod = housing.modifiers.new("Bore", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver    = "EXACT"
    mod.object    = bore
    bpy.context.view_layer.objects.active = housing
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(bore, do_unlink=True)

    # --- Retaining grooves at each end (shallow annular slots on outer surface) ---
    groove_r = outer_r + 0.0001   # slightly proud so boolean cuts cleanly
    for z_pos in (LENGTH * 0.5 - GROOVE_W * 0.5, -LENGTH * 0.5 + GROOVE_W * 0.5):
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=64, radius=groove_r, depth=GROOVE_W,
            align="WORLD", location=(0.0, 0.0, z_pos),
        )
        ring_outer = bpy.context.active_object
        ring_outer.name = "GrooveOuter"

        bpy.ops.mesh.primitive_cylinder_add(
            vertices=64, radius=outer_r - GROOVE_D, depth=GROOVE_W + 0.0002,
            align="WORLD", location=(0.0, 0.0, z_pos),
        )
        ring_inner = bpy.context.active_object
        ring_inner.name = "GrooveInner"

        mod2 = housing.modifiers.new("Groove", "BOOLEAN")
        mod2.operation = "DIFFERENCE"
        mod2.solver    = "EXACT"
        mod2.object    = ring_outer
        bpy.context.view_layer.objects.active = housing
        bpy.ops.object.modifier_apply(modifier=mod2.name)
        bpy.data.objects.remove(ring_outer, do_unlink=True)
        bpy.data.objects.remove(ring_inner, do_unlink=True)

    housing.name = "D3D_LM8UU_Linear_Bearing"
    housing.data.materials.append(mat)

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb":           output_path,
        "units":                "m",
        "designation":          "LM8UU",
        "bore_diameter_mm":     BORE_D  * 1000,
        "outer_diameter_mm":    OUTER_D * 1000,
        "length_mm":            LENGTH  * 1000,
        "pivot":                "geometric_center",
        "axis":                 "Z (bore axis along Z)",
        "standard_reference":   "LM8UU linear ball bearing",
        "source_note":          "No FreeCAD source on OSE wiki — dimension-authored from standard callout",
        "bound_box":            compute_bounds(housing),
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
