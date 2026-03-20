import json

PATH = 'Assets/_Project/Data/Packages/power_cube_frame/machine.json'
with open(PATH, encoding='utf-8') as f:
    d = json.load(f)

# ─── portA / portB per target ───────────────────────────────────────────────
# Derived from component positions in the preview scene:
#   hydraulic_pump (-0.200, 0.172, 0.000)  reservoir (0.200, 0.252, 0.000)
#   oil_cooler (0.000, 0.252, -0.356)      fuel_tank (-0.250, 0.227, 0.050)
#   fuel_shutoff_valve (-0.150, 0.117, 0.050)  battery (0.300, 0.200, -0.120)
#   engine (~0.000, 0.889, 0.000)

PORT_DATA = {
    'target_pressure_hose_slot': {
        'portA': {'x': -0.200, 'y': 0.205, 'z': -0.060},   # pump outlet port
        'portB': {'x':  0.155, 'y': 0.260, 'z': -0.060},   # reservoir pressure inlet
    },
    'target_return_hose_slot': {
        'portA': {'x': -0.040, 'y': 0.215, 'z': -0.055},   # manifold return port
        'portB': {'x':  0.190, 'y': 0.245, 'z': -0.055},   # reservoir suction port
    },
    'target_fuel_line_slot': {
        'portA': {'x': -0.242, 'y': 0.197, 'z':  0.090},   # fuel tank outlet
        'portB': {'x': -0.065, 'y': 0.180, 'z':  0.055},   # carb / shutoff inlet
    },
    'target_battery_cable_slot': {
        'portA': {'x':  0.285, 'y': 0.235, 'z': -0.100},   # battery positive terminal
        'portB': {'x':  0.080, 'y': 0.760, 'z':  0.040},   # engine starter solenoid
    },
}

PIPE_STEP_IDS = {
    'step_connect_pressure_hose',
    'step_connect_return_hose',
    'step_connect_fuel_line',
    'step_connect_battery_cables',
}

# Update targetPlacements
for tp in d.get('previewConfig', {}).get('targetPlacements', []):
    tid = tp.get('targetId', '')
    if tid in PORT_DATA:
        tp['portA'] = PORT_DATA[tid]['portA']
        tp['portB'] = PORT_DATA[tid]['portB']
        print(f"  ✓ Added portA/portB to target '{tid}'")

# Update step completionType
for step in d.get('steps', []):
    sid = step.get('id', '')
    if sid in PIPE_STEP_IDS:
        old = step.get('completionType')
        step['completionType'] = 'pipe_connection'
        print(f"  ✓ Step '{sid}': completionType {old!r} → 'pipe_connection'")

with open(PATH, 'w', encoding='utf-8') as f:
    json.dump(d, f, indent=2, ensure_ascii=False)

print('Done.')
