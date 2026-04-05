#!/usr/bin/env python3
"""
check_all.py — Run validate_machine_json on every machine.json in subdirectories
and print a summary table.

Usage:
    python3 check_all.py
    python3 check_all.py --no-color

Exit code: 0 if all clean or warnings-only, 1 if any package has errors.
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

# Allow running from any directory — locate validate_machine_json.py next to this file.
_HERE = Path(__file__).parent
sys.path.insert(0, str(_HERE))

from validate_machine_json import validate, _red, _yellow, _green, _bold, _dim, _NO_COLOR

# ── ANSI helpers (inherit _NO_COLOR state from parent module) ─────────────────

def _cyan(s: str) -> str:
    return s if _NO_COLOR else f"\033[36m{s}\033[0m"

def _right_pad(s: str, width: int) -> str:
    """Pad a string (stripping ANSI codes for length calculation) to `width` chars."""
    visible = _strip_ansi(s)
    pad = max(0, width - len(visible))
    return s + " " * pad

def _strip_ansi(s: str) -> str:
    import re
    return re.sub(r"\033\[[0-9;]*m", "", s)


# ── Main ──────────────────────────────────────────────────────────────────────

def main(argv: list[str] | None = None) -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Validate all machine.json packages and print a summary table."
    )
    parser.add_argument("--no-color", action="store_true", help="Disable ANSI color output.")
    parser.add_argument(
        "root",
        nargs="?",
        default=str(_HERE),
        help="Root directory to search for machine.json files (default: this script's directory).",
    )
    args = parser.parse_args(argv)

    global _NO_COLOR
    if args.no_color:
        import validate_machine_json as _vmj
        _vmj._NO_COLOR = True
        _NO_COLOR = True

    root = Path(args.root)
    paths = sorted(root.rglob("machine.json"))

    if not paths:
        print(f"No machine.json files found under {root}")
        return 0

    # ── Collect results ───────────────────────────────────────────────────────

    rows: list[dict] = []
    any_errors = False

    for path in paths:
        package_id = path.parent.name
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except json.JSONDecodeError as exc:
            rows.append({
                "package_id": package_id,
                "path": str(path.relative_to(root)),
                "errors": -1,
                "warnings": 0,
                "steps": 0,
                "targets": 0,
                "hints": 0,
                "parts": 0,
                "parse_error": str(exc),
            })
            any_errors = True
            continue

        result = validate(data)
        n_errors   = len(result.errors)
        n_warnings = len(result.warnings)
        if n_errors:
            any_errors = True
        rows.append({
            "package_id": package_id,
            "path": str(path.relative_to(root)),
            "errors": n_errors,
            "warnings": n_warnings,
            "steps":   len(data.get("steps", [])),
            "targets": len(data.get("targets", [])),
            "hints":   len(data.get("hints", [])),
            "parts":   len(data.get("parts", [])),
            "parse_error": None,
        })

    # ── Print table ───────────────────────────────────────────────────────────

    col_pkg  = max(len(r["package_id"]) for r in rows) + 2
    col_err  = 8
    col_warn = 10
    col_step = 7
    col_tgt  = 9
    col_hint = 7
    col_part = 7

    sep = _dim("─" * (col_pkg + col_err + col_warn + col_step + col_tgt + col_hint + col_part + 12))

    def _header_cell(s: str, w: int) -> str:
        return _bold(s.ljust(w))

    header = (
        _header_cell("Package", col_pkg) + "  " +
        _header_cell("Errors", col_err) + "  " +
        _header_cell("Warnings", col_warn) + "  " +
        _header_cell("Steps", col_step) + "  " +
        _header_cell("Targets", col_tgt) + "  " +
        _header_cell("Hints", col_hint) + "  " +
        _header_cell("Parts", col_part)
    )

    print()
    print(header)
    print(sep)

    for r in rows:
        pkg_cell = _right_pad(_cyan(r["package_id"]), col_pkg)

        if r["parse_error"]:
            err_cell  = _right_pad(_red("parse err"), col_err)
            warn_cell = _right_pad("", col_warn)
            step_cell = warn_cell
            tgt_cell  = warn_cell
            hint_cell = warn_cell
            part_cell = warn_cell
        else:
            n_err = r["errors"]
            n_warn = r["warnings"]
            if n_err > 0:
                err_cell = _right_pad(_red(str(n_err)), col_err)
            elif n_warn > 0:
                err_cell = _right_pad(_green("0"), col_err)
            else:
                err_cell = _right_pad(_green("0"), col_err)

            if n_warn > 0:
                warn_cell = _right_pad(_yellow(str(n_warn)), col_warn)
            else:
                warn_cell = _right_pad(_green("0"), col_warn)

            step_cell = _right_pad(str(r["steps"]),   col_step)
            tgt_cell  = _right_pad(str(r["targets"]), col_tgt)
            hint_cell = _right_pad(str(r["hints"]),   col_hint)
            part_cell = _right_pad(str(r["parts"]),   col_part)

        print(f"{pkg_cell}  {err_cell}  {warn_cell}  {step_cell}  {tgt_cell}  {hint_cell}  {part_cell}")

        if r["parse_error"]:
            print(f"  {_red('JSON parse error:')} {r['parse_error']}")

    print(sep)

    # Totals row
    total_err  = sum(r["errors"]   for r in rows if r["errors"] >= 0)
    total_warn = sum(r["warnings"] for r in rows)
    total_steps  = sum(r["steps"]   for r in rows)
    total_targets = sum(r["targets"] for r in rows)
    total_hints  = sum(r["hints"]   for r in rows)
    total_parts  = sum(r["parts"]   for r in rows)

    tot_err_cell  = _right_pad(_red(str(total_err)) if total_err else _green("0"), col_err)
    tot_warn_cell = _right_pad(_yellow(str(total_warn)) if total_warn else _green("0"), col_warn)
    totals = (
        _right_pad(_bold("TOTAL"), col_pkg) + "  " +
        tot_err_cell + "  " +
        tot_warn_cell + "  " +
        _right_pad(str(total_steps),   col_step) + "  " +
        _right_pad(str(total_targets), col_tgt) + "  " +
        _right_pad(str(total_hints),   col_hint) + "  " +
        _right_pad(str(total_parts),   col_part)
    )
    print(totals)
    print()

    # ── Per-package detail ────────────────────────────────────────────────────

    for r in rows:
        if r["parse_error"] or (r["errors"] == 0 and r["warnings"] == 0):
            continue
        # Re-validate for detailed output
        try:
            with open(root / r["path"], encoding="utf-8") as f:
                data = json.load(f)
        except Exception:
            continue
        from validate_machine_json import validate as _validate, _format_report
        result = _validate(data)
        print(_format_report(result, r["package_id"], data))
        print()

    if any_errors:
        print(_red("One or more packages have errors."))
        return 1
    elif any(r["warnings"] > 0 for r in rows):
        print(_yellow("All packages valid — warnings present."))
        return 0
    else:
        print(_green("All packages clean."))
        return 0


if __name__ == "__main__":
    sys.exit(main())
