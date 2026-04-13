#!/usr/bin/env python3
"""
Reverse of strip_isaggregate.py — restores `"isAggregate": true` to every
subassembly that had it at git HEAD. Safe against other uncommitted edits:
only touches subassemblies whose HEAD version explicitly had the flag.

This is needed because some aggregates in the package aggregate via partId
overlap alone (no memberSubassemblyIds), so InferAggregateFlag cannot re-derive
them from structure. For those, the explicit JSON flag remains the only signal.

Usage:
    python Tools/restore_isaggregate.py --write
"""
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys


def git_head_json(repo_root: pathlib.Path, relpath: str):
    try:
        out = subprocess.check_output(
            ["git", "-C", str(repo_root), "show", f"HEAD:{relpath}"],
            stderr=subprocess.DEVNULL,
        )
        return json.loads(out.decode("utf-8"))
    except Exception:
        return None


def collect_aggregate_ids(data) -> set[str]:
    """Find all subassembly ids that had isAggregate=true at HEAD."""
    ids: set[str] = set()
    if not isinstance(data, dict):
        return ids
    subs = data.get("subassemblies") or []
    for sub in subs:
        if isinstance(sub, dict) and sub.get("isAggregate") is True:
            sid = sub.get("id")
            if sid:
                ids.add(sid)
    return ids


def restore(data, ids: set[str]) -> int:
    if not isinstance(data, dict):
        return 0
    subs = data.get("subassemblies") or []
    restored = 0
    for sub in subs:
        if not isinstance(sub, dict):
            continue
        sid = sub.get("id")
        if sid in ids and not sub.get("isAggregate"):
            sub["isAggregate"] = True
            restored += 1
    return restored


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="apply (default: dry run)")
    parser.add_argument("--path", default=".", help="root to scan (default: repo root)")
    args = parser.parse_args()

    repo_root = pathlib.Path(args.path).resolve()
    total = 0
    files_touched = 0

    for p in repo_root.rglob("*.json"):
        if ".migration_backups" in p.parts or ".pose_backups" in p.parts:
            continue
        rel = p.relative_to(repo_root).as_posix()

        head = git_head_json(repo_root, rel)
        if head is None:
            continue
        ids = collect_aggregate_ids(head)
        if not ids:
            continue

        try:
            with p.open("r", encoding="utf-8") as f:
                cur = json.load(f)
        except json.JSONDecodeError:
            continue

        n = restore(cur, ids)
        if n == 0:
            continue

        action = "restored" if args.write else "would restore"
        print(f"{action} {n:>3} in {rel}")
        total += n
        files_touched += 1

        if args.write:
            with p.open("w", encoding="utf-8", newline="\n") as f:
                json.dump(cur, f, indent=2, ensure_ascii=False)
                f.write("\n")

    verb = "Restored" if args.write else "Would restore"
    print(f"\n{verb} {total} `isAggregate` field(s) across {files_touched} file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
