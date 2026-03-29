"""
inspect_assemblies.py
Lists all objects in the downloaded extruder FreeCAD assembly files
so we can identify which one contains sensor_holder and 5015_blower positioned.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" inspect_assemblies.py
"""
import FreeCAD
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

files_to_inspect = [
    "Titanaeromarcin.fcstd",
    "base_extruder_plus_fan.fcstd",
    "holder_motor_titan.fcstd",
]

for fname in files_to_inspect:
    fpath = os.path.join(SCRIPT_DIR, fname)
    if not os.path.exists(fpath):
        print(f"\n{'='*60}")
        print(f"SKIP: {fname} not found")
        continue

    print(f"\n{'='*60}")
    print(f"FILE: {fname} ({os.path.getsize(fpath) / 1024:.1f} KB)")
    print(f"{'='*60}")

    doc = FreeCAD.openDocument(fpath)

    for obj in doc.Objects:
        try:
            shape = obj.Shape
            bb = shape.BoundBox
            vol = (bb.XMax - bb.XMin) * (bb.YMax - bb.YMin) * (bb.ZMax - bb.ZMin)
            dims = f"  BB: ({bb.XMin:.1f},{bb.YMin:.1f},{bb.ZMin:.1f}) to ({bb.XMax:.1f},{bb.YMax:.1f},{bb.ZMax:.1f}) mm"
            center = f"  Center: ({(bb.XMin+bb.XMax)/2:.1f}, {(bb.YMin+bb.YMax)/2:.1f}, {(bb.ZMin+bb.ZMax)/2:.1f}) mm"
        except:
            dims = "  (no shape)"
            center = ""
            vol = 0

        try:
            gpl = obj.getGlobalPlacement()
            pos = gpl.Base
            placement_info = f"  GlobalPlacement: ({pos.x:.1f}, {pos.y:.1f}, {pos.z:.1f}) mm"
        except:
            placement_info = ""

        print(f"\n  [{obj.TypeId}] Label: '{obj.Label}' Name: '{obj.Name}'")
        print(dims)
        if center:
            print(center)
        if placement_info:
            print(placement_info)

    FreeCAD.closeDocument(doc.Name)

print(f"\n{'='*60}")
print("Done. Look for objects matching sensor_holder, 5015_blower, blower, sensor, fan, etc.")
