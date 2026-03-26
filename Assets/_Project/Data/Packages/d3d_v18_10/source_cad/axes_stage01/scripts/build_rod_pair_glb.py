import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1 :]
    else:
        argv = []

    parser = argparse.ArgumentParser(description="Build a simple two-rod GLB from source-backed dimensions.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=False)
    parser.add_argument("--length", required=True, type=float, help="Rod length in meters.")
    parser.add_argument("--diameter", required=True, type=float, help="Rod diameter in meters.")
    parser.add_argument("--center-spacing", required=True, type=float, help="Distance between rod centers in meters.")
    parser.add_argument("--material-name", default="Rod Pair Steel")
    parser.add_argument("--base-color", default="0.74,0.77,0.82,1.0")
    return parser.parse_args(argv)


def parse_color(value):
    parts = [float(item.strip()) for item in value.split(",") if item.strip()]
    if len(parts) == 3:
        parts.append(1.0)
    if len(parts) != 4:
        raise ValueError("base color must have 3 or 4 comma-separated floats")
    return parts


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def create_material(name, rgba):
    material = bpy.data.materials.new(name=name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = rgba
    principled.inputs["Metallic"].default_value = 0.0
    principled.inputs["Roughness"].default_value = 0.55
    return material


def create_rod(radius, length, z_offset, material):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32,
        radius=radius,
        depth=length,
        enter_editmode=False,
        align="WORLD",
        location=(0.0, 0.0, z_offset),
        rotation=(0.0, math.radians(90.0), 0.0),
    )
    rod = bpy.context.active_object
    rod.data.materials.append(material)
    return rod


def compute_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    xs = [corner.x for corner in corners]
    ys = [corner.y for corner in corners]
    zs = [corner.z for corner in corners]
    return {
        "xmin_m": min(xs),
        "xmax_m": max(xs),
        "ymin_m": min(ys),
        "ymax_m": max(ys),
        "zmin_m": min(zs),
        "zmax_m": max(zs),
        "xlen_m": max(xs) - min(xs),
        "ylen_m": max(ys) - min(ys),
        "zlen_m": max(zs) - min(zs),
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
    material = create_material(args.material_name, color)

    radius = args.diameter * 0.5
    half_spacing = args.center_spacing * 0.5

    rods = [
        create_rod(radius, args.length, -half_spacing, material),
        create_rod(radius, args.length, half_spacing, material),
    ]

    for rod in rods:
        rod.select_set(True)
    bpy.context.view_layer.objects.active = rods[0]
    bpy.ops.object.join()

    pair = bpy.context.active_object
    pair.name = "D3D_XAxis_Rod_Pair"
    pair.location = (0.0, 0.0, 0.0)
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
        "length_m": args.length,
        "diameter_m": args.diameter,
        "center_spacing_m": args.center_spacing,
        "material_name": args.material_name,
        "base_color": color,
        "bound_box": compute_bounds(pair),
    }

    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
