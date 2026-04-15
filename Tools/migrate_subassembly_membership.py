"""One-shot migration: move `subassembly.partIds` authoring into
`part.subassemblyIds` claims.

Reads HEAD's authoritative `subassemblies[].partIds` for every assembly file
in a package, then for each (subId, partId) pair writes `subassemblyIds:
[..subId]` onto the matching `parts[]` entry in the CURRENT working-tree
JSON. Idempotent — rerunning merges into existing claims without duplicates.
Leaves `subassembly.partIds` in place; the loader will derive from parts and
ignore/merge the legacy array.

Usage: python tools/migrate_subassembly_membership.py d3d_v18_10
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent


def git_show_head(repo_relative: Path) -> Any | None:
    """Return parsed JSON of the file at HEAD, or None if not in HEAD."""
    rel = repo_relative.as_posix()
    try:
        raw = subprocess.check_output(
            ["git", "show", f"HEAD:{rel}"],
            cwd=REPO_ROOT,
            stderr=subprocess.DEVNULL,
        )
    except subprocess.CalledProcessError:
        return None
    try:
        return json.loads(raw.decode("utf-8"))
    except json.JSONDecodeError:
        return None


def collect_head_membership(assembly_files: list[Path]) -> dict[str, set[str]]:
    """subId -> {partIds} from HEAD's authored subassemblies[].partIds."""
    membership: dict[str, set[str]] = {}
    for path in assembly_files:
        head = git_show_head(path.relative_to(REPO_ROOT))
        if not head:
            continue
        for sub in head.get("subassemblies", []) or []:
            sub_id = sub.get("id") or ""
            part_ids = sub.get("partIds") or []
            # Skip aggregates: they intentionally repeat descendant parts in
            # HEAD, but in the new model their roster is derived transitively
            # from memberSubassemblyIds. Importing their partIds would create
            # spurious multi-membership on every leaf part.
            is_aggregate = bool(sub.get("isAggregate")) or bool(sub.get("memberSubassemblyIds"))
            if not sub_id or not part_ids or is_aggregate:
                continue
            bucket = membership.setdefault(sub_id, set())
            for pid in part_ids:
                if pid:
                    bucket.add(pid)
    return membership


def apply_claims(
    assembly_files: list[Path],
    membership: dict[str, set[str]],
) -> tuple[int, int]:
    """For every part in working-tree JSON, merge in subassemblyIds claims
    from `membership`. Returns (parts_updated, files_written)."""
    part_to_subs: dict[str, set[str]] = {}
    for sub_id, part_ids in membership.items():
        for pid in part_ids:
            part_to_subs.setdefault(pid, set()).add(sub_id)

    parts_updated = 0
    files_written = 0
    for path in assembly_files:
        with path.open("r", encoding="utf-8") as f:
            doc = json.load(f)
        changed = False
        for part in doc.get("parts", []) or []:
            pid = part.get("id") or ""
            if not pid or pid not in part_to_subs:
                continue
            existing = set(part.get("subassemblyIds") or [])
            merged = existing | part_to_subs[pid]
            if merged != existing:
                part["subassemblyIds"] = sorted(merged)
                parts_updated += 1
                changed = True
        if changed:
            with path.open("w", encoding="utf-8", newline="\n") as f:
                json.dump(doc, f, indent=2, ensure_ascii=False)
                f.write("\n")
            files_written += 1
    return parts_updated, files_written


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: python tools/migrate_subassembly_membership.py <packageId>")
        return 1
    package_id = sys.argv[1]
    pkg_dir = REPO_ROOT / "Assets" / "_Project" / "Data" / "Packages" / package_id
    assembly_dir = pkg_dir / "assemblies"
    if not assembly_dir.is_dir():
        print(f"error: no assemblies dir at {assembly_dir}")
        return 2

    assembly_files = sorted(assembly_dir.glob("*.json"))
    if not assembly_files:
        print(f"error: no assembly JSON files under {assembly_dir}")
        return 2

    membership = collect_head_membership(assembly_files)
    total_members = sum(len(v) for v in membership.values())
    print(
        f"[migrate] {len(membership)} subassemblies, "
        f"{total_members} (sub,part) claims from HEAD"
    )

    parts_updated, files_written = apply_claims(assembly_files, membership)
    print(f"[migrate] updated {parts_updated} parts across {files_written} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
