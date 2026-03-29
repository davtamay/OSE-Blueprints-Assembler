"""
convert_all_centered_to_glb.py
Converts ALL centered STLs to GLB with flat shading, matte material,
and FreeCAD colors. Node names use _mesh suffix to prevent
PackageAssetPostprocessor from overwriting positions.

Run: "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" --background --python convert_all_centered_to_glb.py
"""
import bpy
import json
import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STL_DIR = os.path.join(SCRIPT_DIR, "stl_all_centered")
GLB_DIR = os.path.join(SCRIPT_DIR, "glb_all_centered")
COLORS_JSON = os.path.join(SCRIPT_DIR, "part_colors.json")

os.makedirs(GLB_DIR, exist_ok=True)

# Load FreeCAD colors if available
part_colors = {}
if os.path.exists(COLORS_JSON):
    with open(COLORS_JSON) as f:
        part_colors = json.load(f)
    print(f"[convert] Loaded {len(part_colors)} part colors")

stl_files = sorted([f for f in os.listdir(STL_DIR) if f.lower().endswith('.stl')])
if not stl_files:
    print(f"[convert] ERROR: No STL files found in {STL_DIR}")
    sys.exit(1)

print(f"[convert] Found {len(stl_files)} STL files in {STL_DIR}")
print(f"[convert] Output directory: {GLB_DIR}")

converted = 0

for stl_file in stl_files:
    part_id = os.path.splitext(stl_file)[0]
    stl_path = os.path.join(STL_DIR, stl_file)
    glb_path = os.path.join(GLB_DIR, f"{part_id}_approved.glb")

    print(f"\n--- {part_id} ---")

    bpy.ops.wm.read_homefile(use_empty=True)

    try:
        bpy.ops.wm.stl_import(filepath=stl_path)
    except AttributeError:
        try:
            bpy.ops.import_mesh.stl(filepath=stl_path)
        except Exception as e:
            print(f"  SKIP: STL import failed: {e}")
            continue

    imported_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not imported_objects:
        print(f"  SKIP: No mesh objects imported")
        continue

    if len(imported_objects) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in imported_objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = imported_objects[0]
        bpy.ops.object.join()
        imported_objects = [bpy.context.active_object]

    obj = imported_objects[0]
    # _mesh suffix prevents PackageAssetPostprocessor from matching partIds
    obj.name = part_id + "_mesh"

    # Scale mm -> m
    obj.scale = (0.001, 0.001, 0.001)
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    dims = obj.dimensions
    print(f"  Dims (m): {dims.x:.4f} x {dims.y:.4f} x {dims.z:.4f}")

    # Get FreeCAD color or default gray
    fc_color = part_colors.get(part_id, {"r": 0.7, "g": 0.7, "b": 0.7})
    r, g, b = fc_color["r"], fc_color["g"], fc_color["b"]

    # Matte material with FreeCAD color
    mat = bpy.data.materials.new(name=f"{part_id}_material")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
        bsdf.inputs["Metallic"].default_value = 0.0
        bsdf.inputs["Roughness"].default_value = 0.8

    if obj.data.materials:
        obj.data.materials[0] = mat
    else:
        obj.data.materials.append(mat)

    bpy.ops.object.shade_flat()

    # Export GLB
    try:
        bpy.ops.wm.gltf_export(
            filepath=glb_path,
            export_format='GLB',
            use_selection=False,
            export_apply=True,
        )
    except (AttributeError, TypeError):
        try:
            bpy.ops.export_scene.gltf(
                filepath=glb_path,
                export_format='GLB',
                use_selection=False,
                export_apply=True,
            )
        except Exception as e:
            print(f"  SKIP: GLB export failed: {e}")
            continue

    file_size = os.path.getsize(glb_path)
    print(f"  Exported: {glb_path} ({file_size / 1024:.1f} KB)")
    converted += 1

print(f"\n{'='*60}")
print(f"[convert] Done. {converted}/{len(stl_files)} GLB files written to {GLB_DIR}")
