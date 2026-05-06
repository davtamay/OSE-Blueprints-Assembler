#!/usr/bin/env python3
"""Re-runnable fix: c2/c3/c4 carriage halves are spaced wrong.

c1 (canonical) has half_a -> half_b assembledPosition delta of (+0.2972, 0, 0).
Clones got translated half_a positions but kept original (or other wrong)
half_b values, so close_halves ghosts land sideways instead of on top.

Strategy: read c1's exact (half_a, half_b) delta vector from preview_config,
then for each clone (y_right, z_back, z_front) set:
    half_b.assembledPosition = half_a.assembledPosition + delta_c1

half_a positions are trusted (they consistently look correct). Only half_b
gets rewritten to the canonical relative offset.

Companion to package_health check #12 (sibling-half delta consistency).
"""
import json, sys

PKG = "Assets/_Project/Data/Packages/d3d_v18_10"
PC  = f"{PKG}/preview_config.json"

# Canonical source vs clones
CANONICAL_PREFIX = "y_left_carriage"
CLONE_PREFIXES   = ["y_right_carriage", "z_back_carriage", "z_front_carriage"]

def _vec(d):
    return (float(d["x"]), float(d["y"]), float(d["z"]))

def _add(a, b):
    return {"x": a[0] + b[0], "y": a[1] + b[1], "z": a[2] + b[2]}

def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])

def main():
    apply = "--apply" in sys.argv
    with open(PC, "r", encoding="utf-8") as f:
        data = json.load(f)
    inner = data["previewConfig"]
    pps   = inner.get("partPlacements", [])

    # Index by partId
    by_id = {pp.get("partId"): pp for pp in pps if pp.get("partId")}

    can_a = by_id.get(f"{CANONICAL_PREFIX}_half_a")
    can_b = by_id.get(f"{CANONICAL_PREFIX}_half_b")
    if not can_a or not can_b:
        print(f"ERROR: canonical {CANONICAL_PREFIX}_half_a/b not found")
        return 1

    delta = _sub(_vec(can_b["assembledPosition"]), _vec(can_a["assembledPosition"]))
    print(f"Canonical {CANONICAL_PREFIX} half_a -> half_b delta: {delta}")
    print()

    changes = 0
    for prefix in CLONE_PREFIXES:
        a = by_id.get(f"{prefix}_half_a")
        b = by_id.get(f"{prefix}_half_b")
        if not a or not b:
            print(f"  SKIP {prefix} (missing half a or b)")
            continue
        old_b = _vec(b["assembledPosition"])
        new_b = _add(_vec(a["assembledPosition"]), delta)
        cur_delta = _sub(old_b, _vec(a["assembledPosition"]))
        if all(abs(cur_delta[i] - delta[i]) < 0.001 for i in range(3)):
            print(f"  OK  {prefix}_half_b already at canonical delta — no change")
            continue
        print(f"  {prefix}_half_b assembledPosition")
        print(f"    before delta = {cur_delta}  (X={old_b[0]})")
        print(f"    after  delta = {delta}  (X={new_b['x']})")
        b["assembledPosition"] = new_b
        changes += 1

    if apply and changes:
        with open(PC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=4)
            f.write("\n")
    print(f"\n{'APPLIED' if apply else 'DRY-RUN'}: {changes} half_b positions corrected")
    if not apply and changes:
        print("Re-run with --apply to write.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
