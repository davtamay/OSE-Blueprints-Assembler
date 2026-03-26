import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector


def parse_args():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1 :]
    else:
        argv = []

    parser = argparse.ArgumentParser(description="Build a composite Y-axis unit GLB from source-backed STL parts.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=False)
    parser.add_argument("--variant", choices=("left", "right"), required=True)
    parser.add_argument("--side-stl", required=True)
    parser.add_argument("--carriage-stl", required=True)
    parser.add_argument("--endstop-stl", required=False)
    parser.add_argument("--total-height", required=True, type=float, help="Overall mounted unit height in meters.")
    parser.add_argument("--rod-length", required=True, type=float, help="Vertical rod length in meters.")
    parser.add_argument("--rod-diameter", required=True, type=float, help="Rod diameter in meters.")
    parser.add_argument("--rod-center-spacing", required=True, type=float, help="Distance between rod centers in meters.")
    parser.add_argument("--plate-x-offset", default=0.0, type=float, help="Optional x offset for top/bottom blocks.")
    parser.add_argument("--endstop-position", default="-0.009,0.045,-0.026", help="Left-unit endstop local position in meters.")
    return parser.parse_args(argv)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def parse_vector3(value):
    parts = [float(item.strip()) for item in value.split(",") if item.strip()]
    if len(parts) != 3:
        raise ValueError("Expected 3 comma-separated floats.")
    return Vector(parts)


def create_material(name, rgba, metallic=0.0, roughness=0.5):
    material = bpy.data.materials.new(name=name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = rgba
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    return material


def object_bounds_local(obj):
    corners = [Vector(corner) for corner in obj.bound_box]
    xs = [corner.x for corner in corners]
    ys = [corner.y for corner in corners]
    zs = [corner.z for corner in corners]
    return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))


def center_mesh_to_origin(obj):
    minimum, maximum = object_bounds_local(obj)
    center = (minimum + maximum) * 0.5
    obj.data.transform(Matrix.Translation(-center))


def import_stl(filepath, material, name, rotate_y_90=False):
    before = {obj.name for obj in bpy.data.objects}
    absolute_path = os.path.abspath(filepath)
    if hasattr(bpy.ops.wm, "stl_import"):
        bpy.ops.wm.stl_import(filepath=absolute_path, global_scale=0.001)
    else:
        bpy.ops.import_mesh.stl(filepath=absolute_path, global_scale=0.001)
    imported = [obj for obj in bpy.data.objects if obj.name not in before and obj.type == "MESH"]
    if not imported:
        raise RuntimeError(f"Failed to import STL: {filepath}")

    for obj in imported:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = imported[0]
    if len(imported) > 1:
        bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    center_mesh_to_origin(obj)
    minimum, maximum = object_bounds_local(obj)
    extents = maximum - minimum
    if max(extents.x, extents.y, extents.z) > 2.0:
        obj.scale = (0.001, 0.001, 0.001)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        center_mesh_to_origin(obj)
    obj.data.materials.clear()
    obj.data.materials.append(material)
    if rotate_y_90:
        obj.rotation_euler = (0.0, math.radians(90.0), 0.0)
    return obj


def duplicate_object(source, name, location):
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = name
    bpy.context.collection.objects.link(obj)
    obj.location = location
    return obj


def create_rod(name, radius, length, z_offset, material):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32,
        radius=radius,
        depth=length,
        enter_editmode=False,
        align="WORLD",
        location=(0.0, 0.0, z_offset),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


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

    frame_print_material = create_material(
        "D3D Axis Printed Grey",
        (0.69, 0.73, 0.78, 1.0),
        metallic=0.0,
        roughness=0.58,
    )
    carriage_material = create_material(
        "D3D Axis Carriage Grey",
        (0.76, 0.79, 0.83, 1.0),
        metallic=0.0,
        roughness=0.52,
    )
    rod_material = create_material(
        "D3D Axis Rod Steel",
        (0.74, 0.77, 0.82, 1.0),
        metallic=0.0,
        roughness=0.46,
    )
    endstop_material = create_material(
        "D3D Endstop Holder Dark",
        (0.28, 0.30, 0.34, 1.0),
        metallic=0.0,
        roughness=0.62,
    )

    side_source = import_stl(args.side_stl, frame_print_material, "YAxisSideSource", rotate_y_90=True)
    carriage = import_stl(args.carriage_stl, carriage_material, "YAxisCarriage", rotate_y_90=True)
    carriage.location = (0.0, 0.0, 0.0)

    side_min, side_max = object_bounds_local(side_source)
    side_height = side_max.y - side_min.y
    side_center_offset = (args.total_height - side_height) * 0.5
    side_x = args.plate_x_offset

    top_block = duplicate_object(side_source, "YAxisTopBlock", (side_x, side_center_offset, 0.0))
    bottom_block = duplicate_object(side_source, "YAxisBottomBlock", (side_x, -side_center_offset, 0.0))
    bpy.data.objects.remove(side_source, do_unlink=True)

    half_spacing = args.rod_center_spacing * 0.5
    rod_radius = args.rod_diameter * 0.5
    rod_a = create_rod("YAxisRodFront", rod_radius, args.rod_length, -half_spacing, rod_material)
    rod_b = create_rod("YAxisRodRear", rod_radius, args.rod_length, half_spacing, rod_material)

    endstop_report = None
    if args.variant == "left" and args.endstop_stl:
        endstop = import_stl(args.endstop_stl, endstop_material, "YAxisEndstopHolder", rotate_y_90=True)
        endstop.location = parse_vector3(args.endstop_position)
        endstop_report = {
            "position_m": list(endstop.location),
        }

    bpy.ops.object.select_all(action="DESELECT")
    for obj in list(bpy.context.collection.objects):
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.context.view_layer.objects.active = carriage
    bpy.ops.object.join()

    unit = bpy.context.active_object
    unit.name = "D3D_YAxis_Unit_Left" if args.variant == "left" else "D3D_YAxis_Unit_Right"
    unit.location = (0.0, 0.0, 0.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
    )

    report = {
        "output_glb": output_path,
        "variant": args.variant,
        "units": "m",
        "source_side_stl": os.path.abspath(args.side_stl),
        "source_carriage_stl": os.path.abspath(args.carriage_stl),
        "source_endstop_stl": os.path.abspath(args.endstop_stl) if args.endstop_stl else None,
        "total_height_m": args.total_height,
        "rod_length_m": args.rod_length,
        "rod_diameter_m": args.rod_diameter,
        "rod_center_spacing_m": args.rod_center_spacing,
        "top_bottom_block_center_y_m": side_center_offset,
        "bound_box": compute_bounds(unit),
        "endstop": endstop_report,
    }

    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
