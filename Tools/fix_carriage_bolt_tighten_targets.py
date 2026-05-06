#!/usr/bin/env python3
"""Re-runnable fix: carriage bolt-tighten targets are repeatedly
generated wrong by Tools/instantiate_prefab.py — it reads existing
targets from disk verbatim (canonical-from-disk path), preserving bad
values from prior runs.

Auto-discovers all c-N (c2..c9) bolt-tighten targets, restores the c1
mechanism (toolActionRotation = (0, 90, 270), anchorRef path, no
useLocalOffsetFromPart), and bakes them into preview_config.

Companion to package_health checks 9-11 — those detect, this fixes.
"""
import json, sys

PKG = "Assets/_Project/Data/Packages/d3d_v18_10"
ASM = f"{PKG}/assemblies/assembly_d3d_batch_carriage_build.json"
PC  = f"{PKG}/preview_config.json"

# Mapping: target id -> bolt id (assembledPosition source).
# Built from the assembly file's existing target.associatedPartId so we
# don't have to maintain a hand-curated list as new c-N carriages are added.
def _build_target_bolt_map(asm_data):
    out = {}
    for t in asm_data.get("targets", []):
        tid = t.get("id", "")
        if "bolt_tighten_" in tid and any(f"_c{n}_" in tid for n in (2, 3, 4, 5, 6, 7, 8, 9)):
            assoc = t.get("associatedPartId")
            if assoc:
                out[tid] = assoc
    return out

CORRECT_TOOL_EULER = {"x": 0.0, "y": 90.0, "z": 270.0}

def fix_assembly(apply, target_map):
    with open(ASM, "r", encoding="utf-8") as f:
        data = json.load(f)
    changes = 0
    for t in data.get("targets", []):
        if t.get("id") in target_map:
            before = {
                "toolActionRotation": dict(t.get("toolActionRotation", {})),
                "useLocalOffsetFromPart": t.get("useLocalOffsetFromPart"),
                "localOffsetFromPart": dict(t.get("localOffsetFromPart", {})),
            }
            t["toolActionRotation"] = dict(CORRECT_TOOL_EULER)
            t["useToolActionRotation"] = True
            t.pop("useLocalOffsetFromPart", None)
            t.pop("localOffsetFromPart", None)
            print(f"  {t['id']}")
            print(f"    before: {before}")
            print(f"    after : toolActionRotation={t['toolActionRotation']} (anchorRef path)")
            changes += 1
    if apply and changes:
        with open(ASM, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=4)
            f.write("\n")
    return changes

def fix_preview(apply, target_map):
    with open(PC, "r", encoding="utf-8") as f:
        data = json.load(f)
    inner = data["previewConfig"]
    # Bolt assembledPositions
    bolt_pos = {}
    for pp in inner.get("partPlacements", []):
        if pp.get("partId") in set(target_map.values()):
            bolt_pos[pp["partId"]] = pp.get("assembledPosition") or pp.get("startPosition")
    # c1 template — use c1_a's rotation/scale/color as the canonical marker
    template = None
    for tp in inner.get("targetPlacements", []):
        if tp.get("targetId") == "target_c1_bolt_tighten_a":
            template = tp
            break
    if template is None:
        print("ERROR: c1_a placement not found")
        return 0
    correct_rot = dict(template["rotation"])
    correct_scale = dict(template["scale"])
    correct_color = dict(template["color"])

    by_id = {tp.get("targetId"): tp for tp in inner["targetPlacements"]}
    changes = 0
    for tid, bolt in target_map.items():
        pos = bolt_pos.get(bolt) or {"x": 0.0, "y": 0.0, "z": 0.0}
        if tid in by_id:
            tp = by_id[tid]
            old_rot = dict(tp.get("rotation", {}))
            tp["rotation"] = dict(correct_rot)
            tp["scale"]    = dict(correct_scale)
            tp["color"]    = dict(correct_color)
            tp["position"] = {"x": float(pos["x"]),
                              "y": float(pos["y"]),
                              "z": float(pos["z"])}
            print(f"  patched {tid}  rot {old_rot} -> {correct_rot}")
        else:
            new_tp = {
                "targetId": tid,
                "position": {"x": float(pos["x"]),
                             "y": float(pos["y"]),
                             "z": float(pos["z"])},
                "rotation": dict(correct_rot),
                "scale":    dict(correct_scale),
                "color":    dict(correct_color),
                "portA":    {"x": 0.0, "y": 0.0, "z": 0.0},
                "portB":    {"x": 0.0, "y": 0.0, "z": 0.0},
            }
            inner["targetPlacements"].append(new_tp)
            print(f"  added   {tid}  pos={new_tp['position']} rot={correct_rot}")
        changes += 1

    if apply and changes:
        with open(PC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=4)
            f.write("\n")
    return changes

def main():
    apply = "--apply" in sys.argv
    with open(ASM, "r", encoding="utf-8") as f:
        asm_data = json.load(f)
    target_map = _build_target_bolt_map(asm_data)
    if not target_map:
        print("No clone-sibling bolt-tighten targets found.")
        return
    print(f"Targets to fix: {len(target_map)}\n")
    print("=== assembly_d3d_batch_carriage_build.json ===")
    a = fix_assembly(apply, target_map)
    print(f"\n=== preview_config.json ===")
    p = fix_preview(apply, target_map)
    print(f"\n{'APPLIED' if apply else 'DRY-RUN'}: {a} target defs, {p} placements")
    if not apply:
        print("Re-run with --apply to write.")

if __name__ == "__main__":
    main()
