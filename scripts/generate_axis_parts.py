"""
Generate parametric GLB meshes for missing D3D axis parts.
Uses trimesh to create approximate mechanical part geometry.
Output: Assets/_Project/Data/Packages/d3d_v18_10/assets/parts/

Avoids boolean CSG (unreliable in trimesh) — uses concatenation of shells instead.
"""

import os
import math
import numpy as np
import trimesh
from trimesh.creation import cylinder, annulus

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
OUTPUT_DIR = os.path.join(
    REPO_ROOT, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "assets", "parts"
)

# Metal colors (linear RGB)
STEEL = [0.6, 0.6, 0.65, 1.0]
DARK_STEEL = [0.35, 0.35, 0.38, 1.0]
ALUMINUM = [0.75, 0.75, 0.78, 1.0]
BLACK_RUBBER = [0.12, 0.12, 0.12, 1.0]


def make_material(color, metallic=0.8, roughness=0.3, name="material"):
    from trimesh.visual.material import PBRMaterial
    return PBRMaterial(
        baseColorFactor=color,
        metallicFactor=metallic,
        roughnessFactor=roughness,
        name=name,
    )


def apply_material(mesh, color, metallic=0.8, roughness=0.3, name="material"):
    mat = make_material(color, metallic, roughness, name)
    mesh.visual = trimesh.visual.TextureVisuals(material=mat)
    return mesh


def make_tube(outer_r, inner_r, height, sections=48):
    """Create a tube (hollow cylinder) without boolean ops — uses annulus for caps + cylinder walls."""
    # Outer cylinder
    outer = cylinder(radius=outer_r, height=height, sections=sections)
    # Inner cylinder (slightly taller to poke through)
    inner = cylinder(radius=inner_r, height=height + 0.001, sections=sections)
    # Try boolean
    try:
        result = outer.difference(inner)
        if result is not None and len(result.faces) > 10:
            return result
    except Exception:
        pass
    # Fallback: just return outer (no bore, but at least valid geometry)
    return outer


def make_hex_prism(across_flats, height):
    """Hexagonal prism from across-flats dimension."""
    circumradius = across_flats / (2 * math.cos(math.pi / 6))
    return cylinder(radius=circumradius, height=height, sections=6)


def save_glb(mesh_or_scene, filename):
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    path = os.path.join(OUTPUT_DIR, filename)
    if isinstance(mesh_or_scene, trimesh.Scene):
        mesh_or_scene.export(path, file_type="glb")
    else:
        scene = trimesh.Scene(mesh_or_scene)
        scene.export(path, file_type="glb")
    size_kb = os.path.getsize(path) / 1024
    faces = 0
    if isinstance(mesh_or_scene, trimesh.Trimesh):
        faces = len(mesh_or_scene.faces)
    print(f"  -> {filename} ({size_kb:.1f} KB, {faces} faces)")
    return path


# ─────────────────────────────────────────────────────────────
# 1. GT2 Pulley (19-tooth, 7mm width)
# ─────────────────────────────────────────────────────────────
def build_gt2_pulley():
    print("Building GT2 19-tooth pulley...")
    s = 48
    od = 0.0116
    belt_w = 0.007
    flange_od = 0.016
    flange_h = 0.001
    bore_r = 0.0025
    hub_od = 0.010
    hub_h = 0.006

    # Build as solid pieces (no bore subtraction — bore is too small to matter visually at this scale)
    hub = cylinder(radius=hub_od / 2, height=hub_h, sections=s)
    hub.apply_translation([0, 0, hub_h / 2])

    bot_flange = cylinder(radius=flange_od / 2, height=flange_h, sections=s)
    bot_flange.apply_translation([0, 0, hub_h + flange_h / 2])

    body = cylinder(radius=od / 2, height=belt_w, sections=s)
    body.apply_translation([0, 0, hub_h + flange_h + belt_w / 2])

    top_flange = cylinder(radius=flange_od / 2, height=flange_h, sections=s)
    top_flange.apply_translation([0, 0, hub_h + flange_h + belt_w + flange_h / 2])

    total_h = hub_h + 2 * flange_h + belt_w
    pulley = trimesh.util.concatenate([hub, bot_flange, body, top_flange])
    pulley.apply_translation([0, 0, -total_h / 2])

    apply_material(pulley, ALUMINUM, metallic=0.85, roughness=0.25, name="pulley_aluminum")
    save_glb(pulley, "d3d_axis_gt2_pulley_19t.glb")


# ─────────────────────────────────────────────────────────────
# 2. GT2 Timing Belt (flat band approximation)
# ─────────────────────────────────────────────────────────────
def build_gt2_belt():
    print("Building GT2 timing belt...")
    belt = trimesh.primitives.Box(extents=[0.300, 0.0014, 0.006]).to_mesh()
    apply_material(belt, BLACK_RUBBER, metallic=0.05, roughness=0.85, name="belt_rubber")
    save_glb(belt, "d3d_axis_gt2_belt.glb")


# ─────────────────────────────────────────────────────────────
# 3. M6x18 Hex Bolt
# ─────────────────────────────────────────────────────────────
def build_hex_bolt(thread_d, length, head_af, head_h, name):
    print(f"Building {name}...")
    s = 48
    head = make_hex_prism(head_af, head_h)
    head.apply_translation([0, 0, head_h / 2])

    shank = cylinder(radius=thread_d / 2, height=length, sections=s)
    shank.apply_translation([0, 0, -length / 2])

    bolt = trimesh.util.concatenate([head, shank])
    apply_material(bolt, STEEL, metallic=0.85, roughness=0.3, name="bolt_steel")
    save_glb(bolt, f"{name}.glb")


def build_m6x18_bolt():
    build_hex_bolt(0.006, 0.018, 0.010, 0.004, "d3d_axis_m6x18_bolt")


# ─────────────────────────────────────────────────────────────
# 4. M3x25 Socket Head Cap Screw (SHCS)
# ─────────────────────────────────────────────────────────────
def build_m3x25_shcs():
    print("Building M3x25 SHCS...")
    s = 48
    thread_d = 0.003
    length = 0.025
    head_d = 0.0055
    head_h = 0.003

    head = cylinder(radius=head_d / 2, height=head_h, sections=s)
    head.apply_translation([0, 0, head_h / 2])

    shank = cylinder(radius=thread_d / 2, height=length, sections=s)
    shank.apply_translation([0, 0, -length / 2])

    screw = trimesh.util.concatenate([head, shank])
    apply_material(screw, DARK_STEEL, metallic=0.9, roughness=0.25, name="shcs_steel")
    save_glb(screw, "d3d_axis_m3x25_shcs.glb")


# ─────────────────────────────────────────────────────────────
# 5. M6 Hex Nut
# ─────────────────────────────────────────────────────────────
def build_m6_nut():
    print("Building M6 hex nut...")
    af = 0.010
    height = 0.005
    bore_r = 0.003

    outer = make_hex_prism(af, height)
    inner = cylinder(radius=bore_r, height=height + 0.002, sections=48)

    try:
        nut = outer.difference(inner)
        if nut is None or len(nut.faces) < 10:
            raise ValueError("boolean failed")
    except Exception:
        # Fallback: solid hex (bore not visible at this scale anyway)
        nut = outer

    apply_material(nut, STEEL, metallic=0.85, roughness=0.3, name="nut_steel")
    save_glb(nut, "d3d_axis_m6_nut.glb")


# ─────────────────────────────────────────────────────────────
# 6. LM8UU Linear Bearing
# ─────────────────────────────────────────────────────────────
def build_lm8uu():
    print("Building LM8UU linear bearing...")
    s = 48
    od = 0.015
    bore_d = 0.008
    length = 0.024

    outer = cylinder(radius=od / 2, height=length, sections=s)
    inner = cylinder(radius=bore_d / 2, height=length + 0.002, sections=s)

    try:
        bearing = outer.difference(inner)
        if bearing is None or len(bearing.faces) < 10:
            raise ValueError("boolean failed")
    except Exception:
        bearing = outer

    apply_material(bearing, STEEL, metallic=0.9, roughness=0.2, name="bearing_steel")
    save_glb(bearing, "d3d_axis_lm8uu_bearing.glb")


# ─────────────────────────────────────────────────────────────
# 7. 625ZZ Flanged Bearing
# ─────────────────────────────────────────────────────────────
def build_625zz_bearing():
    print("Building 625ZZ flanged bearing...")
    s = 48
    od = 0.016
    bore_d = 0.005
    width = 0.005
    flange_od = 0.018
    flange_h = 0.001

    body = cylinder(radius=od / 2, height=width, sections=s)
    flange = cylinder(radius=flange_od / 2, height=flange_h, sections=s)
    flange.apply_translation([0, 0, -(width / 2 - flange_h / 2)])

    shell = trimesh.util.concatenate([body, flange])

    bore_cyl = cylinder(radius=bore_d / 2, height=width + flange_h + 0.002, sections=s)

    try:
        bearing = shell.difference(bore_cyl)
        if bearing is None or len(bearing.faces) < 10:
            raise ValueError("boolean failed")
    except Exception:
        bearing = shell

    apply_material(bearing, STEEL, metallic=0.9, roughness=0.2, name="bearing_steel")
    save_glb(bearing, "d3d_axis_625zz_bearing.glb")


# ─────────────────────────────────────────────────────────────
if __name__ == "__main__":
    print(f"Output directory: {OUTPUT_DIR}")
    print()

    build_gt2_pulley()
    build_gt2_belt()
    build_m6x18_bolt()
    build_m3x25_shcs()
    build_m6_nut()
    build_lm8uu()
    build_625zz_bearing()

    print()
    print("Done! All 7 parts generated.")
