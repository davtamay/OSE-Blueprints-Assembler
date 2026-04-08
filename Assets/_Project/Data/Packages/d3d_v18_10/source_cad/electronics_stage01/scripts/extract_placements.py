"""
extract_placements.py
FreeCAD macro — run from FreeCADCmd or the FreeCAD Python console.
Inspects each downloaded electronics FCStd file, extracts bounding boxes
and centroid positions, and writes a placements.json summary.

This is an inspection helper, not a global-assembly extractor — each
electronics component lives in its own FCStd file rather than a master
assembly. Positions within the D3D frame must be authored manually in
machine.json from physical reference measurements or the Final Assembly PDF.

Usage (from FreeCADCmd):
    FreeCADCmd.exe extract_placements.py

Or paste into the FreeCAD Python console with the raw/ folder set to RAW_DIR below.
"""

import json
import os

RAW_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "raw")
OUT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "placements.json")

COMPONENTS = [
    ("RAMPS14_v1904.fcstd",         "ramps_14_board"),
    ("Powersupply_v1904.fcstd",     "d3d_psu_atx"),
    ("Smartcontroller_v1904.fcstd", "d3d_smart_controller"),
    ("Controlpanel_v1904.fcstd",    "d3d_control_panel"),
]


def inspect_fcstd(fcstd_path):
    """Return bounding box info for all solid shapes in an FCStd file."""
    import FreeCAD as App
    import Part

    doc = App.openDocument(fcstd_path)
    doc.recompute()

    shapes = []
    combined = None

    for obj in doc.Objects:
        shape = getattr(obj, "Shape", None)
        if shape is None or shape.isNull() or len(shape.Faces) == 0:
            continue
        bb = shape.BoundBox
        if not bb.isValid():
            continue

        # FreeCAD (Z-up, mm) → Unity (Y-up, m) centroid
        cx_u = round(bb.Center.x / 1000, 4)
        cy_u = round(bb.Center.z / 1000, 4)
        cz_u = round(bb.Center.y / 1000, 4)

        shapes.append({
            "label": getattr(obj, "Label", obj.Name),
            "freecad_bb_mm": {
                "xlen": round(bb.XLength, 1),
                "ylen": round(bb.YLength, 1),
                "zlen": round(bb.ZLength, 1),
                "center_x": round(bb.Center.x, 1),
                "center_y": round(bb.Center.y, 1),
                "center_z": round(bb.Center.z, 1),
            },
            "unity_centroid_m": {"x": cx_u, "y": cy_u, "z": cz_u},
        })

        if combined is None:
            combined = bb
        else:
            combined.add(bb)

    App.closeDocument(doc.Name)

    unity_size = None
    if combined and combined.isValid():
        unity_size = {
            "x_m": round(combined.XLength / 1000, 4),
            "y_m": round(combined.ZLength / 1000, 4),   # FreeCAD Z → Unity Y
            "z_m": round(combined.YLength / 1000, 4),   # FreeCAD Y → Unity Z
        }

    return shapes, unity_size


def main():
    results = {
        "_notes": {
            "source": "OSE wiki FCStd files — individual components (not master assembly)",
            "extracted": __import__("datetime").date.today().isoformat(),
            "coordinate_mapping": "FreeCAD (Z-up, mm) → Unity (Y-up, m): X→X, Y→Z, Z→Y, divide by 1000",
            "usage": (
                "centroid positions are relative to each part's own FCStd origin, "
                "not relative to the D3D frame. Author machine.json assembledPosition values "
                "from the Final Assembly PDF slide dimensions or physical measurement."
            ),
        }
    }

    for fcstd_name, part_id in COMPONENTS:
        path = os.path.join(RAW_DIR, fcstd_name)
        if not os.path.exists(path):
            print(f"  [skip] {fcstd_name} not found — run download_ose_electronics.py first")
            results[part_id] = {"error": "file not found"}
            continue

        print(f"  Inspecting {fcstd_name}...")
        try:
            shapes, unity_size = inspect_fcstd(path)
            results[part_id] = {
                "source_file": fcstd_name,
                "shape_count": len(shapes),
                "unity_bounding_size_m": unity_size,
                "shapes": shapes,
            }
            if unity_size:
                print(f"    → {unity_size['x_m']}m × {unity_size['y_m']}m × {unity_size['z_m']}m (Unity)")
        except Exception as exc:
            print(f"  [FAIL] {fcstd_name}: {exc}")
            results[part_id] = {"error": str(exc)}

    with open(OUT_PATH, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    print(f"\nWritten: {OUT_PATH}")


if __name__ == "__main__":
    main()
