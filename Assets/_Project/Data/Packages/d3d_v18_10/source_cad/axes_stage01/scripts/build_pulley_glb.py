"""
build_pulley_glb.py
-------------------
Convert the source-backed pulley_gt2_19teeth7mm.stl into a runtime GLB.

Source:
  raw/Universal8mmaxis/axis_8mm STL + FCSTD/pulley_gt2_19teeth7mm.stl
  (OpenSCAD-generated from the Universal8mmaxis archive)

Usage (Blender CLI):
  blender --background --python build_pulley_glb.py -- \
      --stl  <path/to/pulley_gt2_19teeth7mm.stl> \
      --output <path/to/d3d_x_axis_pulley_approved.glb> \
      [--report <path/to/report.json>]
"""

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Convert GT2 19-tooth pulley STL to GLB.")
    parser.add_argument("--stl",    required=True,  help="Path to pulley_gt2_19teeth7mm.stl")
    parser.add_argument("--output", required=True,  help="Output .glb path")
    parser.add_argument("--report", required=False, help="Optional JSON report path")
    parser.add_argument("--material-name", default="GT2 Pulley Aluminium")
    parser.add_argument("--base-color",    default="0.80,0.82,0.85,1.0")
    return parser.parse_args(argv)


def parse_color(value):
    parts = [float(v.strip()) for v in value.split(",") if v.strip()]
    if len(parts) == 3:
        parts.append(1.0)
    if len(parts) != 4:
        raise ValueError("base-color must be 3 or 4 comma-separated floats")
    return parts


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def compute_bounds(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    xs = [c.x for c in corners]
    ys = [c.y for c in corners]
    zs = [c.z for c in corners]
    return {
        "xmin_m": min(xs), "xmax_m": max(xs), "xlen_m": max(xs) - min(xs),
        "ymin_m": min(ys), "ymax_m": max(ys), "ylen_m": max(ys) - min(ys),
        "zmin_m": min(zs), "zmax_m": max(zs), "zlen_m": max(zs) - min(zs),
    }


def main():
    args = parse_args()
    stl_path    = os.path.abspath(args.stl)
    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    # Import STL (already in metres from the OpenSCAD source)
    bpy.ops.wm.stl_import(filepath=stl_path)
    obj = bpy.context.selected_objects[0]
    obj.name = "D3D_GT2_Pulley_19T"

    # The OpenSCAD source generates the pulley in mm — scale to metres
    obj.scale = (0.001, 0.001, 0.001)
    bpy.ops.object.transform_apply(scale=True)

    # Centre the pivot at the bore axis (geometric centre in XY, bottom face in Z)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    zs = [c.z for c in corners]
    obj.location.z -= min(zs)          # rest on Z=0
    bpy.ops.object.transform_apply(location=True)

    # Material
    color = parse_color(args.base_color)
    mat = bpy.data.materials.new(name=args.material_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.8
    bsdf.inputs["Roughness"].default_value  = 0.3
    obj.data.materials.clear()
    obj.data.materials.append(mat)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    bounds = compute_bounds(obj)
    report = {
        "output_glb": output_path,
        "source_stl": stl_path,
        "units": "m",
        "teeth": 19,
        "belt_pitch_mm": 2.0,
        "bore_diameter_mm": 5.0,
        "material_name": args.material_name,
        "base_color": color,
        "pivot": "bore_axis_bottom_face",
        "bound_box": bounds,
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
