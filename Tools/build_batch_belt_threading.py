"""
build_batch_belt_threading.py — Refactor: extract per-axis belt threading
into assembly_d3d_batch_belt_threading.json. Manual §7.1-7.5.

  3 shared + 8 × 4 = 35 steps (seq 174-208).

Instance mapping: b1=Y-Left, b2=Y-Right, b3=Z-Back, b4=Z-Front.

Migrated parts: 4 GT2 belts (already in motor batch — left there as
canonical home; belt steps reference them but don't own them) and 4
belt pegs (`fastener_<axis>_belt_peg`).

Note: the per-axis QC step `step_qc_<axis>_axis_bench` is kept in
the bench file (NOT migrated to batch) per plan A.4. It moves from
partGroup_<axis>_belt_threading.stepIds into partGroup_<axis>_bench_unit.stepIds.
"""

from __future__ import annotations
import json
import os

PKG_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10"
)
ASM_DIR = os.path.join(PKG_DIR, "assemblies")
BATCH_PATH = os.path.join(ASM_DIR, "assembly_d3d_batch_belt_threading.json")

# (axis_id, axis_label, instance_letter, axis_bench_filename, peg_part)
INSTANCES = [
    ("y_left",  "Y-Left",  "b1", "assembly_d3d_y_left_bench.json",  None),
    ("y_right", "Y-Right", "b2", "assembly_d3d_y_right_bench.json", None),
    ("z_back",  "Z-Back",  "b3", "assembly_d3d_z_back_bench.json",  "fastener_z_back_belt_peg"),
    ("z_front", "Z-Front", "b4", "assembly_d3d_z_front_bench.json", "fastener_z_front_belt_peg"),
]

# 8 belt steps per axis (manual §7.1-7.5 + travel-test + label)
PER_AXIS_OLD_STEP_IDS = {
    axis_id: [
        f"step_{axis_id}_belt_large_hole",
        f"step_{axis_id}_belt_around_idler",
        f"step_{axis_id}_belt_small_hole",
        f"step_{axis_id}_belt_peg_orient",
        f"step_{axis_id}_belt_first_peg",
        f"step_{axis_id}_belt_second_peg",
        f"step_{axis_id}_belt_travel_test",
        f"step_{axis_id}_label_axis",
    ]
    for axis_id, *_ in INSTANCES
}

# Per-axis QC step ID — stays in bench file, moves to bench_unit partGroup
QC_STEP_IDS = {axis_id: f"step_qc_{axis_id}_axis_bench" for axis_id, *_ in INSTANCES}

ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


def make_batch_step(step_id, name, family, instr, parts=None,
                    profile=None, tools=None):
    s = {
        "id": step_id, "name": name,
        "assemblyId": "assembly_d3d_batch_belt_threading",
        "partGroupId": "", "sequenceIndex": 0.0,
        "family": family, "instructionText": instr,
        "removePersistentToolIds": [], "targetIds": [],
        "requiredPartIds": list(parts or []),
        "requiredToolActions": [],
        "taskOrder": [task(p) for p in (parts or [])],
    }
    if profile: s["profile"] = profile
    if tools: s["relevantToolIds"] = list(tools)
    return s


def shared_steps():
    return [
        make_batch_step(
            "step_batch_belt_layout",
            "Lay out 4 belt-pegs and route plan",
            "Confirm",
            "Lay out the 4 GT2 belts (already routed through their motor "
            "holders from batch_motor_build) and 8 belt pegs. Confirm "
            "carriage belt-hole orientation: small ribbed hole beside "
            "large smooth hole on each carriage.",
        ),
        make_batch_step(
            "step_batch_belt_route_plan",
            "Confirm belt route: carriage large → idler → carriage small",
            "Confirm",
            "Mental walkthrough of each axis's belt route: insert one end "
            "through the LARGE smooth carriage hole, route around the "
            "idler bearing (slight curve helps), pull through the SMALL "
            "ribbed carriage hole. Belt-peg foot points away from axis "
            "center.",
        ),
        make_batch_step(
            "step_batch_belt_qc_pegs",
            "QC: belt pegs and toothed-side orientation",
            "Confirm",
            "Verify all 8 belt pegs have intact feet (no cracks). Toothed "
            "side of each belt must face INWARD toward the motor pulley "
            "throughout the route.",
        ),
    ]


def make_per_axis_steps(axis_id, axis_label, inst, peg_part):
    base = f"step_batch_{inst}"
    peg_first = peg_part if peg_part else None
    return [
        make_batch_step(
            f"{base}_belt_large_hole",
            f"Belt {inst[1:]} ({axis_label}): thread belt through large carriage hole",
            "Place",
            f"Insert one end of the {axis_label} GT2 belt through the "
            f"LARGER smooth belt hole on the carriage piece. Manual §7.1.",
        ),
        make_batch_step(
            f"{base}_belt_around_idler",
            f"Belt {inst[1:]}: route belt around idler bearing",
            "Place",
            "Continue threading the belt around the bearing inside the "
            "idler. Pro tip: give a slight curve in the right direction "
            "before sliding inside the idler — eases routing. Manual §7.2.",
        ),
        make_batch_step(
            f"{base}_belt_small_hole",
            f"Belt {inst[1:]}: thread belt back through small ribbed hole",
            "Place",
            "Pull the belt through the bearing and thread it back through "
            "the SMALLER ribbed belt hole on the carriage piece. Manual "
            "§7.2 cont.",
        ),
        make_batch_step(
            f"{base}_belt_peg_orient",
            f"Belt {inst[1:]}: confirm peg orientation before lock",
            "Confirm",
            "Position the first peg with its FOOT facing AWAY from the "
            "axis center, AWAY from the toothed side of the belt. Belt "
            "thread passes through the carriage before going through "
            "the peg foot. Manual §7.3.",
        ),
        make_batch_step(
            f"{base}_belt_first_peg",
            f"Belt {inst[1:]}: lock first belt peg",
            "Use",
            "Insert the loose belt end ~¾ inch into the belt hole "
            "underneath the peg, then simultaneously press the peg into "
            "the small ribbed hole. Tighten the peg down. Manual §7.4.",
            profile="Torque",
            parts=[peg_first] if peg_first else None,
        ),
        make_batch_step(
            f"{base}_belt_second_peg",
            f"Belt {inst[1:]}: insert second peg LOOSE",
            "Place",
            "Insert the second peg into the opposite carriage side, "
            "directly across from the first peg. Tighten the belt by "
            "pulling further through the first peg by hand. Leave the "
            "second peg LOOSE — it tightens after frame fitting. Manual "
            "§7.5.",
        ),
        make_batch_step(
            f"{base}_belt_travel_test",
            f"Belt {inst[1:]}: travel test (full rod-length glide)",
            "Confirm",
            "Slide the carriage along the full rod length. Belt should "
            "track smoothly, no rub against plastic. If rubbing, re-seat "
            "the belt in the channel.",
        ),
        make_batch_step(
            f"{base}_label_axis",
            f"Belt {inst[1:]}: label completed {axis_label} axis",
            "Confirm",
            f"Use masking tape and a marker to label the assembled axis "
            f"\"{axis_label}\" and set it aside ready for frame mounting.",
        ),
    ]


def build_batch():
    shared = shared_steps()
    shared_ids = [s["id"] for s in shared]
    all_steps = list(shared)
    per_axis_step_ids = {}
    seq = 173.001

    for s in shared:
        s["partGroupId"] = "partGroup_belt_batch_all"
        s["sequenceIndex"] = round(seq, 4)
        seq += 0.001

    for axis_id, axis_label, inst, _, peg in INSTANCES:
        steps = make_per_axis_steps(axis_id, axis_label, inst, peg)
        per_axis_step_ids[axis_id] = [s["id"] for s in steps]
        for s in steps:
            s["partGroupId"] = f"partGroup_belt_{axis_id}"
            s["sequenceIndex"] = round(seq, 4)
            seq += 0.001
        all_steps.extend(steps)

    part_groups = [
        {
            "id": "partGroup_belt_batch_all",
            "name": "Batch Belt Threading (all 4 axes)",
            "stepIds": [s["id"] for s in all_steps],
            "assemblyId": "assembly_d3d_batch_belt_threading",
            "description":
                "Production-line belt routing: thread 4 belts through "
                "carriage→idler→carriage, lock first peg, leave second "
                "peg loose for frame-side tensioning. Manual §7.1-7.5.",
            "milestoneMessage":
                "All four axis belts threaded; first pegs locked, second "
                "pegs loose for frame tensioning.",
        }
    ]
    for axis_id, axis_label, inst, *_ in INSTANCES:
        part_groups.append({
            "id": f"partGroup_belt_{axis_id}",
            "name": f"Belt {inst[1:]} ({axis_label})",
            "stepIds": shared_ids + per_axis_step_ids[axis_id],
            "assemblyId": "assembly_d3d_batch_belt_threading",
            "description":
                f"Route the {axis_label} GT2 belt: large hole → idler "
                f"bearing → small hole, lock first peg, second peg loose, "
                f"travel test, label axis.",
            "milestoneMessage":
                f"{axis_label} belt routed; first peg locked.",
        })

    return {
        "assemblies": [{
            "id": "assembly_d3d_batch_belt_threading",
            "name": "Batch Belt Threading",
            "description":
                "Routes belts through 4 axes (Y-Left, Y-Right, Z-Back, "
                "Z-Front) in one production-line session. Manual §7.1-7.5. "
                "X-Axis belt threading stays in assembly_d3d_x_axis_bench.",
            "machineId": "d3d_v18_10",
            "partGroupIds": [pg["id"] for pg in part_groups],
            "stepIds": [s["id"] for s in all_steps],
            "dependencyAssemblyIds": [
                "assembly_d3d_frame",
                "assembly_d3d_batch_carriage_build",
                "assembly_d3d_batch_idler_build",
                "assembly_d3d_batch_motor_build",
                "assembly_d3d_batch_rod_assembly",
            ],
            "learningFocus":
                "Production-line belt routing: large→idler→small hole "
                "path, peg orientation (foot away from axis center, away "
                "from toothed side), first peg locked + second peg loose "
                "for frame tensioning.",
        }],
        "partGroups": part_groups,
        "parts": [],
        "steps": all_steps,
        "targets": [],
        "hints": [],
    }


def main():
    bench_data = {}
    for axis_id, _, _, fname, _ in INSTANCES:
        with open(os.path.join(ASM_DIR, fname), encoding="utf-8") as f:
            bench_data[axis_id] = (fname, json.load(f))

    batch = build_batch()

    # Migrate parts: belt pegs (Z only — Y axes don't have a defined peg part)
    moved_count = 0
    for axis_id, _, _, fname, peg in INSTANCES:
        if not peg:
            continue
        d = bench_data[axis_id][1]
        kept_parts = []
        for pp in d.get("parts", []) or []:
            if pp["id"] == peg:
                pp["partGroupIds"] = [f"partGroup_belt_{axis_id}"]
                batch["parts"].append(pp)
                moved_count += 1
            else:
                kept_parts.append(pp)
        d["parts"] = kept_parts

    print(f"Moved {moved_count} parts into batch")

    # Remove old belt_threading partGroup. The QC step belongs to it currently
    # in some axes — preserve it by moving to bench_unit.
    for axis_id, _, _, fname, _ in INSTANCES:
        d = bench_data[axis_id][1]
        target_pg_id = f"partGroup_{axis_id}_belt_threading"
        bench_unit_id = f"partGroup_{axis_id}_bench_unit"
        qc_id = QC_STEP_IDS[axis_id]
        # Find the belt_threading partGroup, check if QC step is in it
        for pg in d.get("partGroups", []) or []:
            if pg["id"] == target_pg_id:
                if qc_id in (pg.get("stepIds", []) or []):
                    # Move QC to bench_unit
                    for bu_pg in d.get("partGroups", []) or []:
                        if bu_pg["id"] == bench_unit_id:
                            bu_ids = list(bu_pg.get("stepIds", []) or [])
                            if qc_id not in bu_ids:
                                bu_ids.append(qc_id)
                                bu_pg["stepIds"] = bu_ids
                            break
                break
        # Now remove the belt_threading partGroup
        d["partGroups"] = [pg for pg in d.get("partGroups", []) or []
                           if pg["id"] != target_pg_id]
        # Remove old belt steps
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        before = len(d.get("steps", []))
        d["steps"] = [s for s in d.get("steps", []) or []
                      if s["id"] not in old_ids]
        removed = before - len(d["steps"])
        print(f"  - removed {removed} old belt steps from {fname}")

    # Update assembly metadata
    for axis_id, _, _, fname, _ in INSTANCES:
        d = bench_data[axis_id][1]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        target_pg_id = f"partGroup_{axis_id}_belt_threading"
        for a in d.get("assemblies", []):
            a["stepIds"] = [sid for sid in (a.get("stepIds", []) or [])
                            if sid not in old_ids]
            a["partGroupIds"] = [pg for pg in (a.get("partGroupIds", []) or [])
                                 if pg != target_pg_id]
            deps = list(a.get("dependencyAssemblyIds", []) or [])
            if "assembly_d3d_batch_belt_threading" not in deps:
                deps.append("assembly_d3d_batch_belt_threading")
            a["dependencyAssemblyIds"] = deps

    # Write batch + benches
    with open(BATCH_PATH, "w", encoding="utf-8") as f:
        json.dump(batch, f, indent=2)
    print(f"\nWrote batch: {BATCH_PATH}")
    print(f"  parts:      {len(batch['parts'])}")
    print(f"  steps:      {len(batch['steps'])}")
    print(f"  partGroups: {len(batch['partGroups'])}")

    for axis_id, _, _, fname, _ in INSTANCES:
        d = bench_data[axis_id][1]
        path = os.path.join(ASM_DIR, fname)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=2)
        print(f"Wrote bench: {fname}")


if __name__ == "__main__":
    main()
