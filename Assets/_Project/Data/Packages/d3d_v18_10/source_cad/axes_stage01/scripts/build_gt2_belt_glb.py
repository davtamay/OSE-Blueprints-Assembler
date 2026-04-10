"""
build_gt2_belt_glb.py
---------------------
Procedural GT2 timing belt segment for the D3D axis.

GT2 belt standard dimensions:
  Pitch:           2.0 mm (distance between teeth centres)
  Tooth height:    0.75 mm
  Belt width:      6 mm  (standard D3D width)
  Backing height:  1.38 mm (total belt height minus tooth)
  Total height:    ~2.0 mm

No source file exists for this part. Geometry is dimension-authored from
the GT2 belt profile standard.

The output is a straight belt segment of a caller-specified length, centred
at the origin, lying flat (teeth pointing in -Y). For the assembly trainer
context a straight cut section is sufficient — loops and tensioned runs are
handled in the step author's placement, not in the asset geometry.

Usage (Blender CLI):
  blender --background --python build_gt2_belt_glb.py -- \
      --length-mm 200 \
      --output <path/to/d3d_x_axis_gt2_belt_approved.glb> \
      [--report <path/to/report.json>]
"""

import argparse
import json
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector


# GT2 locked dimensions (metres)
PITCH        = 0.002      # 2 mm tooth pitch
BELT_WIDTH   = 0.006      # 6 mm
BACKING_H    = 0.00138    # flat backing thickness
TOOTH_H      = 0.00075    # tooth height above backing
TOOTH_TIP_W  = 0.00085    # tooth tip width
TOOTH_BASE_W = 0.00135    # tooth base width (at backing)


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Build a GT2 belt segment GLB.")
    parser.add_argument("--length-mm", required=False, type=float, default=200.0,
                        help="Segment length in mm (default 200 mm)")
    parser.add_argument("--output",    required=True)
    parser.add_argument("--report",    required=False)
    parser.add_argument("--base-color", default="0.05,0.05,0.05,1.0",
                        help="Belt rubber colour (default near-black)")
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


def build_belt_profile(length):
    """
    Build belt geometry using bmesh.
    Belt runs along X axis, centred at origin.
    Backing occupies Y=[0, BACKING_H], teeth protrude into Y=[-TOOTH_H, 0].
    Width along Z = [-BELT_WIDTH/2, +BELT_WIDTH/2].
    """
    bm = bmesh.new()

    half_len = length * 0.5
    half_w   = BELT_WIDTH * 0.5
    n_teeth  = max(1, int(length / PITCH))

    # ── Backing slab ──────────────────────────────────────────────────────────
    verts_back = [
        bm.verts.new((-half_len,  0.0,       -half_w)),
        bm.verts.new(( half_len,  0.0,       -half_w)),
        bm.verts.new(( half_len,  BACKING_H, -half_w)),
        bm.verts.new((-half_len,  BACKING_H, -half_w)),
        bm.verts.new((-half_len,  0.0,        half_w)),
        bm.verts.new(( half_len,  0.0,        half_w)),
        bm.verts.new(( half_len,  BACKING_H,  half_w)),
        bm.verts.new((-half_len,  BACKING_H,  half_w)),
    ]
    # 6 faces of the backing box
    bm.faces.new([verts_back[0], verts_back[1], verts_back[2], verts_back[3]])  # front -z
    bm.faces.new([verts_back[4], verts_back[7], verts_back[6], verts_back[5]])  # back  +z
    bm.faces.new([verts_back[0], verts_back[4], verts_back[5], verts_back[1]])  # bottom
    bm.faces.new([verts_back[3], verts_back[2], verts_back[6], verts_back[7]])  # top
    bm.faces.new([verts_back[0], verts_back[3], verts_back[7], verts_back[4]])  # left
    bm.faces.new([verts_back[1], verts_back[5], verts_back[6], verts_back[2]])  # right

    # ── Teeth ─────────────────────────────────────────────────────────────────
    half_tb = TOOTH_BASE_W * 0.5
    half_tt = TOOTH_TIP_W  * 0.5

    for i in range(n_teeth):
        # Centre each tooth at its pitch position, starting half a pitch from one end
        cx = -half_len + PITCH * 0.5 + i * PITCH

        # Trapezoid cross-section: base at Y=0 (backing bottom), tip at Y=-TOOTH_H
        # Four verts per side (front and back face along Z), forming a prism
        vt = [
            bm.verts.new((cx - half_tb, 0.0,      -half_w)),
            bm.verts.new((cx + half_tb, 0.0,      -half_w)),
            bm.verts.new((cx + half_tt, -TOOTH_H, -half_w)),
            bm.verts.new((cx - half_tt, -TOOTH_H, -half_w)),
            bm.verts.new((cx - half_tb, 0.0,       half_w)),
            bm.verts.new((cx + half_tb, 0.0,       half_w)),
            bm.verts.new((cx + half_tt, -TOOTH_H,  half_w)),
            bm.verts.new((cx - half_tt, -TOOTH_H,  half_w)),
        ]
        bm.faces.new([vt[0], vt[3], vt[2], vt[1]])  # front face (-z)
        bm.faces.new([vt[4], vt[5], vt[6], vt[7]])  # back face  (+z)
        bm.faces.new([vt[0], vt[1], vt[5], vt[4]])  # base
        bm.faces.new([vt[2], vt[3], vt[7], vt[6]])  # tip
        bm.faces.new([vt[0], vt[4], vt[7], vt[3]])  # left side
        bm.faces.new([vt[1], vt[2], vt[6], vt[5]])  # right side

    bm.normal_update()

    mesh = bpy.data.meshes.new("GT2BeltMesh")
    bm.to_mesh(mesh)
    bm.free()
    mesh.validate()

    obj = bpy.data.objects.new("D3D_GT2_Belt", mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    return obj


def main():
    args = parse_args()
    length     = args.length_mm * 0.001
    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    obj = build_belt_profile(length)

    # Material
    color = parse_color(args.base_color)
    mat = bpy.data.materials.new(name="GT2 Belt Rubber")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.0
    bsdf.inputs["Roughness"].default_value  = 0.9
    obj.data.materials.append(mat)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb":        output_path,
        "units":             "m",
        "designation":       "GT2 timing belt",
        "pitch_mm":          PITCH       * 1000,
        "belt_width_mm":     BELT_WIDTH  * 1000,
        "backing_height_mm": BACKING_H   * 1000,
        "tooth_height_mm":   TOOTH_H     * 1000,
        "segment_length_mm": args.length_mm,
        "n_teeth":           max(1, int(length / PITCH)),
        "pivot":             "geometric_center_of_backing",
        "teeth_direction":   "-Y",
        "source_note":       "No source file on OSE wiki — dimension-authored from GT2 belt profile standard",
        "bound_box":         compute_bounds(obj),
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
