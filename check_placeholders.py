"""
Fix the 7 placeholder parts that don't have real GLB models yet.
These are ~1.1KB placeholder GLBs. We'll set sensible play positions
and use the real-world dimensions as the direct scale (since placeholder
models are probably unit cubes or similar tiny geometry).

We'll check what the placeholder native bounds are first.
"""
import json, struct, os

def read_glb_bounds(path):
    with open(path, 'rb') as f:
        magic, version, length = struct.unpack('<III', f.read(12))
        if magic != 0x46546C67:
            return None
        chunk_len, chunk_type = struct.unpack('<II', f.read(8))
        json_data = json.loads(f.read(chunk_len).decode('utf-8'))
        
    meshes = json_data.get('meshes', [])
    accessors = json_data.get('accessors', [])
    
    for mesh in meshes:
        for prim in mesh.get('primitives', []):
            pos_idx = prim.get('attributes', {}).get('POSITION')
            if pos_idx is not None:
                acc = accessors[pos_idx]
                if 'min' in acc and 'max' in acc:
                    mn = acc['min']
                    mx = acc['max']
                    return {
                        'size': [mx[i] - mn[i] for i in range(3)],
                    }
    return None

parts_dir = r'Assets\_Project\Data\Packages\power_cube_frame\assets\parts'

placeholders = [
    'pump_coupling', 'pressure_hose', 'return_hose', 
    'fuel_line', 'battery_cables', 'choke_cable', 'throttle_cable'
]

print("=== Placeholder GLB Analysis ===")
for pid in placeholders:
    path = os.path.join(parts_dir, f'{pid}.glb')
    fsize = os.path.getsize(path)
    b = read_glb_bounds(path)
    if b:
        sz = b['size']
        print(f"  {pid}: {sz[0]:.4f} x {sz[1]:.4f} x {sz[2]:.4f} ({fsize} bytes)")
    else:
        print(f"  {pid}: no valid mesh bounds ({fsize} bytes)")
        # Read raw JSON to inspect
        with open(path, 'rb') as f:
            magic, version, length = struct.unpack('<III', f.read(12))
            chunk_len, chunk_type = struct.unpack('<II', f.read(8))
            raw = f.read(chunk_len).decode('utf-8')
            jd = json.loads(raw)
            print(f"    nodes: {len(jd.get('nodes', []))}")
            print(f"    meshes: {len(jd.get('meshes', []))}")
            print(f"    accessors: {len(jd.get('accessors', []))}")
            if jd.get('accessors'):
                for i, acc in enumerate(jd['accessors']):
                    print(f"    accessor[{i}]: type={acc.get('type')} count={acc.get('count')} min={acc.get('min')} max={acc.get('max')}")
