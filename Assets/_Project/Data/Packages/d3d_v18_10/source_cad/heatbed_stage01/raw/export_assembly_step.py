"""
export_assembly_step.py
Exports the full D3D assembly as a single STEP file, preserving all part positions.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" export_assembly_step.py
"""
import FreeCAD
import Part
import Import
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
FCSTD = os.path.join(SCRIPT_DIR, "D3Dfinalassemblyv1902.fcstd")
OUT_STEP = os.path.join(SCRIPT_DIR, "D3D_full_assembly.step")

print(f"[export] Opening: {FCSTD}")
doc = FreeCAD.openDocument(FCSTD)
print(f"[export] Documents loaded: {list(FreeCAD.listDocuments().keys())}")

# Collect all Part::Feature-derived objects with shapes
export_objects = []
for doc_name, d in FreeCAD.listDocuments().items():
    for obj in d.Objects:
        if obj.isDerivedFrom("Part::Feature") and hasattr(obj, 'Shape') and not obj.Shape.isNull():
            export_objects.append(obj)

print(f"[export] Found {len(export_objects)} shape objects")

# Export as STEP
Import.export(export_objects, OUT_STEP)
file_size = os.path.getsize(OUT_STEP)
print(f"[export] Written: {OUT_STEP} ({file_size / 1024 / 1024:.1f} MB)")
print(f"[export] Done.")
