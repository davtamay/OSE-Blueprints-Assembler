"""
fix_axis_bench_qc_steps.py — Add the missing per-axis bench QC step for
Y-Right, Z-Back, Z-Front (Y-Left already has it, X-Axis has its own QC).

Per Axes - D3D v18.10.pdf §12.3, every bench-built axis should end with a
four-point quality control:
  • Belt moves smoothly along axis rods
  • Rods are flush with the idler bottom
  • The idler and carriage bolt heads are on the same side
  • Toothed side of belt is facing inwards

Y-Left has step_qc_y_left_axis_bench wiring this up to
target_bench_y_left_qc + hint_qc_y_left_axis_bench. The other three
axes have the parallel target + hint definitions waiting (currently
flagged as orphans by the validator) but no step references them.

Fix: clone Y-Left's QC step structure into Y-Right/Z-Back/Z-Front,
assigning each to the existing partGroup_<axis>_belt_threading (the
last per-axis partGroup before bench_unit composition).

Resolves 6 orphan warnings:
  targets[224]  target_bench_y_right_qc
  targets[226]  target_bench_z_back_qc
  targets[228]  target_bench_z_front_qc
  hints[163]    hint_qc_y_right_axis_bench
  hints[164]    hint_qc_z_back_axis_bench
  hints[165]    hint_qc_z_front_axis_bench
"""

from __future__ import annotations
import json
import os

BASE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "assemblies"
)


def make_qc_step(axis_id: str, axis_label: str, assembly_id: str) -> dict:
    return {
        "id": f"step_qc_{axis_id}_axis_bench",
        "name": f"{axis_label} axis bench QC and label",
        "assemblyId": assembly_id,
        "partGroupId": f"partGroup_{axis_id}_belt_threading",
        "sequenceIndex": 999.0,  # placeholder, --fix-seqindex collapses
        "family": "Confirm",
        "viewMode": "Inspect",
        "instructionText": (
            f"Run the full bench QC checklist before labeling and setting "
            f"aside: belt moves smoothly along the full rod length; rod ends "
            f"are flush with the idler bottom; the idler and carriage bolt "
            f"heads are on the same side; the toothed side of the belt is "
            f"facing inward. Label the axis {axis_label} using masking tape "
            f"and a marker."
        ),
        "targetIds":     [f"target_bench_{axis_id}_qc"],
        "hintIds":       [f"hint_qc_{axis_id}_axis_bench"],
    }


# (axis_id, axis_label, assembly file)
TARGETS = [
    ("y_right", "Y-Right", "assembly_d3d_y_right_bench.json"),
    ("z_back",  "Z-Back",  "assembly_d3d_z_back_bench.json"),
    ("z_front", "Z-Front", "assembly_d3d_z_front_bench.json"),
]


def patch(axis_id, axis_label, asm_file):
    path = os.path.join(BASE, asm_file)
    with open(path, encoding="utf-8") as f:
        d = json.load(f)

    new_step_id = f"step_qc_{axis_id}_axis_bench"

    # 1. Skip if already present
    if any(s.get("id") == new_step_id for s in d.get("steps", [])):
        print(f"  skip {asm_file}: {new_step_id} already exists")
        return

    # 2. Verify the partGroup, target, and hint dependencies exist
    pg_id = f"partGroup_{axis_id}_belt_threading"
    target_id = f"target_bench_{axis_id}_qc"
    hint_id = f"hint_qc_{axis_id}_axis_bench"

    has_pg = any(pg.get("id") == pg_id for pg in d.get("partGroups", []))
    has_target = any(t.get("id") == target_id for t in d.get("targets", []))
    has_hint = any(h.get("id") == hint_id for h in d.get("hints", []))
    if not (has_pg and has_target and has_hint):
        print(f"  ⚠ {asm_file}: missing dependency "
              f"(pg={has_pg} target={has_target} hint={has_hint}); skipping")
        return

    assembly_id = d["assemblies"][0]["id"]
    new_step = make_qc_step(axis_id, axis_label, assembly_id)
    d["steps"].append(new_step)

    # 3. Append step to partGroup.stepIds
    for pg in d["partGroups"]:
        if pg["id"] == pg_id:
            pg["stepIds"] = list(pg.get("stepIds", [])) + [new_step_id]
            break

    # 4. Append step to assembly.stepIds (at the end, after compose_axis)
    asm = d["assemblies"][0]
    asm_step_ids = list(asm.get("stepIds", []))
    if new_step_id not in asm_step_ids:
        asm_step_ids.append(new_step_id)
    asm["stepIds"] = asm_step_ids

    with open(path, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"  + {asm_file}: added {new_step_id} (partGroup={pg_id})")


def main():
    for axis_id, axis_label, asm_file in TARGETS:
        patch(axis_id, axis_label, asm_file)
    print("\nDone. Run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
