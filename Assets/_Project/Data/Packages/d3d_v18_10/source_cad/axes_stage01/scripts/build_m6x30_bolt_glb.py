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

    parser = argparse.ArgumentParser(description="Build a dimension-authored ISO 4762 / DIN 912 M6x30 socket-head cap screw GLB.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=False)
    parser.add_argument("--shaft-length", required=True, type=float, help="Nominal threaded shaft length in meters.")
    parser.add_argument("--shaft-diameter", required=True, type=float, help="Nominal shaft diameter in meters.")
    parser.add_argument("--head-length", required=True, type=float, help="Socket-head cap screw head length in meters.")
    parser.add_argument("--head-diameter", required=True, type=float, help="Socket-head cap screw head diameter in meters.")
    parser.add_argument("--socket-size", required=True, type=float, help="Internal hex socket size across flats in meters.")
    parser.add_argument("--socket-depth", required=True, type=float, help="Socket depth in meters.")
    parser.add_argument("--underhead-chamfer", default=0.0006, type=float, help="Optional underhead and socket-mouth chamfer radius in meters.")
    parser.add_argument("--material-name", default="Axis Mount SHCS Steel")
    parser.add_argument("--base-color", default="0.72,0.74,0.78,1.0")
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
    principled.inputs["Roughness"].default_value = 0.48
    return material


def create_cylinder(radius, depth, location_x, material, name):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32,
        radius=radius,
        depth=depth,
        enter_editmode=False,
        align="WORLD",
        location=(location_x, 0.0, 0.0),
        rotation=(0.0, math.radians(90.0), 0.0),
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def create_hex_prism(across_flats, depth, location_x, name):
    circumradius = across_flats / math.sqrt(3.0)
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=6,
        radius=circumradius,
        depth=depth,
        enter_editmode=False,
        align="WORLD",
        location=(location_x, 0.0, 0.0),
        rotation=(0.0, math.radians(90.0), math.radians(30.0)),
    )
    obj = bpy.context.active_object
    obj.name = name
    return obj


def bevel_edges(obj, width):
    if width <= 0.0:
        return
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.bevel(offset=width, segments=1, affect="EDGES")
    bpy.ops.object.mode_set(mode="OBJECT")


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

    head_radius = args.head_diameter * 0.5
    shaft_radius = args.shaft_diameter * 0.5
    head_center_x = 0.0
    shaft_center_x = (args.head_length * 0.5) + (args.shaft_length * 0.5)

    head = create_cylinder(head_radius, args.head_length, head_center_x, material, "BoltHead")
    shaft = create_cylinder(shaft_radius, args.shaft_length, shaft_center_x, material, "BoltShaft")

    head.select_set(True)
    shaft.select_set(True)
    bpy.context.view_layer.objects.active = head
    bpy.ops.object.join()

    bolt = bpy.context.active_object
    bolt.name = "D3D_Axis_M6x30_SHCS"

    bevel_edges(bolt, args.underhead_chamfer)

    socket_center_x = -0.5 * args.head_length + 0.5 * args.socket_depth
    socket = create_hex_prism(
        args.socket_size,
        args.socket_depth + 0.0002,
        socket_center_x,
        "HexSocketCutter",
    )

    bool_mod = bolt.modifiers.new(name="HexSocket", type="BOOLEAN")
    bool_mod.operation = "DIFFERENCE"
    bool_mod.solver = "EXACT"
    bool_mod.object = socket
    bpy.context.view_layer.objects.active = bolt
    bpy.ops.object.modifier_apply(modifier=bool_mod.name)

    bpy.data.objects.remove(socket, do_unlink=True)

    # Keep a functional fastener pivot at the head center rather than recentering the mesh.
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
        "shaft_length_m": args.shaft_length,
        "shaft_diameter_m": args.shaft_diameter,
        "head_length_m": args.head_length,
        "head_diameter_m": args.head_diameter,
        "socket_size_m": args.socket_size,
        "socket_depth_m": args.socket_depth,
        "material_name": args.material_name,
        "base_color": color,
        "pivot": "head_center",
        "head_style": "socket_head_cap_screw",
        "standard_reference": "ISO 4762 / DIN 912",
        "recommended_socket_center_local_m": {
            "x": -0.5 * args.head_length + 0.5 * args.socket_depth,
            "y": 0.0,
            "z": 0.0,
        },
        "recommended_insertion_axis_local": {
            "x": -1.0,
            "y": 0.0,
            "z": 0.0,
        },
        "bound_box": compute_bounds(bolt),
    }

    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
