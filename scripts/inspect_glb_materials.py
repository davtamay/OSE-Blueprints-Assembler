import json, struct, pathlib, sys

path = sys.argv[1] if len(sys.argv) > 1 else r'Assets/_Project/Data/Packages/power_cube_frame/assets/tools/tool_tape_measure.glb'
revert_normals = '--revert-normals' in sys.argv

b = bytearray(pathlib.Path(path).read_bytes())

# Parse GLB
off = 12
json_len, json_type = struct.unpack_from('<II', b, off)
json_start = off + 8
js = json.loads(b[json_start:json_start+json_len].decode('utf-8'))

bin_off = json_start + json_len
bin_len, bin_type = struct.unpack_from('<II', b, bin_off)
bin_start = bin_off + 8
bin_data = bytearray(b[bin_start:bin_start+bin_len])

accessors = js.get('accessors', [])
buffer_views = js.get('bufferViews', [])

if revert_normals:
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
    b[bin_start:bin_start+bin_len] = bin_data
    pathlib.Path(path).write_bytes(bytes(b))
    print(f'Reverted {flipped} normals')

# Inspect materials
print(f'\nFile: {path}')
materials = js.get('materials', [])
textures = js.get('textures', [])
images = js.get('images', [])

print(f'Materials: {len(materials)}  Textures: {len(textures)}  Images: {len(images)}')

for i, mat in enumerate(materials):
    name = mat.get('name', 'unnamed')
    print(f'\nMaterial {i}: {name}')
    pbr = mat.get('pbrMetallicRoughness', {})
    if 'baseColorTexture' in pbr:
        ti = pbr['baseColorTexture']['index']
        print(f'  baseColorTexture: tex={ti}')
    if 'baseColorFactor' in pbr:
        print(f'  baseColorFactor: {pbr["baseColorFactor"]}')
    if 'metallicFactor' in pbr:
        print(f'  metallicFactor: {pbr["metallicFactor"]}')
    if 'roughnessFactor' in pbr:
        print(f'  roughnessFactor: {pbr["roughnessFactor"]}')
    if 'metallicRoughnessTexture' in pbr:
        ti = pbr['metallicRoughnessTexture']['index']
        print(f'  metallicRoughnessTexture: tex={ti}')
    if 'normalTexture' in mat:
        nt = mat['normalTexture']
        print(f'  normalTexture: tex={nt["index"]} scale={nt.get("scale", 1.0)}')
    if 'occlusionTexture' in mat:
        print(f'  occlusionTexture: tex={mat["occlusionTexture"]["index"]}')
    if 'emissiveTexture' in mat:
        print(f'  emissiveTexture: tex={mat["emissiveTexture"]["index"]}')
    ds = mat.get('doubleSided', False)
    am = mat.get('alphaMode', 'OPAQUE')
    print(f'  doubleSided: {ds}  alphaMode: {am}')
    print(f'  all keys: {list(mat.keys())}')

for i, tex in enumerate(textures):
    src = tex.get('source', '?')
    img_name = images[src].get('name', images[src].get('uri', 'embedded')) if src < len(images) else '?'
    mime = images[src].get('mimeType', '?') if isinstance(src, int) and src < len(images) else '?'
    print(f'\nTexture {i}: image={src} ({img_name}) mime={mime}')
