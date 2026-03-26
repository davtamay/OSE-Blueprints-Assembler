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

    parser = argparse.ArgumentParser(description="Build a dimension-authored metric L-key GLB.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=False)
    parser.add_argument("--across-flats", required=True, type=float, help="Hex key size across flats in meters.")
    parser.add_argument("--short-arm", required=True, type=float, help="Overall short-arm length in meters from bend corner to tip.")
    parser.add_argument("--long-arm", required=True, type=float, help="Overall long-arm length in meters from bend corner to tail.")
    parser.add_argument("--bend-radius", required=True, type=float, help="Centerline bend radius in meters.")
    parser.add_argument("--grip-fraction", default=0.70, type=float, help="Fraction of long-arm length used for the authored grip point.")
    parser.add_argument("--material-name", default="Metric Allen Key Steel")
    parser.add_argument("--base-color", default="0.18,0.19,0.20,1.0")
    parser.add_argument("--roughness", default=0.42, type=float)
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


def create_material(name, rgba, roughness):
    material = bpy.data.materials.new(name=name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = rgba
    principled.inputs["Metallic"].default_value = 0.0
    principled.inputs["Roughness"].default_value = roughness
    return material


def make_hex_profile(across_flats):
    profile_data = bpy.data.curves.new(name="HexProfile", type="CURVE")
    profile_data.dimensions = "2D"
    spline = profile_data.splines.new("POLY")
    spline.points.add(5)

    circumradius = across_flats / math.sqrt(3.0)
    points = []
    for i in range(6):
        angle = math.radians(60.0 * i + 30.0)
        x = circumradius * math.cos(angle)
        y = circumradius * math.sin(angle)
        points.append((x, y))

    for i, (x, y) in enumerate(points):
        spline.points[i].co = (x, y, 0.0, 1.0)
    spline.use_cyclic_u = True

    profile_obj = bpy.data.objects.new("HexProfile", profile_data)
    bpy.context.collection.objects.link(profile_obj)
    return profile_obj


def make_path(long_arm, short_arm, bend_radius, arc_segments=12):
    curve_data = bpy.data.curves.new(name="LKeyPath", type="CURVE")
    curve_data.dimensions = "3D"
    curve_data.fill_mode = "FULL"
    curve_data.resolution_u = 24

    spline = curve_data.splines.new("POLY")

    points = [(0.0, -long_arm, 0.0), (0.0, -bend_radius, 0.0)]
    center = Vector((0.0, -bend_radius, bend_radius))
    for i in range(1, arc_segments):
        angle = (-math.pi / 2.0) + (i / arc_segments) * (math.pi / 2.0)
        y = center.y + bend_radius * math.cos(angle)
        z = center.z + bend_radius * math.sin(angle)
        points.append((0.0, y, z))
    points.extend([(0.0, 0.0, bend_radius), (0.0, 0.0, short_arm)])

    spline.points.add(len(points) - 1)
    for index, point in enumerate(points):
        spline.points[index].co = (*point, 1.0)

    curve_obj = bpy.data.objects.new("LKeyPath", curve_data)
    bpy.context.collection.objects.link(curve_obj)
    return curve_obj


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


def normalize(vector):
    length = math.sqrt(sum(component * component for component in vector))
    if length <= 1e-8:
        return [0.0, 0.0, 0.0]
    return [component / length for component in vector]


def main():
    args = parse_args()
    output_path = os.path.abspath(args.output)
    report_path = os.path.abspath(args.report) if args.report else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    material = create_material(
        args.material_name,
        parse_color(args.base_color),
        args.roughness,
    )

    profile_obj = make_hex_profile(args.across_flats)
    path_obj = make_path(args.long_arm, args.short_arm, args.bend_radius)
    path_obj.data.bevel_mode = "OBJECT"
    path_obj.data.bevel_object = profile_obj
    path_obj.data.use_fill_caps = True

    bpy.context.view_layer.objects.active = path_obj
    path_obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    l_key = bpy.context.active_object
    l_key.name = "Metric_L_Key"
    l_key.data.materials.append(material)

    bpy.ops.object.select_all(action="DESELECT")
    profile_obj.select_set(True)
    bpy.context.view_layer.objects.active = profile_obj
    bpy.ops.object.delete(use_global=False)

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bounds = compute_bounds(l_key)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    grip_point = [0.0, -(args.long_arm * args.grip_fraction), 0.0]
    tip_point = [0.0, 0.0, args.short_arm]
    tip_axis = normalize([tip_point[0] - grip_point[0], tip_point[1] - grip_point[1], tip_point[2] - grip_point[2]])

    report = {
        "output_glb": output_path,
        "units": "m",
        "tool_type": "metric_l_key",
        "across_flats_m": args.across_flats,
        "short_arm_m": args.short_arm,
        "long_arm_m": args.long_arm,
        "bend_radius_m": args.bend_radius,
        "material_name": args.material_name,
        "roughness": args.roughness,
        "pivot": "bend_corner",
        "recommended_tool_pose": {
            "gripPoint": {"x": grip_point[0], "y": grip_point[1], "z": grip_point[2]},
            "tipPoint": {"x": tip_point[0], "y": tip_point[1], "z": tip_point[2]},
            "tipAxis": {"x": tip_axis[0], "y": tip_axis[1], "z": tip_axis[2]},
            "actionAxis": {"x": 0.0, "y": 0.0, "z": 1.0},
            "handedness": "either",
            "poseHint": "precision",
        },
        "bound_box": bounds,
    }

    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
