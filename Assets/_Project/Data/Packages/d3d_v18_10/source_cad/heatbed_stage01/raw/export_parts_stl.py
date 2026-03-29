"""
export_parts_stl.py
Exports individual parts from the D3D assembly as STL files centered at local origin.

For each mapped part (label_map.json):
  1. Find the object in the FreeCAD assembly
  2. Get its Shape (compound geometry)
  3. Strip global placement (move to origin)
  4. Tessellate and export as STL (FreeCAD Z-up, mm)

The resulting STL files are in FreeCAD's coordinate system (Z-up, millimeters).
Blender will handle Z-up -> Y-up conversion and mm -> m scaling during GLB export.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" export_parts_stl.py
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
OUT_DIR = os.path.join(SCRIPT_DIR, "stl_exports")

os.makedirs(OUT_DIR, exist_ok=True)

# Load label map (FreeCAD label -> machine.json partId)
with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

print(f"[export] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)
print(f"[export] Loaded documents: {list(FreeCAD.listDocuments().keys())}")
print(f"[export] Parts to export: {len(label_map)}")

# Build object lookup across all loaded documents
all_objects = {}
for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        key = obj.Label
        if key not in all_objects:
            all_objects[key] = (obj, doc_name)

exported = 0
skipped = []

for fc_label, part_id in label_map.items():
    print(f"\n--- {fc_label} -> {part_id} ---")

    entry = all_objects.get(fc_label)
    if entry is None:
        print(f"  SKIP: '{fc_label}' not found in any document")
        skipped.append(fc_label)
        continue

    obj, doc_name = entry
    print(f"  Found in doc '{doc_name}', TypeId={obj.TypeId}")

    # Get the shape
    try:
        shape = obj.Shape.copy()
    except Exception as e:
        print(f"  SKIP: no Shape property: {e}")
        skipped.append(fc_label)
        continue

    if shape.isNull():
        print(f"  SKIP: Shape is null")
        skipped.append(fc_label)
        continue

    # Strip global placement - move shape to origin
    try:
        gpl = obj.getGlobalPlacement()
        inv_matrix = gpl.inverse().toMatrix()
        shape = shape.transformGeometry(inv_matrix)
        print(f"  Stripped global placement: pos=({gpl.Base.x:.1f}, {gpl.Base.y:.1f}, {gpl.Base.z:.1f})")
    except Exception as e:
        print(f"  WARNING: Could not strip global placement: {e}")
        try:
            pl = obj.Placement
            if pl.Base.Length > 0.01:
                inv_matrix = pl.inverse().toMatrix()
                shape = shape.transformGeometry(inv_matrix)
                print(f"  Stripped local placement instead")
        except:
            print(f"  Using shape as-is (already at origin)")

    # Report bounding box at origin
    bb = shape.BoundBox
    print(f"  BB at origin: ({bb.XMin:.1f}, {bb.YMin:.1f}, {bb.ZMin:.1f}) to ({bb.XMax:.1f}, {bb.YMax:.1f}, {bb.ZMax:.1f})")
    print(f"  Size: {bb.XLength:.1f} x {bb.YLength:.1f} x {bb.ZLength:.1f} mm")

    # Tessellate with good quality
    try:
        mesh = MeshPart.meshFromShape(
            Shape=shape,
            LinearDeflection=0.05,   # 0.05mm linear tolerance
            AngularDeflection=0.1745  # ~10 degrees angular tolerance
        )
    except Exception as e:
        print(f"  WARNING: MeshPart failed ({e}), trying shape.tessellate()")
        try:
            verts, faces = shape.tessellate(0.1)
            mesh = Mesh.Mesh()
            for tri in faces:
                mesh.addFacet(
                    verts[tri[0]].x, verts[tri[0]].y, verts[tri[0]].z,
                    verts[tri[1]].x, verts[tri[1]].y, verts[tri[1]].z,
                    verts[tri[2]].x, verts[tri[2]].y, verts[tri[2]].z,
                )
        except Exception as e2:
            print(f"  SKIP: tessellation failed: {e2}")
            skipped.append(fc_label)
            continue

    out_path = os.path.join(OUT_DIR, f"{part_id}.stl")
    mesh.write(out_path)
    print(f"  Exported: {out_path}")
    print(f"  Mesh: {mesh.CountPoints} vertices, {mesh.CountFacets} triangles")
    exported += 1

print(f"\n{'='*60}")
print(f"[export] Done. {exported}/{len(label_map)} parts exported to {OUT_DIR}")
if skipped:
    print(f"[export] Skipped {len(skipped)}: {skipped}")
