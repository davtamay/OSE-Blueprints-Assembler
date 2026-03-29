import FreeCAD
import json
import os

fcstd_path = os.path.join(os.path.dirname(__file__), "D3Dfinalassemblyv1902.fcstd")
out_path   = os.path.join(os.path.dirname(__file__), "placements.json")

print(f"[extract] Opening: {fcstd_path}")
doc = FreeCAD.openDocument(fcstd_path)

out = {}

for obj in doc.Objects:
    if not obj.isDerivedFrom("Part::Feature"):
        continue
    try:
        pl = obj.getGlobalPlacement()
    except Exception as e:
        print(f"[extract] SKIP {obj.Label}: {e}")
        continue

    b = pl.Base
    q = pl.Rotation.Q  # (x, y, z, w)

    # FreeCAD (Z-up, right-handed, mm) → Unity (Y-up, left-handed, m)
    out[obj.Label] = {
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

print(f"[extract] Done. {len(out)} parts written to {out_path}")
for label in sorted(out.keys()):
    p = out[label]["position"]
    print(f"  {label:40s}  pos=({p['x']:.4f}, {p['y']:.4f}, {p['z']:.4f})")
