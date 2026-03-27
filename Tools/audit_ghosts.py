import json

d = json.load(open(r'Assets\_Project\Data\Packages\power_cube_frame\machine.json', encoding='utf-8'))
targets_top = d.get('targets', [])

# Find targets with associatedPartId
with_part = [t for t in targets_top if t.get('associatedPartId')]
print(f"{len(with_part)} targets have associatedPartId out of {len(targets_top)}")
for t in with_part:
    print(f"  {t['id']} -> {t['associatedPartId']}")

# Now compare using top-level targets
t2p = {t['id']: t.get('associatedPartId', '') for t in targets_top}
tps = d.get('previewConfig', {}).get('targetPlacements', [])
pps = {p['partId']: p for p in d.get('previewConfig', {}).get('partPlacements', [])}

print(f"\n{len(tps)} targetPlacements, comparing to partPlacements...")
mismatches = 0
for tp in tps:
    tid = tp['targetId']
    pid = t2p.get(tid, '')
    if not pid:
        continue
    pp = pps.get(pid)
    if not pp:
        continue
    tp_pos = (round(tp['position']['x'], 4), round(tp['position']['y'], 4), round(tp['position']['z'], 4))
    pp_pos = (round(pp['playPosition']['x'], 4), round(pp['playPosition']['y'], 4), round(pp['playPosition']['z'], 4))

    tp_rot = (round(tp.get('rotation', {}).get('x', 0), 4), round(tp.get('rotation', {}).get('y', 0), 4),
              round(tp.get('rotation', {}).get('z', 0), 4), round(tp.get('rotation', {}).get('w', 1), 4))
    pp_rot = (round(pp.get('playRotation', {}).get('x', 0), 4), round(pp.get('playRotation', {}).get('y', 0), 4),
              round(pp.get('playRotation', {}).get('z', 0), 4), round(pp.get('playRotation', {}).get('w', 1), 4))

    tp_scl = (round(tp.get('scale', {}).get('x', 1), 4), round(tp.get('scale', {}).get('y', 1), 4),
              round(tp.get('scale', {}).get('z', 1), 4))
    pp_scl = (round(pp.get('playScale', {}).get('x', 1), 4), round(pp.get('playScale', {}).get('y', 1), 4),
              round(pp.get('playScale', {}).get('z', 1), 4))

    diffs = []
    if tp_pos != pp_pos:
        diffs.append(f"  pos ghost={tp_pos} play={pp_pos}")
    if tp_rot != pp_rot:
        diffs.append(f"  rot ghost={tp_rot} play={pp_rot}")
    if tp_scl != pp_scl:
        diffs.append(f"  scl ghost={tp_scl} play={pp_scl}")
    if diffs:
        mismatches += 1
        print(f"MISMATCH {tid} -> {pid}")
        for d2 in diffs:
            print(d2)

print(f"\n{mismatches} total mismatches")
