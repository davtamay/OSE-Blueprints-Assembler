"""
convert_stl_to_glb.py
Converts STL files (FreeCAD Z-up, mm) to GLB (Y-up, meters) via Blender.

Pipeline:
  1. Import each STL (Z-up geometry, millimeters)
  2. Scale by 0.001 (mm -> meters)
  3. Apply transforms
  4. Add a default metallic material
  5. Export as GLB (Blender auto-converts Z-up -> Y-up for glTF)

Run: "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" --background --python convert_stl_to_glb.py

Output: glb_exports/ directory with *_approved.glb files ready to replace existing assets.
"""
import bpy
import os
import sys
import math

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STL_DIR = os.path.join(SCRIPT_DIR, "stl_exports")
GLB_DIR = os.path.join(SCRIPT_DIR, "glb_exports")

os.makedirs(GLB_DIR, exist_ok=True)

# Collect STL files
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

    # Clear scene completely
    bpy.ops.wm.read_homefile(use_empty=True)

    # Import STL (geometry in FreeCAD's Z-up, mm)
    try:
        # Blender 4.x+ new IO API
        bpy.ops.wm.stl_import(filepath=stl_path)
    except AttributeError:
        try:
            # Legacy API fallback
            bpy.ops.import_mesh.stl(filepath=stl_path)
        except Exception as e:
            print(f"  SKIP: STL import failed: {e}")
            continue

    imported_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not imported_objects:
        print(f"  SKIP: No mesh objects imported")
        continue

    print(f"  Imported {len(imported_objects)} mesh object(s)")

    # If multiple objects were imported, join them into one
    if len(imported_objects) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in imported_objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = imported_objects[0]
        bpy.ops.object.join()
        imported_objects = [bpy.context.active_object]

    obj = imported_objects[0]
    obj.name = part_id

    # Report original bounds (mm, Z-up)
    dims = obj.dimensions
    print(f"  Original dims (mm): {dims.x:.1f} x {dims.y:.1f} x {dims.z:.1f}")

    # Scale from mm to meters
    obj.scale = (0.001, 0.001, 0.001)

    # Apply scale transform
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Report scaled bounds (meters, Z-up in Blender)
    dims = obj.dimensions
    print(f"  Scaled dims (m):  {dims.x:.4f} x {dims.y:.4f} x {dims.z:.4f}")

    # Add a default PBR material (gray metallic)
    mat = bpy.data.materials.new(name=f"{part_id}_material")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        # Gray metallic appearance
        bsdf.inputs["Base Color"].default_value = (0.5, 0.5, 0.5, 1.0)
        bsdf.inputs["Metallic"].default_value = 0.8
        bsdf.inputs["Roughness"].default_value = 0.4

    # Assign material to object
    if obj.data.materials:
        obj.data.materials[0] = mat
    else:
        obj.data.materials.append(mat)

    # Compute smooth shading for better visual quality
    bpy.ops.object.shade_smooth()

    # Export as GLB
    # Blender's glTF exporter auto-converts from Z-up (Blender) to Y-up (glTF)
    try:
        # Blender 4.x+ new IO API
        bpy.ops.wm.gltf_export(
            filepath=glb_path,
            export_format='GLB',
            use_selection=False,
            export_apply=True,
        )
    except (AttributeError, TypeError):
        try:
            # Standard API
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
print(f"[convert] Copy the *_approved.glb files to assets/parts/ to replace existing meshes.")
