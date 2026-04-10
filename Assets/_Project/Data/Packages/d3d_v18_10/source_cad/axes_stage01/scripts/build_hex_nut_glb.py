"""
build_hex_nut_glb.py
--------------------
Parametric ISO 4032 hex nut generator (any metric size).

Nominal dimensions from ISO 4032 standard tables.
Supported --size values: M3, M4, M5, M6, M8, M10

Usage (Blender CLI):
  blender --background --python build_hex_nut_glb.py -- \
      --size M3 \
      --output <path/to/d3d_x_axis_m3_nut_approved.glb> \
      [--report <path/to/report.json>]

  blender --background --python build_hex_nut_glb.py -- \
      --size M6 \
      --output <path/to/d3d_x_axis_m6_nut_approved.glb> \
      [--report <path/to/report.json>]
"""

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


# ISO 4032 nominal dimensions (all in mm)
# size: (bore_d, across_flats, thickness)
ISO_4032 = {
    "M3":  (3.0,  5.5,  2.4),
    "M4":  (4.0,  7.0,  3.2),
    "M5":  (5.0,  8.0,  4.0),
    "M6":  (6.0, 10.0,  5.0),
    "M8":  (8.0, 13.0,  6.5),
    "M10": (10.0, 17.0,  8.0),
}


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Build a parametric ISO 4032 hex nut GLB.")
    parser.add_argument("--size",   required=True,  help="Metric size: M3, M4, M5, M6, M8, M10")
    parser.add_argument("--output", required=True,  help="Output .glb path")
    parser.add_argument("--report", required=False, help="Optional JSON report path")
    parser.add_argument("--material-name", default=None)
    parser.add_argument("--base-color",    default="0.72,0.74,0.78,1.0")
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
    xs, ys, zs = [c.x for c in corners], [c.y for c in corners], [c.z for c in corners]
    return {
        "xmin_m": min(xs), "xmax_m": max(xs), "xlen_m": max(xs) - min(xs),
        "ymin_m": min(ys), "ymax_m": max(ys), "ylen_m": max(ys) - min(ys),
        "zmin_m": min(zs), "zmax_m": max(zs), "zlen_m": max(zs) - min(zs),
    }


def main():
    args = parse_args()
    size = args.size.upper()
    if size not in ISO_4032:
        raise ValueError(f"Unknown size '{size}'. Supported: {list(ISO_4032.keys())}")

    bore_d_mm, af_mm, thick_mm = ISO_4032[size]
    bore_r  = bore_d_mm * 0.0005   # half, in metres
    af      = af_mm     * 0.001
    thick   = thick_mm  * 0.001

    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    # --- Hex body (6-sided prism) ---
    hex_r = af / math.sqrt(3.0)   # circumradius from across-flats
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=6, radius=hex_r, depth=thick,
        align="WORLD", location=(0.0, 0.0, 0.0),
        rotation=(0.0, 0.0, math.radians(30.0)),
    )
    hex_body = bpy.context.active_object
    hex_body.name = "HexBody"

    # --- Bore (cylindrical through-hole) ---
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32, radius=bore_r, depth=thick + 0.0002,
        align="WORLD", location=(0.0, 0.0, 0.0),
    )
    bore = bpy.context.active_object
    bore.name = "Bore"

    # Boolean subtract bore from hex body
    mod = hex_body.modifiers.new("Bore", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver    = "EXACT"
    mod.object    = bore
    bpy.context.view_layer.objects.active = hex_body
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(bore, do_unlink=True)

    nut = hex_body
    nut.name = f"D3D_{size}_Nut"

    # Material
    color    = parse_color(args.base_color)
    mat_name = args.material_name or f"{size} Hex Nut Steel"
    mat = bpy.data.materials.new(name=mat_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.0
    bsdf.inputs["Roughness"].default_value  = 0.5
    nut.data.materials.append(mat)

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb":      output_path,
        "units":           "m",
        "size":            size,
        "bore_diameter_mm":    bore_d_mm,
        "across_flats_mm":     af_mm,
        "thickness_mm":        thick_mm,
        "material_name":   mat_name,
        "base_color":      color,
        "pivot":           "geometric_center",
        "standard_reference": "ISO 4032",
        "bound_box":       compute_bounds(nut),
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
