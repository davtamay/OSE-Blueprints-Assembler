"""
build_nema17_glb.py
-------------------
Export the NEMA 17 stepper motor from Nema17.fcstd (OSE wiki source) to GLB.

Source:
  raw/Nema17.fcstd
  Downloaded from: https://wiki.opensourceecology.org/wiki/Special:Redirect/file/Nema17.fcstd

This script runs under FreeCAD's Python environment (not Blender).
It exports the full shape to STL, then a second pass with Blender converts
to GLB with correct material and orientation.

Two-step approach because FreeCAD's GLTF exporter is unreliable; the
STL→GLB pipeline (same as other parts in this package) is well-tested.

── Step 1: FreeCAD export to STL ────────────────────────────────────────────
  FreeCAD --console --run build_nema17_glb.py -- \
      --fcstd  raw/Nema17.fcstd \
      --output exported/stl/Nema17.stl \
      --mode   freecad

── Step 2: Blender STL → GLB ─────────────────────────────────────────────────
  blender --background --python build_nema17_glb.py -- \
      --stl    exported/stl/Nema17.stl \
      --output <path/to/d3d_nema17_motor_approved.glb> \
      --mode   blender \
      [--report <path/to/report.json>]
"""

import argparse
import json
import os
import sys


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser(description="Export Nema17.fcstd to GLB (two-step).")
    parser.add_argument("--mode",   required=True, choices=("freecad", "blender"),
                        help="'freecad' to export STL; 'blender' to convert STL to GLB")
    parser.add_argument("--fcstd",  required=False, help="[freecad mode] path to Nema17.fcstd")
    parser.add_argument("--stl",    required=False, help="[blender mode] path to Nema17.stl")
    parser.add_argument("--output", required=True,  help="Output file path (.stl or .glb)")
    parser.add_argument("--report", required=False, help="[blender mode] optional JSON report")
    parser.add_argument("--base-color", default="0.10,0.10,0.10,1.0",
                        help="Motor body colour (default near-black)")
    return parser.parse_args(argv)


# ── FreeCAD mode ──────────────────────────────────────────────────────────────

def run_freecad(args):
    import FreeCAD
    import Part
    import Mesh

    fcstd_path  = os.path.abspath(args.fcstd)
    output_path = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    doc = FreeCAD.openDocument(fcstd_path)

    # Collect all solid shapes from visible objects
    shapes = []
    for obj in doc.Objects:
        if hasattr(obj, "Shape") and obj.Shape and not obj.Shape.isNull():
            shapes.append(obj.Shape)

    if not shapes:
        raise RuntimeError("No solid shapes found in Nema17.fcstd")

    if len(shapes) == 1:
        compound = shapes[0]
    else:
        compound = Part.makeCompound(shapes)

    # Export to STL (millimetre units — FreeCAD default)
    mesh = Mesh.Mesh()
    mesh.addMesh(MeshPart.meshFromShape(
        Shape=compound, LinearDeflection=0.05, AngularDeflection=0.1
    ))
    mesh.write(output_path)

    print(json.dumps({"output_stl": output_path, "n_shapes": len(shapes)}))


# ── Blender mode ──────────────────────────────────────────────────────────────

def run_blender(args):
    import math
    import bpy
    from mathutils import Vector

    def parse_color(value):
        parts = [float(v.strip()) for v in value.split(",") if v.strip()]
        if len(parts) == 3:
            parts.append(1.0)
        return parts

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

    stl_path    = os.path.abspath(args.stl)
    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    # Clear scene
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    # Import STL (FreeCAD exports in mm)
    bpy.ops.wm.stl_import(filepath=stl_path)
    obj = bpy.context.selected_objects[0]
    obj.name = "D3D_NEMA17_Motor"

    # Scale mm → metres
    obj.scale = (0.001, 0.001, 0.001)
    bpy.ops.object.transform_apply(scale=True)

    # Orient: stand motor upright — shaft pointing +Z, body centred at origin
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    obj.location = (0.0, 0.0, 0.0)
    bpy.ops.object.transform_apply(location=True)

    # Material — dark body like real NEMA 17
    color = parse_color(args.base_color)
    mat = bpy.data.materials.new(name="NEMA17 Motor Body")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value   = 0.6
    bsdf.inputs["Roughness"].default_value  = 0.5
    obj.data.materials.clear()
    obj.data.materials.append(mat)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb":   output_path,
        "source_stl":   stl_path,
        "units":        "m",
        "designation":  "NEMA 17 stepper motor",
        "source_fcstd": "raw/Nema17.fcstd",
        "source_url":   "https://wiki.opensourceecology.org/wiki/Special:Redirect/file/Nema17.fcstd",
        "base_color":   color,
        "pivot":        "geometric_center",
        "bound_box":    compute_bounds(obj),
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    args = parse_args()
    if args.mode == "freecad":
        run_freecad(args)
    else:
        run_blender(args)


if __name__ == "__main__":
    main()
