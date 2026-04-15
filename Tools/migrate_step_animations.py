"""Migration: relocate `step.animationCues.cues[]` onto the host
(part / subassembly) it animates.

Each cue is appended to the chosen host's `animationCues[]` with
`stepIds: [step.id]` so its scope stays identical to before. Resolution
priority for picking a host:

1. `targetSubassemblyId` non-empty → that subassembly.
2. `targetPartIds` length >= 2 AND step has a group scope and all parts are
   members of it → that subassembly. (Group-shake intent.)
3. `targetPartIds` length == 1 → that part.
4. `targetPartIds` length >= 2 spanning groups → one cue per part, each
   added to its respective part.
5. Tool-only or unresolvable → kept on the step (legacy fallback) so
   nothing silently disappears.

Also moves `previewDelaySeconds` onto the step itself as a top-level
`previewDelaySeconds` field, then strips the empty `animationCues` block.

Usage: python tools/migrate_step_animations.py d3d_v18_10
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent

SCOPE_KEYS = {"trigger", "stepIds"}


def load_subassembly_index(assembly_files: list[Path]) -> dict[str, set[str]]:
    """subId -> set(member partIds), built from each subassembly's authored
    partIds (HEAD-style legacy) AND from any part.subassemblyIds claims
    already present."""
    sub_to_parts: dict[str, set[str]] = {}
    for path in assembly_files:
        with path.open("r", encoding="utf-8") as f:
            doc = json.load(f)
        for sub in doc.get("subassemblies", []) or []:
            sid = sub.get("id")
            if not sid:
                continue
            roster = sub_to_parts.setdefault(sid, set())
            for pid in sub.get("partIds") or []:
                if pid:
                    roster.add(pid)
        for part in doc.get("parts", []) or []:
            pid = part.get("id")
            if not pid:
                continue
            for sid in part.get("subassemblyIds") or []:
                if sid:
                    sub_to_parts.setdefault(sid, set()).add(pid)
    return sub_to_parts


def find_host_doc_for(host_id: str, host_kind: str, files_index: dict[Path, dict]) -> tuple[Path, dict] | None:
    """Locate which loaded doc owns the host (part or subassembly).
    Returns (path, doc) or None."""
    key = "parts" if host_kind == "part" else "subassemblies"
    for path, doc in files_index.items():
        for entry in doc.get(key, []) or []:
            if entry.get("id") == host_id:
                return path, doc
    return None


def attach_cue(host_obj: dict, cue: dict, step_id: str) -> None:
    """Append cue to host_obj.animationCues with stepIds=[step_id]. Strips
    the legacy authored target fields (host is implicit)."""
    relocated = {k: v for k, v in cue.items() if k not in {"targetPartIds", "targetSubassemblyId", "targetToolIds"}}
    relocated["stepIds"] = [step_id]
    bucket = host_obj.setdefault("animationCues", [])
    bucket.append(relocated)


def migrate_doc(doc: dict, files_index: dict[Path, dict], sub_to_parts: dict[str, set[str]]) -> tuple[int, int, int]:
    """Returns (cues_relocated, previewDelays_moved, cues_kept_on_step)."""
    relocated = 0
    delays = 0
    kept = 0

    for step in doc.get("steps", []) or []:
        step_id = step.get("id") or ""
        ac = step.get("animationCues")
        if not ac:
            continue

        # Move previewDelaySeconds to step-level
        pd = ac.get("previewDelaySeconds")
        if pd:
            step["previewDelaySeconds"] = pd
            delays += 1

        cues = ac.get("cues") or []
        kept_cues: list[dict] = []
        step_sub_id = step.get("requiredSubassemblyId") or step.get("subassemblyId") or ""

        for cue in cues:
            tsub = cue.get("targetSubassemblyId") or ""
            tparts = cue.get("targetPartIds") or []
            ttools = cue.get("targetToolIds") or []
            tparts = [p for p in tparts if p]

            host_kind = None
            host_id = None

            if tsub:
                host_kind = "subassembly"
                host_id = tsub
            elif (
                len(tparts) >= 2
                and step_sub_id
                and step_sub_id in sub_to_parts
                and all(p in sub_to_parts[step_sub_id] for p in tparts)
            ):
                host_kind = "subassembly"
                host_id = step_sub_id
            elif len(tparts) == 1:
                host_kind = "part"
                host_id = tparts[0]
            elif len(tparts) >= 2:
                # Cross-group / no group: one relocated cue per part.
                any_relocated = False
                for pid in tparts:
                    found = find_host_doc_for(pid, "part", files_index)
                    if not found:
                        continue
                    _, host_doc = found
                    for part in host_doc["parts"]:
                        if part["id"] == pid:
                            attach_cue(part, cue, step_id)
                            relocated += 1
                            any_relocated = True
                            break
                if not any_relocated:
                    kept_cues.append(cue)
                    kept += 1
                continue
            elif ttools:
                # Tool-targeted cues stay on the step (no host concept yet).
                kept_cues.append(cue)
                kept += 1
                continue
            else:
                kept_cues.append(cue)
                kept += 1
                continue

            found = find_host_doc_for(host_id, host_kind, files_index)
            if not found:
                kept_cues.append(cue)
                kept += 1
                continue
            _, host_doc = found
            host_key = "parts" if host_kind == "part" else "subassemblies"
            for entry in host_doc[host_key]:
                if entry["id"] == host_id:
                    attach_cue(entry, cue, step_id)
                    relocated += 1
                    break

        if kept_cues:
            ac["cues"] = kept_cues
        else:
            step.pop("animationCues", None)

    return relocated, delays, kept


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: python tools/migrate_step_animations.py <packageId>")
        return 1
    pkg = sys.argv[1]
    base = REPO_ROOT / "Assets" / "_Project" / "Data" / "Packages" / pkg / "assemblies"
    if not base.is_dir():
        print(f"no assemblies dir at {base}")
        return 2

    files = sorted(base.glob("*.json"))
    files_index: dict[Path, dict] = {}
    for p in files:
        with p.open("r", encoding="utf-8") as f:
            files_index[p] = json.load(f)

    sub_to_parts = load_subassembly_index(files)
    print(f"[migrate] {len(sub_to_parts)} subassemblies in roster index")

    total_relocated = 0
    total_delays = 0
    total_kept = 0
    for path, doc in files_index.items():
        r, d, k = migrate_doc(doc, files_index, sub_to_parts)
        total_relocated += r
        total_delays += d
        total_kept += k

    written = 0
    for path, doc in files_index.items():
        with path.open("w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
        written += 1

    print(f"[migrate] relocated {total_relocated} cues, moved {total_delays} previewDelays, kept {total_kept} on step")
    print(f"[migrate] wrote {written} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
