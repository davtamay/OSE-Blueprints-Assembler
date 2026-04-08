"""
Fix floating parts — recalculate Y positions so everything sits on 
or hangs from the correct surfaces.

Frame reference:
  base tube top surface:    Y = 0.1016  (floor for interior parts)
  top ring bottom surface:  Y = 0.6096  (ceiling for interior parts)
  top ring top surface:     Y = 0.7112
  mount plate top surface:  Y = 0.7172  (engine sits here)
  mount plate bottom:       Y = 0.7112  (pump/coupling hang from here)
"""
import json, struct, os

def get_native_dims(path):
    """Returns (sizeX, sizeY, sizeZ, centerY) or None."""
    try:
        with open(path, 'rb') as f:
            magic, ver, length = struct.unpack('<III', f.read(12))
            if magic != 0x46546C67: return None
            cl, ct = struct.unpack('<II', f.read(8))
            jd = json.loads(f.read(cl).decode('utf-8'))
        for mesh in jd.get('meshes', []):
            for prim in mesh.get('primitives', []):
                pi = prim.get('attributes', {}).get('POSITION')
                if pi is not None:
                    acc = jd['accessors'][pi]
                    if 'min' in acc:
                        mn, mx = acc['min'], acc['max']
                        return (
                            mx[0]-mn[0], mx[1]-mn[1], mx[2]-mn[2],
                            (mx[1]+mn[1])/2
                        )
    except: pass
    return None

parts_dir = r'Assets\_Project\Data\Packages\power_cube_frame\assets\parts'

# Frame constants
TUBE = 0.1016
FLOOR = TUBE           # 0.1016 - top of base tubes (floor)
CEILING = TUBE + 0.508 # 0.6096 - top of posts / bottom of top ring
PLATE_BOTTOM = 0.7112  # bottom of engine mount plate
PLATE_TOP = 0.7172     # top of engine mount plate (6mm thick)
INNER_X = 0.5084       # inner frame X extent from center
INNER_Z = 0.305        # inner frame Z extent from center
FRONT_Z = -0.3558      # front tube center Z

# Real-world dimensions (X, Y, Z) meters
real_dims = {
    'engine':             (0.42, 0.40, 0.38),
    'pump_coupling':      (0.10, 0.075, 0.10),
    'hydraulic_pump':     (0.25, 0.14, 0.12),
    'reservoir':          (0.40, 0.30, 0.25),
    'pressure_hose':      (0.025, 0.025, 0.50),
    'return_hose':        (0.032, 0.032, 0.40),
    'oil_cooler':         (0.30, 0.30, 0.05),
    'fuel_tank':          (0.35, 0.25, 0.25),
    'fuel_line':          (0.012, 0.012, 0.35),
    'fuel_shutoff_valve': (0.05, 0.03, 0.03),
    'battery':            (0.208, 0.197, 0.173),
    'battery_cables':     (0.015, 0.015, 0.40),
    'key_switch':         (0.030, 0.040, 0.030),
    'choke_cable':        (0.008, 0.008, 0.50),
    'throttle_cable':     (0.008, 0.008, 0.50),
    'pressure_gauge':     (0.063, 0.063, 0.060),
}

# Position rules:
# - "floor" items: bottom touches FLOOR (Y = 0.1016)
# - "top" items: bottom touches PLATE_TOP or hang from PLATE_BOTTOM  
# - "wall" items: mounted on frame tube face
# - "suspended" items: hang from coupling/engine

def floor_y(part_id):
    """Center Y for a part sitting on the frame floor."""
    return FLOOR + real_dims[part_id][1] / 2

def hang_from_plate(part_id):
    """Center Y for a part hanging from the bottom of the mount plate."""
    return PLATE_BOTTOM - real_dims[part_id][1] / 2

def on_plate(part_id):
    """Center Y for a part sitting on the mount plate."""
    return PLATE_TOP + real_dims[part_id][1] / 2

positions = {
    # ENGINE: sits on mount plate
    'engine': {
        'pos': (0.0, on_plate('engine'), 0.0),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # PUMP COUPLING: hangs just below mount plate, centered on engine shaft axis
    'pump_coupling': {
        'pos': (-0.15, hang_from_plate('pump_coupling'), 0.0),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # HYDRAULIC PUMP: hangs below coupling, directly connected
    'hydraulic_pump': {
        'pos': (-0.15, PLATE_BOTTOM - 0.075 - real_dims['hydraulic_pump'][1]/2, 0.0),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # RESERVOIR: sits on frame floor, center-right area
    'reservoir': {
        'pos': (0.20, floor_y('reservoir'), 0.0),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # PRESSURE HOSE: runs from pump down to reservoir area, along left wall
    'pressure_hose': {
        'pos': (-0.35, floor_y('reservoir'), -0.15),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # RETURN HOSE: runs from reservoir area back to manifold
    'return_hose': {
        'pos': (0.05, floor_y('reservoir'), -0.15),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # OIL COOLER: mounted vertically on front frame face, sitting on base tube
    'oil_cooler': {
        'pos': (0.0, FLOOR + real_dims['oil_cooler'][1]/2, FRONT_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # FUEL TANK: sits on frame floor, left side (no floating bracket!)
    'fuel_tank': {
        'pos': (-0.25, floor_y('fuel_tank'), 0.05),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # FUEL LINE: runs from tank to engine, near the fuel tank
    'fuel_line': {
        'pos': (-0.30, floor_y('fuel_tank'), 0.05),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # FUEL SHUTOFF VALVE: inline on fuel system, near fuel tank bottom
    'fuel_shutoff_valve': {
        'pos': (-0.15, FLOOR + real_dims['fuel_shutoff_valve'][1]/2, 0.05),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # BATTERY: sits on frame floor, front-right corner
    'battery': {
        'pos': (0.30, floor_y('battery'), -0.12),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # BATTERY CABLES: from battery toward starter, along base
    'battery_cables': {
        'pos': (0.20, FLOOR + 0.05, -0.10),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # KEY SWITCH: mounted on front face tube, mid-height
    'key_switch': {
        'pos': (0.15, FLOOR + 0.25, FRONT_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # CHOKE CABLE: from front panel (key switch area) to engine, runs along tube
    'choke_cable': {
        'pos': (-0.05, FLOOR + 0.20, FRONT_Z + 0.05),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # THROTTLE CABLE: adjacent to choke cable
    'throttle_cable': {
        'pos': (0.05, FLOOR + 0.20, FRONT_Z + 0.05),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
    
    # PRESSURE GAUGE: mounted on front face, near pressure manifold
    'pressure_gauge': {
        'pos': (-0.25, FLOOR + 0.30, FRONT_Z),
        'rot': (0.0, 0.0, 0.0, 1.0),
    },
}

# Print computed positions
print("=== Corrected Positions (grounded) ===\n")
for pid, pl in positions.items():
    p = pl['pos']
    rd = real_dims[pid]
    bottom = p[1] - rd[1]/2
    print(f"  {pid:25s}  Y={p[1]:.4f}  bottom={bottom:.4f}  height={rd[1]:.3f}")

# Update machine.json
json_path = r'Assets\_Project\Data\Packages\power_cube_frame\machine.json'
with open(json_path, 'r') as f:
    data = json.load(f)

# Map pressure_gauge → pressure_gauge_inline  
positions['pressure_gauge_inline'] = positions.pop('pressure_gauge')
# Use pressure_gauge real dims for the inline variant
real_dims['pressure_gauge_inline'] = real_dims['pressure_gauge']

count = 0
for pp in data['previewConfig']['partPlacements']:
    pid = pp['partId']
    if pid not in positions:
        continue
    
    pl = positions[pid]
    p = pl['pos']
    r = pl['rot']
    
    # Compute scale
    rd = real_dims.get(pid, real_dims.get(pid.replace('_inline', ''), None))
    glb_name = 'pressure_gauge' if pid == 'pressure_gauge_inline' else pid
    glb_path = os.path.join(parts_dir, f'{glb_name}.glb')
    native = get_native_dims(glb_path)
    
    if native and native[0] > 0.01:
        # Real model: scale = real / native
        sx = rd[0] / native[0]
        sy = rd[1] / native[1]
        sz = rd[2] / native[2]
    else:
        # Placeholder: scale = real dims directly
        sx, sy, sz = rd[0], rd[1], rd[2]
    
    pp['assembledPosition'] = {'x': p[0], 'y': p[1], 'z': p[2]}
    pp['assembledRotation'] = {'x': r[0], 'y': r[1], 'z': r[2], 'w': r[3]}
    pp['assembledScale'] = {'x': sx, 'y': sy, 'z': sz}
    pp['startScale'] = {'x': sx, 'y': sy, 'z': sz}
    
    count += 1

with open(json_path, 'w') as f:
    json.dump(data, f, indent=2)

print(f"\nUpdated {count} part placements")
