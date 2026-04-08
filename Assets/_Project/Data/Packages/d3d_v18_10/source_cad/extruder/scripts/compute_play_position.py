"""
compute_assembled_position.py
Converts a FreeCAD D3D assembly-space anchor (mm) into a Unity assembledPosition (m).

The D3D assembly reference frame (calibrated from D3Dfinalassemblyv1902.fcstd):
  - Frame placed at FreeCAD origin (0, 0, 0).  Bbox 0-304.8 mm (12-inch cube).
  - Frame center in FreeCAD: X=152.4, Y=152.4 mm.
  - In Unity the frame is centered at world (0, 0, 0); frame base sits at Y=0.5532 m.

Transform:
    unity.x = (fc_x_mm - FRAME_CENTER_MM) / 1000
    unity.y =  fc_z_mm                    / 1000 + FLOOR_Y_UNITY
    unity.z = (fc_y_mm - FRAME_CENTER_MM) / 1000

Anchor rules per center_mode:
    base_center  ->  anchor = bottom-center of the part's assembled bounding box
                     i.e. fc_z = lowest Z face of the part in the assembly
    center       ->  anchor = geometric centroid of the part in the assembly

Usage examples
--------------
  # Compute from a blender report + known assembly anchor:
  python3 compute_assembled_position.py \\
      --part-id d3d_control_panel \\
      --blender-report ../electronics_stage01/exported/reports/d3d_control_panel_blender.json \\
      --assembly-x 382.8 --assembly-y 152.4 --assembly-z 0.0

  # Add to an existing placements.json:
  python3 compute_assembled_position.py ... --output ../electronics_stage01/placements.json

  # Batch mode from a JSON components file:
  python3 compute_assembled_position.py --batch components.json --output placements.json

  # Verify against a known part (motor001, assembly center 147.1, -68.1, 310.6):
  python3 compute_assembled_position.py \\
      --part-id motor001 \\
      --assembly-x 147.1 --assembly-y -68.1 --assembly-z 310.6 \\
      --center-mode center
"""

import argparse
import json
import os
import sys

# ── Calibration constants (derived from D3Dfinalassemblyv1902.fcstd + machine.json) ──
FRAME_CENTER_MM  = 152.4   # Half of 304.8 mm (12-inch frame). FC origin at frame corner.
FLOOR_Y_UNITY    = 0.5532  # Unity Y where FreeCAD Z=0 sits (frame bar bottom face).


def fc_to_unity(fc_x_mm: float, fc_y_mm: float, fc_z_mm: float,
                frame_center_mm: float = None, floor_y_unity: float = None) -> dict:
    """Convert a FreeCAD assembly-space point (mm) to Unity world-space (m)."""
    cx = frame_center_mm if frame_center_mm is not None else FRAME_CENTER_MM
    fy = floor_y_unity   if floor_y_unity   is not None else FLOOR_Y_UNITY
    return {
        "x": round((fc_x_mm - cx) / 1000, 4),
        "y": round(fc_z_mm / 1000 + fy, 4),
        "z": round((fc_y_mm - cx) / 1000, 4),
    }


def read_blender_report(path: str) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def compute_start_position(play_pos: dict, start_offset_m: float) -> dict:
    """Place the spawn point to the right (+X) of the assembly."""
    return {
        "x": round(play_pos["x"] + start_offset_m, 4),
        "y": play_pos["y"],
        "z": play_pos["z"],
    }


def compute_single(
    part_id: str,
    assembly_x: float,
    assembly_y: float,
    assembly_z: float,
    center_mode: str = None,
    blender_report: dict = None,
    start_offset_m: float = 1.5,
) -> dict:
    """
    Compute assembledPosition for one part.

    If blender_report is provided, center_mode is read from it.
    The anchor (assembly_x/y/z) should be:
      - base_center: bottom-center of the part bbox in assembly space (fc_z = bottom face Z)
      - center:      geometric centroid of the part in assembly space
    """
    if blender_report and center_mode is None:
        center_mode = blender_report.get("center_mode", "center")

    center_mode = center_mode or "center"

    # The anchor passed in is already the right point (caller is responsible for this).
    # For base_center the anchor IS the part's bottom-center in assembly space.
    # For center the anchor IS the part's centroid in assembly space.
    play_pos = fc_to_unity(assembly_x, assembly_y, assembly_z)
    start_pos = compute_start_position(play_pos, start_offset_m)

    result = {
        "partId": part_id,
        "center_mode": center_mode,
        "assembly_anchor_mm": {"x": assembly_x, "y": assembly_y, "z": assembly_z},
        "assembledPosition":  play_pos,
        "assembledRotation":  {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
        "startPosition": start_pos,
    }

    if blender_report:
        bb = blender_report.get("bound_box", {})
        result["glb_size_m"] = {
            "x": round(bb.get("xlen_m", 0), 4),
            "y": round(bb.get("ylen_m", 0), 4),
            "z": round(bb.get("zlen_m", 0), 4),
        }

    return result


def merge_into_placements_file(output_path: str, new_entry: dict) -> None:
    """Append or replace an entry in a placements.json array file."""
    if os.path.exists(output_path):
        with open(output_path, encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, list):
            data = [data]
        # Replace existing entry for same partId
        data = [e for e in data if e.get("partId") != new_entry["partId"]]
    else:
        data = []

    data.append(new_entry)
    data.sort(key=lambda e: e.get("partId", ""))
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def process_batch(batch_path: str, output_path: str, start_offset_m: float) -> None:
    """
    Batch mode: read a JSON array of component descriptors.

    Each entry:
    {
        "part_id": "d3d_control_panel",
        "blender_report": "path/to/_blender.json",   // optional
        "center_mode": "base_center",                 // required if no blender_report
        "assembly_x": 382.8,
        "assembly_y": 152.4,
        "assembly_z": 0.0
    }
    """
    with open(batch_path, encoding="utf-8") as f:
        components = json.load(f)

    results = []
    for c in components:
        report = None
        if c.get("blender_report"):
            rp = c["blender_report"]
            if not os.path.isabs(rp):
                rp = os.path.join(os.path.dirname(batch_path), rp)
            if os.path.exists(rp):
                report = read_blender_report(rp)
            else:
                print(f"  WARNING: blender_report not found: {rp}", file=sys.stderr)

        entry = compute_single(
            part_id=c["part_id"],
            assembly_x=c["assembly_x"],
            assembly_y=c["assembly_y"],
            assembly_z=c["assembly_z"],
            center_mode=c.get("center_mode"),
            blender_report=report,
            start_offset_m=start_offset_m,
        )
        results.append(entry)
        print(f"  {c['part_id']:35s} playPos=({entry['assembledPosition']['x']:.4f}, {entry['assembledPosition']['y']:.4f}, {entry['assembledPosition']['z']:.4f})")

    if output_path:
        # For batch we replace the whole file
        results.sort(key=lambda e: e.get("partId", ""))
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(results, f, indent=2)
        print(f"\nWritten {len(results)} entries to {output_path}")
    else:
        print(json.dumps(results, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Convert FreeCAD assembly-space anchor to Unity assembledPosition.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )

    # Single-part mode
    parser.add_argument("--part-id", help="Part ID (e.g. d3d_control_panel)")
    parser.add_argument("--blender-report", help="Path to _blender.json report from stl_to_glb.py")
    parser.add_argument("--assembly-x", type=float, help="Part anchor X in FreeCAD assembly space (mm)")
    parser.add_argument("--assembly-y", type=float, help="Part anchor Y in FreeCAD assembly space (mm)")
    parser.add_argument("--assembly-z", type=float, help="Part anchor Z in FreeCAD assembly space (mm)")
    parser.add_argument("--center-mode", choices=["center", "base_center"],
                        help="Override center_mode (default: read from blender report)")

    # Batch mode
    parser.add_argument("--batch", help="JSON batch file with array of component descriptors")

    # Output
    parser.add_argument("--output", help="Path to placements.json to write/update")
    parser.add_argument("--start-offset", type=float, default=1.5,
                        help="X offset (m) for startPosition relative to assembledPosition (default: 1.5)")

    # Calibration override
    parser.add_argument("--frame-center-mm", type=float, default=FRAME_CENTER_MM,
                        help=f"FreeCAD frame center in mm (default: {FRAME_CENTER_MM})")
    parser.add_argument("--floor-y-unity", type=float, default=FLOOR_Y_UNITY,
                        help=f"Unity Y at FreeCAD Z=0 (default: {FLOOR_Y_UNITY})")

    args = parser.parse_args()

    if args.batch:
        print(f"Batch processing: {args.batch}")
        process_batch(args.batch, args.output, args.start_offset)
        return

    # Single-part mode
    if not args.part_id:
        parser.error("--part-id is required (or use --batch)")
    if args.assembly_x is None or args.assembly_y is None or args.assembly_z is None:
        parser.error("--assembly-x, --assembly-y, --assembly-z are all required")

    report = None
    if args.blender_report:
        if not os.path.exists(args.blender_report):
            print(f"ERROR: blender report not found: {args.blender_report}", file=sys.stderr)
            sys.exit(1)
        report = read_blender_report(args.blender_report)

    entry = compute_single(
        part_id=args.part_id,
        assembly_x=args.assembly_x,
        assembly_y=args.assembly_y,
        assembly_z=args.assembly_z,
        center_mode=args.center_mode,
        blender_report=report,
        start_offset_m=args.start_offset,
    )

    print(json.dumps(entry, indent=2))

    if args.output:
        merge_into_placements_file(args.output, entry)
        print(f"\nWritten/updated: {args.output}")


if __name__ == "__main__":
    main()
