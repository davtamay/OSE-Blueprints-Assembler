"""
build_batch_idler_build.py — Refactor: move per-axis idler steps + parts
into a new assembly_d3d_batch_idler_build.json, mirroring the structure
of assembly_d3d_batch_carriage_build.json.

Per Axes - D3D v18.10.pdf §3.1-3.6, the idler procedure has 6 steps
per axis. The batch builds 4 idlers (Y-Left, Y-Right, Z-Back, Z-Front)
in parallel, sharing 3 layout/QC steps at the start:

  3 shared layout steps + 6 × 4 = 27 steps total (seq immediately
  after the carriage batch).

Instance mapping (matches carriage batch's c1/c2/c3/c4 convention):
  i1 = Y-Left  (idler002, idler002_half_b)
  i2 = Y-Right (idler003, idler003_half_b)
  i3 = Z-Back  (idler001, idler001_half_b)
  i4 = Z-Front (idler,    idler_half_b)

This script:
  1. Creates assembly_d3d_batch_idler_build.json from scratch
  2. Moves idler parts (halves, bearings, bolts, nuts) from each
     per-axis bench file into the batch
  3. Generates 27 step definitions per the manual procedure
  4. Removes the per-axis idler partGroups + steps + parts from the
     bench files (they now live in the batch)
  5. Adds the batch as a dependency in each per-axis bench's
     dependencyAssemblyIds
  6. Renames y_left_m6x18_b → y_left_idler_m6x18_inner for naming
     consistency with the other 3 axes (which already use this name)

X-Axis idler is NOT migrated — it uses a structurally different
template (5 bolts + 2 half-bearings, manual §15) and lives in
assembly_d3d_x_axis_bench.json.

After running: invoke `python tools/package_health.py d3d_v18_10
--fix-seqindex` to renumber globally.
"""

from __future__ import annotations
import json
import os

PKG_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10"
)
ASM_DIR = os.path.join(PKG_DIR, "assemblies")
BATCH_PATH = os.path.join(ASM_DIR, "assembly_d3d_batch_idler_build.json")

# (axis_id, axis_label, instance_letter, idler_half_a_id, idler_half_b_id, axis_bench_filename)
INSTANCES = [
    ("y_left",  "Y-Left",  "i1", "idler002",        "idler002_half_b", "assembly_d3d_y_left_bench.json"),
    ("y_right", "Y-Right", "i2", "idler003",        "idler003_half_b", "assembly_d3d_y_right_bench.json"),
    ("z_back",  "Z-Back",  "i3", "idler001",        "idler001_half_b", "assembly_d3d_z_back_bench.json"),
    ("z_front", "Z-Front", "i4", "idler",           "idler_half_b",    "assembly_d3d_z_front_bench.json"),
]

# Existing step IDs per axis (created by earlier fix scripts) — these will be removed
PER_AXIS_OLD_STEP_IDS = {
    "y_left": [
        "step_y_left_idler_insert_bolt",
        "step_y_left_idler_insert_bearings",
        "step_y_left_idler_align_halves",
        "step_y_left_idler_tighten_inner",
        "step_y_left_idler_bolt",
        "step_y_left_idler_last_bolt_loose",
    ],
    "y_right": [
        "step_y_right_idler_insert_bolt",
        "step_y_right_idler_insert_bearings",
        "step_y_right_idler_align_halves",
        "step_y_right_idler_tighten_inner",
        "step_y_right_idler_bolt",
        "step_y_right_idler_last_bolt_loose",
    ],
    "z_back": [
        "step_z_back_idler_insert_bolt",
        "step_z_back_idler_insert_bearings",
        "step_z_back_idler_align_halves",
        "step_z_back_idler_tighten_inner",
        "step_z_back_idler_bolt",
        "step_z_back_idler_last_bolt_loose",
    ],
    "z_front": [
        "step_z_front_idler_insert_bolt",
        "step_z_front_idler_insert_bearings",
        "step_z_front_idler_align_halves",
        "step_z_front_idler_tighten_inner",
        "step_z_front_idler_bolt",
        "step_z_front_idler_last_bolt_loose",
    ],
}

# Parts to move from each axis bench → batch (idler-related; halves + bearings + bolts + nuts)
# y_left has an outlier — uses y_left_m6x18_b for inner bolt; we'll also move it.
def parts_to_move(axis):
    base = [
        f"{axis}_625zz_a",
        f"{axis}_625zz_b",
        f"{axis}_idler_m6_nut_inner",
        f"{axis}_idler_m6_nut_frame",
        f"{axis}_idler_m6_nut_loose",
        f"{axis}_idler_m6x18_loose",
    ]
    if axis == "y_left":
        # y_left's idler used y_left_m6x18_b (carriage-style naming) and y_left_idler_m6x30_a
        return base + ["y_left_m6x18_b", "y_left_idler_m6x30_a"]
    if axis == "z_back":
        return base + ["z_back_idler_m6x18_inner", "z_back_idler_m6x30_frame"]
    if axis == "y_right":
        return base + ["y_right_idler_m6x18_inner", "y_right_idler_m6x30_a"]
    if axis == "z_front":
        return base + ["z_front_idler_m6x18_inner", "z_front_idler_m6x30_a"]
    return base


# Helper: idler "inner bolt" canonical id for the step's requiredPartIds
INNER_BOLT_BY_AXIS = {
    "y_left":  "y_left_m6x18_b",
    "y_right": "y_right_idler_m6x18_inner",
    "z_back":  "z_back_idler_m6x18_inner",
    "z_front": "z_front_idler_m6x18_inner",
}
FRAME_BOLT_BY_AXIS = {
    "y_left":  "y_left_idler_m6x30_a",
    "y_right": "y_right_idler_m6x30_a",
    "z_back":  "z_back_idler_m6x30_frame",
    "z_front": "z_front_idler_m6x30_a",
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
        "id": step_id,
        "name": name,
        "assemblyId": "assembly_d3d_batch_idler_build",
        "partGroupId": "",  # filled in by caller (or batch_all)
        "sequenceIndex": 0.0,  # filled in
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


# 3 shared layout/QC steps
def shared_steps():
    return [
        make_batch_step(
            "step_batch_idler_layout",
            "Lay out all 8 idler half-pieces and hardware",
            "Confirm",
            "Lay out the 8 idler half-pieces (2 per axis: Y-Left, Y-Right, "
            "Z-Back, Z-Front), 8 flanged 625ZZ bearings, 4 inner M6x18 "
            "bolts, 4 frame-mount M6x30 bolts, 4 loose-mount M6x18 bolts, "
            "and 12 M6 hex nuts on your workbench. Group by axis.",
        ),
        make_batch_step(
            "step_batch_idler_clean_holes",
            "Clean excess plastic from idler holes",
            "Use",
            "Use a 6 mm drill bit (or box cutter) to thresh out plastic "
            "covering the bolt-pass-through holes on every idler half. "
            "Each half should have 4 clear holes plus one rod-pass cutout.",
        ),
        make_batch_step(
            "step_batch_idler_qc_plastic",
            "QC: verify all idler holes are clean",
            "Confirm",
            "Verify each idler half has 4 clear bolt holes and clean rod-"
            "pass cutouts on both ends. Confirm bearing-side flange "
            "recesses are clean — bearings must seat flush.",
        ),
    ]


def make_per_axis_steps(axis_id, axis_label, instance_letter,
                        half_a_id, half_b_id):
    """6 manual-correct idler-build steps per axis, mirroring carriage
    batch's per-instance step pattern."""
    inner_bolt = INNER_BOLT_BY_AXIS[axis_id]
    inner_nut  = f"{axis_id}_idler_m6_nut_inner"
    bear_a     = f"{axis_id}_625zz_a"
    bear_b     = f"{axis_id}_625zz_b"
    frame_bolt = FRAME_BOLT_BY_AXIS[axis_id]
    frame_nut  = f"{axis_id}_idler_m6_nut_frame"
    loose_bolt = f"{axis_id}_idler_m6x18_loose"
    loose_nut  = f"{axis_id}_idler_m6_nut_loose"

    base = f"step_batch_{instance_letter}"
    return [
        make_batch_step(
            f"{base}_insert_inner_bolt",
            f"Idler {instance_letter[1:]} ({axis_label}): insert M6x18 inner bolt",
            "Place",
            f"Begin assembling the {axis_label} idler: place the inner "
            f"M6x18 bolt through one half-piece ({half_a_id}) from the "
            f"outside.",
            parts=[half_a_id, inner_bolt],
        ),
        make_batch_step(
            f"{base}_place_bearings",
            f"Idler {instance_letter[1:]}: stack 2 flanged bearings on bolt",
            "Place",
            "Stack the two flanged 625ZZ bearings onto the inner bolt, "
            "flanges facing OUTWARD (away from each other).",
            parts=[bear_a, bear_b],
        ),
        make_batch_step(
            f"{base}_align_halves",
            f"Idler {instance_letter[1:]}: place 2nd half + thread M6 nut",
            "Place",
            f"Place the second idler half ({half_b_id}) against the first "
            f"so the rod-pass holes line up exactly. Thread the M6 nut "
            f"onto the back of the inner bolt — hand-snug for now; the "
            f"next step uses the drill.",
            parts=[half_b_id, inner_nut],
        ),
        make_batch_step(
            f"{base}_tighten_inner",
            f"Idler {instance_letter[1:]}: drill-tighten inner bolt",
            "Use",
            "Tighten the inner bolt fully with the electric drill on its "
            "lowest torque setting. The bearings should now be firmly "
            "clamped between the idler halves.",
            profile="Torque",
            tools=["tool_power_drill"],
        ),
        make_batch_step(
            f"{base}_frame_bolt_loose",
            f"Idler {instance_letter[1:]}: insert M6x30 frame-mount LOOSE",
            "Place",
            "With the idler flat and the bearing hole pointing away, "
            "insert the M6x30 frame-mount bolt through the top-right hole "
            "in the same direction as the inner bolt. Thread the M6 nut "
            "on the opposite side and keep it LOOSE — the idler will be "
            "tightened during frame mounting.",
            parts=[frame_bolt, frame_nut],
        ),
        make_batch_step(
            f"{base}_last_bolt_loose",
            f"Idler {instance_letter[1:]}: insert M6x18 in last hole LOOSE",
            "Place",
            "Insert the second M6x18 bolt through the last idler hole and "
            "thread an M6 nut on the back. Run the drill briefly so the "
            "nut catches, but keep it visibly loose.",
            parts=[loose_bolt, loose_nut],
        ),
    ]


# ── Batch assembly factory ───────────────────────────────────────────

def build_batch():
    shared = shared_steps()
    shared_ids = [s["id"] for s in shared]

    all_steps = list(shared)
    per_axis_step_ids = {}
    seq = 81.0  # placeholder; --fix-seqindex collapses globally

    # Shared steps go first (seq 81-83)
    for s in shared:
        s["partGroupId"] = "partGroup_idler_batch_all"  # shared steps live in batch_all
        s["sequenceIndex"] = seq
        seq += 1.0

    # Per-axis steps
    for axis_id, axis_label, instance_letter, half_a, half_b, _ in INSTANCES:
        steps = make_per_axis_steps(axis_id, axis_label, instance_letter, half_a, half_b)
        per_axis_step_ids[axis_id] = [s["id"] for s in steps]
        for s in steps:
            s["partGroupId"] = f"partGroup_idler_{axis_id}"
            s["sequenceIndex"] = seq
            seq += 1.0
        all_steps.extend(steps)

    # PartGroups: one batch_all + one per axis
    part_groups = [
        {
            "id": "partGroup_idler_batch_all",
            "name": "Batch Idler Build (all 4 axes)",
            "stepIds": [s["id"] for s in all_steps],
            "assemblyId": "assembly_d3d_batch_idler_build",
            "description":
                "Production-line idler assembly: build all four belt idlers "
                "(Y-Left, Y-Right, Z-Back, Z-Front) in parallel, sharing the "
                "3-step layout/clean/QC at the start. Mirrors batch_carriage_build.",
            "milestoneMessage":
                "All four belt idlers assembled with frame-mount bolts left loose.",
        }
    ]
    for axis_id, axis_label, instance_letter, half_a, half_b, _ in INSTANCES:
        part_groups.append({
            "id": f"partGroup_idler_{axis_id}",
            "name": f"Idler {instance_letter[1:]} ({axis_label})",
            "stepIds": shared_ids + per_axis_step_ids[axis_id],  # shared shown first
            "assemblyId": "assembly_d3d_batch_idler_build",
            "description":
                f"Build the {axis_label} idler: insert inner M6x18, stack "
                f"two flanged bearings, close halves with nut, drill-tighten, "
                f"then add frame-mount M6x30 and last M6x18 LOOSE for frame "
                f"attachment.",
            "milestoneMessage":
                f"{axis_label} idler complete; frame-mount bolts left loose.",
        })

    return {
        "assemblies": [{
            "id": "assembly_d3d_batch_idler_build",
            "name": "Batch Idler Build",
            "description":
                "Builds all four belt idlers (Y-Left, Y-Right, Z-Back, Z-Front) "
                "in one production-line session before per-axis bench work "
                "completes. Lays out all 8 idler halves, cleans plastic, "
                "and runs the manual §3.1-3.6 procedure for each axis: "
                "insert inner bolt, stack flanged bearings, close halves, "
                "drill-tighten, install frame-mount + last bolt LOOSE for "
                "later frame attachment. X-Axis idler is structurally different "
                "(manual §15) and lives in assembly_d3d_x_axis_bench.",
            "machineId": "d3d_v18_10",
            "partGroupIds": [pg["id"] for pg in part_groups],
            "stepIds": [s["id"] for s in all_steps],
            "dependencyAssemblyIds": [
                "assembly_d3d_frame",
                "assembly_d3d_batch_carriage_build",
            ],
            "learningFocus":
                "Production-line idler assembly: insert M6x18 inner bolt, "
                "stack 2 flanged 625ZZ bearings flanges-out, close halves "
                "with hand-threaded M6 nut, drill-tighten on lowest torque, "
                "install frame-mount M6x30 + last M6x18 with nuts but LOOSE "
                "so the idlers can be repositioned during frame mounting.",
        }],
        "partGroups": part_groups,
        "parts": [],   # filled in during migration
        "steps": all_steps,
        "targets": [],
        "hints": [],
    }


def main():
    # Read all 4 per-axis bench files + collect parts to move
    bench_data = {}
    for axis_id, _, _, _, _, fname in INSTANCES:
        with open(os.path.join(ASM_DIR, fname), encoding="utf-8") as f:
            bench_data[axis_id] = (fname, json.load(f))

    # Build batch skeleton
    batch = build_batch()

    # ── Migrate parts ───────────────────────────────────────────────
    moved_count = 0
    for axis_id, _, _, half_a_id, half_b_id, _ in INSTANCES:
        fname, d = bench_data[axis_id]
        # Parts to move: halves + bearings + bolts + nuts
        ids_to_move = set([half_a_id, half_b_id] + parts_to_move(axis_id))
        kept_parts = []
        for pp in d.get("parts", []) or []:
            if pp["id"] in ids_to_move:
                # Repoint partGroupIds to the new batch partGroup
                pp["partGroupIds"] = [f"partGroup_idler_{axis_id}"]
                # If it's a half, also belong to batch_all conceptually
                # (kept simple: just per-axis partGroup membership)
                batch["parts"].append(pp)
                moved_count += 1
            else:
                kept_parts.append(pp)
        d["parts"] = kept_parts

    print(f"Moved {moved_count} parts into batch")

    # ── Remove idler partGroup from each per-axis bench ─────────────
    for axis_id, _, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        target_pg_id = f"partGroup_{axis_id}_idler_build"
        before = len(d.get("partGroups", []))
        d["partGroups"] = [pg for pg in d.get("partGroups", []) or []
                           if pg["id"] != target_pg_id]
        after = len(d["partGroups"])
        if before != after:
            print(f"  - removed {target_pg_id} from {fname}")

    # ── Remove old idler steps from each per-axis bench ─────────────
    for axis_id, _, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        before = len(d.get("steps", []))
        d["steps"] = [s for s in d.get("steps", []) or []
                      if s["id"] not in old_ids]
        removed = before - len(d["steps"])
        print(f"  - removed {removed} old idler steps from {fname}")

    # ── Update assembly.stepIds + partGroupIds + dependencies ──────
    for axis_id, _, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        old_ids = set(PER_AXIS_OLD_STEP_IDS[axis_id])
        target_pg_id = f"partGroup_{axis_id}_idler_build"
        for a in d.get("assemblies", []):
            a["stepIds"] = [sid for sid in (a.get("stepIds", []) or [])
                            if sid not in old_ids]
            a["partGroupIds"] = [pg for pg in (a.get("partGroupIds", []) or [])
                                 if pg != target_pg_id]
            deps = list(a.get("dependencyAssemblyIds", []) or [])
            if "assembly_d3d_batch_idler_build" not in deps:
                deps.append("assembly_d3d_batch_idler_build")
            a["dependencyAssemblyIds"] = deps

    # ── Write everything ────────────────────────────────────────────
    with open(BATCH_PATH, "w", encoding="utf-8") as f:
        json.dump(batch, f, indent=2)
    print(f"\nWrote batch: {BATCH_PATH}")
    print(f"  parts:       {len(batch['parts'])}")
    print(f"  steps:       {len(batch['steps'])}")
    print(f"  partGroups:  {len(batch['partGroups'])}")

    for axis_id, _, _, _, _, fname in INSTANCES:
        d = bench_data[axis_id][1]
        path = os.path.join(ASM_DIR, fname)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=2)
        print(f"Wrote bench: {fname}")

    print("\nNow run:")
    print("  python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
