"""
build_shcs_bolt_glb.py
----------------------
Parametric ISO 4762 / DIN 912 socket-head cap screw (SHCS) generator.
Generates ANY metric SHCS size — reuses the geometry logic from build_m6x30_bolt_glb.py
but is driven entirely by --size and --length so no args need to be memorised.

Nominal dimensions sourced from ISO 4762 standard tables.

Supported --size values: M3, M4, M5, M6, M8, M10
Supported --length values: any positive float in mm

Usage (Blender CLI):
  blender --background --python build_shcs_bolt_glb.py -- \
      --size M3 --length 25 \
      --output <path/to/d3d_x_axis_m3x25_shcs_approved.glb> \
      [--report <path/to/report.json>]

  blender --background --python build_shcs_bolt_glb.py -- \
      --size M6 --length 18 \
      --output <path/to/d3d_x_axis_m6x18_shcs_approved.glb> \
      [--report <path/to/report.json>]
"""

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


# ISO 4762 nominal dimensions (all in mm)
ISO_4762 = {
    # size: (shaft_d, head_d, head_h, socket_af, socket_depth)
    "M3":  (3.0,  5.5,  3.0, 2.5, 1.5),
    "M4":  (4.0,  7.0,  4.0, 3.0, 2.0),
    "M5":  (5.0,  8.5,  5.0, 4.0, 2.5),
    "M6":  (6.0, 10.0,  6.0, 5.0, 3.0),
    "M8":  (8.0, 13.0,  8.0, 6.0, 4.0),
    "M10": (10.0, 16.0, 10.0, 8.0, 5.0),
}


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Build a parametric ISO 4762 SHCS GLB.")
    parser.add_argument("--size",   required=True,  help="Metric size: M3, M4, M5, M6, M8, M10")
    parser.add_argument("--length", required=True,  type=float, help="Nominal shaft length in mm")
    parser.add_argument("--output", required=True,  help="Output .glb path")
    parser.add_argument("--report", required=False, help="Optional JSON report path")
    parser.add_argument("--material-name", default=None)
    parser.add_argument("--base-color",    default="0.72,0.74,0.78,1.0")
    parser.add_argument("--chamfer",       default=0.0004, type=float,
                        help="Underhead chamfer radius in metres (default 0.4 mm)")
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


def make_cylinder(radius, depth, loc_x, name):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32, radius=radius, depth=depth,
        align="WORLD", location=(loc_x, 0.0, 0.0),
        rotation=(0.0, math.radians(90.0), 0.0),
    )
    obj = bpy.context.active_object
    obj.name = name
    return obj


def make_hex_prism(across_flats, depth, loc_x, name):
    r = across_flats / math.sqrt(3.0)
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=6, radius=r, depth=depth,
        align="WORLD", location=(loc_x, 0.0, 0.0),
        rotation=(0.0, math.radians(90.0), math.radians(30.0)),
    )
    obj = bpy.context.active_object
    obj.name = name
    return obj


def apply_bevel(obj, width):
    if width <= 0.0:
        return
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.bevel(offset=width, segments=1, affect="EDGES")
    bpy.ops.object.mode_set(mode="OBJECT")


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
    if size not in ISO_4762:
        raise ValueError(f"Unknown size '{size}'. Supported: {list(ISO_4762.keys())}")

    shaft_d_mm, head_d_mm, head_h_mm, socket_af_mm, socket_depth_mm = ISO_4762[size]
    shaft_len_mm = args.length

    # Convert to metres
    shaft_d   = shaft_d_mm   * 0.001
    head_d    = head_d_mm    * 0.001
    head_h    = head_h_mm    * 0.001
    socket_af = socket_af_mm * 0.001
    socket_dp = socket_depth_mm * 0.001
    shaft_len = shaft_len_mm * 0.001

    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    color = parse_color(args.base_color)
    mat_name = args.material_name or f"{size}x{int(shaft_len_mm)} SHCS Steel"
    mat = bpy.data.materials.new(name=mat_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.0
    bsdf.inputs["Roughness"].default_value  = 0.48

    head_cx  = 0.0
    shaft_cx = head_h * 0.5 + shaft_len * 0.5

    head  = make_cylinder(head_d * 0.5,  head_h,  head_cx,  "BoltHead")
    head.data.materials.append(mat)
    shaft = make_cylinder(shaft_d * 0.5, shaft_len, shaft_cx, "BoltShaft")
    shaft.data.materials.append(mat)

    head.select_set(True)
    shaft.select_set(True)
    bpy.context.view_layer.objects.active = head
    bpy.ops.object.join()
    bolt = bpy.context.active_object
    bolt.name = f"D3D_{size}x{int(shaft_len_mm)}_SHCS"

    apply_bevel(bolt, args.chamfer)

    socket_cx = -0.5 * head_h + 0.5 * socket_dp
    cutter = make_hex_prism(socket_af, socket_dp + 0.0002, socket_cx, "HexCutter")

    mod = bolt.modifiers.new("HexSocket", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver    = "EXACT"
    mod.object    = cutter
    bpy.context.view_layer.objects.active = bolt
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb": output_path,
        "units": "m",
        "size": size,
        "shaft_length_mm": shaft_len_mm,
        "shaft_diameter_mm": shaft_d_mm,
        "head_diameter_mm": head_d_mm,
        "head_length_mm": head_h_mm,
        "socket_af_mm": socket_af_mm,
        "socket_depth_mm": socket_depth_mm,
        "material_name": mat_name,
        "base_color": color,
        "pivot": "head_center",
        "head_style": "socket_head_cap_screw",
        "standard_reference": "ISO 4762 / DIN 912",
        "bound_box": compute_bounds(bolt),
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
