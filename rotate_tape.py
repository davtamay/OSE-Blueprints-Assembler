"""Apply rotation to the tape measure by adding a rotation to the root node.
The model bounds show it's ~1.9 wide x 1.81 tall x 0.87 deep.
A tape measure case is a disc/puck - flat cylinder. 
The thin dimension (0.87) is the depth/thickness of the case.
In Unity (Y-up, Z-forward), we want:
- The face of the tape measure visible (facing camera/forward)
- The tape measure upright
Looking at the screenshot, it seems rotated ~90 deg.

Let's try a 90-degree rotation around X to fix it.
Quaternion for 90 deg around X: (sin(45), 0, 0, cos(45)) = (0.7071, 0, 0, 0.7071)
"""
import math
from pygltflib import GLTF2

src = r'generated_models/tape_measure_v4/tape_measure_fixed.glb'
dst = r'generated_models/tape_measure_v4/tape_measure_final.glb'

gltf = GLTF2.load(src)

# Get root node
root_node_idx = gltf.scenes[gltf.scene].nodes[0]
root = gltf.nodes[root_node_idx]

print(f"Root node: '{root.name}'")
print(f"  Current rotation: {root.rotation}")
print(f"  Current translation: {root.translation}")
print(f"  Current scale: {root.scale}")

# Apply 90-degree rotation around X axis to stand it up
# In glTF, quaternion is [x, y, z, w]
angle = math.radians(90)
qx = [math.sin(angle/2), 0, 0, math.cos(angle/2)]
root.rotation = qx
print(f"  Applied rotation: {root.rotation}")

gltf.save(dst)
print(f"Saved to {dst}")

import os
print(f"Size: {os.path.getsize(dst)} bytes")
