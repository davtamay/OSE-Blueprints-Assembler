"""Fix inside-out GLB: reverse triangle winding order AND ensure normals point outward.
Usage: python scripts/fix_winding.py <path.glb> [--flip-normals]
"""
import json, struct, pathlib, sys

path = sys.argv[1]
flip_normals = '--flip-normals' in sys.argv

b = bytearray(pathlib.Path(path).read_bytes())

# Parse GLB
off = 12
json_len, _ = struct.unpack_from('<II', b, off)
json_start = off + 8
js = json.loads(b[json_start:json_start+json_len].decode('utf-8'))

bin_off = json_start + json_len
bin_len, _ = struct.unpack_from('<II', b, bin_off)
bin_start = bin_off + 8
bin_data = bytearray(b[bin_start:bin_start+bin_len])

accessors = js.get('accessors', [])
buffer_views = js.get('bufferViews', [])

# Reverse triangle winding: swap 2nd and 3rd index in each triangle
triangles_reversed = 0
for mesh in js.get('meshes', []):
    for prim in mesh.get('primitives', []):
        mode = prim.get('mode', 4)  # 4 = TRIANGLES
        if mode != 4:
            continue
        if 'indices' not in prim:
            continue
        acc = accessors[prim['indices']]
        bv = buffer_views[acc['bufferView']]
        byte_offset = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
        count = acc['count']
        comp_type = acc['componentType']
        
        if comp_type == 5123:  # UNSIGNED_SHORT
            fmt, size = '<H', 2
        elif comp_type == 5125:  # UNSIGNED_INT
            fmt, size = '<I', 4
        elif comp_type == 5121:  # UNSIGNED_BYTE
            fmt, size = '<B', 1
        else:
            print(f'Unknown index component type: {comp_type}')
            continue
        
        for tri in range(0, count, 3):
            if tri + 2 >= count:
                break
            p0 = byte_offset + (tri + 0) * size
            p1 = byte_offset + (tri + 1) * size
            p2 = byte_offset + (tri + 2) * size
            i1 = struct.unpack_from(fmt, bin_data, p1)[0]
            i2 = struct.unpack_from(fmt, bin_data, p2)[0]
            # Swap 2nd and 3rd indices
            struct.pack_into(fmt, bin_data, p1, i2)
            struct.pack_into(fmt, bin_data, p2, i1)
            triangles_reversed += 1

print(f'Reversed winding on {triangles_reversed} triangles')

# Optionally flip normals
if flip_normals:
    flipped = 0
    for mesh in js.get('meshes', []):
        for prim in mesh.get('primitives', []):
            attrs = prim.get('attributes', {})
            if 'NORMAL' not in attrs:
                continue
            acc = accessors[attrs['NORMAL']]
            bv = buffer_views[acc['bufferView']]
            byte_offset = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
            count = acc['count']
            stride = bv.get('byteStride', 12)
            for i in range(count):
                pos = byte_offset + i * stride
                nx, ny, nz = struct.unpack_from('<fff', bin_data, pos)
                struct.pack_into('<fff', bin_data, pos, -nx, -ny, -nz)
                flipped += 1
    print(f'Flipped {flipped} normals')

b[bin_start:bin_start+bin_len] = bin_data
pathlib.Path(path).write_bytes(bytes(b))
print('Saved:', path)
