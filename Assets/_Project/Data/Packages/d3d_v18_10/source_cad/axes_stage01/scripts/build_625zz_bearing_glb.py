"""
build_625zz_bearing_glb.py
--------------------------
Procedural 625ZZ radial ball bearing for the D3D axis idler.

625ZZ standard dimensions:
  Bore (inner diameter):   5 mm
  Outer diameter:         16 mm
  Width:                   5 mm

No FreeCAD source file exists on the OSE wiki for this part.
Geometry is dimension-authored from the ISO 625 standard callout.

Modelled as three concentric cylinders (inner race, outer race, cage/balls implied
by a middle ring) — sufficient visual fidelity for an XR assembly trainer.

Usage (Blender CLI):
  blender --background --python build_625zz_bearing_glb.py -- \
      --output <path/to/d3d_x_axis_625zz_bearing_approved.glb> \
      [--report <path/to/report.json>]
"""

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


# 625ZZ locked dimensions (metres)
BORE_D   = 0.005
OUTER_D  = 0.016
WIDTH    = 0.005
RACE_T   = 0.0012   # race wall thickness (approx)


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Build a 625ZZ radial bearing GLB.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=False)
    parser.add_argument("--base-color-steel", default="0.72,0.74,0.78,1.0")
    return parser.parse_args(argv)


def parse_color(value):
    parts = [float(v.strip()) for v in value.split(",") if v.strip()]
    if len(parts) == 3:
        parts.append(1.0)
    return parts


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def make_ring(inner_r, outer_r, depth, name, mat):
    """Annular ring via cylinder boolean difference."""
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=64, radius=outer_r, depth=depth,
        align="WORLD", location=(0.0, 0.0, 0.0),
    )
    outer = bpy.context.active_object
    outer.name = name + "_outer"

    bpy.ops.mesh.primitive_cylinder_add(
        vertices=64, radius=inner_r, depth=depth + 0.0002,
        align="WORLD", location=(0.0, 0.0, 0.0),
    )
    inner = bpy.context.active_object
    inner.name = name + "_bore"

    mod = outer.modifiers.new("Bore", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver    = "EXACT"
    mod.object    = inner
    bpy.context.view_layer.objects.active = outer
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(inner, do_unlink=True)

    outer.name = name
    outer.data.materials.append(mat)
    return outer


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

    color = parse_color(args.base_color_steel)
    mat = bpy.data.materials.new(name="625ZZ Bearing Steel")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.9
    bsdf.inputs["Roughness"].default_value  = 0.2

    bore_r  = BORE_D  * 0.5
    outer_r = OUTER_D * 0.5
    mid_r   = (bore_r + outer_r) * 0.5  # mid-line between races

    # Inner race
    inner_race = make_ring(bore_r, bore_r + RACE_T, WIDTH, "InnerRace", mat)

    # Outer race
    outer_race = make_ring(outer_r - RACE_T, outer_r, WIDTH, "OuterRace", mat)

    # Ball cage ring (visual stand-in for balls + cage)
    cage_inner = mid_r - RACE_T * 0.4
    cage_outer = mid_r + RACE_T * 0.4
    cage_width = WIDTH * 0.6
    ball_cage  = make_ring(cage_inner, cage_outer, cage_width, "BallCage", mat)

    # Join all three into one object
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (inner_race, outer_race, ball_cage):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = outer_race
    bpy.ops.object.join()
    bearing = bpy.context.active_object
    bearing.name = "D3D_625ZZ_Bearing"

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb":       output_path,
        "units":            "m",
        "designation":      "625ZZ",
        "bore_diameter_mm":     BORE_D  * 1000,
        "outer_diameter_mm":    OUTER_D * 1000,
        "width_mm":             WIDTH   * 1000,
        "pivot":            "geometric_center",
        "standard_reference": "ISO 625 / 6xx series radial ball bearing",
        "source_note":      "No FreeCAD source on OSE wiki — dimension-authored from standard callout",
        "bound_box":        compute_bounds(bearing),
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
