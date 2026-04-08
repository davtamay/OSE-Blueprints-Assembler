"""
Fix the 7 placeholder parts (no real GLB mesh yet).
These render as fallback primitives (likely cubes), so scale = real-world size directly.
"""
import json

# Frame reference
TUBE = 0.1016
BASE_TOP = TUBE
INNER_Z = 0.305  # from center to inner face of long tube

# Real-world dimensions (X, Y, Z) in meters
# For cable/hose parts, treat as elongated cylinders → scale as thin rods
placeholder_data = {
    'pump_coupling': {
        'real': (0.10, 0.075, 0.10),   # Lovejoy L-100, ~100mm OD x 75mm
        'pos': (-0.20, 0.639, 0.0),     # Just below engine mount plate
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'pressure_hose': {
        'real': (0.025, 0.025, 0.50),   # 3/4" SAE hose, ~50cm long along Z
        'pos': (-0.35, 0.45, -0.10),    # From pump area running forward
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'return_hose': {
        'real': (0.032, 0.032, 0.40),   # 1" return hose, ~40cm long
        'pos': (0.10, 0.30, -0.10),     # From reservoir area toward front
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'fuel_line': {
        'real': (0.012, 0.012, 0.35),   # 3/8" fuel hose
        'pos': (-0.30, 0.25, 0.10),     # From fuel tank down to engine
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'battery_cables': {
        'real': (0.015, 0.015, 0.40),   # 2-gauge cable set
        'pos': (0.30, 0.20, -0.05),     # From battery toward starter
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'choke_cable': {
        'real': (0.008, 0.008, 0.50),   # Universal choke cable
        'pos': (-0.05, 0.45, -0.15),    # From front panel to carburetor
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'throttle_cable': {
        'real': (0.008, 0.008, 0.50),   # Universal throttle cable
        'pos': (0.05, 0.45, -0.15),     # Adjacent to choke cable
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
}

json_path = r'Assets\_Project\Data\Packages\power_cube_frame\machine.json'
with open(json_path, 'r') as f:
    data = json.load(f)

count = 0
for pp in data.get('previewConfig', {}).get('partPlacements', []):
    pid = pp.get('partId', '')
    if pid in placeholder_data:
        pd = placeholder_data[pid]
        p = pd['pos']
        r = pd['rot']
        s = pd['real']  # Scale = real-world dims for placeholder cubes
        
        pp['assembledPosition'] = {'x': p[0], 'y': p[1], 'z': p[2]}
        pp['assembledRotation'] = {'x': r[0], 'y': r[1], 'z': r[2], 'w': r[3]}
        pp['assembledScale'] = {'x': s[0], 'y': s[1], 'z': s[2]}
        pp['startScale'] = {'x': s[0], 'y': s[1], 'z': s[2]}
        
        count += 1
        print(f"  Updated {pid}: pos=({p[0]:.3f},{p[1]:.3f},{p[2]:.3f}) scale=({s[0]:.3f},{s[1]:.3f},{s[2]:.3f})")

with open(json_path, 'w') as f:
    json.dump(data, f, indent=2)

print(f"\nTotal updated: {count} placeholder placements")
