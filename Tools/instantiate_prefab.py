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


def main():
    parser = argparse.ArgumentParser(
        description="Instantiate a Step Configuration Prefab into a step JSON array.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("input", nargs="?", help="Instantiation YAML path")
    parser.add_argument("--output", help="Output JSON path (default: AgentAssistant/outputs/<stem>.json)")
    parser.add_argument("--list-prefabs", action="store_true", help="List available prefabs and exit")
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

    instantiate(input_path, args.output)


if __name__ == "__main__":
    main()
