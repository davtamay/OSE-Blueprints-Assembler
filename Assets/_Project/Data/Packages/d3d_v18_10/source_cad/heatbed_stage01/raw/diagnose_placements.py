"""
diagnose_placements.py
Prints diagnostic info for each mapped part to understand how FreeCAD
stores placement vs shape coordinates.

Also exports STLs in THREE variants so we can see which produces correct assembly:
  A) raw obj.Shape (no transform)
  B) obj.Shape transformed by obj.Placement only
  C) obj.Shape transformed by getGlobalPlacement() (current approach)

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" diagnose_placements.py
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

OUT_A = os.path.join(SCRIPT_DIR, "stl_diag_raw")
OUT_B = os.path.join(SCRIPT_DIR, "stl_diag_local_placement")
OUT_C = os.path.join(SCRIPT_DIR, "stl_diag_global_placement")

for d in [OUT_A, OUT_B, OUT_C]:
    os.makedirs(d, exist_ok=True)

with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

print(f"[diag] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)

all_objects = {}
for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        if obj.Label not in all_objects:
            all_objects[obj.Label] = (obj, doc_name)

def bb_str(bb):
    return f"({bb.XMin:.1f},{bb.YMin:.1f},{bb.ZMin:.1f}) to ({bb.XMax:.1f},{bb.YMax:.1f},{bb.ZMax:.1f})"

def placement_str(pl):
    b = pl.Base
    q = pl.Rotation.Q  # (x, y, z, w)
    return f"pos=({b.x:.2f},{b.y:.2f},{b.z:.2f}) rot_q=({q[0]:.4f},{q[1]:.4f},{q[2]:.4f},{q[3]:.4f})"

def export_mesh(shape, out_path):
    try:
        mesh = MeshPart.meshFromShape(
            Shape=shape, LinearDeflection=0.05, AngularDeflection=0.1745)
    except:
        try:
            verts, faces = shape.tessellate(0.1)
            mesh = Mesh.Mesh()
            for tri in faces:
                mesh.addFacet(
                    verts[tri[0]].x, verts[tri[0]].y, verts[tri[0]].z,
                    verts[tri[1]].x, verts[tri[1]].y, verts[tri[1]].z,
                    verts[tri[2]].x, verts[tri[2]].y, verts[tri[2]].z,
                )
        except Exception as e:
            print(f"  SKIP tessellation: {e}")
            return False
    mesh.write(out_path)
    return True

for fc_label, part_id in label_map.items():
    entry = all_objects.get(fc_label)
    if entry is None:
        print(f"\n--- {fc_label} -> {part_id}: NOT FOUND ---")
        continue

    obj, doc_name = entry
    print(f"\n{'='*60}")
    print(f"  Label:    {fc_label}")
    print(f"  Part ID:  {part_id}")
    print(f"  Type:     {obj.TypeId}")
    print(f"  Doc:      {doc_name}")

    try:
        shape = obj.Shape.copy()
    except:
        print(f"  SKIP: no Shape")
        continue

    if shape.isNull():
        print(f"  SKIP: null Shape")
        continue

    # Raw shape bounding box
    raw_bb = shape.BoundBox
    print(f"  Raw Shape BB:       {bb_str(raw_bb)}")

    # Object's own Placement
    pl = obj.Placement
    print(f"  obj.Placement:      {placement_str(pl)}")

    # Global Placement
    try:
        gpl = obj.getGlobalPlacement()
    except:
        gpl = pl
    print(f"  getGlobalPlacement: {placement_str(gpl)}")

    # Are they the same?
    same = (abs(pl.Base.x - gpl.Base.x) < 0.01 and
            abs(pl.Base.y - gpl.Base.y) < 0.01 and
            abs(pl.Base.z - gpl.Base.z) < 0.01)
    print(f"  Placement == Global: {same}")

    # Variant A: raw shape (no transform)
    export_mesh(shape, os.path.join(OUT_A, f"{part_id}.stl"))

    # Variant B: shape transformed by local Placement
    shape_b = shape.transformGeometry(pl.toMatrix())
    bb_b = shape_b.BoundBox
    print(f"  After local Placement BB: {bb_str(bb_b)}")
    export_mesh(shape_b, os.path.join(OUT_B, f"{part_id}.stl"))

    # Variant C: shape transformed by global Placement
    shape_c = shape.transformGeometry(gpl.toMatrix())
    bb_c = shape_c.BoundBox
    print(f"  After global Placement BB: {bb_str(bb_c)}")
    export_mesh(shape_c, os.path.join(OUT_C, f"{part_id}.stl"))

print(f"\n{'='*60}")
print(f"[diag] Done. Check stl_diag_raw/, stl_diag_local_placement/, stl_diag_global_placement/")
print(f"[diag] Convert each with Blender to see which looks correct.")
