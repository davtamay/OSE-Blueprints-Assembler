"""
extract_frame_geometry.py
Extracts precise frame geometry: outer BB, bar thickness, edge positions.
Also samples the shape at edge mid-points to determine actual bar cross-section.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" extract_frame_geometry.py
"""
import FreeCAD
import Part
import json, os

fcstd_path = os.path.join(os.path.dirname(__file__), "D3Dfinalassemblyv1902.fcstd")
doc = FreeCAD.openDocument(fcstd_path)

frame_obj = next((o for o in doc.Objects if o.Label == "Frame"), None)
if frame_obj is None:
    print("ERROR: Frame not found")
    exit(1)

shape = frame_obj.Shape
bb = shape.BoundBox

print("=== FRAME BOUNDING BOX ===")
print(f"  XMin={bb.XMin:.4f}  XMax={bb.XMax:.4f}  XLen={bb.XLength:.4f}")
print(f"  YMin={bb.YMin:.4f}  YMax={bb.YMax:.4f}  YLen={bb.YLength:.4f}")
print(f"  ZMin={bb.ZMin:.4f}  ZMax={bb.ZMax:.4f}  ZLen={bb.ZLength:.4f}")
print(f"  Center=({bb.Center.x:.4f}, {bb.Center.y:.4f}, {bb.Center.z:.4f})")

# Probe the shape at known edge locations to find bar thickness
# If the frame is a hollow cube, shooting a ray from outside toward center
# should hit the bar surface. The bar thickness = distance between entry and exit.

print("\n=== EDGE PROBES (bar thickness detection) ===")

cx, cy, cz = bb.Center.x, bb.Center.y, bb.Center.z

# Probe along X at bottom-front edge (Y near YMin, Z near ZMin)
# This should pass through a bar
probes = [
    ("X-axis at Y=min,Z=min (bottom-front)",
     FreeCAD.Vector(bb.XMin - 10, bb.YMin + 1, bb.ZMin + 1),
     FreeCAD.Vector(1, 0, 0)),
    ("X-axis at Y=mid,Z=mid (center - should be empty)",
     FreeCAD.Vector(bb.XMin - 10, cy, cz),
     FreeCAD.Vector(1, 0, 0)),
    ("Y-axis at X=min,Z=min (bottom-left edge)",
     FreeCAD.Vector(bb.XMin + 1, bb.YMin - 10, bb.ZMin + 1),
     FreeCAD.Vector(0, 1, 0)),
    ("Z-axis at X=min,Y=min (front-left edge)",
     FreeCAD.Vector(bb.XMin + 1, bb.YMin + 1, bb.ZMin - 10),
     FreeCAD.Vector(0, 0, 1)),
]

for label, origin, direction in probes:
    line = Part.makeLine(origin, origin + direction * (bb.DiagonalLength + 20))
    # Find intersection points
    try:
        dist_info = shape.distToShape(Part.Vertex(origin))
        print(f"  {label}: dist from probe origin = {dist_info[0]:.4f}")
    except:
        pass

    # Use section cuts to measure bar thickness
    # Cut the shape with a plane at the probe location

# More reliable approach: use cross-sections at known edge locations
print("\n=== CROSS-SECTION ANALYSIS ===")

# Take a slice at Z=ZMin+1mm (bottom face) to see the bottom bars
import Part
z_near_bottom = bb.ZMin + 1
plane_bottom = Part.makePlane(bb.XLength + 20, bb.YLength + 20,
    FreeCAD.Vector(bb.XMin - 10, bb.YMin - 10, z_near_bottom),
    FreeCAD.Vector(0, 0, 1))

try:
    section_bottom = shape.section(plane_bottom)
    edges_bottom = section_bottom.Edges
    print(f"  Bottom slice (Z={z_near_bottom:.1f}): {len(edges_bottom)} edges")
    if edges_bottom:
        sec_bb = section_bottom.BoundBox
        print(f"    Section BB: X=[{sec_bb.XMin:.2f}, {sec_bb.XMax:.2f}] "
              f"Y=[{sec_bb.YMin:.2f}, {sec_bb.YMax:.2f}]")
except Exception as e:
    print(f"  Bottom slice failed: {e}")

# Slice at Y=YMin+1 (front face)
y_near_front = bb.YMin + 1
plane_front = Part.makePlane(bb.XLength + 20, bb.ZLength + 20,
    FreeCAD.Vector(bb.XMin - 10, y_near_front, bb.ZMin - 10),
    FreeCAD.Vector(0, 1, 0))

try:
    section_front = shape.section(plane_front)
    edges_front = section_front.Edges
    print(f"  Front slice (Y={y_near_front:.1f}): {len(edges_front)} edges")
    if edges_front:
        sec_bb = section_front.BoundBox
        print(f"    Section BB: X=[{sec_bb.XMin:.2f}, {sec_bb.XMax:.2f}] "
              f"Z=[{sec_bb.ZMin:.2f}, {sec_bb.ZMax:.2f}]")
except Exception as e:
    print(f"  Front slice failed: {e}")

# More direct: check if point is inside shape at various locations
print("\n=== POINT-IN-SOLID TESTS ===")

test_points = [
    # Test bar thickness by checking points at increasing depths from edge
    ("corner (0,0,0)", bb.XMin, bb.YMin, bb.ZMin),
    ("corner +5mm", bb.XMin + 5, bb.YMin + 5, bb.ZMin + 5),
    ("corner +10mm", bb.XMin + 10, bb.YMin + 10, bb.ZMin + 10),
    ("corner +12.7mm", bb.XMin + 12.7, bb.YMin + 12.7, bb.ZMin + 12.7),
    ("corner +20mm", bb.XMin + 20, bb.YMin + 20, bb.ZMin + 20),
    ("corner +25.4mm", bb.XMin + 25.4, bb.YMin + 25.4, bb.ZMin + 25.4),
    ("corner +30mm", bb.XMin + 30, bb.YMin + 30, bb.ZMin + 30),
    ("edge X mid, near Y/Z", bb.Center.x, bb.YMin + 5, bb.ZMin + 5),
    ("edge X mid, Y+12.7, Z+12.7", bb.Center.x, bb.YMin + 12.7, bb.ZMin + 12.7),
    ("edge X mid, Y+25.4, Z+5", bb.Center.x, bb.YMin + 25.4, bb.ZMin + 5),
    ("edge X mid, Y+5, Z+25.4", bb.Center.x, bb.YMin + 5, bb.ZMin + 25.4),
    ("edge X mid, Y+25.4, Z+25.4", bb.Center.x, bb.YMin + 25.4, bb.ZMin + 25.4),
    ("edge X mid, Y+26, Z+26", bb.Center.x, bb.YMin + 26, bb.ZMin + 26),
    ("center", bb.Center.x, bb.Center.y, bb.Center.z),
    # Test along bottom-front edge at various X positions
    ("bottom-front X=min+5", bb.XMin + 5, bb.YMin + 5, bb.ZMin + 5),
    ("bottom-front X=center", bb.Center.x, bb.YMin + 5, bb.ZMin + 5),
    ("bottom-front X=max-5", bb.XMax - 5, bb.YMin + 5, bb.ZMin + 5),
]

for label, x, y, z in test_points:
    pt = FreeCAD.Vector(x, y, z)
    inside = shape.isInside(pt, 0.01, True)
    print(f"  {label:40s} ({x:.1f}, {y:.1f}, {z:.1f}) -> {'INSIDE' if inside else 'outside'}")

# Find bar thickness by binary search along a known edge
print("\n=== BAR THICKNESS MEASUREMENT ===")

# Walk from the edge inward along Z at bottom-front-center edge
# The bar should be solid from ZMin to ZMin+thickness
x_mid = bb.Center.x
y_near = bb.YMin + 5  # 5mm into the bar from Y face

for z_offset in [0.5, 1, 2, 5, 10, 12, 12.7, 13, 15, 20, 25, 25.4, 26, 30, 35, 40, 50]:
    z = bb.ZMin + z_offset
    pt = FreeCAD.Vector(x_mid, y_near, z)
    inside = shape.isInside(pt, 0.01, True)
    print(f"  Z={z:.1f} (offset={z_offset:.1f}mm from ZMin): {'INSIDE' if inside else 'outside'}")

# Same test but walking from Y face inward along Y at bottom-front-center edge
print("\n  Walking inward from Y face:")
z_near = bb.ZMin + 5
for y_offset in [0.5, 1, 2, 5, 10, 12, 12.7, 13, 15, 20, 25, 25.4, 26, 30, 35, 40, 50]:
    y = bb.YMin + y_offset
    pt = FreeCAD.Vector(x_mid, y, z_near)
    inside = shape.isInside(pt, 0.01, True)
    print(f"  Y={y:.1f} (offset={y_offset:.1f}mm from YMin): {'INSIDE' if inside else 'outside'}")

# Get the full JSON output
out = {
    "frame_bb": {
        "min": {"x": bb.XMin, "y": bb.YMin, "z": bb.ZMin},
        "max": {"x": bb.XMax, "y": bb.YMax, "z": bb.ZMax},
        "size": {"x": bb.XLength, "y": bb.YLength, "z": bb.ZLength},
        "center": {"x": bb.Center.x, "y": bb.Center.y, "z": bb.Center.z}
    }
}

out_path = os.path.join(os.path.dirname(__file__), "frame_geometry.json")
with open(out_path, "w") as f:
    json.dump(out, f, indent=2)

print(f"\n[done] Written to {out_path}")
