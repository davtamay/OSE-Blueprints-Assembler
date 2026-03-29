"""
export_full_assembly_raw.py
Exports ALL objects with shapes as STLs using raw obj.Shape (already in
global assembly coordinates). No placement transform, no centering.

This produces a complete assembly reference with ~60+ parts.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" export_full_assembly_raw.py
"""
import FreeCAD
import Mesh
import MeshPart
import json
import os
import re

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
FCSTD = os.path.join(SCRIPT_DIR, "D3Dfinalassemblyv1902.fcstd")
LABEL_MAP = os.path.join(SCRIPT_DIR, "label_map.json")
OUT_DIR = os.path.join(SCRIPT_DIR, "stl_full_raw")

os.makedirs(OUT_DIR, exist_ok=True)

# Load label map for known mappings
with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

# Skip group/compound containers that just duplicate their children
SKIP_TYPES = {
    "App::DocumentObjectGroup",
}

def sanitize_filename(label):
    """Convert FreeCAD label to safe filename."""
    s = label.strip().lower()
    s = re.sub(r'[^a-z0-9]+', '_', s)
    s = s.strip('_')
    return s

print(f"[full-export] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)

exported = 0
seen_names = set()

for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        if obj.TypeId in SKIP_TYPES:
            continue

        try:
            shape = obj.Shape.copy()
        except:
            continue

        if shape.isNull():
            continue

        bb = shape.BoundBox
        vol = (bb.XMax - bb.XMin) * (bb.YMax - bb.YMin) * (bb.ZMax - bb.ZMin)
        if vol < 1.0:
            continue

        # Use mapped name if available, otherwise sanitize the label
        label = obj.Label.strip()
        if label in label_map:
            name = label_map[label]
        else:
            name = sanitize_filename(label)

        # Deduplicate names
        if name in seen_names:
            i = 2
            while f"{name}_{i}" in seen_names:
                i += 1
            name = f"{name}_{i}"
        seen_names.add(name)

        # Tessellate and export
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
                print(f"  SKIP {label}: tessellation failed: {e}")
                continue

        out_path = os.path.join(OUT_DIR, f"{name}.stl")
        mesh.write(out_path)
        exported += 1
        dims = f"{bb.XMax-bb.XMin:.0f}x{bb.YMax-bb.YMin:.0f}x{bb.ZMax-bb.ZMin:.0f}mm"
        print(f"  {exported:3d}. {name:45s} {dims}")

print(f"\n{'='*60}")
print(f"[full-export] {exported} STLs exported to {OUT_DIR}")
