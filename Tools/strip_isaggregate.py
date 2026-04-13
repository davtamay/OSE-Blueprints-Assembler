#!/usr/bin/env python3
"""
One-time cleanup: strip redundant `"isAggregate": true` fields from every
package JSON. The value is now auto-derived at load time by
MachinePackageNormalizer.InferAggregateFlag whenever `memberSubassemblyIds`
is populated, so the explicit flag is no longer needed.

Safe to re-run — idempotent. Skips files under `.migration_backups/`.

Usage:
    python Tools/strip_isaggregate.py                    # dry run, shows what would change
    python Tools/strip_isaggregate.py --write            # apply changes in place
    python Tools/strip_isaggregate.py --write --path X   # limit to a subtree
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys


def walk_strip(node, counter):
    """Remove `isAggregate` only when it's redundant with memberSubassemblyIds
    (the normalizer can re-derive it). PartId-overlap aggregates with no
    memberSubassemblyIds must keep the explicit flag — it's their only signal."""
    if isinstance(node, dict):
        if (
            "isAggregate" in node
            and isinstance(node["isAggregate"], bool)
            and isinstance(node.get("memberSubassemblyIds"), list)
            and len(node["memberSubassemblyIds"]) > 0
        ):
            del node["isAggregate"]
            counter[0] += 1
        for v in node.values():
            walk_strip(v, counter)
    elif isinstance(node, list):
        for item in node:
            walk_strip(item, counter)


def process(path: pathlib.Path, write: bool) -> int:
    try:
        with path.open("r", encoding="utf-8") as f:
            data = json.load(f)
    except json.JSONDecodeError:
        return 0

    counter = [0]
    walk_strip(data, counter)
    if counter[0] == 0:
        return 0

    action = "stripped" if write else "would strip"
    print(f"{action} {counter[0]:>3} from {path}")

    if write:
        with path.open("w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
            f.write("\n")
    return counter[0]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="apply changes (default: dry run)")
    parser.add_argument("--path", default=".", help="root to scan (default: repo root)")
    args = parser.parse_args()

    root = pathlib.Path(args.path).resolve()
    total = 0
    files = 0
    for p in root.rglob("*.json"):
        if ".migration_backups" in p.parts or ".pose_backups" in p.parts:
            continue
        n = process(p, args.write)
        if n > 0:
            files += 1
            total += n

    verb = "Stripped" if args.write else "Would strip"
    print(f"\n{verb} {total} `isAggregate` field(s) across {files} file(s).")
    if not args.write and total > 0:
        print("Re-run with --write to apply.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
