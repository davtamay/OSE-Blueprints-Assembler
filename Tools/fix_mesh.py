import trimesh
import numpy as np
import json, struct, copy, io

# Use pygltflib or raw struct approach since trimesh export may lose PBR materials
# Instead, let's use trimesh for the mesh fixing, then use the raw GLB approach
# to patch the accessor data in-place

src = 'generated_models/tape_measure_v4/36a47dcd-d59a-483c-8e89-4a7c9fc63d87/base_basic_pbr.glb'
scene = trimesh.load(src)

for name, geo in scene.geometry.items():
    print(f"Processing: {name}")
    print(f"  Before: {geo.vertices.shape[0]}v, {geo.faces.shape[0]}f")
    
    # Fix winding order to make normals consistent 
    trimesh.repair.fix_normals(geo)
    trimesh.repair.fix_winding(geo)
    
    # Check how many inward after fix
    fc = geo.triangles_center[:200]
    fn = geo.face_normals[:200]
    outward = fc - geo.centroid
    dots = np.sum(fn * outward, axis=1)
    pct = np.sum(dots < 0) / len(dots) * 100
    print(f"  After fix: inward={pct:.1f}%")

# Export - trimesh should preserve PBR textures when exporting scene
out = 'generated_models/tape_measure_v4/tape_measure_fixed.glb'
scene.export(out)
print(f"Exported to {out}")
import os
print(f"Size: {os.path.getsize(out)} bytes")
