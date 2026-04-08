import json
with open('Assets/_Project/Data/Packages/power_cube_frame/machine.json', encoding='utf-8') as f:
    d = json.load(f)
pc = d.get('previewConfig', {})
parts_of_interest = ['pump','reservoir','engine','hose','fuel','battery','oil_cooler','manifold','radiator']
print('=== PART PLACEMENTS ===')
for pp in pc.get('partPlacements', []):
    pid = pp.get('partId','')
    if any(x in pid for x in parts_of_interest):
        pos = pp.get('assembledPosition', {})
        x,y,z = pos.get('x',0), pos.get('y',0), pos.get('z',0)
        print(f"  {pid}: ({x:.3f}, {y:.3f}, {z:.3f})")

print()
print('=== TARGET PLACEMENTS (pipe slots) ===')
targets_of_interest = ['hose','fuel_line','battery_cable','cooler','pump','battery','fuel']
for tp in pc.get('targetPlacements', []):
    tid = tp.get('targetId','')
    if any(x in tid for x in targets_of_interest):
        pos = tp.get('position', {})
        x,y,z = pos.get('x',0), pos.get('y',0), pos.get('z',0)
        pa = tp.get('portA')
        pb = tp.get('portB')
        print(f"  {tid}: position=({x:.3f}, {y:.3f}, {z:.3f}) portA={pa} portB={pb}")

print()
print('=== ALL STEPS (pipe) ===')
for step in d.get('steps', []):
    sid = step.get('id','')
    ct = step.get('completionType','')
    if any(x in sid for x in ['hose','fuel','battery_cable','cable']):
        tids = step.get('targetIds', [])
        pids = step.get('requiredPartIds', [])
        print(f"  step {sid}: completionType={ct} targets={tids} parts={pids}")
