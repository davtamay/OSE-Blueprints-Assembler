#!/usr/bin/env python3
"""Mirror carriage-related fixes from authoring preview_config.json into the
bundled StreamingAssets machine.json so WebGL builds pick them up without
needing Unity's PackageSyncTool to run.

This is a stop-gap so users can verify fixes in WebGL between authoring
sessions. The canonical path is `OSE > Sync Packages to StreamingAssets`
in Unity (or any save through TTAW which auto-syncs).

Mirrors:
  - partPlacements (assembledPosition / startPosition / stepPoses)
  - targetPlacements (rotation / position) for any matching targetIds

Identity match by partId / targetId.
"""
import json, sys

PKG = "d3d_v18_10"
AUTH = f"Assets/_Project/Data/Packages/{PKG}/preview_config.json"
SA   = f"Assets/StreamingAssets/MachinePackages/{PKG}/machine.json"

# Only mirror entries whose id contains these substrings — narrows the
# blast radius to carriage-related parts/targets.
ID_FILTER_SUBSTRINGS = ["carriage", "_lm8uu_", "_m6_nut_", "_m6x18_", "_m6x30_",
                        "_bolt_tighten_"]

def matches(pid):
    return any(s in (pid or "") for s in ID_FILTER_SUBSTRINGS)

def main():
    apply = "--apply" in sys.argv
    with open(AUTH, "r", encoding="utf-8") as f:
        auth = json.load(f)
    with open(SA, "r", encoding="utf-8") as f:
        sa = json.load(f)
    auth_inner = auth["previewConfig"]
    sa_inner   = sa["previewConfig"]

    # partPlacements
    auth_pp = {p.get("partId"): p for p in auth_inner.get("partPlacements", [])}
    sa_pp_list = sa_inner.get("partPlacements", [])
    sa_pp_idx  = {p.get("partId"): i for i, p in enumerate(sa_pp_list) if p.get("partId")}
    pp_changes = 0
    for pid, src in auth_pp.items():
        if not matches(pid): continue
        if pid in sa_pp_idx:
            i = sa_pp_idx[pid]
            old = sa_pp_list[i]
            for key in ("startPosition", "assembledPosition", "startRotation",
                        "assembledRotation", "startScale", "assembledScale",
                        "stepPoses"):
                if key in src and src[key] != old.get(key):
                    print(f"  PART {pid:35s} {key:18s}  {old.get(key)!r} -> {src[key]!r}")
                    if apply: old[key] = src[key]
                    pp_changes += 1

    # targetPlacements
    auth_tp = {t.get("targetId"): t for t in auth_inner.get("targetPlacements", [])}
    sa_tp_list = sa_inner.get("targetPlacements", [])
    sa_tp_idx  = {t.get("targetId"): i for i, t in enumerate(sa_tp_list) if t.get("targetId")}
    tp_changes = 0
    for tid, src in auth_tp.items():
        if not matches(tid): continue
        if tid in sa_tp_idx:
            i = sa_tp_idx[tid]
            old = sa_tp_list[i]
            for key in ("position", "rotation", "scale"):
                if key in src and src[key] != old.get(key):
                    print(f"  TARG {tid:35s} {key:10s}  {old.get(key)!r} -> {src[key]!r}")
                    if apply: old[key] = src[key]
                    tp_changes += 1
        else:
            print(f"  TARG {tid} ADD  pos={src.get('position')} rot={src.get('rotation')}")
            if apply: sa_tp_list.append(src)
            tp_changes += 1

    if apply and (pp_changes or tp_changes):
        with open(SA, "w", encoding="utf-8", newline="\n") as f:
            json.dump(sa, f, indent=4)
            f.write("\n")
    total = pp_changes + tp_changes
    print(f"\n{'APPLIED' if apply else 'DRY-RUN'}: {pp_changes} part field updates, {tp_changes} target updates")
    if not apply and total:
        print("Re-run with --apply to write.")

if __name__ == "__main__":
    main()
