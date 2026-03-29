"""
export_all_centered.py
Exports ALL FreeCAD objects as centered STLs and computes M-mapped Unity positions.

Same pipeline as export_centered_parts.py but operates on EVERY mesh object,
not just the 9 in label_map.json. Uses label_map for known mappings,
sanitized filenames for the rest.

Outputs:
  stl_all_centered/   — centered STL per object
  all_centered_transforms.json — M-mapped Unity positions for every object

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" export_all_centered.py
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
OUT_DIR = os.path.join(SCRIPT_DIR, "stl_all_centered")
OUT_JSON = os.path.join(SCRIPT_DIR, "all_centered_transforms.json")

FRAME_OUTER_M = 0.3048
WORKTABLE_Y = 0.552

os.makedirs(OUT_DIR, exist_ok=True)

# Load label map for known mappings
with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

# Skip group/compound containers
SKIP_TYPES = {"App::DocumentObjectGroup"}

def sanitize_filename(label):
    s = label.strip().lower()
    s = re.sub(r'[^a-z0-9]+', '_', s)
    return s.strip('_')

print(f"[export-all] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)

transforms = {}
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

        # Apply global placement so BB is in assembly coordinates
        # (same as export_global_assembly.py — some objects like cable_chain
        # have a Placement that isn't baked into Shape)
        try:
            gpl = obj.getGlobalPlacement()
            shape = shape.transformGeometry(gpl.toMatrix())
        except:
            pass  # If no placement available, shape is already global

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

        print(f"\n--- {label} -> {name} ---")

        # Bounding box center in global coords
        cx = (bb.XMin + bb.XMax) / 2.0
        cy = (bb.YMin + bb.YMax) / 2.0
        cz = (bb.ZMin + bb.ZMax) / 2.0

        print(f"  Global BB: ({bb.XMin:.1f},{bb.YMin:.1f},{bb.ZMin:.1f}) to ({bb.XMax:.1f},{bb.YMax:.1f},{bb.ZMax:.1f})")
        print(f"  Global center: ({cx:.2f}, {cy:.2f}, {cz:.2f}) mm")

        # Center the shape
        center_vec = FreeCAD.Vector(-cx, -cy, -cz)
        centered_shape = shape.translated(center_vec)

        # M-mapped Unity position: FreeCAD (fx,fy,fz) -> Unity (-fx, fz, -fy)/1000 + offsets
        unity_x = round(FRAME_OUTER_M / 2 - cx / 1000.0, 4)
        unity_y = round(cz / 1000.0 + WORKTABLE_Y, 4)
        unity_z = round(FRAME_OUTER_M / 2 - cy / 1000.0, 4)

        print(f"  Unity pos: ({unity_x}, {unity_y}, {unity_z})")

        transforms[name] = {
            "playPosition": {"x": unity_x, "y": unity_y, "z": unity_z},
            "playRotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "fc_label": label,
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

        out_path = os.path.join(OUT_DIR, f"{name}.stl")
        mesh.write(out_path)
        print(f"  Exported: {out_path} ({mesh.CountFacets} tris)")
        exported += 1

# Write transforms JSON
with open(OUT_JSON, "w") as f:
    json.dump(transforms, f, indent=2)

print(f"\n{'='*60}")
print(f"[export-all] {exported} centered STLs exported to {OUT_DIR}")
print(f"[export-all] Transforms written to {OUT_JSON}")
