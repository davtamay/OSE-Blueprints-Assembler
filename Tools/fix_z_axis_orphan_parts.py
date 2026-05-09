"""
fix_z_axis_orphan_parts.py — Wire up 6 Z-axis parts that are defined but
referenced by no step (Unity validator: "defined but never referenced").

After my earlier rod-assembly fixes (replaced spacers/motor_piece in step
parts with actual rods/carriage), these legitimate Z-axis parts no longer
appeared in any step.requiredPartIds:

  z1_half_carriage  (Z-Back composite carriage)
  z1_spacer_1       (Z-Back upper rod spacer)
  z1_spacer_002     (Z-Back lower rod spacer)
  z2_half_carriage  (Z-Front composite carriage)
  z2_spacer         (Z-Front upper rod spacer)
  z2_spacer_2       (Z-Front lower rod spacer)

Z axes use rod-end spacers that mount on the guide rods between the rod
end and the frame — a Z-specific addition that Y axes don't have. The
half_carriage is the assembled carriage as a single conceptual unit.

Fix:
  - Add half_carriage to the carriage-onto-rods step (currently empty parts;
    the trainee physically handles the assembled carriage at this step).
  - Add spacers to the rods-into-idler step (they go on the rods together).
"""

from __future__ import annotations
import json
import os

BASE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "assemblies"
)

ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


# (assembly file, step id, parts to ADD to requiredPartIds + taskOrder, instruction note)
PATCHES = [
    ("assembly_d3d_z_back_bench.json",
     "step_z_back_rods_into_idler",
     ["z1_spacer_1", "z1_spacer_002"],
     " Slide the upper and lower spacers onto the rods before they enter the idler — Z-axis rods seat against these."),
    ("assembly_d3d_z_back_bench.json",
     "step_z_back_carriage_onto_rods",
     ["z1_half_carriage"],
     ""),  # instruction already covers "the assembled carriage"
    ("assembly_d3d_z_front_bench.json",
     "step_z_front_rods_into_idler",
     ["z2_spacer", "z2_spacer_2"],
     " Slide the upper and lower spacers onto the rods before they enter the idler — Z-axis rods seat against these."),
    ("assembly_d3d_z_front_bench.json",
     "step_z_front_carriage_onto_rods",
     ["z2_half_carriage"],
     ""),
]


def main():
    for asm_file, step_id, parts_to_add, instr_note in PATCHES:
        path = os.path.join(BASE, asm_file)
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        step = next((s for s in d["steps"] if s["id"] == step_id), None)
        if not step:
            print(f"  ⚠ {asm_file}: step '{step_id}' not found; skipping")
            continue

        existing_parts = list(step.get("requiredPartIds", []) or [])
        existing_task  = list(step.get("taskOrder", []) or [])
        added = []
        for pid in parts_to_add:
            if pid not in existing_parts:
                existing_parts.append(pid)
                existing_task.append(task(pid))
                added.append(pid)
        step["requiredPartIds"] = existing_parts
        step["taskOrder"]       = existing_task

        if instr_note and instr_note not in (step.get("instructionText", "") or ""):
            step["instructionText"] = (step.get("instructionText", "") or "") + instr_note

        with open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=2)
        print(f"  ~ {asm_file}/{step_id}: added {added}")

    print("\nVerify: python tools/package_health.py d3d_v18_10")


if __name__ == "__main__":
    main()
