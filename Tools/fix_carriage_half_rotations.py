#!/usr/bin/env python3
"""Fix c3/c4 carriage half assembledRotation values to match c1/c2.

c1 and c2 carriage halves rotate from their startRotation to a different
clamshell-close assembledRotation:
    half_a: (-0.5, +0.5, -0.5, +0.5) -> (-0.5, -0.5, +0.5, +0.5)
    half_b: (+0.5, +0.5, -0.5, -0.5) -> (-0.5, +0.5, -0.5, +0.5)

c3 and c4 (cloned from c1) ended up with broken assembledRotations:
    half_a: assembledRotation == startRotation (no clamshell rotation)
    half_b: assembledRotation = (0, 0.7071, -0.7071, 0) (garbage)

Copy c1's correct values onto c3/c4. The cloner mishandled rotation
when generating the back/front Z-axis carriages.
"""
import json, sys

PC = "Assets/_Project/Data/Packages/d3d_v18_10/preview_config.json"

# Canonical from c1
HALF_A_ROT = {"x": -0.5, "y": -0.5, "z":  0.5, "w":  0.5}
HALF_B_ROT = {"x": -0.5, "y":  0.5, "z": -0.5, "w":  0.5}

TARGETS = {
    "z_back_carriage_half_a":  HALF_A_ROT,
    "z_back_carriage_half_b":  HALF_B_ROT,
    "z_front_carriage_half_a": HALF_A_ROT,
    "z_front_carriage_half_b": HALF_B_ROT,
}

def main():
    apply = "--apply" in sys.argv
    with open(PC, "r", encoding="utf-8") as f:
        pc = json.load(f)
    inner = pc["previewConfig"]
    changes = 0
    for pp in inner.get("partPlacements", []):
        pid = pp.get("partId")
        if pid in TARGETS:
            old = pp.get("assembledRotation", {})
            new = TARGETS[pid]
            if old != new:
                print(f"  {pid:32s}")
                print(f"    before: {old}")
                print(f"    after : {new}")
                if apply:
                    pp["assembledRotation"] = new
                changes += 1
    if apply and changes:
        with open(PC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(pc, f, indent=4)
            f.write("\n")
    print(f"\n{'APPLIED' if apply else 'DRY-RUN'}: {changes} rotation(s) corrected")
    if not apply and changes:
        print("Re-run with --apply to write.")

if __name__ == "__main__":
    main()
