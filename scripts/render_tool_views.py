"""Blender headless script: render 4 orthographic views of a GLB tool model.

Usage (called by analyze_tool_pose.py, not directly by users):
    blender -b -P render_tool_views.py -- \
        --input  tool.glb \
        --output-dir ./renders/ \
        --markers markers.json   (optional: PCA candidate points to overlay)

Produces: front.png, right.png, top.png, perspective.png in output-dir.
Markers JSON format: {"gripPoint":[x,y,z], "tipPoint":[x,y,z], "tipAxis":[x,y,z], "actionAxis":[x,y,z]}
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector, Matrix


def parse_args(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    args = {"--input": None, "--output-dir": None, "--markers": None, "--resolution": "1024"}
    i = 0
    while i < len(argv):
        if argv[i] in args and i + 1 < len(argv):
            args[argv[i]] = argv[i + 1]
            i += 2
        else:
            i += 1
    if not args["--input"] or not args["--output-dir"]:
        raise SystemExit(
            "Usage: blender -b -P render_tool_views.py -- "
            "--input <tool.glb> --output-dir <dir> [--markers <markers.json>] [--resolution 1024]"
        )
    return args


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for collection in bpy.data.collections:
        bpy.data.collections.remove(collection)


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.context.selected_objects]
    return imported


def get_scene_bounds(objects):
    """Compute world-space AABB of all mesh objects."""
    mins = Vector((float("inf"),) * 3)
    maxs = Vector((float("-inf"),) * 3)
    for obj in objects:
        if obj.type != "MESH":
            continue
        bbox_corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        for c in bbox_corners:
            mins.x = min(mins.x, c.x)
            mins.y = min(mins.y, c.y)
            mins.z = min(mins.z, c.z)
            maxs.x = max(maxs.x, c.x)
            maxs.y = max(maxs.y, c.y)
            maxs.z = max(maxs.z, c.z)
    return mins, maxs


def setup_camera_ortho(name, location, rotation_euler, ortho_scale):
    """Create an orthographic camera."""
    cam_data = bpy.data.cameras.new(name)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_data.clip_start = 0.001
    cam_data.clip_end = 100.0
    cam_obj = bpy.data.objects.new(name, cam_data)
    bpy.context.collection.objects.link(cam_obj)
    cam_obj.location = location
    cam_obj.rotation_euler = rotation_euler
    return cam_obj


def setup_lighting():
    """Simple 3-point lighting for clean renders."""
    # Key light
    key = bpy.data.lights.new("Key", "SUN")
    key.energy = 3.0
    key_obj = bpy.data.objects.new("Key", key)
    bpy.context.collection.objects.link(key_obj)
    key_obj.rotation_euler = (math.radians(45), 0, math.radians(30))

    # Fill light
    fill = bpy.data.lights.new("Fill", "SUN")
    fill.energy = 1.5
    fill_obj = bpy.data.objects.new("Fill", fill)
    bpy.context.collection.objects.link(fill_obj)
    fill_obj.rotation_euler = (math.radians(30), 0, math.radians(-60))

    # Rim light
    rim = bpy.data.lights.new("Rim", "SUN")
    rim.energy = 1.0
    rim_obj = bpy.data.objects.new("Rim", rim)
    bpy.context.collection.objects.link(rim_obj)
    rim_obj.rotation_euler = (math.radians(-20), 0, math.radians(150))


def create_marker_sphere(location, color, radius=0.005, name="Marker"):
    """Create a colored sphere at the given location as a visual marker."""
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=location, segments=16, ring_count=8)
    sphere = bpy.context.active_object
    sphere.name = name
    mat = bpy.data.materials.new(name + "_mat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = 2.0
    sphere.data.materials.append(mat)
    return sphere


def create_marker_arrow(origin, direction, color, length=0.03, name="Arrow"):
    """Create a cone (arrow) pointing in the given direction."""
    direction = Vector(direction).normalized()
    tip = Vector(origin) + direction * length

    bpy.ops.mesh.primitive_cone_add(
        radius1=0.003, radius2=0, depth=length,
        location=(Vector(origin) + tip) / 2,
        vertices=12,
    )
    arrow = bpy.context.active_object
    arrow.name = name

    # Orient cone to point along direction
    up = Vector((0, 0, 1))
    rot = up.rotation_difference(direction)
    arrow.rotation_mode = "QUATERNION"
    arrow.rotation_quaternion = rot

    mat = bpy.data.materials.new(name + "_mat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = 2.0
    arrow.data.materials.append(mat)
    return arrow


def add_markers(markers_data, center, scale):
    """Add colored spheres and arrows for PCA-detected points."""
    marker_radius = scale * 0.03  # 3% of model size
    arrow_length = scale * 0.08

    created = []

    if "gripPoint" in markers_data:
        gp = Vector(markers_data["gripPoint"])
        created.append(create_marker_sphere(gp, (0.2, 0.4, 1.0, 1.0), marker_radius, "GripPoint"))

    if "tipPoint" in markers_data:
        tp = Vector(markers_data["tipPoint"])
        created.append(create_marker_sphere(tp, (1.0, 0.2, 0.2, 1.0), marker_radius, "TipPoint"))

    if "tipAxis" in markers_data and "gripPoint" in markers_data:
        origin = Vector(markers_data.get("gripPoint", [0, 0, 0]))
        direction = Vector(markers_data["tipAxis"])
        created.append(create_marker_arrow(origin, direction, (0.2, 1.0, 0.3, 1.0), arrow_length, "TipAxis"))

    if "actionAxis" in markers_data and "gripPoint" in markers_data:
        origin = Vector(markers_data.get("gripPoint", [0, 0, 0]))
        direction = Vector(markers_data["actionAxis"])
        created.append(create_marker_arrow(origin, direction, (1.0, 0.9, 0.1, 1.0), arrow_length, "ActionAxis"))

    return created


def render_view(cam_obj, output_path, resolution):
    """Render a single view."""
    scene = bpy.context.scene
    scene.camera = cam_obj
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    # Blender requires absolute paths for reliable output
    scene.render.filepath = os.path.abspath(output_path)
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args(sys.argv)
    input_path = args["--input"]
    output_dir = args["--output-dir"]
    markers_path = args["--markers"]
    resolution = int(args["--resolution"])

    os.makedirs(output_dir, exist_ok=True)

    clear_scene()
    imported = import_glb(input_path)

    if not imported:
        raise SystemExit(f"No objects imported from {input_path}")

    mesh_objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    mins, maxs = get_scene_bounds(mesh_objects)
    center = (mins + maxs) / 2
    size = maxs - mins
    max_extent = max(size.x, size.y, size.z)
    ortho_scale = max_extent * 1.4  # padding
    cam_distance = max_extent * 3

    # Load and place markers if provided
    markers_data = None
    if markers_path and os.path.exists(markers_path):
        with open(markers_path, "r") as f:
            markers_data = json.load(f)
        add_markers(markers_data, center, max_extent)

    setup_lighting()

    # Set up render engine
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if hasattr(bpy.types, "BLENDER_EEVEE_NEXT") else "BLENDER_EEVEE"

    # World background: neutral gray
    world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.18, 0.18, 0.20, 1.0)
        bg.inputs["Strength"].default_value = 0.5

    # Define 4 camera views (Blender: Z-up, Y-forward in viewport)
    views = {
        "front": {
            "location": (center.x, center.y - cam_distance, center.z),
            "rotation": (math.radians(90), 0, 0),
        },
        "right": {
            "location": (center.x + cam_distance, center.y, center.z),
            "rotation": (math.radians(90), 0, math.radians(90)),
        },
        "top": {
            "location": (center.x, center.y, center.z + cam_distance),
            "rotation": (0, 0, 0),
        },
        "perspective": {
            "location": (
                center.x + cam_distance * 0.7,
                center.y - cam_distance * 0.7,
                center.z + cam_distance * 0.5,
            ),
            "rotation": (math.radians(55), 0, math.radians(45)),
        },
    }

    for view_name, view_config in views.items():
        cam = setup_camera_ortho(
            f"cam_{view_name}",
            view_config["location"],
            view_config["rotation"],
            ortho_scale,
        )
        output_path = os.path.join(output_dir, f"{view_name}.png")
        render_view(cam, output_path, resolution)
        print(f"Rendered {view_name} -> {output_path}")

    print(f"All 4 views rendered to {output_dir}")


if __name__ == "__main__":
    main()
