"""Fix tape measure GLB:
1. Bake correct rotation (90X + 90Y) so it faces camera by default
2. Fix normals by flipping winding order + making material double-sided
Start from the ORIGINAL unmodified GLB.
"""
import struct, math
import numpy as np
from pygltflib import GLTF2

src = r'generated_models/tape_measure_v4/36a47dcd-d59a-483c-8e89-4a7c9fc63d87/base_basic_pbr.glb'
dst = r'generated_models/tape_measure_v4/tape_measure_final.glb'

gltf = GLTF2.load(src)

# --- 1. Fix normals by flipping winding order (swap v1,v2 in every triangle) ---
for mesh in gltf.meshes:
    for prim in mesh.primitives:
        if prim.indices is None:
            continue
        acc = gltf.accessors[prim.indices]
        bv = gltf.bufferViews[acc.bufferView]
        blob = bytearray(gltf.binary_blob())
        offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
        
        comp_type_map = {5120: ('b',1), 5121: ('B',1), 5122: ('h',2), 5123: ('H',2), 5125: ('I',4), 5126: ('f',4)}
        fmt, comp_size = comp_type_map[acc.componentType]
        
        # Read all indices
        indices = []
        for i in range(acc.count):
            pos = offset + i * comp_size
            val = struct.unpack_from(fmt, blob, pos)[0]
            indices.append(val)
        
        # Flip winding: swap indices 1 and 2 in each triangle
        flipped = 0
        for i in range(0, len(indices) - 2, 3):
            idx1_pos = offset + (i+1) * comp_size
            idx2_pos = offset + (i+2) * comp_size
            v1 = struct.unpack_from(fmt, blob, idx1_pos)[0]
            v2 = struct.unpack_from(fmt, blob, idx2_pos)[0]
            struct.pack_into(fmt, blob, idx1_pos, v2)
            struct.pack_into(fmt, blob, idx2_pos, v1)
            flipped += 1
        
        print(f"Flipped winding on {flipped} triangles")
        gltf.set_binary_blob(bytes(blob))

# --- 2. Also negate all normals ---
for mesh in gltf.meshes:
    for prim in mesh.primitives:
        norm_idx = prim.attributes.NORMAL
        if norm_idx is None:
            continue
        acc = gltf.accessors[norm_idx]
        bv = gltf.bufferViews[acc.bufferView]
        blob = bytearray(gltf.binary_blob())
        offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
        stride = bv.byteStride or 12  # 3 floats * 4 bytes
        
        for i in range(acc.count):
            for j in range(3):
                pos = offset + i * stride + j * 4
                val = struct.unpack_from('f', blob, pos)[0]
                struct.pack_into('f', blob, pos, -val)
        
        # Update min/max
        if acc.min is not None and acc.max is not None:
            old_min = acc.min[:]
            old_max = acc.max[:]
            acc.min = [-old_max[j] for j in range(3)]
            acc.max = [-old_min[j] for j in range(3)]
        
        print(f"Negated {acc.count} normals")
        gltf.set_binary_blob(bytes(blob))

# --- 3. Make all materials double-sided as safety net ---
for mat in gltf.materials:
    mat.doubleSided = True
    print(f"Material '{mat.name}' set to doubleSided")

# --- 4. Apply rotation: 90X then 90Y (baked into root node) ---
# Quaternion multiplication: q_total = q_Y * q_X (applied right to left)
# q_X = 90 deg around X: (sin45, 0, 0, cos45)
# q_Y = 90 deg around Y: (0, sin45, 0, cos45)
s = math.sin(math.radians(45))
c = math.cos(math.radians(45))

qx = np.array([s, 0, 0, c])  # [x,y,z,w]
qy = np.array([0, s, 0, c])

# Hamilton product: q_total = qy * qx
def qmul(a, b):
    # a,b in [x,y,z,w] format
    ax,ay,az,aw = a
    bx,by,bz,bw = b
    return np.array([
        aw*bx + ax*bw + ay*bz - az*by,
        aw*by - ax*bz + ay*bw + az*bx,
        aw*bz + ax*by - ay*bx + az*bw,
        aw*bw - ax*bx - ay*by - az*bz,
    ])

qt = qmul(qy, qx)
print(f"Combined quaternion (x,y,z,w): {qt}")

root_idx = gltf.scenes[gltf.scene].nodes[0]
root = gltf.nodes[root_idx]
root.rotation = qt.tolist()
print(f"Applied rotation to root node '{root.name}'")

gltf.save(dst)
print(f"\nSaved to {dst}")
import os
print(f"Size: {os.path.getsize(dst)} bytes (original: {os.path.getsize(src)} bytes)")
