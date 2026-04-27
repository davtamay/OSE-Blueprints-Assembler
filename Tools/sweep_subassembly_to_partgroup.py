#!/usr/bin/env python3
"""
sweep_subassembly_to_partgroup.py — One-shot rename sweep
==========================================================

Replaces every occurrence of `Subassembly` / `subassembly` / `SUBASSEMBLY`
(plus plurals) with the `PartGroup` family across the codebase, including
C# class names, field names, JSON keys, JSON value IDs, and active docs.

Scope (passed via --scope flag):
  --scope=cs          — Assets/_Project/Scripts/  (C# active code)
  --scope=json        — Assets/_Project/Data/Packages/ + Assets/StreamingAssets/MachinePackages/
  --scope=docs        — ose-xr-foundation/docs/  (excluding archived/)
  --scope=all         — all three above

Excluded (always):
  - Tools/migrate_subassembly_*.py        (frozen historical migrations)
  - Tools/sweep_subassembly_to_partgroup.py  (this script itself)
  - ose-xr-foundation/docs/archived/      (frozen snapshots)
  - .git/, .vs/, Library/, Temp/, obj/, bin/  (build / IDE artifacts)

Modes:
  (default)  Dry-run — reports per-file occurrence counts, no edits.
  --apply    Writes changes. Creates *.bak alongside each modified file
             so individual files can be restored if needed.

Replacement order matters (longer first to avoid double-rewriting):
  Subassemblies  → PartGroups
  subassemblies  → partGroups
  SUBASSEMBLIES  → PART_GROUPS
  Subassembly    → PartGroup
  subassembly    → partGroup
  SUBASSEMBLY    → PART_GROUP

Run:
  python Tools/sweep_subassembly_to_partgroup.py --scope=all              # dry-run
  python Tools/sweep_subassembly_to_partgroup.py --scope=all --apply      # write
  python Tools/sweep_subassembly_to_partgroup.py --restore                # roll back
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import Iterable

REPO_ROOT = Path(__file__).resolve().parent.parent

# Replacement table — order matters (plurals before singulars; longer before
# shorter). NO word boundaries: `\b` doesn't fire between `y` and `_` (so
# `subassembly_carriage_y_left` would be skipped) nor between `y` and `D`
# inside camelCase compounds (so `requiredSubassemblyId` would be skipped).
# We need full conceptual rename (per user decision), so do literal
# substring replacement — `subassembled` doesn't contain the substring
# (the `y` isn't there), so we don't risk mangling unrelated words.
REPLACEMENTS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"Subassemblies"), "PartGroups"),
    (re.compile(r"subassemblies"), "partGroups"),
    (re.compile(r"SUBASSEMBLIES"), "PART_GROUPS"),
    (re.compile(r"Subassembly"),   "PartGroup"),
    (re.compile(r"subassembly"),   "partGroup"),
    (re.compile(r"SUBASSEMBLY"),   "PART_GROUP"),
]

EXCLUDE_DIRS = {".git", ".vs", "Library", "Temp", "obj", "bin", "node_modules"}
EXCLUDE_PATH_SUBSTRINGS = [
    "Tools/migrate_subassembly",       # historical migration scripts
    "Tools\\migrate_subassembly",
    "Tools/sweep_subassembly_to_partgroup.py",  # this script
    "Tools\\sweep_subassembly_to_partgroup.py",
    "ose-xr-foundation/docs/archived",
    "ose-xr-foundation\\docs\\archived",
    ".pose_backups",                   # JSON backups; would diverge from authoring
    "/Backup",
    "\\Backup",
]

CS_EXTS    = {".cs"}
JSON_EXTS  = {".json"}
DOC_EXTS   = {".md", ".rst", ".txt"}
YAML_EXTS  = {".yaml", ".yml"}


def scope_roots(scope: str) -> list[Path]:
    """Resolve the scope flag to filesystem roots."""
    roots: list[Path] = []
    if scope in ("cs", "all"):
        roots.append(REPO_ROOT / "Assets" / "_Project" / "Scripts")
    if scope in ("json", "all"):
        roots.append(REPO_ROOT / "Assets" / "_Project" / "Data" / "Packages")
        roots.append(REPO_ROOT / "Assets" / "StreamingAssets" / "MachinePackages")
    if scope in ("docs", "all"):
        roots.append(REPO_ROOT / "ose-xr-foundation" / "docs")
    return roots


def is_excluded(path: Path) -> bool:
    """True if the path should be skipped per EXCLUDE_DIRS / EXCLUDE_PATH_SUBSTRINGS."""
    parts = path.parts
    for d in EXCLUDE_DIRS:
        if d in parts:
            return True
    s = str(path)
    for sub in EXCLUDE_PATH_SUBSTRINGS:
        if sub in s:
            return True
    return False


def file_in_scope(path: Path, scope: str) -> bool:
    """True if the file's extension matches the requested scope category."""
    ext = path.suffix.lower()
    if scope == "cs":
        return ext in CS_EXTS
    if scope == "json":
        return ext in JSON_EXTS
    if scope == "docs":
        return ext in DOC_EXTS
    if scope == "all":
        # YAML inputs/prefabs travel with the json scope; include them when
        # sweeping JSON. Skip in cs/docs scopes.
        return ext in (CS_EXTS | JSON_EXTS | DOC_EXTS | YAML_EXTS)
    return False


def iter_files(scope: str) -> Iterable[Path]:
    """Walk every file under the requested scope's roots, applying exclusions."""
    for root in scope_roots(scope):
        if not root.exists():
            continue
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
            dp = Path(dirpath)
            if is_excluded(dp):
                continue
            for fn in filenames:
                p = dp / fn
                if is_excluded(p):
                    continue
                # YAML files also travel with JSON scope (prefabs, inputs)
                ext = p.suffix.lower()
                if scope == "json" and ext in YAML_EXTS:
                    yield p
                elif file_in_scope(p, scope):
                    yield p


def replace_in_text(text: str) -> tuple[str, int]:
    """Apply the replacement table; return (new text, total replacements)."""
    total = 0
    for pat, repl in REPLACEMENTS:
        text, n = pat.subn(repl, text)
        total += n
    return text, total


def process_file(path: Path, apply: bool) -> int:
    """Read the file, apply replacements, optionally write. Returns occurrence count."""
    try:
        original = path.read_text(encoding="utf-8")
    except (UnicodeDecodeError, OSError) as e:
        print(f"  SKIP  {path} ({e})", file=sys.stderr)
        return 0

    new, count = replace_in_text(original)
    if count == 0:
        return 0

    if apply:
        # Backup once per file (don't overwrite an earlier backup).
        bak = path.with_suffix(path.suffix + ".sweepbak")
        if not bak.exists():
            bak.write_text(original, encoding="utf-8")
        path.write_text(new, encoding="utf-8")
    return count


def restore_backups() -> int:
    """Restore *.sweepbak files alongside each modified file."""
    restored = 0
    for dirpath, dirnames, filenames in os.walk(REPO_ROOT):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
        for fn in filenames:
            if not fn.endswith(".sweepbak"):
                continue
            bak = Path(dirpath) / fn
            target = bak.with_suffix("")  # strip ".sweepbak"
            target.write_text(bak.read_text(encoding="utf-8"), encoding="utf-8")
            bak.unlink()
            restored += 1
    return restored


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scope", choices=["cs", "json", "docs", "all"], default="cs")
    ap.add_argument("--apply", action="store_true", help="actually write changes (default: dry-run)")
    ap.add_argument("--restore", action="store_true", help="restore *.sweepbak files (undoes the last --apply)")
    args = ap.parse_args()

    if args.restore:
        n = restore_backups()
        print(f"Restored {n} files from .sweepbak.")
        return 0

    print(f"Scope: {args.scope}   Apply: {args.apply}")
    print(f"Repo:  {REPO_ROOT}\n")

    total_files = 0
    total_hits  = 0
    per_file_hits: list[tuple[int, str]] = []

    for path in iter_files(args.scope):
        n = process_file(path, args.apply)
        if n > 0:
            total_files += 1
            total_hits  += n
            per_file_hits.append((n, str(path.relative_to(REPO_ROOT))))

    per_file_hits.sort(reverse=True)
    for hits, rel in per_file_hits[:30]:
        print(f"  {hits:5d}  {rel}")
    if len(per_file_hits) > 30:
        print(f"  ... ({len(per_file_hits) - 30} more files)")

    print(f"\n{'APPLIED' if args.apply else 'DRY-RUN'}: {total_hits} replacements across {total_files} files.")
    if args.apply:
        print("Backup files written with .sweepbak suffix. Restore with --restore.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
