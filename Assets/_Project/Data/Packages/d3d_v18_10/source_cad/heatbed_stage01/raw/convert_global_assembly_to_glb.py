"""
convert_global_assembly_to_glb.py
Imports global-position STLs, scales mm -> m, applies FreeCAD colors,
and exports ONE combined GLB for visual verification.

Pipeline:
  1. Import each STL (already in FreeCAD global coords, Z-up, mm)
  2. Scale 0.001 (mm -> m)
  3. Clean mesh (merge-by-distance to fix z-fighting on compounds)
  4. Apply flat shading + matte material with FreeCAD color
  5. Export combined scene as GLB (Blender auto Z-up -> Y-up)

Run: "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" --background --python convert_global_assembly_to_glb.py

Output: d3d_full_assembly_reference.glb in the same directory.
"""
import bpy
import bmesh
import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STL_DIR = os.path.join(SCRIPT_DIR, "stl_full_raw")
GLB_PATH = os.path.join(SCRIPT_DIR, "d3d_full_assembly_61parts.glb")
COLORS_JSON = os.path.join(SCRIPT_DIR, "part_colors.json")

# Load FreeCAD colors if available
part_colors = {}
if os.path.exists(COLORS_JSON):
    with open(COLORS_JSON) as f:
        part_colors = json.load(f)
    print(f"[convert] Loaded {len(part_colors)} part colors")

# Clear scene
bpy.ops.wm.read_homefile(use_empty=True)

stl_files = sorted([f for f in os.listdir(STL_DIR) if f.lower().endswith('.stl')])
if not stl_files:
    print(f"[convert] ERROR: No STL files found in {STL_DIR}")
    import sys; sys.exit(1)

print(f"[convert] Found {len(stl_files)} STL files in {STL_DIR}")

imported_count = 0

for stl_file in stl_files:
    part_id = os.path.splitext(stl_file)[0]
    stl_path = os.path.join(STL_DIR, stl_file)

    print(f"\n--- {part_id} ---")

    # Import STL
    try:
        bpy.ops.wm.stl_import(filepath=stl_path)
    except AttributeError:
        try:
            bpy.ops.import_mesh.stl(filepath=stl_path)
        except Exception as e:
            print(f"  SKIP: STL import failed: {e}")
            continue

    # Get newly imported objects
    imported = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    if not imported:
        imported = [o for o in bpy.context.scene.objects
                    if o.type == 'MESH' and o.name not in {obj.name for obj in bpy.data.objects if obj.name.startswith("__done_")}]

    if not imported:
        print(f"  SKIP: no mesh imported")
        continue

    # Join if multiple
    if len(imported) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in imported:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = imported[0]
        bpy.ops.object.join()
        imported = [bpy.context.active_object]

    obj = imported[0]
    obj.name = part_id

    # Clean mesh: merge overlapping vertices to fix z-fighting on compounds
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.001)
    bpy.ops.object.mode_set(mode='OBJECT')

    dims = obj.dimensions
    print(f"  Dims (mm): {dims.x:.1f} x {dims.y:.1f} x {dims.z:.1f}")

    # Scale mm -> m
    obj.scale = (0.001, 0.001, 0.001)

    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Get FreeCAD color or use default light gray
    fc_color = part_colors.get(part_id, {"r": 0.7, "g": 0.7, "b": 0.7})
    r, g, b = fc_color["r"], fc_color["g"], fc_color["b"]

    # Add matte material with FreeCAD color
    mat = bpy.data.materials.new(name=f"{part_id}_mat")
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

    # Flat shading — no shade_smooth()
    bpy.ops.object.shade_flat()
    imported_count += 1
    print(f"  OK ({len(obj.data.vertices)} verts, color=({r:.2f},{g:.2f},{b:.2f}))")

# Deselect all before export
bpy.ops.object.select_all(action='DESELECT')

# Export combined GLB
print(f"\n[convert] Exporting {imported_count} parts as single GLB...")
try:
    bpy.ops.wm.gltf_export(
        filepath=GLB_PATH,
        export_format='GLB',
        use_selection=False,
        export_apply=True,
    )
except (AttributeError, TypeError):
    bpy.ops.export_scene.gltf(
        filepath=GLB_PATH,
        export_format='GLB',
        use_selection=False,
        export_apply=True,
    )

file_size = os.path.getsize(GLB_PATH)
print(f"\n{'='*60}")
print(f"[convert] Done. {imported_count} parts -> {GLB_PATH} ({file_size/1024:.1f} KB)")
print(f"[convert] Flat shading, matte material, FreeCAD colors applied.")
