#!/usr/bin/env python3
"""Strip __notask_auto stepPoses from carriage half_a/half_b parts so all
carriages behave like c1 (which has none, defaulting to assembledPosition).

The auto-baker pinned c2 half_b + c3/c4 halves to startPosition for early
steps via __notask_auto stepPoses. Result: halves render only ~14 cm
apart (start delta) instead of ~30 cm (assembled delta) and overlap
visually due to mesh bounds. c1 escaped this auto-bake; c2 half_a too;
c2 half_b + c3/c4 halves got it.

This strips the __notask_auto entries from carriage halves so the runtime
falls back to assembledPosition from layout step onward.

Note: if the runtime baker re-creates these on next save, we'll need to
audit the bake trigger. As of 2026-05-05 the bake appears to have run
on c2/c3/c4 historically and has not been re-running on c1 — so a single
strip should hold.
"""
import json, sys

PC = "Assets/_Project/Data/Packages/d3d_v18_10/preview_config.json"

CARRIAGE_HALVES = {
    "y_left_carriage_half_a", "y_left_carriage_half_b",
    "y_right_carriage_half_a", "y_right_carriage_half_b",
    "z_back_carriage_half_a", "z_back_carriage_half_b",
    "z_front_carriage_half_a", "z_front_carriage_half_b",
}

def main():
    apply = "--apply" in sys.argv
    with open(PC, "r", encoding="utf-8") as f:
        pc = json.load(f)
    inner = pc["previewConfig"]

    changes = 0
    for pp in inner.get("partPlacements", []):
        if pp.get("partId") not in CARRIAGE_HALVES:
            continue
        sp = pp.get("stepPoses") or []
        if not sp:
            continue
        kept = [s for s in sp
                if not (s.get("label", "") or "").startswith("__notask_auto")]
        stripped = len(sp) - len(kept)
        if stripped > 0:
            print(f"  {pp['partId']:32s} stripping {stripped} __notask_auto stepPose(s)")
            if apply:
                if kept:
                    pp["stepPoses"] = kept
                else:
                    pp.pop("stepPoses", None)
            changes += stripped

    if apply and changes:
        with open(PC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(pc, f, indent=4)
            f.write("\n")
    print(f"\n{'APPLIED' if apply else 'DRY-RUN'}: {changes} stepPose(s) removed")
    if not apply and changes:
        print("Re-run with --apply to write.")

if __name__ == "__main__":
    main()
