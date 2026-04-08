"""
extract_transforms_from_assembly.py
Imports the full D3D assembly STEP into Blender, identifies parts by name,
and outputs Unity-space (Y-up, left-handed, meters) transforms for each.

Also re-exports each identified part as an individual GLB at origin with
correct Y-up orientation.

Blender handles ALL coordinate conversion:
  - STEP import: geometry stays in Z-up (Blender native)
  - Blender world coords (Z-up) -> Unity/glTF coords (Y-up):
    Unity.x =  Blender.x
    Unity.y =  Blender.z
    Unity.z = -Blender.y  (handedness flip, but GLB export handles this)

For positions, we use the same conversion that Blender's glTF exporter uses internally.
For rotations, we convert the Blender quaternion to glTF/Unity quaternion.

Run: "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" --background --python extract_transforms_from_assembly.py

Output:
  - assembly_transforms.json  (Unity-space positions + rotations for label_map parts)
  - glb_exports_v2/           (individual GLBs at origin, correct Y-up)
"""
import bpy
import json
import os
import sys
import mathutils
import math

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STEP_FILE = os.path.join(SCRIPT_DIR, "D3D_full_assembly.step")
LABEL_MAP_FILE = os.path.join(SCRIPT_DIR, "label_map.json")
OUT_JSON = os.path.join(SCRIPT_DIR, "assembly_transforms.json")
GLB_DIR = os.path.join(SCRIPT_DIR, "glb_exports_v2")

# machine.json coordinate offset:
# The frame in FreeCAD starts at (0,0,0) with size 304.8mm = 0.3048m
# In machine.json, frame is centered at X=0, Z=0, bottom at Y=0.552 (worktable)
FRAME_SIZE_M = 0.3048
WORKTABLE_Y = 0.552

os.makedirs(GLB_DIR, exist_ok=True)

# Load label map
with open(LABEL_MAP_FILE) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

print(f"[extract] Label map: {len(label_map)} parts")
for fc_label, part_id in label_map.items():
    print(f"  {fc_label} -> {part_id}")

# Clear scene and import STEP
print(f"\n[extract] Importing STEP: {STEP_FILE}")
bpy.ops.wm.read_homefile(use_empty=True)

if not os.path.exists(STEP_FILE):
    print(f"[extract] ERROR: STEP file not found: {STEP_FILE}")
    sys.exit(1)

try:
    bpy.ops.wm.step_import(filepath=STEP_FILE)
except AttributeError:
    try:
        bpy.ops.import_scene.step(filepath=STEP_FILE)
    except Exception as e:
        print(f"[extract] ERROR: STEP import failed: {e}")
        print("[extract] Trying OBJ import as fallback...")
        sys.exit(1)

# List all imported objects
all_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print(f"\n[extract] Imported {len(all_objects)} mesh objects:")
for obj in sorted(all_objects, key=lambda o: o.name):
    loc = obj.location
    dims = obj.dimensions
    print(f"  {obj.name:<50s} loc=({loc.x:.4f}, {loc.y:.4f}, {loc.z:.4f}) dims=({dims.x:.4f}, {dims.y:.4f}, {dims.z:.4f})")

# Try to find the Frame object to determine coordinate offset
# In Blender Z-up space, the frame should be at origin with size ~0.3048m (after mm->m)
# Actually STEP preserves mm coordinates, so frame will be ~304.8mm
frame_obj = None
for obj in all_objects:
    if obj.name.lower().startswith("frame") or "frame" in obj.name.lower():
        frame_obj = obj
        break

if frame_obj:
    bb = frame_obj.bound_box
    world_corners = [frame_obj.matrix_world @ mathutils.Vector(c) for c in bb]
    xs = [c.x for c in world_corners]
    ys = [c.y for c in world_corners]
    zs = [c.z for c in world_corners]
    print(f"\n[extract] Frame bounding box (Blender Z-up, mm):")
    print(f"  X: [{min(xs):.1f}, {max(xs):.1f}] = {max(xs)-min(xs):.1f}")
    print(f"  Y: [{min(ys):.1f}, {max(ys):.1f}] = {max(ys)-min(ys):.1f}")
    print(f"  Z: [{min(zs):.1f}, {max(zs):.1f}] = {max(zs)-min(zs):.1f}")
    frame_center_x = (min(xs) + max(xs)) / 2
    frame_center_y = (min(ys) + max(ys)) / 2
    frame_min_z = min(zs)
    print(f"  Center XY: ({frame_center_x:.1f}, {frame_center_y:.1f}), Min Z: {frame_min_z:.1f}")


def blender_to_unity_position(bx, by, bz, scale=0.001):
    """
    Convert Blender world position (Z-up, mm) to Unity position (Y-up, meters).

    Then apply the machine.json offset:
    - Center frame at X=0, Z=0
    - Place frame bottom at Y=WORKTABLE_Y

    Blender Z-up -> Unity Y-up:
      Unity.x =  Blender.x
      Unity.y =  Blender.z
      Unity.z =  Blender.y

    Note: handedness flip is handled by negating rotation components, not position.
    The position swap (Y<->Z) is sufficient because GLB/Unity treats the geometry
    consistently with the rotation.
    """
    # Convert to meters and swap Y/Z for axis change
    ux = bx * scale
    uy = bz * scale  # Blender Z -> Unity Y (up)
    uz = by * scale  # Blender Y -> Unity Z (forward)

    # Apply machine.json world offset
    # Frame center in Blender is at ~(152.4, 152.4, 152.4) mm
    # In Unity: center at X=0,Z=0, bottom at Y=0.552
    ux -= FRAME_SIZE_M / 2   # center X
    uy += WORKTABLE_Y         # raise to worktable
    uz -= FRAME_SIZE_M / 2   # center Z

    return (round(ux, 4), round(uy, 4), round(uz, 4))


def blender_to_unity_rotation(quat):
    """
    Convert Blender quaternion (Z-up, right-handed) to Unity quaternion (Y-up, left-handed).

    Blender quaternion: (w, x, y, z) in Blender's Z-up right-handed system
    Unity quaternion: (x, y, z, w) in Unity's Y-up left-handed system

    The conversion swaps Y/Z components and negates for handedness:
      Unity.qx = -Blender.qx
      Unity.qy = -Blender.qz
      Unity.qz = -Blender.qy
      Unity.qw =  Blender.qw

    This matches the FreeCAD->Unity conversion that was already validated.
    """
    w, x, y, z = quat
    return (round(-x, 4), round(-z, 4), round(-y, 4), round(w, 4))


# Match imported objects to label_map entries
# STEP import may rename objects; try fuzzy matching
print(f"\n[extract] Matching objects to label_map...")

transforms = {}
matched = []
unmatched_labels = list(label_map.keys())

for fc_label, part_id in label_map.items():
    # Try exact match first, then fuzzy
    match = None
    for obj in all_objects:
        obj_name = obj.name.replace("_", " ").strip()
        if obj_name == fc_label or obj.name == fc_label:
            match = obj
            break

    if match is None:
        # Try partial match (STEP import may append numbers)
        for obj in all_objects:
            if fc_label.lower() in obj.name.lower() or obj.name.lower() in fc_label.lower():
                match = obj
                break

    if match is None:
        print(f"  NO MATCH: '{fc_label}' -> {part_id}")
        continue

    # Get world transform
    mat = match.matrix_world
    loc = mat.to_translation()
    rot = mat.to_quaternion()

    unity_pos = blender_to_unity_position(loc.x, loc.y, loc.z)
    unity_rot = blender_to_unity_rotation(rot)

    print(f"  MATCH: '{fc_label}' = '{match.name}'")
    print(f"    Blender: pos=({loc.x:.1f}, {loc.y:.1f}, {loc.z:.1f})  rot=({rot.w:.3f}, {rot.x:.3f}, {rot.y:.3f}, {rot.z:.3f})")
    print(f"    Unity:   pos=({unity_pos[0]:.4f}, {unity_pos[1]:.4f}, {unity_pos[2]:.4f})  rot=({unity_rot[0]:.4f}, {unity_rot[1]:.4f}, {unity_rot[2]:.4f}, {unity_rot[3]:.4f})")

    transforms[part_id] = {
        "blender_name": match.name,
        "fc_label": fc_label,
        "assembledPosition": {"x": unity_pos[0], "y": unity_pos[1], "z": unity_pos[2]},
        "assembledRotation": {"x": unity_rot[0], "y": unity_rot[1], "z": unity_rot[2], "w": unity_rot[3]}
    }
    matched.append((fc_label, match))
    if fc_label in unmatched_labels:
        unmatched_labels.remove(fc_label)

# Also extract Frame position for reference
if frame_obj:
    loc = frame_obj.matrix_world.to_translation()
    rot = frame_obj.matrix_world.to_quaternion()
    unity_pos = blender_to_unity_position(loc.x, loc.y, loc.z)
    unity_rot = blender_to_unity_rotation(rot)
    transforms["_frame_reference"] = {
        "blender_name": frame_obj.name,
        "assembledPosition": {"x": unity_pos[0], "y": unity_pos[1], "z": unity_pos[2]},
        "assembledRotation": {"x": unity_rot[0], "y": unity_rot[1], "z": unity_rot[2], "w": unity_rot[3]},
        "note": "Frame reference - should be centered at (0, WORKTABLE_Y, 0)"
    }

# Write transforms JSON
with open(OUT_JSON, "w") as f:
    json.dump(transforms, f, indent=2)
print(f"\n[extract] Transforms written to: {OUT_JSON}")

# --- Re-export individual GLBs ---
print(f"\n[extract] Re-exporting individual GLBs to {GLB_DIR}...")

for fc_label, obj in matched:
    part_id = label_map[fc_label]
    glb_path = os.path.join(GLB_DIR, f"{part_id}_approved.glb")

    # Deselect all, select only this object
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    # Store original transform
    orig_loc = obj.location.copy()
    orig_rot = obj.rotation_euler.copy()
    orig_scale = obj.scale.copy()

    # Move to origin for export
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)

    # Scale from mm to meters
    obj.scale = (0.001, 0.001, 0.001)

    # Apply transforms
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Add material
    if not obj.data.materials:
        mat = bpy.data.materials.new(name=f"{part_id}_material")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (0.5, 0.5, 0.5, 1.0)
            bsdf.inputs["Metallic"].default_value = 0.8
            bsdf.inputs["Roughness"].default_value = 0.4
        obj.data.materials.append(mat)

    # Smooth shading
    bpy.ops.object.shade_smooth()

    # Export GLB
    try:
        bpy.ops.export_scene.gltf(
            filepath=glb_path,
            export_format='GLB',
            use_selection=True,
            export_apply=True,
        )
        size = os.path.getsize(glb_path)
        print(f"  {part_id} -> {glb_path} ({size/1024:.1f} KB)")
    except Exception as e:
        print(f"  {part_id} EXPORT FAILED: {e}")

    # Note: we don't restore transforms since each GLB is exported independently

# Print summary of all imported objects that weren't matched
unmatched_objects = [o for o in all_objects if o not in [m[1] for m in matched]]
if unmatched_objects:
    print(f"\n[extract] Unmatched Blender objects ({len(unmatched_objects)}):")
    for obj in sorted(unmatched_objects, key=lambda o: o.name)[:30]:
        loc = obj.location
        print(f"  {obj.name:<50s} loc=({loc.x:.1f}, {loc.y:.1f}, {loc.z:.1f})")

if unmatched_labels:
    print(f"\n[extract] Unmatched label_map entries: {unmatched_labels}")

print(f"\n[extract] Summary: {len(matched)}/{len(label_map)} parts matched and exported.")
print(f"[extract] Done.")
