"""
build_batch_rod_assembly.py — Refactor: extract per-axis rod assembly
into assembly_d3d_batch_rod_assembly.json. Manual §6.1-6.5.

  3 shared + 5 × 4 = 23 steps (seq 151-173).

Instance mapping (matches motor m1-m4 / idler i1-i4):
  r1 = Y-Left  / r2 = Y-Right / r3 = Z-Back / r4 = Z-Front

X-Axis rod assembly stays in x_axis_bench.
"""

from __future__ import annotations
import json
import os

PKG_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10"
)
ASM_DIR = os.path.join(PKG_DIR, "assemblies")
BATCH_PATH = os.path.join(ASM_DIR, "assembly_d3d_batch_rod_assembly.json")

# (axis_id, axis_label, instance_letter, axis_bench_filename, rods, carriage_onto_rods_parts, motor_piece_parts)
INSTANCES = [
    ("y_left",  "Y-Left",  "r1", "assembly_d3d_y_left_bench.json",
     ["rod_005", "rod_006"],
     ["y1_bracket", "pocket039", "pocket040"],
     []),
    ("y_right", "Y-Right", "r2", "assembly_d3d_y_right_bench.json",
     ["rod_007", "rod_008"],
     ["y2_bracket"],
     []),
    ("z_back",  "Z-Back",  "r3", "assembly_d3d_z_back_bench.json",
     ["z_back_rod_a", "z_back_rod_b", "z1_spacer_1", "z1_spacer_002"],
     ["z1_half_carriage"],
     ["motor_piece001"]),
    ("z_front", "Z-Front", "r4", "assembly_d3d_z_front_bench.json",
     ["z_front_rod_a", "z_front_rod_b", "z2_spacer", "z2_spacer_2"],
     ["z2_half_carriage"],
     ["motor_piece"]),
]

PER_AXIS_OLD_STEP_IDS = {
    axis_id: [
        f"step_{axis_id}_rods_into_idler",
        f"step_{axis_id}_idler_tighten_rods",
        f"step_{axis_id}_carriage_onto_rods",
        f"step_{axis_id}_motor_onto_rods",
        f"step_{axis_id}_rods_qc",
    ]
    for axis_id, *_ in INSTANCES
}

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
        "assemblyId": "assembly_d3d_batch_rod_assembly",
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
            "step_batch_rod_layout",
            "Lay out 8 guide rods + idler/carriage/motor units",
            "Confirm",
            "Lay out the 8 guide rods (2 per axis: Y-Left, Y-Right, Z-Back, "
            "Z-Front), the 4 assembled idlers from batch_idler_build, the 4 "
            "assembled carriages from batch_carriage_build, and the 4 "
            "assembled motor units from batch_motor_build. Group by axis.",
        ),
        make_batch_step(
            "step_batch_rod_clean_ends",
            "Clean and inspect rod ends",
            "Use",
            "Wipe each rod end with a clean cloth and inspect for burrs. "
            "Burred ends will catch on the LM8UU bearings inside the "
            "carriage.",
        ),
        make_batch_step(
            "step_batch_rod_qc_straight",
            "QC: confirm rod straightness",
            "Confirm",
            "Roll each rod on a flat surface — rods must roll smoothly "
            "without wobble. Bent rods cause uneven carriage motion.",
        ),
    ]


def make_per_axis_steps(axis_id, axis_label, instance_letter, rods, carriage_parts, motor_parts):
    base = f"step_batch_{instance_letter}"
    return [
        make_batch_step(
            f"{base}_rods_into_idler",
            f"Rod {instance_letter[1:]} ({axis_label}): insert 2 rods into idler",
            "Place",
            f"Insert both {axis_label} guide rods into the completed idler. "
            f"Rod ends must be flush against the bottom of the idler. If a "
            f"rod won't slide in, loosen the idler bolts slightly until it "
            f"does, then re-tighten.",
            parts=list(rods),
        ),
        make_batch_step(
            f"{base}_idler_tighten_short_bolts",
            f"Rod {instance_letter[1:]}: drill-tighten 2 short idler bolts",
            "Use",
            "Tighten the two SHORTER idler bolts with the electric drill on "
            "lowest torque setting — these grip the rods firmly. LEAVE the "
            "longer M6x30 frame-mount bolt LOOSE; it stays loose for frame "
            "attachment later.",
            profile="Torque",
            tools=["tool_power_drill"],
        ),
        make_batch_step(
            f"{base}_carriage_onto_rods",
            f"Rod {instance_letter[1:]}: slide carriage onto rods",
            "Place",
            f"Slide the completed {axis_label} carriage onto the rods with "
            f"the long bolt ends closer to the idler. This orientation "
            f"maximizes axis travel.",
            parts=list(carriage_parts),
        ),
        make_batch_step(
            f"{base}_motor_holder_onto_rods",
            f"Rod {instance_letter[1:]}: push motor holder onto rods",
            "Place",
            "Push the motor-holder unit onto the rods so the motor faces "
            "the carriage bolt heads. Loosen carriage bolts slightly if "
            "needed to slide on, then re-tighten.",
            parts=list(motor_parts),
        ),
        make_batch_step(
            f"{base}_rods_qc",
            f"Rod {instance_letter[1:]}: QC rod-flush + carriage-slide",
            "Confirm",
            "Verify rod ends are still flush at the idler bottom. Slide "
            "the carriage along the full rod length — should glide with "
            "consistent slight resistance.",
        ),
    ]


def build_batch():
    shared = shared_steps()
    shared_ids = [s["id"] for s in shared]
    all_steps = list(shared)
    per_axis_step_ids = {}
    seq = 150.001

    for s in shared:
        s["partGroupId"] = "partGroup_rod_batch_all"
        s["sequenceIndex"] = round(seq, 4)
        seq += 0.001

    for axis_id, axis_label, inst, _, rods, carriage_parts, motor_parts in INSTANCES:
        steps = make_per_axis_steps(axis_id, axis_label, inst, rods, carriage_parts, motor_parts)
        per_axis_step_ids[axis_id] = [s["id"] for s in steps]
        for s in steps:
            s["partGroupId"] = f"partGroup_rod_{axis_id}"
            s["sequenceIndex"] = round(seq, 4)
            seq += 0.001
        all_steps.extend(steps)

    part_groups = [
        {
            "id": "partGroup_rod_batch_all",
            "name": "Batch Rod Assembly (all 4 axes)",
            "stepIds": [s["id"] for s in all_steps],
            "assemblyId": "assembly_d3d_batch_rod_assembly",
            "description":
                "Production-line rod threading: insert 8 rods into 4 idlers, "
                "drill-tighten short idler bolts (LEAVE long bolt loose), "
                "slide carriages and motor holders onto rods. Manual §6.1-6.5.",
            "milestoneMessage":
                "All four axis rod-bundles complete; long idler bolts left loose.",
        }
    ]
    for axis_id, axis_label, inst, *_ in INSTANCES:
        part_groups.append({
            "id": f"partGroup_rod_{axis_id}",
            "name": f"Rod {inst[1:]} ({axis_label})",
            "stepIds": shared_ids + per_axis_step_ids[axis_id],
            "assemblyId": "assembly_d3d_batch_rod_assembly",
            "description":
                f"Thread the {axis_label} rod bundle: 2 rods into idler "
                f"flush at bottom, drill-tighten short idler bolts, slide "
                f"carriage on (long bolt ends toward idler), slide motor "
                f"holder on, QC.",
            "milestoneMessage":
                f"{axis_label} rod bundle threaded; carriage slides freely.",
        })

    return {
        "assemblies": [{
            "id": "assembly_d3d_batch_rod_assembly",
            "name": "Batch Rod Assembly",
            "description":
                "Threads guide rods through idler/carriage/motor units for "
                "all 4 Y/Z axes (Y-Left, Y-Right, Z-Back, Z-Front) in one "
                "production-line session. Manual §6.1-6.5. X-Axis rod "
                "assembly stays in assembly_d3d_x_axis_bench.",
            "machineId": "d3d_v18_10",
            "partGroupIds": [pg["id"] for pg in part_groups],
            "stepIds": [s["id"] for s in all_steps],
            "dependencyAssemblyIds": [
                "assembly_d3d_frame",
                "assembly_d3d_batch_carriage_build",
                "assembly_d3d_batch_idler_build",
                "assembly_d3d_batch_motor_build",
            ],
            "learningFocus":
                "Production-line rod threading: rod-flush invariant at "
                "idler, drill-tighten short bolts only (long bolts stay "
                "loose for frame), correct carriage orientation (long bolt "
                "ends toward idler).",
        }],
        "partGroups": part_groups,
        "parts": [],
        "steps": all_steps,
        "targets": [],
        "hints": [],
    }


def main():
    bench_data = {}
    for axis_id, _, _, fname, *_ in INSTANCES:
        with open(os.path.join(ASM_DIR, fname), encoding="utf-8") as f:
            bench_data[axis_id] = (fname, json.load(f))

    batch = build_batch()

    # Migrate parts (rods + carriage_parts + motor_piece)
    moved_count = 0
    for axis_id, _, _, fname, rods, carriage_parts, motor_parts in INSTANCES:
        d = bench_data[axis_id][1]
        ids_to_move = set(rods + carriage_parts + motor_parts)
        kept_parts = []
        for pp in d.get("parts", []) or []:
            if pp["id"] in ids_to_move:
                pp["partGroupIds"] = [f"partGroup_rod_{axis_id}"]
                batch["parts"].append(pp)
                moved_count += 1
            else:
                kept_parts.append(pp)
        d["parts"] = kept_parts

    print(f"Moved {moved_count} parts into batch")

    # Remove old rod_assembly partGroup + steps
    for axis_id, _, _, fname, *_ in INSTANCES:
        d = bench_data[axis_id][1]
        target_pg_id = f"partGroup_{axis_id}_rod_assembly"
        d["partGroups"] = [pg for pg in d.get("partGroups", []) or []
                           if pg["id"] != target_pg_id]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        before = len(d.get("steps", []))
        d["steps"] = [s for s in d.get("steps", []) or []
                      if s["id"] not in old_ids]
        removed = before - len(d["steps"])
        print(f"  - removed {removed} old rod steps from {fname}")

    # Update assembly metadata
    for axis_id, _, _, fname, *_ in INSTANCES:
        d = bench_data[axis_id][1]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        target_pg_id = f"partGroup_{axis_id}_rod_assembly"
        for a in d.get("assemblies", []):
            a["stepIds"] = [sid for sid in (a.get("stepIds", []) or [])
                            if sid not in old_ids]
            a["partGroupIds"] = [pg for pg in (a.get("partGroupIds", []) or [])
                                 if pg != target_pg_id]
            deps = list(a.get("dependencyAssemblyIds", []) or [])
            if "assembly_d3d_batch_rod_assembly" not in deps:
                deps.append("assembly_d3d_batch_rod_assembly")
            a["dependencyAssemblyIds"] = deps

    # Write
    with open(BATCH_PATH, "w", encoding="utf-8") as f:
        json.dump(batch, f, indent=2)
    print(f"\nWrote batch: {BATCH_PATH}")
    print(f"  parts:      {len(batch['parts'])}")
    print(f"  steps:      {len(batch['steps'])}")
    print(f"  partGroups: {len(batch['partGroups'])}")

    for axis_id, _, _, fname, *_ in INSTANCES:
        d = bench_data[axis_id][1]
        path = os.path.join(ASM_DIR, fname)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=2)
        print(f"Wrote bench: {fname}")


if __name__ == "__main__":
    main()
