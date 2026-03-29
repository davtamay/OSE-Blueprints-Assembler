"""
verify_glb_orientation.py
Verifies that re-exported GLB files have correct Y-up axis orientation.

Imports each GLB into Blender and reports dimensions in glTF/Unity space (Y-up).
Expected: thin/flat dimensions should be along Y axis (up), not Z.

Run: "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" --background --python verify_glb_orientation.py
"""
import bpy
import os
import sys

PARTS_DIR = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "..", "assets", "parts"
))

# Parts to verify with expected thin axis
VERIFY = {
    "d3d_heatbed_body_approved.glb": "Heated bed plate - should be thin in Y (up)",
    "d3d_heatbed_snapbuckle_approved.glb": "Snap buckle clamp",
    "d3d_titan_aero_core_approved.glb": "Titan Aero extruder core",
    "d3d_x_axis_motor_holder_unit_approved.glb": "X-axis motor holder",
    "d3d_x_axis_idler_unit_approved.glb": "X-axis idler unit",
    "d3d_y_left_axis_unit_approved.glb": "Y-axis left motor unit",
    "d3d_y_right_axis_unit_approved.glb": "Y-axis right motor unit",
    "d3d_extruder_simplified_carriage_approved.glb": "Simplified carriage",
}

print(f"[verify] Checking {len(VERIFY)} GLB files in {PARTS_DIR}")
print(f"{'Part':<50s}  {'X (m)':>8s}  {'Y (m)':>8s}  {'Z (m)':>8s}  Notes")
print("-" * 100)

for glb_file, description in VERIFY.items():
    glb_path = os.path.join(PARTS_DIR, glb_file)
    if not os.path.exists(glb_path):
        print(f"{glb_file:<50s}  MISSING")
        continue

    # Clear scene
    bpy.ops.wm.read_homefile(use_empty=True)

    # Import GLB (Blender converts Y-up -> Z-up internally)
    bpy.ops.wm.gltf_import(filepath=glb_path)

    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        print(f"{glb_file:<50s}  NO MESH")
        continue

    # Get bounding box in Blender space (Z-up)
    # After GLB import: Blender X = glTF X, Blender Y = glTF -Z, Blender Z = glTF Y
    # So to report in glTF/Unity space: X = blend_X, Y = blend_Z, Z = blend_Y
    min_x = min_y = min_z = float('inf')
    max_x = max_y = max_z = float('-inf')

    for obj in meshes:
        for corner in obj.bound_box:
            world = obj.matrix_world @ bpy.types.Object.bl_rna.properties['bound_box'].fixed_type(corner)
            # Actually simpler: just use dimensions
            pass

    # Use combined bounding box from all mesh objects
    import mathutils
    all_corners = []
    for obj in meshes:
        for corner in obj.bound_box:
            world_corner = obj.matrix_world @ mathutils.Vector(corner)
            all_corners.append(world_corner)

    if all_corners:
        xs = [c.x for c in all_corners]
        ys = [c.y for c in all_corners]
        zs = [c.z for c in all_corners]

        # Blender Z-up -> glTF Y-up conversion for reporting
        # Blender (X, Y, Z) -> glTF (X, Z, -Y) approximately
        # But actually Blender's glTF importer already handles this
        # So Blender dims (X, Y, Z with Z-up) correspond to glTF (X, Z, Y)
        # Width in Blender X = Width in glTF X
        # Width in Blender Y = Depth in glTF Z
        # Height in Blender Z = Height in glTF Y

        bx = max(xs) - min(xs)  # glTF X
        by = max(ys) - min(ys)  # glTF Z (depth)
        bz = max(zs) - min(zs)  # glTF Y (up)

        # Report in glTF/Unity coordinates
        unity_x = bx
        unity_y = bz  # Blender Z -> Unity Y
        unity_z = by  # Blender Y -> Unity Z

        thin = min(unity_x, unity_y, unity_z)
        thin_axis = "X" if thin == unity_x else ("Y" if thin == unity_y else "Z")

        print(f"{glb_file:<50s}  {unity_x:8.4f}  {unity_y:8.4f}  {unity_z:8.4f}  thin={thin_axis} | {description}")

print("\n[verify] Done. If heatbed has Y as thinnest axis, orientation is correct.")
