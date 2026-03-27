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
with open(r'Assets\_Project\Data\Packages\power_cube_frame\machine.json') as f:
    data = json.load(f)

FLOOR = 0.1016
print(f"FLOOR (base top) = {FLOOR}, PLATE_TOP = 0.7172\n")

for pp in data['previewConfig']['partPlacements']:
    pid = pp['partId']
    py = pp['playPosition']['y']
    px = pp['playPosition']['x']
    pz = pp['playPosition']['z']
    sy = pp['playScale']['y']
    sx = pp['playScale']['x']
    sz = pp['playScale']['z']
    
    glb_name = pid
    if 'base_tube_long' in pid or 'top_tube_long' in pid: glb_name = 'base_tube_long'
    elif 'base_tube_short' in pid or 'top_tube_short' in pid: glb_name = 'base_tube_short'
    elif 'vertical_post' in pid: glb_name = 'vertical_post'
    elif pid == 'pressure_gauge_inline': glb_name = 'pressure_gauge'
    
    glb = os.path.join(parts_dir, f'{glb_name}.glb')
    fsize = os.path.getsize(glb) // 1024
    is_placeholder = fsize < 2
    
    nd = get_native(glb)
    if nd:
        native_sz, native_ctr = nd
        world_h = sy * native_sz[1]
        center_off = sy * native_ctr[1]
        bottom = py - world_h/2 + center_off
        label = "PLACEHOLDER" if is_placeholder else "real"
        print(f"  {pid:25s}  Y={py:.4f}  bottom={bottom:.4f}  worldH={world_h:.4f}  [{fsize}KB {label}]")
    else:
        bottom = py - sy/2
        print(f"  {pid:25s}  Y={py:.4f}  bottom={bottom:.4f}  worldH={sy:.4f}  [{fsize}KB EMPTY]")

# Also list asset status
print("\n\n=== ASSET STATUS ===\n")
all_parts = [
    'engine', 'pump_coupling', 'hydraulic_pump', 'reservoir',
    'pressure_hose', 'return_hose', 'oil_cooler',
    'fuel_tank', 'fuel_line', 'fuel_shutoff_valve',
    'battery', 'battery_cables', 'key_switch',
    'choke_cable', 'throttle_cable', 'pressure_gauge',
    'base_tube_long', 'base_tube_short', 'vertical_post', 'engine_mount_plate'
]

for pid in sorted(all_parts):
    glb = os.path.join(parts_dir, f'{pid}.glb')
    if os.path.exists(glb):
        fsize = os.path.getsize(glb)
        if fsize < 2000:
            print(f"  {pid:25s}  {fsize:>10d} bytes  ** PLACEHOLDER (no real model) **")
        else:
            print(f"  {pid:25s}  {fsize:>10d} bytes  OK (real model)")
    else:
        print(f"  {pid:25s}  MISSING")
