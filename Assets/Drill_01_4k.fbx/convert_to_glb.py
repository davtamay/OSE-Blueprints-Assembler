"""
Blender CLI script: import Drill_01_4k.fbx with textures, export as GLB.
Run via: blender --background --python convert_to_glb.py
"""
import bpy
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
fbx_path   = os.path.join(script_dir, "Drill_01_4k.fbx")
tex_dir    = os.path.join(script_dir, "textures")
out_path   = os.path.join(
    script_dir, "..", "..", "..", "..", "assets", "tools", "tool_power_drill.glb"
)
out_path = os.path.normpath(out_path)

# ── Clear default scene ────────────────────────────────────────────────────────
bpy.ops.wm.read_factory_settings(use_empty=True)

# ── Import FBX ────────────────────────────────────────────────────────────────
bpy.ops.import_scene.fbx(filepath=fbx_path)

# ── Load and assign textures ──────────────────────────────────────────────────
diff_path      = os.path.join(tex_dir, "Drill_01_diff_4k.jpg")
rough_path     = os.path.join(tex_dir, "Drill_01_roughness_4k.jpg")
normal_path    = os.path.join(tex_dir, "Drill_01_nor_gl_4k.exr")

def load_image(path):
    if os.path.exists(path):
        return bpy.data.images.load(path)
    print(f"  WARNING: texture not found: {path}")
    return None

img_diff   = load_image(diff_path)
img_rough  = load_image(rough_path)
img_normal = load_image(normal_path)

# Apply to all mesh objects
for obj in bpy.data.objects:
    if obj.type != 'MESH':
        continue

    # Ensure a material slot
    if not obj.data.materials:
        mat = bpy.data.materials.new(name="DrillMat")
        obj.data.materials.append(mat)
    else:
        mat = obj.data.materials[0]

    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    # PBR output nodes
    out   = nodes.new("ShaderNodeOutputMaterial")
    bsdf  = nodes.new("ShaderNodeBsdfPrincipled")
    out.location  = (400, 0)
    bsdf.location = (0, 0)
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    def tex_node(img, label, loc, colorspace="sRGB"):
        n = nodes.new("ShaderNodeTexImage")
        n.image = img
        n.label = label
        n.location = loc
        if colorspace != "sRGB":
            img.colorspace_settings.name = colorspace
        return n

    if img_diff:
        t = tex_node(img_diff, "Diffuse", (-300, 200))
        links.new(t.outputs["Color"], bsdf.inputs["Base Color"])

    if img_rough:
        t = tex_node(img_rough, "Roughness", (-300, -100), "Non-Color")
        links.new(t.outputs["Color"], bsdf.inputs["Roughness"])

    if img_normal:
        t  = tex_node(img_normal, "Normal", (-600, -200), "Non-Color")
        nm = nodes.new("ShaderNodeNormalMap")
        nm.location = (-300, -300)
        links.new(t.outputs["Color"], nm.inputs["Color"])
        links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

# ── Orient: rotate so drill barrel points along +Z ───────────────────────────
# FBX is typically Y-up; glTF expects Y-up but tool convention needs +Z forward.
# Rotate all root objects -90 deg around X so the long axis becomes +Z.
for obj in bpy.data.objects:
    if obj.parent is None:
        obj.rotation_euler[0] += -1.5708  # -90 degrees in radians

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.transform_apply(rotation=True)

# ── Export GLB (embed textures) ───────────────────────────────────────────────
os.makedirs(os.path.dirname(out_path), exist_ok=True)
bpy.ops.export_scene.gltf(
    filepath=out_path,
    export_format='GLB',
    export_image_format='AUTO',
    export_texcoords=True,
    export_normals=True,
    export_materials='EXPORT',
    use_selection=False,
)

print(f"\n[convert_to_glb] Done → {out_path}")
