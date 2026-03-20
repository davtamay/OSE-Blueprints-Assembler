import struct, json, os, math

def get_glb_info(path):
    """Get center offset and extents of GLB model"""
    with open(path, 'rb') as f:
        magic, version, length = struct.unpack('<III', f.read(12))
        json_len, json_type = struct.unpack('<II', f.read(8))
        gltf = json.loads(f.read(json_len))
    mins = [float('inf')]*3
    maxs = [float('-inf')]*3
    for mesh in gltf.get('meshes', []):
        for prim in mesh.get('primitives', []):
            pos_idx = prim.get('attributes', {}).get('POSITION')
            if pos_idx is None:
                continue
            acc = gltf['accessors'][pos_idx]
            if 'min' in acc and 'max' in acc:
                for i in range(3):
                    mins[i] = min(mins[i], acc['min'][i])
                    maxs[i] = max(maxs[i], acc['max'][i])
    dims = [maxs[i] - mins[i] for i in range(3)]
    center = [(mins[i] + maxs[i]) / 2 for i in range(3)]
    return mins, maxs, dims, center

parts_dir = r'Assets\_Project\Data\Packages\power_cube_frame\assets\parts'
parts = ['base_tube_long', 'base_tube_short', 'vertical_post', 'engine_mount_plate']

for name in parts:
    path = os.path.join(parts_dir, f'{name}.glb')
    if not os.path.exists(path):
        print(f'{name}: FILE MISSING')
        continue
    mins, maxs, dims, center = get_glb_info(path)
    print(f'{name}:')
    print(f'  native dims: {dims[0]:.4f} x {dims[1]:.4f} x {dims[2]:.4f}')
    print(f'  native min:  ({mins[0]:.4f}, {mins[1]:.4f}, {mins[2]:.4f})')
    print(f'  native max:  ({maxs[0]:.4f}, {maxs[1]:.4f}, {maxs[2]:.4f})')
    print(f'  native center: ({center[0]:.4f}, {center[1]:.4f}, {center[2]:.4f})')
