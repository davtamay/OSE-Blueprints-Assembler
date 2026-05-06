#!/usr/bin/env python3
"""Migrate legacy ToolDefinition.useOrientationOverride / orientationEuler
into ToolPoseConfig.useCursorRotation / cursorRotation, in-place.

Tier 1 (cursorRotation) already wins at runtime when active, so this is a
straight 1:1 transfer that preserves visuals. Skips tools where the legacy
flag is off, or where toolPose.cursorRotation is already authored.

Run from repo root:
    python tools/migrate_tool_orientation_legacy.py            # dry-run
    python tools/migrate_tool_orientation_legacy.py --apply
"""
import argparse, json, sys, glob

ROOTS = [
    "Assets/_Project/Data/Packages",
    "Assets/StreamingAssets/MachinePackages",
]

def is_tool(o):
    return isinstance(o, dict) and "useOrientationOverride" in o

def walk_tools(node):
    if isinstance(node, dict):
        if is_tool(node):
            yield node
        for v in node.values():
            yield from walk_tools(v)
    elif isinstance(node, list):
        for v in node:
            yield from walk_tools(v)

def migrate_tool(tool):
    has_keys = "useOrientationOverride" in tool or "orientationEuler" in tool
    if not has_keys:
        return None
    use_legacy = bool(tool.get("useOrientationOverride", False))
    if not use_legacy:
        # Just strip the dead keys (C# class no longer has the fields).
        tool.pop("useOrientationOverride", None)
        tool.pop("orientationEuler", None)
        return ("strip-dead-keys", tool.get("id"), None, None)
    eu = tool.get("orientationEuler") or {"x": 0.0, "y": 0.0, "z": 0.0}

    tp = tool.get("toolPose")
    if not isinstance(tp, dict):
        tp = {}
        tool["toolPose"] = tp

    cr = tp.get("cursorRotation") or {"x": 0.0, "y": 0.0, "z": 0.0}
    cr_nonzero = any(float(cr.get(k, 0.0)) != 0.0 for k in ("x", "y", "z"))
    use_cr_already = bool(tp.get("useCursorRotation", False))

    if use_cr_already or cr_nonzero:
        # Modern field already wins (Tier 1). Just clear legacy.
        tool.pop("useOrientationOverride", None)
        tool.pop("orientationEuler", None)
        return ("clear-legacy-only", tool.get("id"), eu, cr)

    # Migrate value into Tier 1.
    tp["cursorRotation"] = {"x": float(eu.get("x", 0.0)),
                            "y": float(eu.get("y", 0.0)),
                            "z": float(eu.get("z", 0.0))}
    tp["useCursorRotation"] = True
    tool.pop("useOrientationOverride", None)
    tool.pop("orientationEuler", None)
    return ("migrated", tool.get("id"), eu, tp["cursorRotation"])

def process(path, apply):
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception:
        return []
    actions = []
    for tool in walk_tools(data):
        result = migrate_tool(tool)
        if result is not None:
            actions.append((path, *result))
    if actions and apply:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=4)
            f.write("\n")
    return actions

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true",
                    help="Write changes; otherwise dry-run.")
    args = ap.parse_args()

    files = []
    for root in ROOTS:
        files.extend(glob.glob(f"{root}/**/*.json", recursive=True))

    total = 0
    for f in sorted(set(files)):
        for path, kind, tid, eu, cr in process(f, args.apply):
            total += 1
            print(f"[{kind:20}] {tid:24} euler={eu} -> cursorRotation={cr}  ({path})")

    print(f"\n{'APPLIED' if args.apply else 'DRY-RUN'}: {total} migrations")
    if not args.apply and total:
        print("Re-run with --apply to write.")

if __name__ == "__main__":
    sys.exit(main())
