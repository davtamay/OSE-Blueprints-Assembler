"""
export_global_assembly.py
Exports ALL mapped parts as STLs in their GLOBAL FreeCAD positions (with
placement applied). No centering — each STL contains the part exactly where
FreeCAD places it in the assembly.

This lets us drop every STL into Blender, scale to meters, and export a single
GLB to visually verify the full assembly pipeline before any Unity rotation math.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" export_global_assembly.py
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
OUT_DIR = os.path.join(SCRIPT_DIR, "stl_global")

os.makedirs(OUT_DIR, exist_ok=True)

with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

print(f"[global-export] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)
print(f"[global-export] Loaded docs: {list(FreeCAD.listDocuments().keys())}")

# Build object lookup
all_objects = {}
for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        if obj.Label not in all_objects:
            all_objects[obj.Label] = (obj, doc_name)

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

    # Apply GLOBAL placement so the STL is in assembly coordinates
    try:
        gpl = obj.getGlobalPlacement()
    except:
        gpl = obj.Placement

    global_shape = shape.transformGeometry(gpl.toMatrix())

    bb = global_shape.BoundBox
    print(f"  Global BB: ({bb.XMin:.1f},{bb.YMin:.1f},{bb.ZMin:.1f}) to ({bb.XMax:.1f},{bb.YMax:.1f},{bb.ZMax:.1f}) mm")

    # Tessellate and export
    try:
        mesh = MeshPart.meshFromShape(
            Shape=global_shape,
            LinearDeflection=0.05,
            AngularDeflection=0.1745
        )
    except:
        try:
            verts, faces = global_shape.tessellate(0.1)
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

print(f"\n{'='*60}")
print(f"[global-export] {exported}/{len(label_map)} STLs exported to {OUT_DIR}")
print(f"[global-export] All STLs are in FreeCAD global coordinates (Z-up, mm).")
print(f"[global-export] Next: run convert_global_assembly_to_glb.py in Blender.")
