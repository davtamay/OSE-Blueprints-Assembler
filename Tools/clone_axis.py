"""
clone_axis.py — Clone partGroups from a source assembly to a target axis.

Treats one assembly (e.g. Y-Left) as the gold reference and produces the
authored content for another (e.g. Z-Back) by applying a JSON mapping
config: ID translations, instruction-text substitutions, and a staging
position offset for new parts.

Usage:
    python tools/clone_axis.py AgentAssistant/clone_configs/<config>.json

Config schema (see y_left_to_z_back.example.json):
{
  "source_assembly":  "assembly_d3d_y_left_bench",
  "target_assembly":  "assembly_d3d_z_back_bench",
  "target_axis":      "z_back",                     // for *_<axis>_* prefix swap
  "package_id":       "d3d_v18_10",
  "clone_partGroups": ["motor_build", "rod_assembly", "belt_threading"],
  "id_map": {
      "motor002":    "motor001",
      "rod_005":     "rod_009",
      ...
  },
  "skip_part_ids": ["y_left_gt2_pulley", ...],  // shared parts; do not clone
  "staging_offset": {"x": -1.7, "y": 0.0, "z": 3.45},
  "text_substitutions": {
      "Y-left": "Z-back",
      "y-left": "z-back",
      "y_left": "z_back"
  }
}

What it does (idempotent):
  1. Reads source + target assembly JSON.
  2. Computes a partId mapping: id_map ∪ axis-prefix swap (y_left_X → <axis>_X).
  3. For each partGroup in `clone_partGroups`:
     - Translates all source steps (id, name, instructionText, requiredPartIds,
       taskOrder) using the part/step mappings.
     - Translates the source partGroup itself (id, stepIds, description).
     - REPLACES any same-id target partGroup; APPENDS otherwise.
  4. Ensures every part referenced by cloned steps exists in target.parts:
     - If `id_map` covers it and the target part exists: leave alone.
     - Else clone source part with remapped id + offset stagingPose.
  5. Updates target.assembly.stepIds and partGroupIds preserving non-cloned IDs.
  6. Writes the target file. Run package_health.py --fix-seqindex afterward.

Non-destructive for:
  - Aggregate `*_bench_unit` partGroup (axis composition step).
  - Target's `dependencyAssemblyIds`, `learningFocus`, parts unrelated to clone.
"""

from __future__ import annotations
import json
import os
import re
import sys
from copy import deepcopy

PKG_DIR_TPL = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "{pkg}", "assemblies"
)


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def save(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def offset_pose(pose: dict, off: dict) -> dict:
    if not pose:
        return pose
    new = deepcopy(pose)
    pos = new.get("position", {})
    pos["x"] = pos.get("x", 0.0) + off.get("x", 0.0)
    pos["y"] = pos.get("y", 0.0) + off.get("y", 0.0)
    pos["z"] = pos.get("z", 0.0) + off.get("z", 0.0)
    new["position"] = pos
    return new


class Translator:
    def __init__(self, cfg):
        self.cfg = cfg
        self.src_axis = self._infer_src_axis(cfg["source_assembly"])
        self.tgt_axis = cfg["target_axis"]
        self.id_map = dict(cfg.get("id_map", {}))
        self.skip_parts = set(cfg.get("skip_part_ids", []))
        self.text_subs = dict(cfg.get("text_substitutions", {}))
        self.offset = cfg.get("staging_offset", {"x": 0, "y": 0, "z": 0})
        # Synthesized maps for steps + partGroups + assemblies
        self.step_id_map = {}
        self.pg_id_map = {}

    @staticmethod
    def _infer_src_axis(src_asm_id):
        # assembly_d3d_y_left_bench → y_left
        m = re.match(r"assembly_d3d_(.+)_bench$", src_asm_id)
        if m:
            return m.group(1)
        # assembly_d3d_x_axis_bench → x_axis (already _axis)
        return src_asm_id.replace("assembly_d3d_", "").replace("_bench", "")

    def map_part_id(self, pid: str) -> str:
        if pid in self.id_map:
            return self.id_map[pid]
        # axis-prefix swap (y_left_foo → z_back_foo)
        if pid.startswith(self.src_axis + "_"):
            return self.tgt_axis + pid[len(self.src_axis):]
        return pid  # unchanged (shared/global)

    def map_step_id(self, sid: str) -> str:
        if sid in self.step_id_map:
            return self.step_id_map[sid]
        new = sid.replace(self.src_axis, self.tgt_axis) if self.src_axis in sid else sid
        self.step_id_map[sid] = new
        return new

    def map_pg_id(self, gid: str) -> str:
        if gid in self.pg_id_map:
            return self.pg_id_map[gid]
        new = gid.replace(self.src_axis, self.tgt_axis) if self.src_axis in gid else gid
        self.pg_id_map[gid] = new
        return new

    def translate_text(self, text: str) -> str:
        if not text:
            return text
        # First apply explicit substitutions (longest-first to avoid partial matches)
        for src, tgt in sorted(self.text_subs.items(), key=lambda x: -len(x[0])):
            text = text.replace(src, tgt)
        # Then axis-prefix swap as fallback (safe because explicit subs ran first)
        text = re.sub(rf"\b{re.escape(self.src_axis)}\b", self.tgt_axis, text)
        return text

    def translate_step(self, step: dict, target_assembly_id: str) -> dict:
        s = deepcopy(step)
        s["id"] = self.map_step_id(s["id"])
        s["assemblyId"] = target_assembly_id
        s["partGroupId"] = self.map_pg_id(s["partGroupId"])
        s["name"] = self.translate_text(s.get("name", ""))
        s["instructionText"] = self.translate_text(s.get("instructionText", ""))
        s["requiredPartIds"] = [self.map_part_id(p) for p in s.get("requiredPartIds", [])]
        if "taskOrder" in s:
            for entry in s["taskOrder"]:
                if entry.get("kind") == "part" and "id" in entry:
                    entry["id"] = self.map_part_id(entry["id"])
        # Bump seq slightly so --fix-seqindex puts it AFTER the source's spot.
        # We use float collision-avoidance by shifting all cloned seqs to use
        # source seq + offset_seq (caller sets), but we keep this simple — let
        # --fix-seqindex collapse globally based on logical order via stepIds.
        # Strategy: assign placeholder large seq; final order comes from
        # target.assembly.stepIds + global renumber.
        # For now: shift seq by len(steps_already_in_target)*0.001 from a base.
        return s

    def translate_partgroup(self, pg: dict, target_assembly_id: str) -> dict:
        g = deepcopy(pg)
        g["id"] = self.map_pg_id(g["id"])
        g["name"] = self.translate_text(g.get("name", ""))
        g["assemblyId"] = target_assembly_id
        g["description"] = self.translate_text(g.get("description", ""))
        g["milestoneMessage"] = self.translate_text(g.get("milestoneMessage", ""))
        g["stepIds"] = [self.map_step_id(s) for s in g.get("stepIds", [])]
        if "partIds" in g:
            g["partIds"] = [self.map_part_id(p) for p in g["partIds"]]
        return g

    def translate_part(self, part: dict) -> dict:
        p = deepcopy(part)
        p["id"] = self.map_part_id(p["id"])
        p["name"] = self.translate_text(p.get("name", ""))
        p["displayName"] = self.translate_text(p.get("displayName", ""))
        p["function"] = self.translate_text(p.get("function", ""))
        if "stagingPose" in p:
            p["stagingPose"] = offset_pose(p["stagingPose"], self.offset)
        if "partGroupIds" in p:
            p["partGroupIds"] = [self.map_pg_id(g) for g in p["partGroupIds"]]
        return p


def filter_partgroup_key(pg_id: str, axis: str, candidates: list[str]) -> str | None:
    """Match short keys like 'motor_build' against full IDs like 'partGroup_y_left_motor_build'."""
    for key in candidates:
        if pg_id.endswith("_" + key) or pg_id.endswith(axis + "_" + key) \
                or key in pg_id.split("_" + axis + "_")[-1]:
            return key
    return None


def run(cfg_path: str):
    cfg = load(cfg_path)
    asm_dir = PKG_DIR_TPL.format(pkg=cfg["package_id"])
    src_path = os.path.join(asm_dir, cfg["source_assembly"] + ".json")
    tgt_path = os.path.join(asm_dir, cfg["target_assembly"] + ".json")
    src = load(src_path)
    tgt = load(tgt_path)
    tx = Translator(cfg)
    target_asm_id = cfg["target_assembly"]

    # 1. Collect source partGroups to clone
    short_keys = set(cfg["clone_partGroups"])
    src_pgs_to_clone = []
    for pg in src.get("partGroups", []):
        k = filter_partgroup_key(pg["id"], tx.src_axis, list(short_keys))
        if k:
            src_pgs_to_clone.append((k, pg))
    cloned_keys = {k for k, _ in src_pgs_to_clone}
    missing = short_keys - cloned_keys
    if missing:
        print(f"WARNING: source has no partGroups matching: {missing}")

    # 2. For each, gather steps + their referenced parts
    src_step_by_id = {s["id"]: s for s in src.get("steps", [])}
    src_part_by_id = {p["id"]: p for p in src.get("parts", [])}
    cloned_step_ids_src = []
    for _, pg in src_pgs_to_clone:
        cloned_step_ids_src.extend(pg.get("stepIds", []))

    # 3. Translate steps
    new_steps = [tx.translate_step(src_step_by_id[sid], target_asm_id)
                 for sid in cloned_step_ids_src if sid in src_step_by_id]

    # 4. Compute parts that need to exist in target
    needed_part_ids = set()
    for s in new_steps:
        for pid in s.get("requiredPartIds", []):
            needed_part_ids.add(pid)
        for entry in s.get("taskOrder", []):
            if entry.get("kind") == "part":
                needed_part_ids.add(entry["id"])

    # 5. Translate partGroups
    new_pgs = [tx.translate_partgroup(pg, target_asm_id)
               for _, pg in src_pgs_to_clone]
    # Add partIds parts to needed set
    for pg in new_pgs:
        for pid in pg.get("partIds", []) or []:
            needed_part_ids.add(pid)

    # 6. Determine which parts to clone vs skip vs already-exist
    target_part_ids = {p["id"] for p in tgt.get("parts", [])}
    to_create = []
    for tgt_pid in needed_part_ids:
        if tgt_pid in target_part_ids:
            continue  # already exists in target
        if tgt_pid in tx.skip_parts:
            print(f"  skip part (configured): {tgt_pid}")
            continue
        # Find source part: reverse-map
        candidates = [src_pid for src_pid, sp in src_part_by_id.items()
                      if tx.map_part_id(src_pid) == tgt_pid]
        if not candidates:
            print(f"  ⚠ part missing from source, will need manual definition: {tgt_pid}")
            continue
        src_part = src_part_by_id[candidates[0]]
        to_create.append(tx.translate_part(src_part))

    # 7. Replace existing target partGroups + steps + parts
    new_pg_ids = {pg["id"] for pg in new_pgs}
    new_step_ids = {s["id"] for s in new_steps}
    new_part_ids = {p["id"] for p in to_create}

    # Steps that belonged to a cloned target partGroup must be removed
    # (they're being replaced). Identify by partGroupId match.
    tgt_steps_kept = []
    removed_step_count = 0
    for s in tgt.get("steps", []):
        if s.get("partGroupId") in new_pg_ids:
            removed_step_count += 1
            continue
        # Also remove if step ID will collide with a cloned step
        if s["id"] in new_step_ids:
            removed_step_count += 1
            continue
        tgt_steps_kept.append(s)
    tgt["steps"] = tgt_steps_kept + new_steps

    # PartGroups: replace by id
    tgt_pgs_kept = [pg for pg in tgt.get("partGroups", []) if pg["id"] not in new_pg_ids]
    tgt["partGroups"] = tgt_pgs_kept + new_pgs

    # Parts: dedupe and append new ones
    tgt_parts_kept = [p for p in tgt.get("parts", []) if p["id"] not in new_part_ids]
    tgt["parts"] = tgt_parts_kept + to_create

    # 8. Update assembly.stepIds and partGroupIds — replace old IDs from
    #    cloned partGroups with the new step ID order; add cloned partGroup IDs.
    for a in tgt.get("assemblies", []):
        if a["id"] != target_asm_id:
            continue
        # Step IDs: drop old, splice new in source order
        old_pg_step_ids = set()
        for pg in tgt_pgs_kept:
            pass  # not relevant; we want to drop steps from REPLACED pgs
        # The replaced pgs are gone; we need to drop their old stepIds from a.stepIds
        # Easiest: build kept_step_ids as those whose step we kept
        kept_ids = {s["id"] for s in tgt_steps_kept}
        ids = [i for i in a.get("stepIds", []) if i in kept_ids]
        # Append new step IDs in clone order (first-found wins per partGroup)
        for ns in new_steps:
            if ns["id"] not in ids:
                ids.append(ns["id"])
        a["stepIds"] = ids

        # partGroupIds: ensure cloned PGs are present
        pg_ids = list(a.get("partGroupIds", []))
        for pg in new_pgs:
            if pg["id"] not in pg_ids:
                pg_ids.append(pg["id"])
        a["partGroupIds"] = pg_ids

    # 9. Assign placeholder seqIndex to cloned steps so --fix-seqindex
    #    places them after existing target steps.
    max_existing_seq = max(
        (s.get("sequenceIndex", 0) for s in tgt_steps_kept), default=0
    )
    for i, ns in enumerate(new_steps):
        ns["sequenceIndex"] = max_existing_seq + 0.001 * (i + 1)

    save(tgt_path, tgt)

    # Summary
    print(f"\nWrote {tgt_path}")
    print(f"  partGroups cloned: {len(new_pgs)}")
    print(f"  steps cloned:      {len(new_steps)}")
    print(f"  steps removed:     {removed_step_count}")
    print(f"  parts cloned:      {len(to_create)}")
    print(f"\nNow run: python tools/package_health.py "
          f"{cfg['package_id']} --fix-seqindex")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    run(sys.argv[1])
