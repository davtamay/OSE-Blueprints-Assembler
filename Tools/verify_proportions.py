#!/usr/bin/env python3
"""
Proportion verification for AI-generated 3D models.

Compares a GLB model's bounding-box aspect ratio against the real-world
dimensions from PartDimensionCatalog.cs.

Usage:
    python verify_proportions.py <part_id> <path_to_glb>
    python verify_proportions.py engine ./generated_models/latest/base_basic_pbr.glb

Exit codes:
    0 — proportions within tolerance (< 10% deviation)
    1 — proportions deviate (10-20%), warning
    2 — proportions severely off (> 20%), regeneration recommended
    3 — error (missing file, missing catalog entry, etc.)
"""

import json
import pathlib
import re
import struct
import sys
from math import inf

# ── Catalog: copied from PartDimensionCatalog.cs ──
# Values are (W, H, D) in meters
CATALOG = {
    "base_tube_long_1":   (1.22,  0.05, 0.05),
    "base_tube_long_2":   (1.22,  0.05, 0.05),
    "base_tube_short_1":  (0.05,  0.05, 0.61),
    "base_tube_short_2":  (0.05,  0.05, 0.61),
    "vertical_post":      (0.05,  0.61, 0.05),
    "engine_mount_plate": (0.30,  0.006, 0.30),
    "engine":             (0.45,  0.35, 0.40),
    "hydraulic_pump":     (0.20,  0.15, 0.15),
    "pump_coupling":      (0.10,  0.08, 0.08),
    "reservoir":          (0.30,  0.25, 0.20),
    "pressure_hose":      (0.03,  0.03, 0.90),
    "return_hose":        (0.03,  0.03, 0.90),
    "oil_cooler":         (0.25,  0.20, 0.05),
    "pressure_gauge":     (0.06,  0.06, 0.04),
    "fuel_tank":          (0.30,  0.25, 0.20),
    "fuel_line":          (0.01,  0.01, 0.60),
    "fuel_shutoff_valve": (0.04,  0.03, 0.03),
    "battery":            (0.21,  0.18, 0.17),
    "battery_cables":     (0.01,  0.01, 0.45),
    "key_switch":         (0.04,  0.05, 0.04),
    "starter_wiring":     (0.005, 0.005, 0.50),
    "choke_cable":        (0.005, 0.005, 0.40),
    "throttle_cable":     (0.005, 0.005, 0.40),
    "tool_tape_measure":  (0.08,  0.08, 0.04),
    "tool_framing_square":(0.40,  0.30, 0.005),
    "tool_clamp":         (0.25,  0.10, 0.05),
    "tool_welder":        (0.40,  0.30, 0.25),
    "tool_angle_grinder": (0.30,  0.10, 0.10),
    "tool_torque_wrench": (0.45,  0.05, 0.05),
    "tool_socket_set":    (0.30,  0.08, 0.15),
    "tool_line_wrench":   (0.20,  0.03, 0.02),
    "tool_wire_crimper":  (0.22,  0.06, 0.02),
    "tool_multimeter":    (0.08,  0.15, 0.04),
}


def glb_bounds(path: str) -> tuple[float, float, float]:
    """Read a GLB and return its bounding-box size (W, H, D)."""
    data = pathlib.Path(path).read_bytes()
    if data[:4] != b"glTF":
        raise ValueError(f"Not a valid GLB file: {path}")

    off = 12
    json_chunk = None
    while off < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, off)
        off += 8
        chunk = data[off : off + chunk_len]
        off += chunk_len
        if chunk_type == 0x4E4F534A:  # JSON
            json_chunk = json.loads(chunk.decode("utf-8"))
            break

    if json_chunk is None:
        raise ValueError(f"No JSON chunk found in GLB: {path}")

    accessors = json_chunk.get("accessors", [])
    meshes = json_chunk.get("meshes", [])
    mins = [inf, inf, inf]
    maxs = [-inf, -inf, -inf]

    for mesh in meshes:
        for prim in mesh.get("primitives", []):
            attrs = prim.get("attributes", {})
            if "POSITION" not in attrs:
                continue
            acc = accessors[attrs["POSITION"]]
            mn = acc.get("min")
            mx = acc.get("max")
            if mn is None or mx is None:
                continue
            for i in range(3):
                mins[i] = min(mins[i], float(mn[i]))
                maxs[i] = max(maxs[i], float(mx[i]))

    size = tuple(maxs[i] - mins[i] for i in range(3))
    if any(s <= 0 for s in size):
        raise ValueError(f"Could not extract valid bounds from: {path}")
    return size


def to_ratio(w: float, h: float, d: float) -> tuple[float, float, float]:
    """Normalize dimensions to a ratio where the smallest axis = 1.0."""
    smallest = min(w, h, d)
    if smallest < 1e-6:
        smallest = max(w, h, d) or 1.0
    return (w / smallest, h / smallest, d / smallest)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(3)

    part_id = sys.argv[1]
    glb_path = sys.argv[2]

    # Validate inputs
    if not pathlib.Path(glb_path).exists():
        print(f"ERROR: GLB file not found: {glb_path}")
        sys.exit(3)

    catalog_key = part_id.lower()
    catalog_entry = None
    for k, v in CATALOG.items():
        if k.lower() == catalog_key:
            catalog_entry = v
            break

    if catalog_entry is None:
        print(f"ERROR: Part '{part_id}' not found in catalog.")
        print(f"Available parts: {', '.join(sorted(CATALOG.keys()))}")
        sys.exit(3)

    # Read model bounds
    try:
        model_size = glb_bounds(glb_path)
    except Exception as e:
        print(f"ERROR: {e}")
        sys.exit(3)

    cat_w, cat_h, cat_d = catalog_entry
    mod_w, mod_h, mod_d = model_size

    cat_ratio = to_ratio(cat_w, cat_h, cat_d)
    mod_ratio = to_ratio(mod_w, mod_h, mod_d)

    # Compute per-axis deviation of ratios
    deviations = []
    for axis_name, cr, mr in zip(("W", "H", "D"), cat_ratio, mod_ratio):
        if cr < 1e-6:
            dev = 0.0
        else:
            dev = (mr - cr) / cr * 100.0
        deviations.append((axis_name, dev))

    max_dev = max(abs(d) for _, d in deviations)

    # Output
    print(f"Part: {part_id}")
    print(f"  Catalog (meters):  W={cat_w:.3f}  H={cat_h:.3f}  D={cat_d:.3f}")
    print(f"  Catalog ratio:     {cat_ratio[0]:.2f} : {cat_ratio[1]:.2f} : {cat_ratio[2]:.2f}")
    print()
    print(f"  Model bounds:      W={mod_w:.3f}  H={mod_h:.3f}  D={mod_d:.3f}")
    print(f"  Model ratio:       {mod_ratio[0]:.2f} : {mod_ratio[1]:.2f} : {mod_ratio[2]:.2f}")
    print()
    dev_str = "  ".join(f"{name}={dev:+.1f}%" for name, dev in deviations)
    print(f"  Axis deviation:    {dev_str}")
    print(f"  Max deviation:     {max_dev:.1f}%")
    print()

    if max_dev <= 10:
        print("  OK: Proportions are within acceptable range (< 10% deviation).")
        sys.exit(0)
    elif max_dev <= 20:
        print("  WARNING: Proportions deviate by more than 10%.")
        print("  Consider re-generating with explicit dimensions in the prompt.")
        sys.exit(1)
    else:
        print("  FAIL: Proportions deviate by more than 20%.")
        print("  Re-generate with explicit proportions or use image-to-3D.")
        print(f"  Suggested prompt addition: \"approximately {cat_w*39.37:.0f} inches wide, "
              f"{cat_h*39.37:.0f} inches tall, {cat_d*39.37:.0f} inches deep\"")
        sys.exit(2)


if __name__ == "__main__":
    main()
