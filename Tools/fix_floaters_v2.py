"""
Fix remaining floating parts. Key issues:
1. hydraulic_pump & pump_coupling should form a vertical chain hanging from engine
2. Small items (key_switch, gauge, cables) should be on frame tubes, not floating
3. Placeholder hoses/cables should be at floor or tube level
"""
import json, struct, os

def get_native(path):
    try:
        with open(path, 'rb') as f:
            m,v,l = struct.unpack('<III', f.read(12))
            if m != 0x46546C67: return None
            cl,ct = struct.unpack('<II', f.read(8))
            jd = json.loads(f.read(cl).decode('utf-8'))
        for mesh in jd.get('meshes',[]):
            for prim in mesh.get('primitives',[]):
                pi = prim.get('attributes',{}).get('POSITION')
                if pi is not None:
                    acc = jd['accessors'][pi]
                    if 'min' in acc:
                        mn,mx = acc['min'],acc['max']
                        return [mx[i]-mn[i] for i in range(3)], [(mx[i]+mn[i])/2 for i in range(3)]
    except: pass
    return None

parts_dir = r'Assets\_Project\Data\Packages\power_cube_frame\assets\parts'

# Frame geometry
FLOOR = 0.1016        # top of base tubes
FRONT_TUBE_Z = -0.3558  # front long tube center Z
FRONT_INNER_Z = FRONT_TUBE_Z + 0.0508  # inner face of front tube

# Engine hangs from/sits on mount plate. The pump is bolted directly 
# to the engine - NOT floating in mid-air. On a Power Cube, the pump 
# is bracket-mounted to the frame or directly coupled under the engine.
# Let's put pump on the floor since it's a heavy cast iron piece.
# The coupling connects engine shaft (above) to pump shaft (below/beside).

real_dims = {
    'pump_coupling':      (0.10, 0.075, 0.10),
    'hydraulic_pump':     (0.25, 0.14, 0.12),
    'pressure_hose':      (0.025, 0.025, 0.50),
    'return_hose':        (0.032, 0.032, 0.40),
    'fuel_line':          (0.012, 0.012, 0.35),
    'battery_cables':     (0.015, 0.015, 0.40),
    'key_switch':         (0.030, 0.040, 0.030),
    'choke_cable':        (0.008, 0.008, 0.50),
    'throttle_cable':     (0.008, 0.008, 0.50),
    'pressure_gauge':     (0.063, 0.063, 0.060),
}

# Corrected positions
fixes = {
    # Pump on floor, left side of frame - it's a heavy gear pump
    'hydraulic_pump': {
        'pos': (-0.20, FLOOR + 0.14/2, 0.0),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Coupling sits on top of pump, aligned with engine shaft
    'pump_coupling': {
        'pos': (-0.20, FLOOR + 0.14 + 0.075/2, 0.0),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Key switch: mounted on front base tube, midway up post
    'key_switch': {
        'pos': (0.15, FLOOR + 0.15, FRONT_TUBE_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Pressure gauge: on front tube, beside key switch
    'pressure_gauge_inline': {
        'pos': (-0.20, FLOOR + 0.15, FRONT_TUBE_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Hoses: lay on floor along Z axis
    'pressure_hose': {
        'pos': (-0.40, FLOOR + 0.025/2, -0.10),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    'return_hose': {
        'pos': (0.10, FLOOR + 0.032/2, -0.10),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Fuel line: runs near fuel tank along Z
    'fuel_line': {
        'pos': (-0.35, FLOOR + 0.012/2, 0.05),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Battery cables: from battery along floor
    'battery_cables': {
        'pos': (0.20, FLOOR + 0.015/2, -0.10),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Choke cable: along front tube face
    'choke_cable': {
        'pos': (-0.05, FLOOR + 0.10, FRONT_INNER_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    # Throttle cable: along front tube face
    'throttle_cable': {
        'pos': (0.05, FLOOR + 0.10, FRONT_INNER_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
}

json_path = r'Assets\_Project\Data\Packages\power_cube_frame\machine.json'
with open(json_path, 'r') as f:
    data = json.load(f)

count = 0
for pp in data['previewConfig']['partPlacements']:
    pid = pp['partId']
    if pid not in fixes:
        continue
    
    fix = fixes[pid]
    p = fix['pos']
    r = fix['rot']
    
    pp['assembledPosition'] = {'x': p[0], 'y': p[1], 'z': p[2]}
    pp['assembledRotation'] = {'x': r[0], 'y': r[1], 'z': r[2], 'w': r[3]}
    # Keep existing scale (already computed correctly)
    
    rd_key = 'pressure_gauge' if pid == 'pressure_gauge_inline' else pid
    if rd_key in real_dims:
        bottom = p[1] - real_dims[rd_key][1]/2
        print(f"  {pid:25s}  Y={p[1]:.4f}  bottom={bottom:.4f}")
    
    count += 1

with open(json_path, 'w') as f:
    json.dump(data, f, indent=2)

print(f"\nFixed {count} placements")
