"""
Compute correct play positions and scales for ALL non-structural parts
in the Power Cube frame based on real-world dimensions.

Power Cube layout (top-down, looking down -Y):
    
         FRONT (Z negative)
    ┌──────────────────────────┐
    │  oil_cooler (mounted     │
    │  on front face)          │
    │                          │
    │  reservoir    fuel_tank  │
    │  (left/floor) (right)    │
    │                          │
    │  battery (floor, front)  │
    │                          │
    │      ENGINE (top center) │
    │      pump_coupling       │
    │      hydraulic_pump      │
    │                          │
    │  key_switch (front face) │
    │  choke/throttle cables   │
    │  pressure_gauge          │
    └──────────────────────────┘
         BACK (Z positive)

Frame dimensions (from fix_frame_geometry.py):
  Long tubes along X: 1.22m
  Short tubes along Z: 0.61m  
  Post height: 0.508m
  Tube cross-section: 0.1016m
  Frame inner space: ~1.02m (X) x ~0.41m (Z) x ~0.508m (Y)
  Base Y (top of base tubes): 0.1016m
  Top Y (bottom of top tubes): 0.6096m
  Post corners at X=±0.5592, Z=±0.3558
"""

import json, struct, os, math

# ── Frame reference constants ──
TUBE = 0.1016
HALF_TUBE = TUBE / 2
BASE_TOP = TUBE                    # 0.1016 - top surface of base tubes
POST_TOP = TUBE + 0.508            # 0.6096 - top of vertical posts
TOP_TUBE_TOP = POST_TOP + TUBE     # 0.7112 - top surface of top ring
PLATE_Y = TOP_TUBE_TOP + 0.003     # 0.7142 - engine mount plate center

# Frame inner boundaries
INNER_X = 0.5592 - HALF_TUBE  # ~0.5084 from center
INNER_Z = 0.3558 - HALF_TUBE  # ~0.305 from center

# ── GLB bounding box reader ──
def read_glb_bounds(path):
    """Read GLB file and compute mesh bounding box."""
    with open(path, 'rb') as f:
        magic, version, length = struct.unpack('<III', f.read(12))
        if magic != 0x46546C67:
            return None
        
        # Read JSON chunk
        chunk_len, chunk_type = struct.unpack('<II', f.read(8))
        json_data = json.loads(f.read(chunk_len).decode('utf-8'))
        
        # Read BIN chunk
        bin_offset = 12 + 8 + chunk_len
        f.seek(bin_offset)
        if f.read(4) == b'':
            return None
        f.seek(bin_offset)
        bin_chunk_len, bin_chunk_type = struct.unpack('<II', f.read(8))
        bin_data = f.read(bin_chunk_len)
    
    # Find position accessor from mesh primitives
    meshes = json_data.get('meshes', [])
    accessors = json_data.get('accessors', [])
    buffer_views = json_data.get('bufferViews', [])
    
    # Check if accessor has min/max (most GLBs do)
    for mesh in meshes:
        for prim in mesh.get('primitives', []):
            pos_idx = prim.get('attributes', {}).get('POSITION')
            if pos_idx is not None:
                acc = accessors[pos_idx]
                if 'min' in acc and 'max' in acc:
                    mn = acc['min']
                    mx = acc['max']
                    return {
                        'min': mn,
                        'max': mx,
                        'size': [mx[i] - mn[i] for i in range(3)],
                        'center': [(mx[i] + mn[i]) / 2 for i in range(3)],
                    }
    return None

# ── Get bounds for all part GLBs ──
parts_dir = r'Assets\_Project\Data\Packages\power_cube_frame\assets\parts'

non_structural_ids = [
    'engine', 'pump_coupling', 'hydraulic_pump', 'reservoir',
    'pressure_hose', 'return_hose', 'oil_cooler',
    'fuel_tank', 'fuel_line', 'fuel_shutoff_valve',
    'battery', 'battery_cables', 'key_switch',
    'choke_cable', 'throttle_cable', 'pressure_gauge'
]

print("=== Non-Structural GLB Bounds ===\n")
bounds = {}
for pid in non_structural_ids:
    glb_path = os.path.join(parts_dir, f'{pid}.glb')
    fsize = os.path.getsize(glb_path) if os.path.exists(glb_path) else 0
    is_placeholder = fsize < 2000  # placeholder GLBs are ~1.1KB
    
    if os.path.exists(glb_path):
        b = read_glb_bounds(glb_path)
        if b:
            bounds[pid] = b
            sz = b['size']
            label = " [PLACEHOLDER]" if is_placeholder else ""
            print(f"  {pid}: {sz[0]:.4f} x {sz[1]:.4f} x {sz[2]:.4f}  ({fsize} bytes){label}")
        else:
            print(f"  {pid}: COULD NOT READ BOUNDS ({fsize} bytes)")
    else:
        print(f"  {pid}: FILE NOT FOUND")

# ── Real-world dimensions (meters) ──
# Based on OSE Power Cube VII specifications and standard component sizes
real_dims = {
    # Engine: 27HP Kohler/Lifan - approx 420mm L x 380mm W x 400mm H
    'engine': (0.42, 0.40, 0.38),
    
    # Lovejoy L-100 spider coupling - ~100mm OD x 75mm long  
    'pump_coupling': (0.10, 0.075, 0.10),
    
    # 2-section hydraulic gear pump - ~250mm L x 120mm W x 140mm H
    'hydraulic_pump': (0.25, 0.14, 0.12),
    
    # 10-gallon hydraulic reservoir - ~400mm L x 250mm W x 300mm H
    'reservoir': (0.40, 0.30, 0.25),
    
    # SAE 100R2 pressure hose 3/4" - ~600mm long, ~25mm OD
    'pressure_hose': (0.025, 0.025, 0.60),
    
    # SAE 100R1 return hose 1" - ~500mm long, ~32mm OD
    'return_hose': (0.032, 0.032, 0.50),
    
    # Oil cooler (aluminum heat exchanger) - ~300mm W x 300mm H x 50mm D
    'oil_cooler': (0.30, 0.30, 0.05),
    
    # 7-gallon steel fuel tank - ~350mm L x 250mm W x 250mm H
    'fuel_tank': (0.35, 0.25, 0.25),
    
    # Fuel line 3/8" hose - ~400mm long, ~12mm OD
    'fuel_line': (0.012, 0.012, 0.40),
    
    # 1/4-turn brass shutoff valve - ~50mm x 30mm x 30mm
    'fuel_shutoff_valve': (0.05, 0.03, 0.03),
    
    # Group 26 battery - 208mm L x 173mm W x 197mm H
    'battery': (0.208, 0.197, 0.173),
    
    # Battery cables 2-gauge - ~500mm, ~15mm OD
    'battery_cables': (0.015, 0.015, 0.50),
    
    # 4-position key switch - ~30mm dia x 40mm deep, mounted in panel
    'key_switch': (0.030, 0.040, 0.030),
    
    # Choke cable with sheath - ~600mm long, ~8mm OD
    'choke_cable': (0.008, 0.008, 0.60),
    
    # Throttle cable with sheath - ~600mm long, ~8mm OD  
    'throttle_cable': (0.008, 0.008, 0.60),
    
    # Pressure gauge 0-5000 PSI, 63mm dial face, ~60mm deep  
    'pressure_gauge': (0.063, 0.063, 0.060),
}

# ── Placement positions within the frame ──
# Engine sits on mount plate, centered, shaft pointing toward -X (pump side)
# Engine bottom on mount plate top surface
# Engine center Y = PLATE_Y + engine_height/2

ENGINE_Y = PLATE_Y + real_dims['engine'][1] / 2  # ~0.914

# Pump coupling hangs below engine mount plate, aligned with engine shaft
# At -X side (left end of frame) just below the plate
COUPLING_Y = PLATE_Y - real_dims['pump_coupling'][1] / 2  # ~0.677

# Hydraulic pump below coupling, centered on the shaft
PUMP_Y = COUPLING_Y - real_dims['pump_coupling'][1]/2 - real_dims['hydraulic_pump'][1]/2  # ~0.564

# Reservoir sits on the base floor, right side of frame interior
RES_Y = BASE_TOP + real_dims['reservoir'][1] / 2  #  ~0.252

# Oil cooler mounted on front face of frame (Z negative), vertically
COOLER_Y = BASE_TOP + 0.508 / 2  # centered vertically in frame opening

# Fuel tank inside frame, upper area, left side - sits on a shelf or bracket
# Mount it partway up the frame
FUEL_TANK_Y = BASE_TOP + real_dims['fuel_tank'][1] / 2 + 0.10  # above base with bracket

# Battery on the base floor, right-front area
BATTERY_Y = BASE_TOP + real_dims['battery'][1] / 2

# Small items positions
placements = {}

# Engine - centered on frame top, slightly offset toward pump side (-X)
placements['engine'] = {
    'pos': (0.05, ENGINE_Y, 0.0),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Pump coupling - below engine at -X end, aligned with engine shaft
placements['pump_coupling'] = {
    'pos': (-0.20, COUPLING_Y, 0.0),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Hydraulic pump - bolted below coupling
placements['hydraulic_pump'] = {
    'pos': (-0.20, PUMP_Y, 0.0),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Reservoir - inside frame, on base floor, right side (+X)
placements['reservoir'] = {
    'pos': (0.25, RES_Y, 0.0),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Pressure hose - from pump outlet running along frame
placements['pressure_hose'] = {
    'pos': (-0.35, 0.45, -0.15),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Return hose - from return manifold to reservoir
placements['return_hose'] = {
    'pos': (0.10, 0.30, -0.15),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Oil cooler - mounted on front face (Z-), vertically centered
placements['oil_cooler'] = {
    'pos': (0.0, COOLER_Y, -INNER_Z - 0.03),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Fuel tank - inside frame, left side, upper area
placements['fuel_tank'] = {
    'pos': (-0.30, FUEL_TANK_Y, 0.10),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Fuel line - runs from tank down to shutoff valve / engine
placements['fuel_line'] = {
    'pos': (-0.35, 0.30, 0.10),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Fuel shutoff valve - inline on fuel system, near engine
placements['fuel_shutoff_valve'] = {
    'pos': (-0.20, 0.25, 0.10),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Battery - on the base floor, front-right
placements['battery'] = {
    'pos': (0.30, BATTERY_Y, -0.15),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Battery cables - from battery to starter / ground
placements['battery_cables'] = {
    'pos': (0.30, 0.25, -0.05),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Key switch - mounted on front face, operator side
placements['key_switch'] = {
    'pos': (0.15, 0.40, -INNER_Z - 0.02),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Choke cable - from front panel to engine carburetor
placements['choke_cable'] = {
    'pos': (-0.10, 0.45, -0.20),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Throttle cable - from front panel to engine carburetor
placements['throttle_cable'] = {
    'pos': (0.10, 0.45, -0.20),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# Pressure gauge - on pressure manifold, front-facing
placements['pressure_gauge'] = {
    'pos': (-0.30, 0.50, -INNER_Z - 0.02),
    'rot': (0.0, 0.0, 0.0, 1.0),
}

# ── Compute scales ──
print("\n=== Computed Placements ===\n")
final_placements = {}

for pid in non_structural_ids:
    if pid not in bounds:
        # Use pressure_gauge bounds for pressure_gauge_inline mapping
        continue
    
    rd = real_dims[pid]
    native_sz = bounds[pid]['size']
    
    # Scale = real / native per axis
    sx = rd[0] / native_sz[0] if native_sz[0] > 0.001 else 1.0
    sy = rd[1] / native_sz[1] if native_sz[1] > 0.001 else 1.0
    sz = rd[2] / native_sz[2] if native_sz[2] > 0.001 else 1.0
    
    pl = placements[pid]
    
    final_placements[pid] = {
        'pos': pl['pos'],
        'rot': pl['rot'],
        'scale': (sx, sy, sz),
    }
    
    p = pl['pos']
    print(f"  {pid}:")
    print(f"    real: {rd[0]:.3f} x {rd[1]:.3f} x {rd[2]:.3f}")
    print(f"    native: {native_sz[0]:.4f} x {native_sz[1]:.4f} x {native_sz[2]:.4f}")
    print(f"    scale: ({sx:.6f}, {sy:.6f}, {sz:.6f})")
    print(f"    pos: ({p[0]:.3f}, {p[1]:.3f}, {p[2]:.3f})")

# Map pressure_gauge to pressure_gauge_inline
if 'pressure_gauge' in final_placements:
    final_placements['pressure_gauge_inline'] = final_placements.pop('pressure_gauge')

# ── Update machine.json ──
json_path = r'Assets\_Project\Data\Packages\power_cube_frame\machine.json'
with open(json_path, 'r') as f:
    data = json.load(f)

count = 0
for pp in data.get('previewConfig', {}).get('partPlacements', []):
    pid = pp.get('partId', '')
    if pid in final_placements:
        pl = final_placements[pid]
        p, r, s = pl['pos'], pl['rot'], pl['scale']
        
        pp['playPosition'] = {'x': p[0], 'y': p[1], 'z': p[2]}
        pp['playRotation'] = {'x': r[0], 'y': r[1], 'z': r[2], 'w': r[3]}
        pp['playScale'] = {'x': s[0], 'y': s[1], 'z': s[2]}
        
        # Also update start scale to match (same model scale in tray)
        pp['startScale'] = {'x': s[0], 'y': s[1], 'z': s[2]}
        
        count += 1
        print(f"\n  Updated {pid}")

with open(json_path, 'w') as f:
    json.dump(data, f, indent=2)

print(f"\n\nTotal updated: {count} placements")
