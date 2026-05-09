"""
build_batch_motor_build.py — Refactor: extract per-axis motor build into
assembly_d3d_batch_motor_build.json, mirroring batch_carriage_build /
batch_idler_build patterns.

Per Axes - D3D v18.10.pdf §4.1-4.3, §5.1-5.5, the motor-build procedure
has 10 steps per axis. The batch builds 4 motors (Y-Left, Y-Right,
Z-Back, Z-Front) in parallel with 3 shared layout steps:

  3 shared + 10 × 4 = 43 steps total (seq 108-150 after carriage at
  50-80 and idler at 81-107).

Instance mapping (matches carriage c1/c2/c3/c4 + idler i1/i2/i3/i4):
  m1 = Y-Left  (motor002)
  m2 = Y-Right (motor003)
  m3 = Z-Back  (motor001)
  m4 = Z-Front (motor)

X-Axis motor stays in x_axis_bench (per-user decision — keeps X
self-contained alongside its structurally-different carriage/idler).

This script:
  1. Creates assembly_d3d_batch_motor_build.json from scratch
  2. Moves 13 motor-related parts × 4 axes = 52 parts from per-axis
     bench files into the batch (motor, pulley, belt, 4×M3x25 screws,
     3×nuts, 3×M6x18 holder bolts)
  3. Generates 43 step definitions per the manual procedure
  4. Removes the per-axis motor partGroups + steps + parts from bench files
  5. Adds the batch as a dependency in each per-axis bench

After running: invoke `python tools/package_health.py d3d_v18_10
--fix-seqindex` to renumber globally (sub-decimals 107.001-107.043
collapse to integer 108-150).
"""

from __future__ import annotations
import json
import os

PKG_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10"
)
ASM_DIR = os.path.join(PKG_DIR, "assemblies")
BATCH_PATH = os.path.join(ASM_DIR, "assembly_d3d_batch_motor_build.json")

# (axis_id, axis_label, instance_letter, motor_id, axis_bench_filename)
INSTANCES = [
    ("y_left",  "Y-Left",  "m1", "motor002", "assembly_d3d_y_left_bench.json"),
    ("y_right", "Y-Right", "m2", "motor003", "assembly_d3d_y_right_bench.json"),
    ("z_back",  "Z-Back",  "m3", "motor001", "assembly_d3d_z_back_bench.json"),
    ("z_front", "Z-Front", "m4", "motor",    "assembly_d3d_z_front_bench.json"),
]

PER_AXIS_OLD_STEP_IDS = {
    axis_id: [
        f"step_{axis_id}_motor_pulley",
        f"step_{axis_id}_motor_pulley_pop",
        f"step_{axis_id}_motor_half_nuts",
        f"step_{axis_id}_motor_belt_channel",
        f"step_{axis_id}_motor_close_halves",
        f"step_{axis_id}_motor_belt_test",
        f"step_{axis_id}_motor_screws",
        f"step_{axis_id}_motor_m6_bolts",
        f"step_{axis_id}_motor_m6_bolts_tighten",
        f"step_{axis_id}_motor_dangle_test",
    ]
    for axis_id, *_ in INSTANCES
}

# Parts to migrate per axis (13 parts: motor + pulley + belt + 4 screws + 3 nuts + 3 bolts)
def parts_to_move(axis):
    return [
        f"{axis}_gt2_pulley",
        f"{axis}_gt2_belt",
        f"{axis}_m3x25_a",
        f"{axis}_m3x25_b",
        f"{axis}_m3x25_c",
        f"{axis}_m3x25_d",
        f"{axis}_motor_m6_nut_a",
        f"{axis}_motor_m6_nut_b",
        f"{axis}_motor_m6_nut_c",
        f"{axis}_m6x18_c",
        f"{axis}_motor_holder_m6x18_b",
        f"{axis}_motor_holder_m6x18_c",
    ]


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
        "id": step_id,
        "name": name,
        "assemblyId": "assembly_d3d_batch_motor_build",
        "partGroupId": "",
        "sequenceIndex": 0.0,
        "family": family,
        "instructionText": instr,
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": list(parts or []),
        "requiredToolActions": [],
        "taskOrder": [task(p) for p in (parts or [])],
    }
    if profile:
        s["profile"] = profile
    if tools:
        s["relevantToolIds"] = list(tools)
    return s


def shared_steps():
    return [
        make_batch_step(
            "step_batch_motor_layout",
            "Lay out 4 motors and motor-holder hardware",
            "Confirm",
            "Lay out the 4 stepper motors (Y-Left, Y-Right, Z-Back, Z-Front), "
            "8 motor-holder half-pieces, 4 GT2 pulleys with set screws, "
            "4 GT2 belts, 16 M3x25 motor screws, 12 M6 hex nuts, and 12 "
            "M6x18 holder bolts on your workbench. Group by axis.",
        ),
        make_batch_step(
            "step_batch_motor_clean_holes",
            "Clean excess plastic from motor holder holes",
            "Use",
            "Use a 6 mm drill bit (or box cutter) to thresh out plastic "
            "covering the bolt holes on every motor-holder half. Each half "
            "should have clean M6 holes plus a recessed channel for the "
            "GT2 belt.",
        ),
        make_batch_step(
            "step_batch_motor_qc_plastic",
            "QC: verify all motor-holder holes are clean",
            "Confirm",
            "Verify each motor-holder half has clean M6x18 bolt holes, "
            "clear M3x25 motor-screw holes, and a clean belt channel. "
            "Plastic flash here will prevent the motor from seating.",
        ),
    ]


def make_per_axis_steps(axis_id, axis_label, instance_letter, motor_id):
    pulley   = f"{axis_id}_gt2_pulley"
    belt     = f"{axis_id}_gt2_belt"
    screws   = [f"{axis_id}_m3x25_{x}" for x in "abcd"]
    nuts     = [f"{axis_id}_motor_m6_nut_{x}" for x in "abc"]
    bolts    = [f"{axis_id}_m6x18_c",
                f"{axis_id}_motor_holder_m6x18_b",
                f"{axis_id}_motor_holder_m6x18_c"]

    base = f"step_batch_{instance_letter}"
    return [
        make_batch_step(
            f"{base}_pulley",
            f"Motor {instance_letter[1:]} ({axis_label}): mount pulley on motor shaft",
            "Place",
            f"Place the GT2 pulley on the {axis_label} motor shaft using "
            f"the thin spacer to keep the pulley off the housing. Tighten "
            f"the first set screw with an Allen key.",
            parts=[motor_id, pulley],
        ),
        make_batch_step(
            f"{base}_pulley_pop",
            f"Motor {instance_letter[1:]}: lock pulley set screw until pop",
            "Use",
            "Remove the spacer. Tighten the second set screw as firmly as "
            "possible. A soft \"pop\" means the screw is fully seated. "
            "Whether or not you hear it, tighten as hard as you can — the "
            "belt pulls 20 lbs and the screw cannot slip.",
        ),
        make_batch_step(
            f"{base}_half_nuts",
            f"Motor {instance_letter[1:]}: load motor-holder half with 3 nuts",
            "Place",
            "Lay one motor-holder half-piece flat with the rectangular "
            "belt channel away from you. Drop 3 M6 hex nuts into the "
            "embedded slots. Per manual §5.1.",
            parts=list(nuts),
        ),
        make_batch_step(
            f"{base}_belt_channel",
            f"Motor {instance_letter[1:]}: insert belt in motor-holder channel",
            "Place",
            "Lay the GT2 belt in the holder channel with the toothed side "
            "facing INWARD (toward the motor pulley). Per manual §5.2.",
            parts=[belt],
        ),
        make_batch_step(
            f"{base}_close_halves",
            f"Motor {instance_letter[1:]}: close motor-holder halves",
            "Place",
            "Press the second motor-holder half against the first, "
            "trapping the belt and nuts. Halves should align flush. "
            "Per manual §5.3.",
        ),
        make_batch_step(
            f"{base}_belt_test",
            f"Motor {instance_letter[1:]}: confirm belt runs smooth on pulley",
            "Confirm",
            "Hold the motor against the holder and pull the belt gently. "
            "Belt should glide smoothly on the pulley with no rub. If it "
            "rubs, reseat the belt in the channel.",
        ),
        make_batch_step(
            f"{base}_attach_motor_screws",
            f"Motor {instance_letter[1:]}: attach motor with 4× M3x25 screws",
            "Place",
            "Insert the first M3x25 screw and tighten it ¾ tight with a "
            "screwdriver — leaves slight flexibility. Insert the 3 "
            "remaining M3x25 screws somewhat loose. Per manual §5.4.",
            parts=list(screws),
        ),
        make_batch_step(
            f"{base}_attach_motor_holder_bolts",
            f"Motor {instance_letter[1:]}: insert 3× M6x18 holder bolts",
            "Place",
            "Insert 3 M6x18 bolts into the remaining motor-holder holes, "
            "threading each into its embedded nut. Hand-snug; the next "
            "step uses the drill. Per manual §5.4-cont.",
            parts=list(bolts),
        ),
        make_batch_step(
            f"{base}_drill_tighten_holder",
            f"Motor {instance_letter[1:]}: drill-tighten holder M6x18 bolts",
            "Use",
            "Tighten each of the 3 motor-holder M6x18 bolts into its "
            "embedded nut using the electric drill on its lowest torque "
            "setting. The motor holder is now fully clamped.",
            profile="Torque",
            tools=["tool_power_drill"],
        ),
        make_batch_step(
            f"{base}_dangle_test",
            f"Motor {instance_letter[1:]}: dangle test",
            "Confirm",
            "Hold the assembled motor unit by the belt — it should hang "
            "freely from the holder without the motor slipping out. Per "
            "manual §5.5.",
        ),
    ]


def build_batch():
    shared = shared_steps()
    shared_ids = [s["id"] for s in shared]
    all_steps = list(shared)
    per_axis_step_ids = {}
    seq = 107.001  # collapses to 108

    for s in shared:
        s["partGroupId"] = "partGroup_motor_batch_all"
        s["sequenceIndex"] = round(seq, 4)
        seq += 0.001

    for axis_id, axis_label, instance_letter, motor_id, _ in INSTANCES:
        steps = make_per_axis_steps(axis_id, axis_label, instance_letter, motor_id)
        per_axis_step_ids[axis_id] = [s["id"] for s in steps]
        for s in steps:
            s["partGroupId"] = f"partGroup_motor_{axis_id}"
            s["sequenceIndex"] = round(seq, 4)
            seq += 0.001
        all_steps.extend(steps)

    part_groups = [
        {
            "id": "partGroup_motor_batch_all",
            "name": "Batch Motor Build (all 4 axes)",
            "stepIds": [s["id"] for s in all_steps],
            "assemblyId": "assembly_d3d_batch_motor_build",
            "description":
                "Production-line motor assembly: build all four belt-drive "
                "motors (Y-Left, Y-Right, Z-Back, Z-Front) in parallel "
                "from manual §4-§5. Mirrors batch_carriage_build and "
                "batch_idler_build.",
            "milestoneMessage":
                "All four motor units assembled with belts in channels.",
        }
    ]
    for axis_id, axis_label, instance_letter, _, _ in INSTANCES:
        part_groups.append({
            "id": f"partGroup_motor_{axis_id}",
            "name": f"Motor {instance_letter[1:]} ({axis_label})",
            "stepIds": shared_ids + per_axis_step_ids[axis_id],
            "assemblyId": "assembly_d3d_batch_motor_build",
            "description":
                f"Build the {axis_label} motor: pulley with pop check, "
                f"holder half with 3 nuts, belt in channel, close halves, "
                f"attach motor with M3x25 screws, install + drill-tighten "
                f"3× M6x18 bolts, dangle test.",
            "milestoneMessage":
                f"{axis_label} motor unit complete; passes dangle test.",
        })

    return {
        "assemblies": [{
            "id": "assembly_d3d_batch_motor_build",
            "name": "Batch Motor Build",
            "description":
                "Builds all four belt-drive motors (Y-Left, Y-Right, Z-Back, "
                "Z-Front) in one production-line session. Manual §4 (pulley) "
                "and §5 (motor holder + screws + drill-tighten + dangle test). "
                "X-Axis motor stays in assembly_d3d_x_axis_bench.",
            "machineId": "d3d_v18_10",
            "partGroupIds": [pg["id"] for pg in part_groups],
            "stepIds": [s["id"] for s in all_steps],
            "dependencyAssemblyIds": [
                "assembly_d3d_frame",
                "assembly_d3d_batch_carriage_build",
                "assembly_d3d_batch_idler_build",
            ],
            "learningFocus":
                "Production-line motor assembly: pulley pop check, motor-"
                "holder nut bedding, belt-in-channel toothed-side-inward, "
                "drill-tighten on lowest torque, dangle-test verification.",
        }],
        "partGroups": part_groups,
        "parts": [],
        "steps": all_steps,
        "targets": [],
        "hints": [],
    }


def main():
    bench_data = {}
    for axis_id, _, _, _, fname in INSTANCES:
        with open(os.path.join(ASM_DIR, fname), encoding="utf-8") as f:
            bench_data[axis_id] = (fname, json.load(f))

    batch = build_batch()

    # Migrate parts
    moved_count = 0
    for axis_id, _, _, motor_id, _ in INSTANCES:
        fname, d = bench_data[axis_id]
        ids_to_move = set([motor_id] + parts_to_move(axis_id))
        kept_parts = []
        for pp in d.get("parts", []) or []:
            if pp["id"] in ids_to_move:
                pp["partGroupIds"] = [f"partGroup_motor_{axis_id}"]
                batch["parts"].append(pp)
                moved_count += 1
            else:
                kept_parts.append(pp)
        d["parts"] = kept_parts

    print(f"Moved {moved_count} parts into batch")

    # Remove old motor partGroup from each per-axis bench
    for axis_id, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        target_pg_id = f"partGroup_{axis_id}_motor_build"
        before = len(d.get("partGroups", []))
        d["partGroups"] = [pg for pg in d.get("partGroups", []) or []
                           if pg["id"] != target_pg_id]
        if before != len(d["partGroups"]):
            print(f"  - removed {target_pg_id} from {fname}")

    # Remove old motor steps
    for axis_id, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        before = len(d.get("steps", []))
        d["steps"] = [s for s in d.get("steps", []) or []
                      if s["id"] not in old_ids]
        removed = before - len(d["steps"])
        print(f"  - removed {removed} old motor steps from {fname}")

    # Update assembly.stepIds + partGroupIds + dependencies
    for axis_id, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        target_pg_id = f"partGroup_{axis_id}_motor_build"
        for a in d.get("assemblies", []):
            a["stepIds"] = [sid for sid in (a.get("stepIds", []) or [])
                            if sid not in old_ids]
            a["partGroupIds"] = [pg for pg in (a.get("partGroupIds", []) or [])
                                 if pg != target_pg_id]
            deps = list(a.get("dependencyAssemblyIds", []) or [])
            if "assembly_d3d_batch_motor_build" not in deps:
                deps.append("assembly_d3d_batch_motor_build")
            a["dependencyAssemblyIds"] = deps

    # Write batch + per-axis benches
    with open(BATCH_PATH, "w", encoding="utf-8") as f:
        json.dump(batch, f, indent=2)
    print(f"\nWrote batch: {BATCH_PATH}")
    print(f"  parts:       {len(batch['parts'])}")
    print(f"  steps:       {len(batch['steps'])}")
    print(f"  partGroups:  {len(batch['partGroups'])}")

    for axis_id, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        path = os.path.join(ASM_DIR, fname)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=2)
        print(f"Wrote bench: {fname}")

    print("\nNow run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
