"""
restage_batch_idler_build.py — Two fixes for batch_idler_build:

1. RESTAGE: cluster all 40 parts on a single production bench, mirroring
   batch_carriage_build's tight layout (x span ~60cm, z span ~15cm,
   y=0.55). The refactor preserved per-axis bench staging poses, which
   spread parts across 2.06m × 3.75m. Cluster them on one bench so
   trainees can pick them up like the carriage parts.

2. CONTIGUOUS SEQ: assign sub-decimal seqIndex 81.000, 81.001, ... 81.026
   so a follow-up `package_health.py --fix-seqindex` collapses them to
   integer seq 81-107 (right after carriage's 50-80) and pushes all
   other interleaved steps to 108+.

Bench layout (mirrors carriage's cluster):
  • y = 0.55 (uniform bench surface)
  • x grouped by axis instance: i1 (Y-Left) at x=[0.00,0.16],
    i2 (Y-Right) at [0.18,0.34], i3 (Z-Back) at [0.36,0.52],
    i4 (Z-Front) at [0.54,0.70]
  • z layered by part role:
      0.00  (front-most): idler halves
      0.05            : bearings
      0.10            : bolts
      0.15  (back)    : nuts
"""

from __future__ import annotations
import json
import os

ASM_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    "assemblies", "assembly_d3d_batch_idler_build.json"
)


# Per-instance x base (cm-level cluster on the bench)
INSTANCE_X_BASE = {
    "y_left":  0.00,
    "y_right": 0.18,
    "z_back":  0.36,
    "z_front": 0.54,
}
COL_W = 0.16  # 16 cm per instance — enough for 2 halves side by side

# Per-axis part roles (slot offsets within the instance's column)
# Returns (x_offset_within_column, z_value, color_tint)
def slot_for(axis, part_id):
    base_x = INSTANCE_X_BASE[axis]
    # Halves: front row (z=0)
    if part_id.endswith("_half_b") or part_id == f"idler{ {'y_left':'002','y_right':'003','z_back':'001','z_front':''}[axis] }_half_b":
        return (base_x + 0.08, 0.55, 0.00)
    # Halves "a" (legacy idler002, idler003, idler001, idler):
    halves_a = {"y_left": "idler002", "y_right": "idler003",
                "z_back": "idler001", "z_front": "idler"}
    if part_id == halves_a[axis]:
        return (base_x + 0.00, 0.55, 0.00)
    # Bearings: row z=0.05
    if part_id.endswith("_625zz_a"):
        return (base_x + 0.02, 0.55, 0.05)
    if part_id.endswith("_625zz_b"):
        return (base_x + 0.06, 0.55, 0.05)
    # Bolts: row z=0.10
    if "m6x18_inner" in part_id or part_id == "y_left_m6x18_b":
        return (base_x + 0.02, 0.55, 0.10)
    if "m6x18_loose" in part_id:
        return (base_x + 0.06, 0.55, 0.10)
    if "m6x30" in part_id:
        return (base_x + 0.10, 0.55, 0.10)
    # Nuts: row z=0.15
    if "m6_nut_inner" in part_id:
        return (base_x + 0.02, 0.55, 0.15)
    if "m6_nut_frame" in part_id:
        return (base_x + 0.06, 0.55, 0.15)
    if "m6_nut_loose" in part_id:
        return (base_x + 0.10, 0.55, 0.15)
    return None


# Map partId → axis instance (from partGroupIds field of the part)
def axis_of(part):
    pgs = part.get("partGroupIds", []) or []
    for pg in pgs:
        if pg.startswith("partGroup_idler_"):
            return pg[len("partGroup_idler_"):]
    return None


def main():
    with open(ASM_PATH, encoding="utf-8") as f:
        d = json.load(f)

    # 1) Re-stage parts
    moved = 0
    skipped = []
    for pp in d.get("parts", []) or []:
        axis = axis_of(pp)
        if not axis:
            skipped.append(pp["id"])
            continue
        slot = slot_for(axis, pp["id"])
        if not slot:
            skipped.append(pp["id"])
            continue
        x, y, z = slot
        pose = pp.setdefault("stagingPose", {})
        pose["position"] = {"x": round(x, 4), "y": round(y, 4), "z": round(z, 4)}
        pose.setdefault("rotation", {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0})
        pose.setdefault("scale",    {"x": 1.0, "y": 1.0, "z": 1.0})
        pose.setdefault("color",    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0})
        moved += 1

    print(f"Re-staged {moved} parts onto production bench")
    if skipped:
        print(f"  ⚠ skipped (no slot rule): {skipped}")

    # 2) Reassign seqIndex sub-decimals to put batch right after carriage (50-80)
    #    --fix-seqindex collapses them to integer 81-107 globally
    steps_sorted = sorted(d.get("steps", []) or [],
                          key=lambda s: s.get("sequenceIndex", 0))
    for i, s in enumerate(steps_sorted):
        s["sequenceIndex"] = 81.0 + (i * 0.001)

    print(f"Assigned 81.000-{81.0 + (len(steps_sorted)-1)*0.001:.3f} to "
          f"{len(steps_sorted)} idler batch steps")

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")
    print("Run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
