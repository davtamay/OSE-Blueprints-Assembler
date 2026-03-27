import struct
from pygltflib import GLTF2

# Start from ORIGINAL unmodified GLB
src = r'generated_models/tape_measure_v4/36a47dcd-d59a-483c-8e89-4a7c9fc63d87/base_basic_pbr.glb'
dst = r'generated_models/tape_measure_v4/tape_measure_final.glb'

gltf = GLTF2.load(src)
blob = bytearray(gltf.binary_blob())

for mesh in gltf.meshes:
    for prim in mesh.primitives:
        # --- Bake -90 Y rotation into POSITIONS ---
        # -90 deg Y: x'=-z, y'=y, z'=x
        pi = prim.attributes.POSITION
        if pi is not None:
            acc = gltf.accessors[pi]
            bv = gltf.bufferViews[acc.bufferView]
            offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
            stride = bv.byteStride or 12
            for i in range(acc.count):
                pos = offset + i * stride
                x = struct.unpack_from('f', blob, pos)[0]
                y = struct.unpack_from('f', blob, pos+4)[0]
                z = struct.unpack_from('f', blob, pos+8)[0]
                struct.pack_into('f', blob, pos, -z)
                struct.pack_into('f', blob, pos+4, y)
                struct.pack_into('f', blob, pos+8, x)
            if acc.min and len(acc.min)==3 and acc.max and len(acc.max)==3:
                mn, mx = acc.min[:], acc.max[:]
                acc.min = [min(-mx[2],-mn[2]), mn[1], min(mn[0],mx[0])]
                acc.max = [max(-mx[2],-mn[2]), mx[1], max(mn[0],mx[0])]
            print(f"Rotated {acc.count} positions (-90Y)")

        # --- Same for NORMALS ---
        ni = prim.attributes.NORMAL
        if ni is not None:
            acc = gltf.accessors[ni]
            bv = gltf.bufferViews[acc.bufferView]
            offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
            stride = bv.byteStride or 12
            for i in range(acc.count):
                pos = offset + i * stride
                x = struct.unpack_from('f', blob, pos)[0]
                y = struct.unpack_from('f', blob, pos+4)[0]
                z = struct.unpack_from('f', blob, pos+8)[0]
                struct.pack_into('f', blob, pos, -z)
                struct.pack_into('f', blob, pos+4, y)
                struct.pack_into('f', blob, pos+8, x)
            print(f"Rotated {acc.count} normals (-90Y)")

        # --- Same for TANGENTS ---
        ti = prim.attributes.TANGENT
        if ti is not None:
            acc = gltf.accessors[ti]
            bv = gltf.bufferViews[acc.bufferView]
            offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
            stride = bv.byteStride or 16
            for i in range(acc.count):
                pos = offset + i * stride
                x = struct.unpack_from('f', blob, pos)[0]
                y = struct.unpack_from('f', blob, pos+4)[0]
                z = struct.unpack_from('f', blob, pos+8)[0]
                struct.pack_into('f', blob, pos, -z)
                struct.pack_into('f', blob, pos+4, y)
                struct.pack_into('f', blob, pos+8, x)
            print(f"Rotated {acc.count} tangents (-90Y)")

        # --- Flip winding to fix normals ---
        if prim.indices is not None:
            acc = gltf.accessors[prim.indices]
            bv = gltf.bufferViews[acc.bufferView]
            idx_offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
            comp_map = {5121:('B',1), 5123:('H',2), 5125:('I',4)}
            fmt, sz = comp_map[acc.componentType]
            flipped = 0
            for i in range(0, acc.count - 2, 3):
                p1 = idx_offset + (i+1) * sz
                p2 = idx_offset + (i+2) * sz
                v1 = struct.unpack_from(fmt, blob, p1)[0]
                v2 = struct.unpack_from(fmt, blob, p2)[0]
                struct.pack_into(fmt, blob, p1, v2)
                struct.pack_into(fmt, blob, p2, v1)
                flipped += 1
            print(f"Flipped {flipped} triangles")

        # --- Negate normals to match flipped winding ---
        if ni is not None:
            acc = gltf.accessors[ni]
            bv = gltf.bufferViews[acc.bufferView]
            offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
            stride = bv.byteStride or 12
            for i in range(acc.count):
                for j in range(3):
                    pos = offset + i * stride + j * 4
                    val = struct.unpack_from('f', blob, pos)[0]
                    struct.pack_into('f', blob, pos, -val)
            print(f"Negated {acc.count} normals")

gltf.set_binary_blob(bytes(blob))

for mat in gltf.materials:
    mat.doubleSided = True

for node in gltf.nodes:
    node.rotation = None

gltf.save(dst)
import os
print(f"Saved ({os.path.getsize(dst)} bytes)")
