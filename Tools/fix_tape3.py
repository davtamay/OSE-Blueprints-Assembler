import struct, math
import numpy as np
from pygltflib import GLTF2

src = r'generated_models/tape_measure_v4/36a47dcd-d59a-483c-8e89-4a7c9fc63d87/base_basic_pbr.glb'
dst = r'generated_models/tape_measure_v4/tape_measure_final.glb'

gltf = GLTF2.load(src)

# 1. Flip winding order (swap v1,v2 in every triangle)
for mesh in gltf.meshes:
    for prim in mesh.primitives:
        if prim.indices is None:
            continue
        acc = gltf.accessors[prim.indices]
        bv = gltf.bufferViews[acc.bufferView]
        blob = bytearray(gltf.binary_blob())
        offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
        comp_map = {5121:('B',1), 5123:('H',2), 5125:('I',4)}
        fmt, sz = comp_map[acc.componentType]
        flipped = 0
        for i in range(0, acc.count - 2, 3):
            p1 = offset + (i+1) * sz
            p2 = offset + (i+2) * sz
            v1 = struct.unpack_from(fmt, blob, p1)[0]
            v2 = struct.unpack_from(fmt, blob, p2)[0]
            struct.pack_into(fmt, blob, p1, v2)
            struct.pack_into(fmt, blob, p2, v1)
            flipped += 1
        print(f"Flipped {flipped} triangles")
        gltf.set_binary_blob(bytes(blob))

# 2. Negate all normals
for mesh in gltf.meshes:
    for prim in mesh.primitives:
        ni = prim.attributes.NORMAL
        if ni is None:
            continue
        acc = gltf.accessors[ni]
        bv = gltf.bufferViews[acc.bufferView]
        blob = bytearray(gltf.binary_blob())
        offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
        stride = bv.byteStride or 12
        for i in range(acc.count):
            for j in range(3):
                pos = offset + i * stride + j * 4
                val = struct.unpack_from('f', blob, pos)[0]
                struct.pack_into('f', blob, pos, -val)
        print(f"Negated {acc.count} normals")
        gltf.set_binary_blob(bytes(blob))

# 3. Double-sided materials
for mat in gltf.materials:
    mat.doubleSided = True
    print(f"Material '{mat.name}' -> doubleSided")

# 4. Bake rotation: 90X then 90Y
s = math.sin(math.radians(45))
c = math.cos(math.radians(45))
qx = np.array([s, 0, 0, c])
qy = np.array([0, s, 0, c])
def qmul(a, b):
    ax,ay,az,aw = a; bx,by,bz,bw = b
    return np.array([aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx,
                     aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz])
qt = qmul(qy, qx)
print(f"Rotation quat: {qt}")

root = gltf.nodes[gltf.scenes[gltf.scene].nodes[0]]
root.rotation = qt.tolist()
print(f"Set rotation on '{root.name}'")

gltf.save(dst)
import os
print(f"Saved {dst} ({os.path.getsize(dst)} bytes)")
