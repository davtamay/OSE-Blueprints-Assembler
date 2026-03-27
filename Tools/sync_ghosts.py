"""
Sync targetPlacements to match their associated part's playPosition/playRotation/playScale.
This eliminates the stale data that caused ghost vs placement discrepancies.
"""
import json

JSON_PATH = r"Assets\_Project\Data\Packages\power_cube_frame\machine.json"

with open(JSON_PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

# Build target -> associatedPartId map
t2p = {t["id"]: t.get("associatedPartId", "") for t in data.get("targets", [])}

# Build partId -> partPlacement map
pps = {p["partId"]: p for p in data.get("previewConfig", {}).get("partPlacements", [])}

tps = data.get("previewConfig", {}).get("targetPlacements", [])
synced = 0

for tp in tps:
    tid = tp["targetId"]
    pid = t2p.get(tid, "")
    if not pid:
        continue
    pp = pps.get(pid)
    if not pp:
        continue

    # Sync position from playPosition
    tp["position"] = dict(pp["playPosition"])
    # Sync rotation from playRotation
    tp["rotation"] = dict(pp["playRotation"])
    # Sync scale from playScale
    tp["scale"] = dict(pp["playScale"])

    synced += 1
    print(f"  synced {tid} <- {pid}")

with open(JSON_PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)

print(f"\nSynced {synced} targetPlacements to match playPosition")
