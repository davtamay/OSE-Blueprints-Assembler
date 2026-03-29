"""
extract_colors.py
Extracts ShapeColor from FreeCAD objects where available,
writes a JSON mapping part_name -> {r, g, b}.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" extract_colors.py
"""
import FreeCAD
import FreeCADGui
import json
import os
import re

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
FCSTD = os.path.join(SCRIPT_DIR, "D3Dfinalassemblyv1902.fcstd")
LABEL_MAP = os.path.join(SCRIPT_DIR, "label_map.json")
OUT_JSON = os.path.join(SCRIPT_DIR, "part_colors.json")

with open(LABEL_MAP) as f:
    label_map = json.load(f)
label_map = {k: v for k, v in label_map.items() if k != "_notes"}

SKIP_TYPES = {"App::DocumentObjectGroup"}

def sanitize_filename(label):
    s = label.strip().lower()
    s = re.sub(r'[^a-z0-9]+', '_', s)
    return s.strip('_')

# Try to init GUI for ViewObject access
try:
    FreeCADGui.showMainWindow()
    has_gui = True
    print("[colors] GUI initialized — ViewObject colors available")
except:
    has_gui = False
    print("[colors] No GUI — will use default colors")

doc = FreeCAD.openDocument(FCSTD)

colors = {}
seen_names = set()

for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        if obj.TypeId in SKIP_TYPES:
            continue

        try:
            shape = obj.Shape
            if shape.isNull():
                continue
            bb = shape.BoundBox
            vol = (bb.XMax - bb.XMin) * (bb.YMax - bb.YMin) * (bb.ZMax - bb.ZMin)
            if vol < 1.0:
                continue
        except:
            continue

        label = obj.Label.strip()
        name = label_map.get(label, sanitize_filename(label))

        if name in seen_names:
            i = 2
            while f"{name}_{i}" in seen_names:
                i += 1
            name = f"{name}_{i}"
        seen_names.add(name)

        # Try to get color
        color = None
        if has_gui and hasattr(obj, 'ViewObject') and obj.ViewObject:
            try:
                sc = obj.ViewObject.ShapeColor
                color = {"r": round(sc[0], 3), "g": round(sc[1], 3), "b": round(sc[2], 3)}
            except:
                pass

        if not color:
            # Try DiffuseColor on shape faces
            try:
                if hasattr(obj, 'ViewObject') and obj.ViewObject:
                    dc = obj.ViewObject.DiffuseColor
                    if dc:
                        c = dc[0]
                        color = {"r": round(c[0], 3), "g": round(c[1], 3), "b": round(c[2], 3)}
            except:
                pass

        if color:
            colors[name] = color
            print(f"  {name}: ({color['r']}, {color['g']}, {color['b']})")
        else:
            print(f"  {name}: no color available")

with open(OUT_JSON, "w") as f:
    json.dump(colors, f, indent=2)

print(f"\n[colors] {len(colors)} colors written to {OUT_JSON}")
