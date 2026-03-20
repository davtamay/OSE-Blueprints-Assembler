import json, struct, pathlib, sys

path = sys.argv[1]
b = bytearray(pathlib.Path(path).read_bytes())

off = 12
json_len, _ = struct.unpack_from('<II', b, off)
json_start = off + 8
js = json.loads(b[json_start:json_start+json_len].decode('utf-8'))

# Remove normalTexture from all materials
for mat in js.get('materials', []):
    if 'normalTexture' in mat:
        del mat['normalTexture']
        print('Removed normalTexture from material:', mat.get('name', 'unnamed'))

# Re-encode JSON, pad to 4-byte alignment
new_json = json.dumps(js, separators=(',', ':')).encode('utf-8')
while len(new_json) % 4 != 0:
    new_json += b' '

# Rebuild GLB
bin_off = json_start + json_len
bin_len, bin_type = struct.unpack_from('<II', b, bin_off)
bin_start = bin_off + 8
bin_data = b[bin_start:bin_start+bin_len]

total = 12 + 8 + len(new_json) + 8 + len(bin_data)
out = bytearray()
out += struct.pack('<III', 0x46546C67, 2, total)
out += struct.pack('<II', len(new_json), 0x4E4F534A)
out += new_json
out += struct.pack('<II', len(bin_data), 0x004E4942)
out += bin_data

pathlib.Path(path).write_bytes(bytes(out))
print('Saved. Old JSON:', json_len, 'New JSON:', len(new_json), 'Total:', total)
