"""Fix tape measure GLB: recalculate normals outward + apply rotation.
Uses pygltflib to preserve all PBR materials and textures exactly.
"""
import struct, math, copy
import numpy as np
from pygltflib import GLTF2

src = r'generated_models/tape_measure_v4/36a47dcd-d59a-483c-8e89-4a7c9fc63d87/base_basic_pbr.glb'
dst = r'generated_models/tape_measure_v4/tape_measure_fixed.glb'

gltf = GLTF2.load(src)

# Helper to read accessor data
def read_accessor(gltf, accessor_idx):
    acc = gltf.accessors[accessor_idx]
    bv = gltf.bufferViews[acc.bufferView]
    blob = gltf.binary_blob()
    offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
    
    type_count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
    count = acc.count
    comp_count = type_count[acc.type]
    
    comp_type_map = {5120: 'b', 5121: 'B', 5122: 'h', 5123: 'H', 5125: 'I', 5126: 'f'}
    fmt = comp_type_map[acc.componentType]
    comp_size = struct.calcsize(fmt)
    
    stride = bv.byteStride or (comp_count * comp_size)
    
    data = np.zeros((count, comp_count), dtype=np.float32 if fmt == 'f' else np.int32)
    for i in range(count):
        for j in range(comp_count):
            pos = offset + i * stride + j * comp_size
            val = struct.unpack_from(fmt, blob, pos)[0]
            data[i, j] = val
    return data

def write_accessor(gltf, accessor_idx, data):
    acc = gltf.accessors[accessor_idx]
    bv = gltf.bufferViews[acc.bufferView]
    blob = bytearray(gltf.binary_blob())
    offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
    
    type_count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
    comp_count = type_count[acc.type]
    
    comp_type_map = {5120: 'b', 5121: 'B', 5122: 'h', 5123: 'H', 5125: 'I', 5126: 'f'}
    fmt = comp_type_map[acc.componentType]
    comp_size = struct.calcsize(fmt)
    
    stride = bv.byteStride or (comp_count * comp_size)
    
    for i in range(acc.count):
        for j in range(comp_count):
            pos = offset + i * stride + j * comp_size
            struct.pack_into(fmt, blob, pos, data[i, j] if fmt != 'f' else float(data[i, j]))
    
    # Update min/max
    if acc.min is not None:
        acc.min = [float(data[:, j].min()) for j in range(comp_count)]
    if acc.max is not None:
        acc.max = [float(data[:, j].max()) for j in range(comp_count)]
    
    gltf.set_binary_blob(bytes(blob))

# Process each mesh primitive
for mesh in gltf.meshes:
    for prim in mesh.primitives:
        pos_idx = prim.attributes.POSITION
        norm_idx = prim.attributes.NORMAL
        idx_idx = prim.indices
        
        if pos_idx is None:
            continue
            
        positions = read_accessor(gltf, pos_idx)
        print(f"Positions: {positions.shape}, range: {positions.min(axis=0)} to {positions.max(axis=0)}")
        
        # Read indices
        if idx_idx is not None:
            indices = read_accessor(gltf, idx_idx).astype(np.int32).flatten()
            # Reshape into triangles
            triangles = indices.reshape(-1, 3)
            print(f"Triangles: {triangles.shape[0]}")
        
        if norm_idx is not None:
            normals = read_accessor(gltf, norm_idx)
            
            # Recalculate normals from face geometry
            # For each face, compute face normal, then average per vertex
            new_normals = np.zeros_like(normals)
            centroid = positions.mean(axis=0)
            
            for tri in triangles:
                v0, v1, v2 = positions[tri[0]], positions[tri[1]], positions[tri[2]]
                edge1 = v1 - v0
                edge2 = v2 - v0
                face_normal = np.cross(edge1, edge2)
                length = np.linalg.norm(face_normal)
                if length > 1e-10:
                    face_normal /= length
                    
                    # Ensure outward facing
                    face_center = (v0 + v1 + v2) / 3.0
                    if np.dot(face_normal, face_center - centroid) < 0:
                        face_normal = -face_normal
                        
                    new_normals[tri[0]] += face_normal
                    new_normals[tri[1]] += face_normal
                    new_normals[tri[2]] += face_normal
            
            # Normalize
            lengths = np.linalg.norm(new_normals, axis=1, keepdims=True)
            lengths = np.maximum(lengths, 1e-10)
            new_normals /= lengths
            
            write_accessor(gltf, norm_idx, new_normals)
            print("Normals recalculated (outward-facing)")

# Now fix rotation by modifying the root node transform
# The tape measure needs to be upright. Looking at the screenshot, 
# it's probably tilted. Let's check the node transforms.
print("\nNode transforms:")
for i, node in enumerate(gltf.nodes):
    if node.rotation or node.matrix:
        print(f"  Node {i} '{node.name}': rotation={node.rotation}, matrix={node.matrix}")

# Apply a rotation to the scene root to fix orientation
# From the screenshot it appears to need a rotation around X or Z axis
# The model bounds are 1.9 x 1.81 x 0.87 - it's already laid flat
# For Unity (Y-up), we want the thin dimension along Z (depth)
# and the tape measure face pointing towards camera
# Let's check if there's a root scene node we can rotate
scene_nodes = gltf.scenes[gltf.scene].nodes
print(f"Scene root nodes: {scene_nodes}")

gltf.save(dst)
print(f"\nSaved fixed GLB to {dst}")

import os
print(f"Size: {os.path.getsize(dst)} bytes (original: {os.path.getsize(src)} bytes)")
