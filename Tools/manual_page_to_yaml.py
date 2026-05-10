"""
manual_page_to_yaml.py — Propose a Translator Input YAML for a given
manual page using the prebuilt index.

Usage:
    python tools/manual_page_to_yaml.py <index.json> <page>
    python tools/manual_page_to_yaml.py <index.json> <page> --instances y_left,z_back

What it does:
  1. Looks up the page in the index
  2. Pulls (template, axes) from the rollup
  3. Emits a Translator Input YAML stub to stdout (or --out path),
     pre-filling assembly, subassembly, template, and a parts skeleton
     with placeholders the agent fills in.

Stops short of guessing part IDs — that step still requires reading
the PDF page or asking the user. But it removes the boilerplate of
choosing the assembly, template, instance set, and subassembly name.
"""

from __future__ import annotations
import argparse
import json
import os
import sys

ASSEMBLY_BY_AXIS = {
    "y_left":  "assembly_d3d_y_left_bench",
    "y_right": "assembly_d3d_y_right_bench",
    "z_front": "assembly_d3d_z_front_bench",
    "z_back":  "assembly_d3d_z_back_bench",
    "x_axis":  "assembly_d3d_x_axis_bench",
}

# Skeleton parts dict per template. Caller fills in the actual partIds.
TEMPLATE_PARTS_SKELETON = {
    "BearingCarriage": {
        "half_a": "<axis>_carriage_half_a",
        "half_b": "<axis>_carriage_half_b",
        "bearings": ["<axis>_lm8uu_a", "<axis>_lm8uu_b",
                     "<axis>_lm8uu_c", "<axis>_lm8uu_d"],
        "bolts_top": ["<axis>_m6x18_a", "<axis>_m6x18_b"],
        "bolts_bot": ["<axis>_m6x30_a", "<axis>_m6x30_b"],
        "nuts": ["<axis>_m6_nut_a", "<axis>_m6_nut_b",
                 "<axis>_m6_nut_c", "<axis>_m6_nut_d"],
    },
    "IdlerHalves": {
        "half_a": "idler<n>",
        "half_b": "idler<n>_half_b",
        "bearings": ["<axis>_625zz_a", "<axis>_625zz_b"],
        "bolt_inner": "<axis>_m6x18_b",
        "bolt_inner_nut": "<axis>_m6_nut_inner",
        "bolt_frame_mount": "<axis>_idler_m6x30_a",
        "bolt_frame_mount_nut": "<axis>_m6_nut_frame",
        "bolt_last_hole": "<axis>_idler_m6x18_loose",
        "bolt_last_hole_nut": "<axis>_m6_nut_last",
    },
    "MotorHolder": {
        "motor": "motor<n>",
        "pulley": "<axis>_gt2_pulley",
        "belt": "<axis>_gt2_belt",
        "half_nuts": ["<axis>_motor_m6_nut_a", "<axis>_motor_m6_nut_b",
                      "<axis>_motor_m6_nut_c"],
        "belt_bolt": "<axis>_motor_m6x30_a",
        "motor_screws": ["<axis>_m3x25_a", "<axis>_m3x25_b",
                         "<axis>_m3x25_c", "<axis>_m3x25_d"],
        "close_bolts": ["<axis>_m6x18_c", "<axis>_m6x18_d", "<axis>_m6x18_e"],
    },
    "RodAssembly": {
        "rod_a": "rod_<axis>_a",
        "rod_b": "rod_<axis>_b",
        "idler": "idler<n>",
        "carriage": "<axis>_carriage",
        "motor_holder": "<axis>_motor_holder",
    },
    "BeltThread": {
        "belt": "<axis>_gt2_belt",
        "idler": "idler<n>",
        "peg_1": "<axis>_belt_peg_1",
        "peg_2": "<axis>_belt_peg_2",
    },
}


def find_page(index: dict, page: int) -> dict | None:
    for p in index["pages"]:
        if p["page"] == page:
            return p
    return None


def find_rollup(index: dict, template: str, page: int):
    for r in index["rollups"]:
        if r["template"] == template and page in r["pages"]:
            return r
    return None


def yaml_dump(d, indent=0):
    """Minimal YAML emitter — avoids pyyaml dep."""
    out = []
    pad = "  " * indent
    for k, v in d.items():
        if isinstance(v, dict):
            out.append(f"{pad}{k}:")
            out.append(yaml_dump(v, indent + 1))
        elif isinstance(v, list):
            if all(isinstance(x, str) for x in v):
                inline = ", ".join(v)
                out.append(f"{pad}{k}: [{inline}]")
            else:
                out.append(f"{pad}{k}:")
                for x in v:
                    out.append(f"{pad}  - {x}")
        elif isinstance(v, bool):
            out.append(f"{pad}{k}: {str(v).lower()}")
        elif v is None:
            out.append(f"{pad}{k}: null")
        else:
            out.append(f"{pad}{k}: {v}")
    return "\n".join(out)


def propose(index_path: str, page: int, instances_override: list[str] | None,
            out_path: str | None):
    with open(index_path, encoding="utf-8") as f:
        idx = json.load(f)
    p = find_page(idx, page)
    if not p:
        print(f"page {page} not in index", file=sys.stderr)
        return 1

    tpl = p.get("matches_template")
    if not tpl or tpl.startswith("_"):
        print(f"page {page} ({p.get('section')}) — no actionable template "
              f"(detected: {tpl}). Skipping.", file=sys.stderr)
        return 2

    axes = instances_override or p.get("applies_to") or []
    if not axes:
        print(f"page {page} — no axis instances detected; pass --instances",
              file=sys.stderr)
        return 3

    rollup = find_rollup(idx, tpl, page)
    page_range = f"{rollup['pages'][0]}-{rollup['pages'][-1]}" if rollup else str(page)

    yaml_blocks = []
    yaml_blocks.append(
        f"# Translator Input — generated from manual_page_to_yaml.py\n"
        f"# Source: {idx['source']} pages {page_range}\n"
        f"# Section: {p.get('section')} / {p.get('subtopic')}\n"
        f"# Template: {tpl}\n"
        f"# Instances: {','.join(axes)}\n"
        f"# Steps in section: " + "; ".join(
            f"{s['num']} {s['title']}" for s in p.get("steps", [])
        ) + "\n"
        "#\n"
        "# !! Replace placeholder partIds (<axis>_*, idler<n>, motor<n>) with real IDs.\n"
        "# !! Verify start_seq does not collide with existing assemblies.\n"
    )

    skeleton = TEMPLATE_PARTS_SKELETON.get(tpl, {})
    for axis in axes:
        assembly = ASSEMBLY_BY_AXIS.get(axis, f"assembly_d3d_{axis}_bench")
        sub = f"subassembly_{axis}_{tpl.lower()}"
        # Substitute axis into placeholder strings
        def sub_axis(x):
            if isinstance(x, str):
                return x.replace("<axis>", axis)
            if isinstance(x, list):
                return [sub_axis(e) for e in x]
            if isinstance(x, dict):
                return {k: sub_axis(v) for k, v in x.items()}
            return x
        parts = sub_axis(skeleton)
        block = {
            "assembly": assembly,
            "subassembly": sub,
            "template": tpl,
            "start_seq": "<TODO>",
            "parts": parts,
        }
        if tpl in {"BearingCarriage", "IdlerHalves", "MotorHolder"}:
            block["tool"] = "tool_power_drill"
            block["torque_setting"] = "lowest"
        block["milestone"] = f"{axis} {tpl} complete"
        yaml_blocks.append(f"---\n{yaml_dump(block)}")

    output = "\n".join(yaml_blocks) + "\n"

    if out_path:
        os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(output)
        print(f"wrote {out_path}", file=sys.stderr)
    else:
        sys.stdout.write(output)
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("index", help="path to <manual>.index.json")
    ap.add_argument("page", type=int, help="page number to author")
    ap.add_argument("--instances", help="comma-separated axes (overrides index)")
    ap.add_argument("--out", help="write YAML to file (default: stdout)")
    args = ap.parse_args()
    insts = args.instances.split(",") if args.instances else None
    sys.exit(propose(args.index, args.page, insts, args.out))


if __name__ == "__main__":
    main()
