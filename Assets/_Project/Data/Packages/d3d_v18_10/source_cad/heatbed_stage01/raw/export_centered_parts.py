"""
export_centered_parts.py
Exports mapped parts as STL files CENTERED at bounding box center,
and computes the correct Unity playPositions.

Key insight (2026-03-28 diagnostic):
  obj.Shape in this FreeCAD file already contains geometry in GLOBAL
  assembly coordinates.  Placement == GlobalPlacement and should NOT
  be applied on top (that double-transforms).

Pipeline:
  1. Use obj.Shape directly (already in global coords, Z-up, mm)
  2. Find bounding-box center in global coords
  3. Translate shape so BB center is at origin
  4. Export centered STL
  5. Position = BB center converted to Unity coords
  6. Rotation = identity (global orientation baked into mesh vertices)

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" export_centered_parts.py
"""
import FreeCAD
import Part
import Mesh
import MeshPart
import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
FCSTD = os.path.join(SCRIPT_DIR, "D3Dfinalassemblyv1902.fcstd")
LABEL_MAP = os.path.join(SCRIPT_DIR, "label_map.json")
OUT_DIR = os.path.join(SCRIPT_DIR, "stl_centered")
OUT_JSON = os.path.join(SCRIPT_DIR, "centered_transforms.json")

FRAME_OUTER_M = 0.3048
WORKTABLE_Y = 0.552

os.makedirs(OUT_DIR, exist_ok=True)

with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

print(f"[export] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)
print(f"[export] Loaded docs: {list(FreeCAD.listDocuments().keys())}")

# Build object lookup
all_objects = {}
for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        if obj.Label not in all_objects:
            all_objects[obj.Label] = (obj, doc_name)

transforms = {}
exported = 0

for fc_label, part_id in label_map.items():
    print(f"\n--- {fc_label} -> {part_id} ---")

    entry = all_objects.get(fc_label)
    if entry is None:
        print(f"  SKIP: not found")
        continue

    obj, doc_name = entry

    try:
        shape = obj.Shape.copy()
    except:
        print(f"  SKIP: no Shape")
        continue

    if shape.isNull():
        print(f"  SKIP: null Shape")
        continue

    # obj.Shape is already in global assembly coordinates (verified by diagnostic).
    # No placement transform needed — just read the bounding box directly.
    bb = shape.BoundBox
    cx = (bb.XMin + bb.XMax) / 2.0
    cy = (bb.YMin + bb.YMax) / 2.0
    cz = (bb.ZMin + bb.ZMax) / 2.0

    print(f"  Global BB: ({bb.XMin:.1f},{bb.YMin:.1f},{bb.ZMin:.1f}) to ({bb.XMax:.1f},{bb.YMax:.1f},{bb.ZMax:.1f})")
    print(f"  Global center: ({cx:.2f}, {cy:.2f}, {cz:.2f}) mm")

    # Center the shape at bounding box center
    center_vec = FreeCAD.Vector(-cx, -cy, -cz)
    centered_shape = shape.translated(center_vec)

    # Verify centered BB
    cbb = centered_shape.BoundBox
    print(f"  Centered BB: ({cbb.XMin:.1f},{cbb.YMin:.1f},{cbb.ZMin:.1f}) to ({cbb.XMax:.1f},{cbb.YMax:.1f},{cbb.ZMax:.1f})")

    # Convert global center to Unity coordinates using M mapping.
    # The vertex pipeline (Blender Z→Y-up + glTFast X-negate) maps
    # FreeCAD (x,y,z) to Unity (-x, z, -y)/1000.  Positions must use
    # the same mapping so mesh and position are consistent:
    #   playPos = M(center) + assembly_offsets
    unity_x = round(FRAME_OUTER_M / 2 - cx / 1000.0, 4)
    unity_y = round(cz / 1000.0 + WORKTABLE_Y, 4)
    unity_z = round(FRAME_OUTER_M / 2 - cy / 1000.0, 4)

    # Rotation is identity — global orientation is baked into mesh vertices
    unity_qx = 0.0
    unity_qy = 0.0
    unity_qz = 0.0
    unity_qw = 1.0

    print(f"  Unity pos: ({unity_x}, {unity_y}, {unity_z})")
    print(f"  Unity rot: identity")

    transforms[part_id] = {
        "playPosition": {"x": unity_x, "y": unity_y, "z": unity_z},
        "playRotation": {"x": unity_qx, "y": unity_qy, "z": unity_qz, "w": unity_qw},
        "fc_label": fc_label,
        "global_center_mm": {"x": round(cx, 2), "y": round(cy, 2), "z": round(cz, 2)}
    }

    # Tessellate and export centered STL
    try:
        mesh = MeshPart.meshFromShape(
            Shape=centered_shape,
            LinearDeflection=0.05,
            AngularDeflection=0.1745
        )
    except:
        try:
            verts, faces = centered_shape.tessellate(0.1)
            mesh = Mesh.Mesh()
            for tri in faces:
                mesh.addFacet(
                    verts[tri[0]].x, verts[tri[0]].y, verts[tri[0]].z,
                    verts[tri[1]].x, verts[tri[1]].y, verts[tri[1]].z,
                    verts[tri[2]].x, verts[tri[2]].y, verts[tri[2]].z,
                )
        except Exception as e:
            print(f"  SKIP: tessellation failed: {e}")
            continue

    out_path = os.path.join(OUT_DIR, f"{part_id}.stl")
    mesh.write(out_path)
    print(f"  Exported: {out_path} ({mesh.CountFacets} tris)")
    exported += 1

# Write transforms JSON
with open(OUT_JSON, "w") as f:
    json.dump(transforms, f, indent=2)

print(f"\n{'='*60}")
print(f"[export] {exported}/{len(label_map)} centered STLs exported to {OUT_DIR}")
print(f"[export] Transforms written to {OUT_JSON}")
