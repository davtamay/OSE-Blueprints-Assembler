#!/usr/bin/env python3
"""
instantiate_prefab.py — Step Configuration Prefab Engine
========================================================
Reads a Step Configuration Prefab (YAML in AgentAssistant/prefabs/) and an
instantiation YAML (in AgentAssistant/inputs/), then emits a step JSON array
ready to merge into a machine-package assembly file.

Usage:
    python Tools/instantiate_prefab.py AgentAssistant/inputs/<file>.yaml
    python Tools/instantiate_prefab.py AgentAssistant/inputs/<file>.yaml --output custom.json
    python Tools/instantiate_prefab.py --list-prefabs

Instantiation YAML format:
    prefab: CarriageBuild
    prefix: y_left_carriage              # used in step IDs: step_<prefix>_<id_suffix>
    start_seq: 87
    parts:
      half_a: y_left_carriage_half_a
      half_b: y_left_carriage_half_b
      bearings: [y_left_lm8uu_a, y_left_lm8uu_b, y_left_lm8uu_c, y_left_lm8uu_d]
      bolts_top: [...]
      bolts_bot: [...]
      nuts: [...]
    options:                              # optional — overrides prefab defaults
      milestone: "Y-Left carriage complete — 1 of 4"

Substitution rules in prefab steps:
    Any string field:    {role} or {role.count}; format() applied with the context
    Array of role refs:  "{role}" inserts a single value; "*{role}" expands a list role
    Other types pass through unchanged

The Python templates in generate_steps.py remain available; this engine is
additive. Either path produces step JSON consumable by the Unity package loader.
"""

import argparse
import json
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    print("ERROR: PyYAML is required. Install with: pip install pyyaml", file=sys.stderr)
    sys.exit(1)


REPO_ROOT     = Path(__file__).parent.parent
PREFABS_DIR   = REPO_ROOT / "AgentAssistant" / "prefabs"
INPUTS_DIR    = REPO_ROOT / "AgentAssistant" / "inputs"
OUTPUTS_DIR   = REPO_ROOT / "AgentAssistant" / "outputs"


# ── Role-list wrapper exposing .count for {role.count} substitutions ──────────

class ListRole:
    """Wraps a part-id list so str.format can resolve {role.count}."""
    __slots__ = ("items", "count")

    def __init__(self, items):
        self.items = list(items)
        self.count = len(self.items)

    def __str__(self):
        return ", ".join(self.items)

    def __format__(self, spec):
        if spec:
            raise ValueError(f"List role does not accept format spec '{spec}'")
        return self.__str__()


# ── Prefab and instantiation loading ──────────────────────────────────────────

def load_prefab(name):
    """Locate a prefab by name (with or without .yaml extension)."""
    candidates = [
        PREFABS_DIR / f"{name}.yaml",
        PREFABS_DIR / f"{name}.yml",
        Path(name),
    ]
    for c in candidates:
        if c.exists():
            return yaml.safe_load(c.read_text(encoding="utf-8")), c
    available = sorted(p.stem for p in PREFABS_DIR.glob("*.yaml"))
    raise FileNotFoundError(
        f"Prefab '{name}' not found in {PREFABS_DIR}. Available: {', '.join(available) or '(none)'}"
    )


def load_instantiation(path):
    text = Path(path).read_text(encoding="utf-8")
    return yaml.safe_load(text)


# ── Role validation + context build ───────────────────────────────────────────

def _validate_and_resolve_roles(prefab, instance):
    roles = prefab.get("roles", {}) or {}
    parts = instance.get("parts", {}) or {}

    resolved = {}
    for role_name, role_decl in roles.items():
        kind = role_decl.get("kind", "part")
        provided = parts.get(role_name)
        if provided is None:
            raise ValueError(f"Instantiation missing required role '{role_name}' (kind={kind})")

        if kind == "part":
            if not isinstance(provided, str):
                raise ValueError(
                    f"Role '{role_name}' is kind=part — expected a single partId string, "
                    f"got {type(provided).__name__}"
                )
            resolved[role_name] = provided

        elif kind == "part_list":
            if not isinstance(provided, list):
                raise ValueError(
                    f"Role '{role_name}' is kind=part_list — expected a list of partIds, "
                    f"got {type(provided).__name__}"
                )
            expected_count = role_decl.get("count")
            if expected_count is not None and len(provided) != expected_count:
                raise ValueError(
                    f"Role '{role_name}' expects {expected_count} entries, got {len(provided)}"
                )
            resolved[role_name] = ListRole(provided)

        else:
            raise ValueError(f"Role '{role_name}' has unknown kind '{kind}'")

    # Reject extras: parts entries that don't correspond to any declared role.
    extras = set(parts.keys()) - set(roles.keys())
    if extras:
        raise ValueError(
            f"Instantiation provides parts for roles not declared by prefab '{prefab.get('prefab','?')}': "
            f"{sorted(extras)}"
        )

    return resolved


def _resolve_options(prefab, instance):
    options = prefab.get("options", {}) or {}
    overrides = instance.get("options", {}) or {}
    resolved = {}
    for opt_name, opt_decl in options.items():
        if opt_name in overrides:
            resolved[opt_name] = overrides[opt_name]
        elif "default" in opt_decl:
            resolved[opt_name] = opt_decl["default"]
        else:
            raise ValueError(f"Option '{opt_name}' has no default and no instantiation value")
    extras = set(overrides.keys()) - set(options.keys())
    if extras:
        raise ValueError(
            f"Instantiation overrides options not declared by prefab: {sorted(extras)}"
        )
    return resolved


def _resolve_derived(prefab, ctx):
    derived = prefab.get("derived", {}) or {}
    for name, decl in derived.items():
        kind = decl.get("kind", "part_list")
        if kind != "part_list":
            raise ValueError(f"Derived role '{name}' has unsupported kind '{kind}' (only part_list for now)")
        combine = decl.get("combine") or []
        items = []
        for src in combine:
            val = ctx.get(src)
            if val is None:
                raise ValueError(f"Derived role '{name}' references unknown role '{src}'")
            if isinstance(val, ListRole):
                items.extend(val.items)
            else:
                items.append(val)
        ctx[name] = ListRole(items)
    return ctx


# ── Substitution ──────────────────────────────────────────────────────────────

def _substitute_string(s, ctx):
    """Apply str.format with ctx. ListRole values handle .count and bare-name uses."""
    try:
        return s.format(**ctx)
    except KeyError as e:
        raise ValueError(f"Unknown role '{e.args[0]}' in template string: {s!r}") from None
    except (IndexError, AttributeError) as e:
        raise ValueError(f"Substitution failure in template string {s!r}: {e}") from None


def _substitute_array(items, ctx):
    """Walk array; resolve {role} / *{role} markers; recurse into dicts/lists."""
    out = []
    for item in items:
        if isinstance(item, str):
            stripped = item.strip()
            if stripped.startswith("*{") and stripped.endswith("}") and "{" not in stripped[2:-1]:
                role = stripped[2:-1]
                val = ctx.get(role)
                if val is None:
                    raise ValueError(f"Unknown role '{role}' in *{{{role}}} expansion")
                if not isinstance(val, ListRole):
                    raise ValueError(f"Role '{role}' is not a list — use {{{role}}} not *{{{role}}}")
                out.extend(val.items)
                continue
            if stripped.startswith("{") and stripped.endswith("}") and "{" not in stripped[1:-1]:
                role = stripped[1:-1]
                val = ctx.get(role)
                if val is None:
                    raise ValueError(f"Unknown role '{role}' in {{{role}}}")
                if isinstance(val, ListRole):
                    raise ValueError(
                        f"Role '{role}' is a list — use *{{{role}}} to expand it in array context"
                    )
                out.append(val)
                continue
            out.append(_substitute_string(item, ctx))
        elif isinstance(item, list):
            out.append(_substitute_array(item, ctx))
        elif isinstance(item, dict):
            out.append(_substitute_dict(item, ctx))
        else:
            out.append(item)
    return out


def _substitute_dict(d, ctx):
    return {k: _substitute_value(v, ctx) for k, v in d.items()}


def _substitute_value(v, ctx):
    if isinstance(v, str):
        return _substitute_string(v, ctx)
    if isinstance(v, list):
        return _substitute_array(v, ctx)
    if isinstance(v, dict):
        return _substitute_dict(v, ctx)
    return v


# ── Step rendering ────────────────────────────────────────────────────────────

def _render_step(step_template, ctx, prefix, seq):
    if "id_suffix" not in step_template:
        raise ValueError(f"Step template missing 'id_suffix': {step_template}")
    rendered = _substitute_dict({k: v for k, v in step_template.items() if k != "id_suffix"}, ctx)
    # Prepend id and sequenceIndex so they appear early in the JSON output for readability.
    out = {"id": f"step_{prefix}_{step_template['id_suffix']}"}
    if "name" in rendered:
        out["name"] = rendered.pop("name")
    out["sequenceIndex"] = seq
    if "family" in rendered:
        out["family"] = rendered.pop("family")
    out.update(rendered)
    return out


# ── Main orchestration ────────────────────────────────────────────────────────

def instantiate(instance_yaml_path, output_path=None):
    instance = load_instantiation(instance_yaml_path)

    prefab_name = instance.get("prefab")
    if not prefab_name:
        raise ValueError("Instantiation YAML must specify 'prefab:' field")

    prefab, prefab_path = load_prefab(prefab_name)

    prefix = instance.get("prefix")
    if not prefix:
        raise ValueError("Instantiation YAML must specify 'prefix:' field")

    start_seq = instance.get("start_seq")
    if start_seq is None:
        raise ValueError("Instantiation YAML must specify 'start_seq:' field")
    start_seq = int(start_seq)

    ctx = _validate_and_resolve_roles(prefab, instance)
    ctx.update(_resolve_options(prefab, instance))
    _resolve_derived(prefab, ctx)

    step_templates = prefab.get("steps", []) or []
    if not step_templates:
        raise ValueError(f"Prefab '{prefab_name}' has no steps defined")

    steps = [_render_step(st, ctx, prefix, start_seq + i) for i, st in enumerate(step_templates)]

    if output_path is None:
        stem = Path(instance_yaml_path).stem
        OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)
        output_path = OUTPUTS_DIR / f"{stem}.json"

    Path(output_path).write_text(
        json.dumps(steps, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    print(f"Generated {len(steps)} steps  ->  {output_path}")
    print(f"  seqIndex range: {steps[0]['sequenceIndex']} – {steps[-1]['sequenceIndex']}")
    print(f"  Prefab: {prefab_name}  (file: {prefab_path.relative_to(REPO_ROOT)})")
    print(f"  Prefix: {prefix}")
    print()
    print("Next steps:")
    print(f"  1. Review {output_path}")
    print(f"  2. Merge steps[] array into your target assembly file")
    print(f"  3. python tools/package_health.py <packageId> --fix-seqindex")

    return steps


def list_prefabs():
    print(f"Prefabs in {PREFABS_DIR}:")
    found = sorted(PREFABS_DIR.glob("*.yaml"))
    if not found:
        print("  (none — create one in AgentAssistant/prefabs/<name>.yaml)")
        return
    for p in found:
        try:
            data = yaml.safe_load(p.read_text(encoding="utf-8"))
            name = data.get("prefab", p.stem)
            desc = data.get("description", "(no description)")
            steps = len(data.get("steps", []) or [])
            roles = list((data.get("roles") or {}).keys())
            print(f"  {name}  ({steps} steps, roles: {', '.join(roles) or '(none)'})")
            print(f"    {desc}")
        except Exception as e:
            print(f"  {p.stem}  [ERROR loading: {e}]")


# ── Patch-plan emission (Sub-slice A) ─────────────────────────────────────────
# Emits a layer-by-layer plan describing the intended (file, region, content)
# tuples that fully instantiating a 7-layer prefab (BatchCarriageUnit) would
# produce. NEVER WRITES to the package — output goes to
# AgentAssistant/outputs/<stem>_patch_plan.json. The accompanying diff routine
# compares each layer entry against current package state and reports gaps.

def _round4(v):
    if isinstance(v, float):
        return round(v, 4)
    if isinstance(v, dict):
        return {k: _round4(x) for k, x in v.items()}
    if isinstance(v, list):
        return [_round4(x) for x in v]
    return v


def _vec3_add(origin, offset):
    return _round4({
        "x": origin["x"] + offset.get("x", 0.0),
        "y": origin["y"] + offset.get("y", 0.0),
        "z": origin["z"] + offset.get("z", 0.0),
    })


def _resolve_part_id(role_value):
    return role_value.items if isinstance(role_value, ListRole) else role_value


def _load_package_catalog(package_root):
    """Load every assembly JSON in <package_root>/assemblies/ and index parts,
    steps, targets, partGroups, assemblies by id. Each value is (file_path, dict).
    Also loads preview_config.json placements by partId."""
    asm_dir = Path(package_root) / "assemblies"
    parts, steps, targets, partGroups, assemblies = {}, {}, {}, {}, {}
    for fp in sorted(asm_dir.glob("*.json")):
        d = json.loads(fp.read_text(encoding="utf-8"))
        for p in d.get("parts") or []:
            parts[p["id"]] = (str(fp), p)
        for s in d.get("steps") or []:
            steps[s["id"]] = (str(fp), s)
        for t in d.get("targets") or []:
            targets[t["id"]] = (str(fp), t)
        for g in d.get("partGroups") or []:
            partGroups[g["id"]] = (str(fp), g)
        for a in d.get("assemblies") or []:
            assemblies[a["id"]] = (str(fp), a)
    preview_path = Path(package_root) / "preview_config.json"
    placements = {}
    if preview_path.exists():
        pc = json.loads(preview_path.read_text(encoding="utf-8"))
        for pl in (pc.get("previewConfig") or {}).get("partPlacements") or []:
            placements[pl["partId"]] = pl
    return {
        "parts": parts, "steps": steps, "targets": targets,
        "partGroups": partGroups, "assemblies": assemblies,
        "previewPlacements": placements,
    }


def build_patch_plan(instance_yaml_path, package_root):
    instance = load_instantiation(instance_yaml_path)
    if instance.get("prefab") != "BatchCarriageUnit":
        raise ValueError("Patch-plan mode currently supports only BatchCarriageUnit prefab")
    prefab, _ = load_prefab(instance["prefab"])

    ctx = _validate_and_resolve_roles(prefab, instance)
    ctx.update(_resolve_options(prefab, instance))
    _resolve_derived(prefab, ctx)

    origin = {"x": float(ctx["origin_x"]), "y": float(ctx["origin_y"]), "z": float(ctx["origin_z"])}
    prefix = ctx["prefix"]
    target_prefix = ctx["target_prefix"]
    rail_label = ctx["rail_label"]
    partgroup_id = ctx["partgroup_id"]
    partgroup_name = ctx["partgroup_name"]
    start_seq = int(ctx["start_seq"])
    parts_file = ctx["parts_assembly_file"]
    build_file = ctx.get("build_assembly_file", "assembly_d3d_batch_carriage_build.json")

    pkg_assemblies = f"{package_root}/assemblies"
    parts_path = f"{pkg_assemblies}/{parts_file}"
    build_path = f"{pkg_assemblies}/{build_file}"
    preview_path = f"{package_root}/preview_config.json"

    # Sub-slice B: load canonical catalog from disk. For round-trip and
    # c1-mirroring instances, the disk block is the source of truth — emit
    # verbatim. The YAML's partDefinitions/steps/targets are a FALLBACK
    # only when an id is genuinely net-new (not yet on disk).
    catalog = _load_package_catalog(package_root)

    plan = []

    # ── LAYER 1: parts[] — canonical-from-disk if present ────────────────────
    part_entries = []
    role_pid_pairs = []  # (role, pid) — preserves declared order for downstream
    for role in ("half_a", "half_b"):
        role_pid_pairs.append((role, _resolve_part_id(ctx[role])))
    for pid in ctx["bearings"].items:
        role_pid_pairs.append(("bearings", pid))
    for role in ("bolt_top_a", "bolt_top_b", "bolt_bot_a", "bolt_bot_b"):
        role_pid_pairs.append((role, _resolve_part_id(ctx[role])))
    for pid in ctx["nuts"].items:
        role_pid_pairs.append(("nuts", pid))

    pdefs = prefab.get("partDefinitions", {})
    for role, pid in role_pid_pairs:
        if pid in catalog["parts"]:
            # Canonical: emit disk block verbatim (this is the source of truth).
            _file, disk_part = catalog["parts"][pid]
            part_entries.append({"_canonical": True, "_file": _file, "block": disk_part})
        else:
            # Fallback: synthesize from YAML partDefinitions (net-new path).
            decl = pdefs.get(role) or {}
            sp = decl.get("stagingPose") or {}
            offset = sp.get("position_offset") or {"x": 0, "y": 0, "z": 0}
            position = _vec3_add(origin, offset)
            rot = _round4(sp.get("rotation", {"x": 0, "y": 0, "z": 0, "w": 1}))
            scl = _round4(sp.get("scale", {"x": 1, "y": 1, "z": 1}))
            col = sp.get("color", {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0})
            block = {
                "id": pid,
                "name": (decl.get("name_template") or "").format(**ctx),
                "function": ((decl.get("function_template") or "").format(**ctx)) or None,
                "category": decl.get("category"),
                "material": decl.get("material"),
                "quantity": decl.get("quantity", 1),
                "assetRef": decl.get("assetRef"),
                "stagingPose": {"position": position, "rotation": rot, "scale": scl, "color": col},
                "partGroupIds": [s.format(**ctx) for s in (decl.get("partGroupIds") or [])],
            }
            part_entries.append({"_canonical": False, "_file": parts_path, "block": block})

    plan.append({
        "layer": "1_parts",
        "file": parts_path,
        "operation": "merge_by_id_preserve_metadata",
        "merge_rule": "Canonical mode: existing parts emitted verbatim from disk. Net-new parts use YAML partDefinitions fallback.",
        "entries": part_entries,
    })

    # ── LAYER 2: preview_config partPlacements ───────────────────────────────
    pc_entries = []
    pc_policy = prefab.get("previewConfigPolicy", {})
    for pe in part_entries:
        pid = pe["block"]["id"]
        if pid in catalog["previewPlacements"]:
            pc_entries.append({"_canonical": True, "block": catalog["previewPlacements"][pid]})
        else:
            sp = pe["block"].get("stagingPose") or {}
            pc_entries.append({"_canonical": False, "block": {
                "partId": pid,
                "startPosition": sp.get("position"),
                "startRotation": sp.get("rotation"),
                "startScale":    sp.get("scale"),
                "color":         sp.get("color"),
                "assembledPosition": "<TTAW-AUTHORED — defer to TTAW pass>",
                "assembledRotation": "<TTAW-AUTHORED — defer to TTAW pass>",
                "assembledScale":    {"x": 1.0, "y": 1.0, "z": 1.0},
                "stepPoses": pc_policy.get("partPlacementShape", {}).get("stepPoses_default", []),
                "splinePath": pc_policy.get("partPlacementShape", {}).get("splinePath_default", {}),
            }})
    plan.append({
        "layer": "2_preview_config_partPlacements",
        "file": preview_path,
        "operation": "merge_by_partId_preserve_assembled",
        "merge_rule": "Canonical mode: existing placements emitted verbatim. Never overwrite TTAW-authored assembledPosition/Rotation.",
        "entries": pc_entries,
    })

    # ── LAYER 3: partGroup (canonical from disk) ─────────────────────────────
    if partgroup_id in catalog["partGroups"]:
        _file, disk_pg = catalog["partGroups"][partgroup_id]
        layer3 = {
            "layer": "3_partGroup_animationCues",
            "file": _file,
            "operation": "merge_by_id_partgroup",
            "partGroup_id": partgroup_id,
            "_canonical": True,
            "block": disk_pg,
            "cues_count": len(disk_pg.get("animationCues") or []),
        }
    else:
        layer3 = {
            "layer": "3_partGroup_animationCues",
            "file": build_path,
            "operation": "merge_by_id_partgroup",
            "partGroup_id": partgroup_id,
            "_canonical": False,
            "status": "NET_NEW: clone from a sibling partGroup (e.g. partGroup_carriage_y_left), apply substitutions in partGroupDefinition.animationCues_substitutions.",
        }
    plan.append(layer3)

    # ── LAYER 4: 7 step definitions (canonical from disk) ────────────────────
    step_entries = []
    for i, st in enumerate(prefab.get("steps", [])):
        step_id = f"step_{prefix}_{st['id_suffix']}"
        if step_id in catalog["steps"]:
            _file, disk_step = catalog["steps"][step_id]
            step_entries.append({"_canonical": True, "_file": _file, "block": disk_step})
        else:
            # Fallback: shape-only (net-new path; full body deferred).
            step_entries.append({"_canonical": False, "_file": build_path, "block": {
                "id": step_id,
                "sequenceIndex": start_seq + i,
                "family": st.get("family"),
                "partGroupId": partgroup_id,
                "name": _substitute_value(st.get("name", ""), ctx),
                "instructionText": _substitute_value(st.get("instructionText"), ctx) if st.get("instructionText") else None,
                "guidance": _substitute_value(st.get("guidance"), ctx) if st.get("guidance") else None,
                "validation": _substitute_value(st.get("validation"), ctx) if st.get("validation") else None,
                "feedback": _substitute_value(st.get("feedback"), ctx) if st.get("feedback") else None,
                "requiredPartIds": _substitute_value(st.get("requiredPartIds", []), ctx),
                "visualPartIds": _substitute_value(st.get("visualPartIds", []), ctx) if st.get("visualPartIds") else None,
                "relevantToolIds": st.get("relevantToolIds"),
                "targetIds": _substitute_value(st.get("targetIds", []), ctx) if st.get("targetIds") else [],
                "_NET_NEW": "Body needs taskOrder + requiredToolActions (full Unity-default fields) — clone from sibling step and apply substitutions.",
            }})
    plan.append({
        "layer": "4_steps",
        "file": build_path,
        "operation": "replace_or_append_by_id",
        "merge_rule": "Canonical mode: existing steps emitted verbatim. Net-new steps need full taskOrder + requiredToolActions clone from sibling.",
        "entries": step_entries,
    })

    # ── LAYER 5: 4 anchor-resolved targets (canonical from disk) ─────────────
    target_entries = []
    for t in prefab.get("targets", []):
        target_id = f"target_{target_prefix}_{t['id_suffix']}"
        if target_id in catalog["targets"]:
            _file, disk_tg = catalog["targets"][target_id]
            target_entries.append({"_canonical": True, "_file": _file, "block": disk_tg})
        else:
            anchor_role = t["anchorRef"]
            anchor_pid = _resolve_part_id(ctx[anchor_role])
            target_entries.append({"_canonical": False, "_file": build_path, "block": {
                "id": target_id,
                "name": _substitute_value(t["name"], ctx),
                "associatedPartId": anchor_pid,
                "anchorRef": anchor_pid,
                "tags": [_substitute_value(s, ctx) for s in t.get("tags", [])],
                "weldAxis": t.get("weldAxis", {"x": 0, "y": 0, "z": 0}),
                "useToolActionRotation": t.get("useToolActionRotation", True),
                "toolActionRotation": t.get("toolActionRotation", {"x": 0, "y": 90, "z": 270}),
            }})
    plan.append({
        "layer": "5_targets",
        "file": build_path,
        "operation": "merge_by_id",
        "entries": target_entries,
    })

    # Computed step_ids / part_ids from canonical-or-fallback blocks
    step_ids = [e["block"]["id"] for e in step_entries]
    part_ids = [e["block"]["id"] for e in part_entries]

    # ── LAYER 6: roll-up references ───────────────────────────────────────────
    plan.append({
        "layer": "6_rollups",
        "file": build_path,
        "operation": "append_to_arrays",
        "rollups": {
            "assemblies[assembly_d3d_batch_carriage_build].stepIds": step_ids,
            "partGroups[partGroup_carriage_batch_all].partIds": part_ids,
            "partGroups[partGroup_carriage_batch_all].stepIds": step_ids,
            "partGroups[partGroup_carriage_batch_all].memberPartGroupIds": [partgroup_id],
        },
    })

    # ── LAYER 7: seqIndex shift policy ────────────────────────────────────────
    plan.append({
        "layer": "7_seqIndex_shift",
        "operation": "shift_global_seqindex",
        "rule": f"Increment sequenceIndex by +{len(step_entries)} for every step with sequenceIndex >= {start_seq + len(step_entries)} across ALL assembly files in {pkg_assemblies}/. (For c1 round-trip start_seq=53, no shift needed since c1 already occupies 53-59.)",
        "step_count": len(step_entries),
        "start_seq": start_seq,
        "validator_after_apply": "tools/package_health.py <pkgId> --fix-seqindex",
    })

    return plan


def diff_plan_against_package(plan, package_root):
    """Sub-slice B: deep byte-equivalence diff. For each canonical block in the
    plan, verify the on-disk block matches verbatim. Net-new blocks are flagged
    separately as 'awaiting Sub-slice C'.

    Returns a list of layer reports — empty 'drift' lists mean byte-equivalent.
    """
    catalog = _load_package_catalog(package_root)
    reports = []

    def _deep_diff(a, b, path=""):
        """Return list of paths where a and b differ. Empty = byte-equivalent."""
        if type(a) != type(b):
            # JSON int/float unification
            if isinstance(a, (int, float)) and isinstance(b, (int, float)):
                return [] if a == b else [f"{path}: {a} != {b}"]
            return [f"{path}: type mismatch {type(a).__name__} vs {type(b).__name__}"]
        if isinstance(a, dict):
            diffs = []
            for k in set(a.keys()) | set(b.keys()):
                if k not in a:
                    diffs.append(f"{path}.{k}: only_in_disk={b[k]!r}")
                elif k not in b:
                    diffs.append(f"{path}.{k}: only_in_plan={a[k]!r}")
                else:
                    diffs.extend(_deep_diff(a[k], b[k], f"{path}.{k}"))
            return diffs
        if isinstance(a, list):
            if len(a) != len(b):
                return [f"{path}: length {len(a)} != {len(b)}"]
            diffs = []
            for i, (x, y) in enumerate(zip(a, b)):
                diffs.extend(_deep_diff(x, y, f"{path}[{i}]"))
            return diffs
        return [] if a == b else [f"{path}: {a!r} != {b!r}"]

    for entry in plan:
        layer = entry["layer"]
        report = {"layer": layer, "operation": entry.get("operation")}

        if layer in ("1_parts", "4_steps", "5_targets"):
            kind = {"1_parts": "parts", "4_steps": "steps", "5_targets": "targets"}[layer]
            canonical_match = []
            canonical_drift = []
            net_new = []
            for e in entry["entries"]:
                blk = e["block"]
                bid = blk["id"]
                if not e.get("_canonical"):
                    net_new.append(bid)
                    continue
                disk = catalog[kind].get(bid)
                if disk is None:
                    canonical_drift.append({"id": bid, "diff": "MISSING_ON_DISK_AT_DIFF_TIME"})
                    continue
                _file, disk_blk = disk
                d = _deep_diff(blk, disk_blk, path=bid)
                if d:
                    canonical_drift.append({"id": bid, "diffs": d[:8] + (["..."] if len(d) > 8 else [])})
                else:
                    canonical_match.append(bid)
            report["canonical_byte_equivalent"] = canonical_match
            report["canonical_drift"] = canonical_drift
            report["net_new_awaiting_clone"] = net_new

        elif layer == "2_preview_config_partPlacements":
            match = []
            drift = []
            net_new = []
            for e in entry["entries"]:
                blk = e["block"]
                pid = blk["partId"]
                if not e.get("_canonical"):
                    net_new.append(pid)
                    continue
                disk_blk = catalog["previewPlacements"].get(pid)
                if disk_blk is None:
                    drift.append({"partId": pid, "diff": "MISSING_ON_DISK"})
                    continue
                d = _deep_diff(blk, disk_blk, path=pid)
                if d:
                    drift.append({"partId": pid, "diffs": d[:8]})
                else:
                    match.append(pid)
            report["canonical_byte_equivalent"] = match
            report["canonical_drift"] = drift
            report["net_new_awaiting_clone"] = net_new

        elif layer == "3_partGroup_animationCues":
            if entry.get("_canonical"):
                disk = catalog["partGroups"].get(entry["partGroup_id"])
                if disk is None:
                    report["status"] = "missing_on_disk"
                else:
                    _file, disk_blk = disk
                    d = _deep_diff(entry["block"], disk_blk, path=entry["partGroup_id"])
                    report["byte_equivalent"] = (len(d) == 0)
                    report["cues_count"] = len(disk_blk.get("animationCues") or [])
                    if d:
                        report["drift"] = d[:8]
            else:
                report["status"] = entry.get("status")

        elif layer == "6_rollups":
            asm_disk = catalog["assemblies"].get("assembly_d3d_batch_carriage_build")
            ba_disk = catalog["partGroups"].get("partGroup_carriage_batch_all")
            asm_step_ids = set((asm_disk[1] if asm_disk else {}).get("stepIds") or [])
            ba_part_ids = set((ba_disk[1] if ba_disk else {}).get("partIds") or [])
            ba_step_ids = set((ba_disk[1] if ba_disk else {}).get("stepIds") or [])
            ba_member = set((ba_disk[1] if ba_disk else {}).get("memberPartGroupIds") or [])
            rolls = entry["rollups"]
            report["assembly_stepIds_missing"] = [s for s in rolls["assemblies[assembly_d3d_batch_carriage_build].stepIds"] if s not in asm_step_ids]
            report["batch_all_partIds_missing"] = [p for p in rolls["partGroups[partGroup_carriage_batch_all].partIds"] if p not in ba_part_ids]
            report["batch_all_stepIds_missing"] = [s for s in rolls["partGroups[partGroup_carriage_batch_all].stepIds"] if s not in ba_step_ids]
            report["batch_all_memberPartGroupIds_missing"] = [m for m in rolls["partGroups[partGroup_carriage_batch_all].memberPartGroupIds"] if m not in ba_member]

        elif layer == "7_seqIndex_shift":
            report["rule"] = entry.get("rule")

        elif layer == "7_seqIndex_shift":
            report["rule"] = entry["rule"]

        reports.append(report)

    return reports


# ── Sub-slice C: clone-from-canonical apply plan ─────────────────────────────
# Reads source canonical (c1) from the package catalog, applies token + partId
# substitutions and origin translation to produce target blocks (c2/c3/c4),
# then plans remove/insert/shift operations against current target state.

def _walk_substitute(node, str_subs, parts_remap, origin_delta):
    """Recursively rewrite strings (token replace), partIds (in known fields),
    and translate xyz position dicts by origin_delta. Used to retarget c1
    canonical blocks into c2/c3/c4."""
    if isinstance(node, dict):
        out = {}
        for k, v in node.items():
            # Detect "position" dicts with x/y/z floats and translate.
            if k in ("position",) and isinstance(v, dict) and {"x", "y", "z"} <= set(v.keys()):
                out[k] = {
                    "x": _round4(v["x"] + origin_delta["x"]),
                    "y": _round4(v["y"] + origin_delta["y"]),
                    "z": _round4(v["z"] + origin_delta["z"]),
                }
                continue
            # Translate fromPose/toPose.position similarly.
            if k in ("fromPose", "toPose") and isinstance(v, dict) and isinstance(v.get("position"), dict):
                pos = v["position"]
                v = dict(v)
                v["position"] = {
                    "x": _round4(pos.get("x", 0) + origin_delta["x"]),
                    "y": _round4(pos.get("y", 0) + origin_delta["y"]),
                    "z": _round4(pos.get("z", 0) + origin_delta["z"]),
                }
                # Continue recursing to apply other substitutions in nested fields.
            # endTransform.position translate too.
            if k == "endTransform" and isinstance(v, dict) and isinstance(v.get("position"), dict):
                pos = v["position"]
                # Only translate if position is non-zero (zero endTransform is a sentinel).
                if any(abs(pos.get(ax, 0.0)) > 1e-6 for ax in ("x", "y", "z")):
                    v = dict(v)
                    v["position"] = {
                        "x": _round4(pos.get("x", 0) + origin_delta["x"]),
                        "y": _round4(pos.get("y", 0) + origin_delta["y"]),
                        "z": _round4(pos.get("z", 0) + origin_delta["z"]),
                    }
            # partId rewrite — fields whose values are partIds.
            if k in ("partId", "associatedPartId", "anchorRef", "id") and isinstance(v, str):
                if v in parts_remap:
                    out[k] = parts_remap[v]
                    continue
            out[k] = _walk_substitute(v, str_subs, parts_remap, origin_delta)
        return out
    if isinstance(node, list):
        return [_walk_substitute(x, str_subs, parts_remap, origin_delta) for x in node]
    if isinstance(node, str):
        s = node
        # 1) parts_remap full-string match (catches partGroupIds entries, requiredPartIds, etc.)
        if s in parts_remap:
            return parts_remap[s]
        # 2) token rewrites (longest-first to avoid prefix collisions)
        for src in sorted(str_subs.keys(), key=len, reverse=True):
            if src in s:
                s = s.replace(src, str_subs[src])
        return s
    return node


def build_clone_apply_plan(target_yaml_path, package_root):
    instance = load_instantiation(target_yaml_path)
    if instance.get("prefab") != "BatchCarriageUnit":
        raise ValueError("Clone-apply mode supports only BatchCarriageUnit prefab")
    prefab, _ = load_prefab(instance["prefab"])

    clone_src = instance.get("clone_from") or {}
    if not clone_src:
        raise ValueError("clone_from: block missing — required for --apply-clone")
    src_prefix = clone_src["prefix"]
    src_target_prefix = clone_src["target_prefix"]
    src_partgroup_id = clone_src["partgroup_id"]
    src_origin = clone_src["origin"]

    ctx = _validate_and_resolve_roles(prefab, instance)
    ctx.update(_resolve_options(prefab, instance))
    _resolve_derived(prefab, ctx)

    tgt_prefix = ctx["prefix"]
    tgt_target_prefix = ctx["target_prefix"]
    tgt_partgroup_id = ctx["partgroup_id"]
    tgt_partgroup_name = ctx["partgroup_name"]
    tgt_origin = {"x": float(ctx["origin_x"]), "y": float(ctx["origin_y"]), "z": float(ctx["origin_z"])}
    start_seq = int(ctx["start_seq"])

    origin_delta = {ax: tgt_origin[ax] - src_origin[ax] for ax in ("x", "y", "z")}
    str_subs = {
        f"step_{src_prefix}_": f"step_{tgt_prefix}_",
        f"target_{src_target_prefix}_": f"target_{tgt_target_prefix}_",
        src_partgroup_id: tgt_partgroup_id,
        f"action_target_{src_target_prefix}_": f"action_target_{tgt_target_prefix}_",
        clone_src.get("rail", "") + "_": (ctx.get("rail") or "") + "_",
    }
    # Extra user-supplied subs (human labels, tags, etc.) — applied longest-first.
    for k, v in (clone_src.get("extra_subs") or {}).items():
        str_subs[k] = v
    # Merge extra_subs from clone_from (longest-first applied in _walk_substitute).
    for k, v in (clone_src.get("extra_subs") or {}).items():
        str_subs[k] = v
    parts_remap = instance.get("parts_remap") or {}

    catalog = _load_package_catalog(package_root)

    # ── Source canonical lookup ──────────────────────────────────────────────
    source_step_ids = [f"step_{src_prefix}_{st['id_suffix']}" for st in prefab.get("steps", [])]
    source_target_ids = [f"target_{src_target_prefix}_{t['id_suffix']}" for t in prefab.get("targets", [])]

    src_steps = []
    for sid in source_step_ids:
        if sid not in catalog["steps"]:
            raise ValueError(f"Source step {sid} not found in package — clone source incomplete")
        src_steps.append(catalog["steps"][sid][1])
    src_targets = []
    for tid in source_target_ids:
        if tid not in catalog["targets"]:
            raise ValueError(f"Source target {tid} not found")
        src_targets.append(catalog["targets"][tid][1])
    if src_partgroup_id not in catalog["partGroups"]:
        raise ValueError(f"Source partGroup {src_partgroup_id} not found")
    src_partgroup = catalog["partGroups"][src_partgroup_id][1]

    # ── Retarget canonical blocks → cloned target blocks ─────────────────────
    cloned_steps = []
    for i, src in enumerate(src_steps):
        cloned = _walk_substitute(src, str_subs, parts_remap, origin_delta)
        cloned["sequenceIndex"] = start_seq + i  # explicitly assign the new seq slot
        cloned_steps.append(cloned)

    cloned_targets = [_walk_substitute(t, str_subs, parts_remap, origin_delta) for t in src_targets]

    cloned_partgroup_cues = [_walk_substitute(c, str_subs, parts_remap, origin_delta)
                             for c in (src_partgroup.get("animationCues") or [])]

    # Compute the seqIndex shift: how many net new steps does target gain?
    # target's existing step IDs in this prefix range are the steps to remove.
    existing_target_step_ids = []
    for s_meta in catalog["steps"].values():
        s = s_meta[1]
        if s.get("id", "").startswith(f"step_{tgt_prefix}_"):
            existing_target_step_ids.append((s["id"], s.get("sequenceIndex", -1)))
    existing_target_step_ids.sort(key=lambda x: x[1])
    net_step_delta = len(cloned_steps) - len(existing_target_step_ids)

    # Apply order: (1) remove old block, (2) shift remaining disk steps with
    # seq >= start_seq+len(old_block), (3) insert new block at start_seq.
    # The threshold uses the OLD block size so the seq slot freed by removal
    # is bridged correctly when the new (potentially larger) block inserts.
    shift_threshold = start_seq + len(existing_target_step_ids)  # 60+6 = 66 for c2

    # Plan operations
    plan = {
        "summary": {
            "source": {"prefix": src_prefix, "partgroup_id": src_partgroup_id, "origin": src_origin},
            "target": {"prefix": tgt_prefix, "partgroup_id": tgt_partgroup_id, "origin": tgt_origin,
                       "name": tgt_partgroup_name, "rail": ctx.get("rail")},
            "origin_delta": origin_delta,
            "str_subs": str_subs,
            "parts_remap_count": len(parts_remap),
            "cloned_step_count": len(cloned_steps),
            "cloned_target_count": len(cloned_targets),
            "cloned_partgroup_cues_count": len(cloned_partgroup_cues),
            "existing_target_step_count": len(existing_target_step_ids),
            "net_step_delta": net_step_delta,
            "seqIndex_shift_threshold": shift_threshold,
            "seqIndex_shift_amount": net_step_delta,
            "start_seq": start_seq,
        },
        "operations": [
            {
                "op": "remove_steps",
                "ids": [sid for sid, _ in existing_target_step_ids],
                "rationale": f"Drop existing {len(existing_target_step_ids)}-step block before inserting cloned 7-step block.",
            },
            {
                "op": "shift_seqindex_globally",
                "rule": f"For every step on disk with sequenceIndex >= {shift_threshold} (excluding those being removed), add {net_step_delta} to sequenceIndex.",
                "scope": "all assembly_*.json files in <package>/assemblies/",
            },
            {
                "op": "insert_steps",
                "count": len(cloned_steps),
                "ids_and_seqs": [(s["id"], s["sequenceIndex"]) for s in cloned_steps],
                "target_file": f"{package_root}/assemblies/assembly_d3d_batch_carriage_build.json",
            },
            {
                "op": "replace_partGroup_animationCues",
                "partGroup_id": tgt_partgroup_id,
                "old_cue_count": len(src_partgroup.get("animationCues") or []),  # source has same count assumption
                "new_cue_count": len(cloned_partgroup_cues),
            },
            {
                "op": "insert_or_merge_targets",
                "ids": [t["id"] for t in cloned_targets],
                "target_file": f"{package_root}/assemblies/assembly_d3d_batch_carriage_build.json",
            },
            {
                "op": "update_rollups",
                "assembly_id": "assembly_d3d_batch_carriage_build",
                "new_step_ids": [s["id"] for s in cloned_steps],
                "partGroup_carriage_batch_all_stepIds_rewrite": "Replace removed step IDs with cloned step IDs in-place.",
                "partGroup_target_stepIds_rewrite": f"{tgt_partgroup_id}.stepIds = [layout, clean_holes, qc_plastic] + cloned_step_ids",
            },
        ],
        "cloned_blocks": {
            "steps":   cloned_steps,
            "targets": cloned_targets,
            "partGroup_animationCues": cloned_partgroup_cues,
            "partGroup_stepIds": [
                "step_batch_carriage_layout",
                "step_batch_carriage_clean_holes",
                "step_batch_carriage_qc_plastic",
            ] + [s["id"] for s in cloned_steps],
        },
    }
    return plan


def apply_clone_plan(target_yaml_path, package_root):
    """Sub-slice C --write: apply the clone-from-canonical plan to disk.
    Order: (1) global seqIndex shift on all assembly files for steps
    with seq >= shift_threshold, (2) remove old target block, (3) insert
    new cloned blocks in batch_carriage_build.json, (4) replace partGroup
    cues + stepIds, (5) insert targets, (6) rewrite rollup arrays."""
    plan = build_clone_apply_plan(target_yaml_path, package_root)
    s = plan["summary"]
    threshold = s["seqIndex_shift_threshold"]
    delta = s["seqIndex_shift_amount"]
    tgt_partgroup_id = s["target"]["partgroup_id"]
    cloned = plan["cloned_blocks"]

    asm_dir = Path(package_root) / "assemblies"
    files_changed = []

    # Step 1: global seqIndex shift on every assembly file.
    for fp in sorted(asm_dir.glob("*.json")):
        d = json.loads(fp.read_text(encoding="utf-8"))
        steps = d.get("steps") or []
        if not steps:
            continue
        # Skip steps being removed (they'll vanish in step 2).
        remove_ids = set(plan["operations"][0]["ids"])
        changed = False
        for st in steps:
            if st.get("id") in remove_ids:
                continue
            seq = st.get("sequenceIndex")
            if isinstance(seq, int) and seq >= threshold:
                st["sequenceIndex"] = seq + delta
                changed = True
        if changed:
            fp.write_text(json.dumps(d, indent=2, ensure_ascii=False), encoding="utf-8")
            files_changed.append(str(fp))

    # Step 2-6: structural edits in batch_carriage_build.json
    build_fp = asm_dir / "assembly_d3d_batch_carriage_build.json"
    d = json.loads(build_fp.read_text(encoding="utf-8"))

    remove_ids = set(plan["operations"][0]["ids"])
    new_step_ids = [s["id"] for s in cloned["steps"]]

    # 2: remove old c2 step blocks; insert cloned at the same logical position
    d["steps"] = [s for s in (d.get("steps") or []) if s.get("id") not in remove_ids]
    # Append cloned steps; sort by sequenceIndex preserves global ordering.
    d["steps"].extend(cloned["steps"])
    d["steps"].sort(key=lambda s: s.get("sequenceIndex", 0))

    # 3: replace partGroup target's animationCues + stepIds
    for g in d.get("partGroups") or []:
        if g.get("id") == tgt_partgroup_id:
            g["animationCues"] = cloned["partGroup_animationCues"]
            g["stepIds"] = cloned["partGroup_stepIds"]
            break

    # 4: insert/merge targets (no overwrite — append only if id not present)
    existing_target_ids = {t["id"] for t in d.get("targets") or []}
    for t in cloned["targets"]:
        if t["id"] not in existing_target_ids:
            d.setdefault("targets", []).append(t)

    # 5: rollups — assembly.stepIds + partGroup_carriage_batch_all.stepIds
    # Replace removed IDs in-place with cloned ones; keep ordering.
    def _rewrite_step_ids(arr, removed, replacement):
        out = []
        inserted = False
        for sid in arr:
            if sid in removed:
                if not inserted:
                    out.extend(replacement)
                    inserted = True
                continue
            out.append(sid)
        if not inserted:
            out.extend(replacement)
        return out

    for a in d.get("assemblies") or []:
        if a.get("id") == "assembly_d3d_batch_carriage_build":
            a["stepIds"] = _rewrite_step_ids(a.get("stepIds") or [], remove_ids, new_step_ids)
            break
    for g in d.get("partGroups") or []:
        if g.get("id") == "partGroup_carriage_batch_all":
            g["stepIds"] = _rewrite_step_ids(g.get("stepIds") or [], remove_ids, new_step_ids)
            break

    build_fp.write_text(json.dumps(d, indent=2, ensure_ascii=False), encoding="utf-8")
    if str(build_fp) not in files_changed:
        files_changed.append(str(build_fp))

    print(f"APPLIED clone-plan to {len(files_changed)} files.")
    for f in files_changed:
        print(f"  ~ {f}")
    print()
    print("Run `python tools/package_health.py d3d_v18_10 --fix-seqindex` to validate.")
    return files_changed


def emit_clone_apply_plan(target_yaml_path, package_root, output_path=None):
    plan = build_clone_apply_plan(target_yaml_path, package_root)
    if output_path is None:
        stem = Path(target_yaml_path).stem
        OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)
        output_path = OUTPUTS_DIR / f"{stem}_clone_plan.json"
    Path(output_path).write_text(
        json.dumps(plan, indent=2, ensure_ascii=False, default=str),
        encoding="utf-8",
    )
    s = plan["summary"]
    print(f"Clone-apply DRY-RUN  ->  {output_path}")
    print()
    print("Summary:")
    for k, v in s.items():
        print(f"  {k}: {v}")
    print()
    print("Operations:")
    for op in plan["operations"]:
        print(f"  - {op['op']}: " + ", ".join(f"{k}={v if not isinstance(v, list) else f'({len(v)} items)'}"
                                              for k, v in op.items() if k != "op")[:200])
    print()
    print("NO files were modified. Re-run with --write to apply.")
    return plan


def emit_patch_plan(instance_yaml_path, package_root, output_path=None):
    plan = build_patch_plan(instance_yaml_path, package_root)
    diff = diff_plan_against_package(plan, package_root)

    if output_path is None:
        stem = Path(instance_yaml_path).stem
        OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)
        output_path = OUTPUTS_DIR / f"{stem}_patch_plan.json"

    bundle = {"plan": plan, "diff_against_disk": diff}
    Path(output_path).write_text(
        json.dumps(bundle, indent=2, ensure_ascii=False, default=str),
        encoding="utf-8",
    )

    print(f"Patch plan dry-run  ->  {output_path}")
    print(f"  Layers planned: {len(plan)}")
    print()
    print("Layer-by-layer gap report:")
    for r in diff:
        print(f"  [{r['layer']}]  op={r.get('operation','-')}")
        for k, v in r.items():
            if k in ("layer", "operation"):
                continue
            if isinstance(v, list):
                print(f"     {k}: ({len(v)}) {v if len(v) <= 6 else v[:6] + ['...']}")
            else:
                print(f"     {k}: {v}")
    print()
    print("NO files in Assets/_Project/Data/Packages/ were modified.")

    return bundle


def main():
    parser = argparse.ArgumentParser(
        description="Instantiate a Step Configuration Prefab into a step JSON array.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("input", nargs="?", help="Instantiation YAML path")
    parser.add_argument("--output", help="Output JSON path (default: AgentAssistant/outputs/<stem>.json)")
    parser.add_argument("--list-prefabs", action="store_true", help="List available prefabs and exit")
    parser.add_argument("--patch-plan", action="store_true",
                        help="Sub-slice A/B: emit a layer-by-layer patch plan + diff against package state. NEVER WRITES.")
    parser.add_argument("--apply-clone", action="store_true",
                        help="Sub-slice C: emit a clone-from-canonical apply plan (target instance YAML w/ clone_from: block). NEVER WRITES; pair with --write to apply.")
    parser.add_argument("--write", action="store_true",
                        help="With --apply-clone: actually write the changes to the package.")
    parser.add_argument("--package-root",
                        help="Package root for --patch-plan/--apply-clone (e.g. Assets/_Project/Data/Packages/d3d_v18_10).")
    args = parser.parse_args()

    if args.list_prefabs:
        list_prefabs()
        return

    if not args.input:
        parser.print_help()
        sys.exit(1)

    input_path = Path(args.input)
    if not input_path.exists():
        candidate = INPUTS_DIR / args.input
        if candidate.exists():
            input_path = candidate
        else:
            print(f"ERROR: Input file not found: {args.input}", file=sys.stderr)
            sys.exit(1)

    if args.patch_plan:
        if not args.package_root:
            print("ERROR: --patch-plan requires --package-root <path>", file=sys.stderr)
            sys.exit(1)
        emit_patch_plan(input_path, args.package_root, args.output)
        return

    if args.apply_clone:
        if not args.package_root:
            print("ERROR: --apply-clone requires --package-root <path>", file=sys.stderr)
            sys.exit(1)
        if args.write:
            apply_clone_plan(input_path, args.package_root)
        else:
            emit_clone_apply_plan(input_path, args.package_root, args.output)
        return

    instantiate(input_path, args.output)


if __name__ == "__main__":
    main()
