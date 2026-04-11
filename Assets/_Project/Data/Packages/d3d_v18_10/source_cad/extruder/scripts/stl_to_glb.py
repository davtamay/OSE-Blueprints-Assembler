import json
import os
import sys

import bpy
from mathutils import Vector


def parse_args(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    args = {
        "--input": os.environ.get("BLENDER_INPUT"),
        "--output": os.environ.get("BLENDER_OUTPUT"),
        "--report": os.environ.get("BLENDER_REPORT"),
        "--material-name": os.environ.get("BLENDER_MATERIAL_NAME"),
        "--base-color": os.environ.get("BLENDER_BASE_COLOR"),
        "--metallic": os.environ.get("BLENDER_METALLIC"),
        "--roughness": os.environ.get("BLENDER_ROUGHNESS"),
        "--center-mode": os.environ.get("BLENDER_CENTER_MODE"),
        "--flat-shading": os.environ.get("BLENDER_FLAT_SHADING"),
    }
    index = 0
    while index < len(argv):
        token = argv[index]
        if token in args and index + 1 < len(argv):
            args[token] = argv[index + 1]
            index += 2
            continue
        index += 1
    if not args["--input"] or not args["--output"]:
        raise SystemExit(
            "Usage: blender.exe -b -P stl_to_glb.py -- --input <file.stl> --output <file.glb> [--report <file.json>] or set BLENDER_INPUT/BLENDER_OUTPUT/BLENDER_REPORT"
        )
    return args


def parse_float(value, default):
    if value is None or value == "":
        return default
    return float(value)


def parse_color(value):
    if not value:
        return (0.42, 0.42, 0.42, 1.0)
    parts = [float(part.strip()) for part in value.split(",")]
    if len(parts) == 3:
        return (parts[0], parts[1], parts[2], 1.0)
    if len(parts) == 4:
        return tuple(parts)
    raise ValueError("BLENDER_BASE_COLOR must have 3 or 4 comma-separated floats.")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablock_collection in (
        bpy.data.meshes,
        bpy.data.materials,
        bpy.data.images,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for datablock in list(datablock_collection):
            if datablock.users == 0:
                datablock_collection.remove(datablock)


def world_bounds(objects):
    points = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            points.append(obj.matrix_world @ Vector(corner))
    if not points:
        return None
    xs = [point.x for point in points]
    ys = [point.y for point in points]
    zs = [point.z for point in points]
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


def recenter_objects(objects, mode):
    if not mode:
        return
    bounds = world_bounds(objects)
    if bounds is None:
        return
    if mode == "center":
        offset = Vector(
            (
                -0.5 * (bounds["xmin_m"] + bounds["xmax_m"]),
                -0.5 * (bounds["ymin_m"] + bounds["ymax_m"]),
                -0.5 * (bounds["zmin_m"] + bounds["zmax_m"]),
            )
        )
    elif mode == "base_center":
        offset = Vector(
            (
                -0.5 * (bounds["xmin_m"] + bounds["xmax_m"]),
                -0.5 * (bounds["ymin_m"] + bounds["ymax_m"]),
                -bounds["zmin_m"],
            )
        )
    else:
        raise ValueError("BLENDER_CENTER_MODE must be 'center' or 'base_center'.")
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.location += offset


def build_default_material(name, base_color, metallic, roughness):
    material = bpy.data.materials.new(name=name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = base_color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    return material


def apply_flat_shading(objects):
    """Force hard-edge flat shading in the exported glTF.

    STL has no smoothing info, but glTF stores one normal per vertex. When
    adjacent flat-shaded faces share a vertex, the exporter averages their
    normals, which looks like a fake smoothing seam. Splitting every edge
    duplicates vertices per face so each face keeps its own normal.
    """
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        for poly in obj.data.polygons:
            poly.use_smooth = False
        if obj.data.has_custom_normals:
            obj.data.free_normals_split()
        modifier = obj.modifiers.new(name="EdgeSplitAll", type="EDGE_SPLIT")
        modifier.split_angle = 0.0
        modifier.use_edge_angle = True
        modifier.use_edge_sharp = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)


def ensure_materials(objects, material_name, base_color, metallic, roughness):
    shared_material = build_default_material(material_name, base_color, metallic, roughness)
    for obj in objects:
        if obj.type != "MESH":
            continue
        if len(obj.data.materials) == 0:
            obj.data.materials.append(shared_material)
        else:
            for index, existing in enumerate(obj.data.materials):
                if existing is None:
                    obj.data.materials[index] = shared_material


def main():
    arguments = parse_args(sys.argv)
    input_path = os.path.abspath(arguments["--input"])
    output_path = os.path.abspath(arguments["--output"])
    report_path = (
        os.path.abspath(arguments["--report"]) if arguments["--report"] else None
    )
    material_name = arguments["--material-name"] or "OSE Auto Material"
    base_color = parse_color(arguments["--base-color"])
    metallic = parse_float(arguments["--metallic"], 0.0)
    roughness = parse_float(arguments["--roughness"], 0.55)
    center_mode = arguments["--center-mode"]
    flat_shading_raw = (arguments["--flat-shading"] or "").strip().lower()
    flat_shading = flat_shading_raw in ("1", "true", "yes", "on")

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    clear_scene()

    bpy.ops.wm.stl_import(filepath=input_path)
    imported_objects = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
    if not imported_objects:
        raise RuntimeError("No mesh objects were imported from STL.")

    bpy.ops.object.select_all(action="DESELECT")
    for obj in imported_objects:
        obj.select_set(True)
        obj.scale = (0.001, 0.001, 0.001)
    bpy.context.view_layer.objects.active = imported_objects[0]
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    recenter_objects(imported_objects, center_mode)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    if flat_shading:
        apply_flat_shading(imported_objects)

    ensure_materials(imported_objects, material_name, base_color, metallic, roughness)

    bounds = world_bounds(imported_objects)

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        use_selection=True,
    )

    report = {
        "input_stl": input_path,
        "output_glb": output_path,
        "units": "m",
        "object_count": len(imported_objects),
        "material_name": material_name,
        "base_color": list(base_color),
        "metallic": metallic,
        "roughness": roughness,
        "center_mode": center_mode,
        "flat_shading": flat_shading,
        "bound_box": bounds,
    }
    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
