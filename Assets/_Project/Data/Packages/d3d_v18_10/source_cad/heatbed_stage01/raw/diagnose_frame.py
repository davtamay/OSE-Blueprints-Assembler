"""
diagnose_frame.py
Investigates the Frame compound to find individual bar positions.
Tries multiple approaches: OutList, Links, Shape sub-shapes, all object types.

Run: "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe" diagnose_frame.py
"""
import FreeCAD
import Part
import json, os

fcstd_path = os.path.join(os.path.dirname(__file__), "D3Dfinalassemblyv1902.fcstd")
out_path   = os.path.join(os.path.dirname(__file__), "frame_bars.json")

doc = FreeCAD.openDocument(fcstd_path)

# --- 1. List every object in the document (no type filter) ---
print("\n=== ALL OBJECTS IN DOCUMENT ===")
for obj in doc.Objects:
    lbl = obj.Label
    tid = obj.TypeId
    try:
        pl = obj.Placement
        pos = pl.Base
        pos_str = f"({pos.x:.1f}, {pos.y:.1f}, {pos.z:.1f})"
    except:
        pos_str = "no placement"
    print(f"  {lbl:40s}  {tid:40s}  {pos_str}")

# --- 2. Find the Frame object and inspect it ---
frame_obj = next((o for o in doc.Objects if o.Label == "Frame"), None)

if frame_obj is None:
    print("\nFrame object NOT FOUND")
else:
    print(f"\n=== FRAME OBJECT ===")
    print(f"  TypeId: {frame_obj.TypeId}")
    print(f"  InList:  {[o.Label for o in frame_obj.InList]}")
    print(f"  OutList: {[o.Label for o in frame_obj.OutList]}")

    if hasattr(frame_obj, 'Links'):
        print(f"  Links: {[o.Label for o in frame_obj.Links]}")

    # --- 3. Try to get positions from OutList children ---
    print(f"\n=== FRAME OutList CHILDREN POSITIONS ===")
    out = {}
    for child in frame_obj.OutList:
        try:
            pl = child.getGlobalPlacement()
            b = pl.Base
            q = pl.Rotation.Q
            print(f"  {child.Label:40s}  pos=({b.x:.2f}, {b.y:.2f}, {b.z:.2f})  type={child.TypeId}")
            out[child.Label] = {
                "position": {"x": round(b.x/1000,4), "y": round(b.z/1000,4), "z": round(b.y/1000,4)},
                "rotation": {"x": round(-q[0],6), "y": round(-q[2],6), "z": round(-q[1],6), "w": round(q[3],6)}
            }
        except Exception as e:
            try:
                pl = child.Placement
                b = pl.Base
                print(f"  {child.Label:40s}  Placement=({b.x:.2f},{b.y:.2f},{b.z:.2f})  [no global]  type={child.TypeId}")
            except:
                print(f"  {child.Label:40s}  NO POSITION  type={child.TypeId}")

    # --- 4. Try sub-shapes of the Frame compound shape ---
    print(f"\n=== FRAME SHAPE SUB-SHAPES ===")
    try:
        shape = frame_obj.Shape
        print(f"  Shape type: {shape.ShapeType}, {len(shape.SubShapes)} sub-shapes")
        for i, sub in enumerate(shape.SubShapes):
            bb = sub.BoundBox
            cx = (bb.XMin + bb.XMax) / 2
            cy = (bb.YMin + bb.YMax) / 2
            cz = (bb.ZMin + bb.ZMax) / 2
            sx = bb.XMax - bb.XMin
            sy = bb.YMax - bb.YMin
            sz = bb.ZMax - bb.ZMin
            print(f"  sub[{i:02d}] center=({cx:.1f},{cy:.1f},{cz:.1f})  size=({sx:.1f}x{sy:.1f}x{sz:.1f})")
            # Convert to Unity coords
            ux = round(cx/1000, 4)
            uy = round(cz/1000, 4)
            uz = round(cy/1000, 4)
            out[f"frame_bar_{i:02d}"] = {
                "position": {"x": ux, "y": uy, "z": uz},
                "rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
                "size_mm": {"x": round(sx,1), "y": round(sy,1), "z": round(sz,1)}
            }
    except Exception as e:
        print(f"  Shape access failed: {e}")

    if out:
        with open(out_path, "w") as f:
            json.dump(out, f, indent=2)
        print(f"\n[done] {len(out)} entries written to {out_path}")
