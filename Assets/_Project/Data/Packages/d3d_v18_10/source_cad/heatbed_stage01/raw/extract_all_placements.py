"""
extract_all_placements.py
Extracts global placements for ALL Part::Feature objects across all documents
loaded by the main assembly (including Frame sub-assembly bars).

Run from FreeCAD headless:
  "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" extract_all_placements.py

Output: all_placements.json  (FreeCAD -> Unity coordinate conversion applied)
"""

import FreeCAD
import json
import os

SKIP_TYPES = {"Sketcher::SketchObject", "PartDesign::Body", "App::DocumentObjectGroup"}
SKIP_LABEL_PREFIXES = ("Sketch", "Pocket", "Pad", "Body", "Fillet", "Chamfer", "Extrude", "Boolean")

fcstd_path = os.path.join(os.path.dirname(__file__), "D3Dfinalassemblyv1902.fcstd")
out_path   = os.path.join(os.path.dirname(__file__), "all_placements.json")

print(f"[extract] Opening: {fcstd_path}")
doc = FreeCAD.openDocument(fcstd_path)
print(f"[extract] Loaded documents: {list(FreeCAD.listDocuments().keys())}")

out = {}
skipped = []

for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        # Skip non-geometry types
        if obj.TypeId in SKIP_TYPES:
            continue
        if not obj.isDerivedFrom("Part::Feature"):
            continue
        # Skip construction helpers by label prefix
        label = obj.Label
        if any(label.startswith(p) for p in SKIP_LABEL_PREFIXES):
            continue

        try:
            pl = obj.getGlobalPlacement()
        except Exception as e:
            skipped.append(f"{label} ({doc_name}): {e}")
            continue

        b = pl.Base
        q = pl.Rotation.Q  # (x, y, z, w)

        # FreeCAD (Z-up, right-handed, mm) -> Unity (Y-up, left-handed, m)
        key = f"{doc_name}::{label}" if label in out else label
        out[key] = {
            "doc": doc_name,
            "position": {
                "x":  round(b.x / 1000, 4),
                "y":  round(b.z / 1000, 4),
                "z":  round(b.y / 1000, 4)
            },
            "rotation": {
                "x": round(-q[0], 6),
                "y": round(-q[2], 6),
                "z": round(-q[1], 6),
                "w": round( q[3], 6)
            }
        }

with open(out_path, "w") as f:
    json.dump(out, f, indent=2)

print(f"\n[extract] Done. {len(out)} parts written to {out_path}")
if skipped:
    print(f"[extract] Skipped {len(skipped)} objects:")
    for s in skipped:
        print(f"  SKIP: {s}")

# Print all extracted labels grouped by doc
by_doc = {}
for label, data in sorted(out.items()):
    d = data["doc"]
    by_doc.setdefault(d, []).append(label)

for d, labels in sorted(by_doc.items()):
    print(f"\n  [{d}] {len(labels)} objects:")
    for label in labels:
        p = out[label]["position"]
        print(f"    {label:50s}  pos=({p['x']:.4f}, {p['y']:.4f}, {p['z']:.4f})")
