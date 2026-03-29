"""
list_all_objects.py
Lists ALL objects with shapes in the FreeCAD assembly, showing their
label, type, bounding box, and placement — so we can pick more parts
to add to label_map.json.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" list_all_objects.py
"""
import FreeCAD
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
FCSTD = os.path.join(SCRIPT_DIR, "D3Dfinalassemblyv1902.fcstd")

doc = FreeCAD.openDocument(FCSTD)

print(f"\n{'='*80}")
print(f"ALL MESH OBJECTS IN {os.path.basename(FCSTD)}")
print(f"{'='*80}")

count = 0
for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        try:
            shape = obj.Shape
            if shape.isNull():
                continue
        except:
            continue

        bb = shape.BoundBox
        # Skip tiny/degenerate shapes
        vol = (bb.XMax - bb.XMin) * (bb.YMax - bb.YMin) * (bb.ZMax - bb.ZMin)
        if vol < 1.0:  # less than 1 mm^3
            continue

        count += 1
        dims = f"{bb.XMax-bb.XMin:.0f}x{bb.YMax-bb.YMin:.0f}x{bb.ZMax-bb.ZMin:.0f}"
        center = f"({(bb.XMin+bb.XMax)/2:.0f},{(bb.YMin+bb.YMax)/2:.0f},{(bb.ZMin+bb.ZMax)/2:.0f})"
        print(f"  {count:3d}. [{obj.TypeId:30s}] Label: \"{obj.Label:40s}\" dims: {dims:20s} center: {center}")

print(f"\n{'='*80}")
print(f"Total: {count} objects with non-trivial shapes")
