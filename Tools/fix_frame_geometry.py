"""
Compute correct play positions for Power Cube frame assembly.

Power Cube frame (top-down view):
                base_tube_long_1 (front, along X)
    ┌──────────────────────────────────┐
    │ VP1                          VP2 │
    │                                  │
  short_1 (left, along Z)          short_2 (right, along Z)
    │                                  │
    │ VP3                          VP4 │
    └──────────────────────────────────┘
                base_tube_long_2 (back, along X)

Real-world dimensions:
- Long tubes: 1.22m (4 ft) along X, cross-section 0.1016m (4 in)
- Short tubes: 0.61m (2 ft) along Z, cross-section 0.1016m (4 in)
- Vertical posts: 0.508m (20 in) tall along Y, cross-section 0.1016m (4 in)
- All tubes are 4x4 inch (0.1016m) square steel

Frame layout:
- Short tubes connect the ends of long tubes
- Long tubes: 1.22m span along X
- Short tubes: 0.61m span along Z  
- But the short tubes sit BETWEEN the long tubes at each end
- So outer-to-outer along Z = 0.61m + 2 * 0.1016m / 2 = 0.61m (short tube center-to-center matches)
  Actually: the frame outer dimension along Z = short tube length = 0.61m
  And along X = long tube length = 1.22m

Coordinate system: Y is up, X is long axis, Z is short axis
Ground level at Y = 0

Base tube positions (tubes sit on ground, center at half-height):
- tube_size = 0.1016m (4 inches)
- half_tube = 0.0508m

Base layer (Y center = half_tube = 0.0508):
- base_tube_long_1: center at (0, 0.0508, -half_short + half_tube) = (0, 0.0508, -0.254)
  Wait, let me think more carefully.

Let me define the frame rectangle:
- Along X: total span = 1.22m, so X goes from -0.61 to +0.61
- Along Z: total span = 0.61m, so Z goes from -0.305 to +0.305

The long tubes run along X. They sit at the front and back edges (Z direction).
The short tubes run along Z. They sit at the left and right edges (X direction).

At the base, tubes are stacked/welded. Typically:
- Long tubes lay on ground first
- Short tubes sit on top of long tubes (or same level if welded flush)

For simplicity, let's put all base tubes at the same level with centers touching:
- All base tube centers at Y = half_tube = 0.0508

Long tubes (along X):
- base_tube_long_1: pos = (0, 0.0508, -0.305 + 0.0508) = (0, 0.0508, -0.254)
  Actually no - the long tubes define the Z extent. Let me say:
  - Front long tube at Z = -0.305 + 0.0508 = -0.254  (inset by half tube width)
  - Back long tube at Z = +0.305 - 0.0508 = +0.254   (inset by half tube width)
  
  Wait, that makes the frame smaller. In reality the tubes form a rectangle where
  the short tubes connect the INSIDE of the long tubes. So:
  
  Long tubes at Z = ±(short_tube_length/2) = ±0.305
  Short tubes at X = ±(long_tube_length/2) = ±0.61
  
  No wait. The short tubes span between the long tubes. So:
  - Long tubes at Z = ± (0.305) but the tube CENTER is at Z = ±0.305
  - Short tube length = distance between long tube inner faces = 0.61 - 0.1016 = 0.5084m?
  
  Hmm, but we have fixed tube lengths. Let me just use:
  - The frame is 1.22m x 0.61m (outer dimensions)
  - Long tubes centers at Z = ±(0.305 - 0.0508) = ±0.254  ... no
  
  Simplest approach: outer dimensions of box.
  - Long tube centers at Z = ±0.305 (half of short dimension)
  - Short tubes connect at X = ±0.61 (half of long dimension)
  
  But the short tube is only 0.61m. If it spans the FULL Z width, 
  it would need to be 0.61m + tube_width = 0.71m. That doesn't work.
  
  OK let me just make it simple and correct:
  The OSE Power Cube base is a rectangle made from tubes welded at corners.
  - 2 long tubes (1.22m, ~4ft) run parallel
  - 2 short tubes (0.61m, ~2ft) connect their ends
  
  The short tubes sit BETWEEN the long tubes at each end.
  So the outer-to-outer Z span = 0.61m + tube_width = 0.61 + 0.1016 = 0.7116m
  The X span = 1.22m (long tube length)
  
  Positions:
  - Long tube centers at Z = ±(0.61/2 + 0.0508) = ±0.3558
  - Short tube centers at X = ±(1.22/2 - 0.0508) = ±0.5592
  
  Hmm this gets complicated. Let me just use a clean approach:
"""

import json, math

# Real-world measurements
TUBE_CROSS = 0.1016  # 4 inches in meters (tube width/height)
HALF_TUBE = TUBE_CROSS / 2  # 0.0508

LONG_TUBE = 1.22    # 4 ft
SHORT_TUBE = 0.61   # 2 ft  
POST_HEIGHT = 0.508  # 20 in

# The frame rectangle:
# Long tubes run along X, short tubes run along Z
# Short tubes connect between the two long tubes
# Inner Z spacing = short tube length = 0.61m
# Long tube center Z = ± (SHORT_TUBE/2 + HALF_TUBE) = ± 0.3558
LONG_Z = SHORT_TUBE / 2 + HALF_TUBE  # 0.3558

# Short tubes at ends of long tubes
# Short tube center X = ± (LONG_TUBE/2 - HALF_TUBE) = ± 0.5592
SHORT_X = LONG_TUBE / 2 - HALF_TUBE  # 0.5592

# Base layer: tubes on ground, center at half tube height
BASE_Y = HALF_TUBE  # 0.0508

# Vertical posts sit on top of base tubes, at corners
# Post bottom = TUBE_CROSS (top of base tube)
# Post center Y = TUBE_CROSS + POST_HEIGHT/2
POST_Y = TUBE_CROSS + POST_HEIGHT / 2  # 0.3556

# Corner positions for posts
POST_X = SHORT_X   # same X as short tube centers
POST_Z = LONG_Z    # same Z as long tube centers

# Top ring: sits on top of posts
# Top of posts = TUBE_CROSS + POST_HEIGHT = 0.6096
# Top tube center Y = TUBE_CROSS + POST_HEIGHT + HALF_TUBE
TOP_Y = TUBE_CROSS + POST_HEIGHT + HALF_TUBE  # 0.6604

# Engine mount plate sits on top of top ring
# Plate center Y = TUBE_CROSS + POST_HEIGHT + TUBE_CROSS + plate_half_thickness
PLATE_Y = TUBE_CROSS + POST_HEIGHT + TUBE_CROSS + 0.003  # ~0.6126

print("=== Correct Frame Positions ===")
print(f"Tube cross-section: {TUBE_CROSS:.4f}m")
print(f"Long tube Z offset: ±{LONG_Z:.4f}m")  
print(f"Short tube X offset: ±{SHORT_X:.4f}m")
print(f"Base Y: {BASE_Y:.4f}m")
print(f"Post Y: {POST_Y:.4f}m")
print(f"Top Y: {TOP_Y:.4f}m")
print()

# Native model dims (from check_pivots.py)
native = {
    'base_tube_long':  (1.9001, 0.4647, 0.4133),
    'base_tube_short': (1.8999, 0.3442, 0.2852),
    'vertical_post':   (0.4123, 1.9020, 0.4084),
    'engine_mount_plate': (1.8999, 0.1374, 1.8987),
}

# Target real-world dims
real = {
    'base_tube_long':  (LONG_TUBE, TUBE_CROSS, TUBE_CROSS),  # along X
    'base_tube_short': (SHORT_TUBE, TUBE_CROSS, TUBE_CROSS), # along Z (needs rotation)
    'vertical_post':   (TUBE_CROSS, POST_HEIGHT, TUBE_CROSS), # along Y
    'engine_mount_plate': (0.30, 0.006, 0.30),
}

# Compute scales
# base_tube_long: model X is long axis, same as world → no rotation needed
# base_tube_short: model X is long axis, need it along world Z → 90° Y rotation
# After 90° Y rot: localScale (sx,sy,sz) → world size = (sz*nz, sy*ny, sx*nx)
#   So: sz*nz = TUBE_CROSS, sy*ny = TUBE_CROSS, sx*nx = SHORT_TUBE
# vertical_post: model Y is long axis, same as world → no rotation needed
# engine_mount_plate: already flat → no rotation needed

sin45 = math.sin(math.radians(45))
cos45 = math.cos(math.radians(45))
ROT_90Y = (0.0, sin45, 0.0, cos45)
ROT_IDENTITY = (0.0, 0.0, 0.0, 1.0)

placements = {}

# base_tube_long_1 (front)
n = native['base_tube_long']
r = real['base_tube_long']
scale = (r[0]/n[0], r[1]/n[1], r[2]/n[2])
placements['base_tube_long_1'] = {
    'pos': (0.0, BASE_Y, -LONG_Z),
    'rot': ROT_IDENTITY,
    'scale': scale,
}
# base_tube_long_2 (back)
placements['base_tube_long_2'] = {
    'pos': (0.0, BASE_Y, LONG_Z),
    'rot': ROT_IDENTITY,
    'scale': scale,
}

# base_tube_short_1 (left end)
n = native['base_tube_short']
r = real['base_tube_short']
# After 90° Y: world = (sz*nz, sy*ny, sx*nx)
# Want world = (TUBE_CROSS, TUBE_CROSS, SHORT_TUBE)
# So: sz = TUBE_CROSS/nz, sy = TUBE_CROSS/ny, sx = SHORT_TUBE/nx
scale_short = (r[0]/n[0], r[1]/n[1], r[2]/n[2])
placements['base_tube_short_1'] = {
    'pos': (-SHORT_X, BASE_Y, 0.0),
    'rot': ROT_90Y,
    'scale': scale_short,
}
# base_tube_short_2 (right end)
placements['base_tube_short_2'] = {
    'pos': (SHORT_X, BASE_Y, 0.0),
    'rot': ROT_90Y,
    'scale': scale_short,
}

# Vertical posts at 4 corners
n = native['vertical_post']
r = real['vertical_post']
scale_post = (r[0]/n[0], r[1]/n[1], r[2]/n[2])
corners = [
    ('vertical_post_1', (-SHORT_X, POST_Y, -LONG_Z)),  # front-left
    ('vertical_post_2', ( SHORT_X, POST_Y, -LONG_Z)),  # front-right
    ('vertical_post_3', (-SHORT_X, POST_Y,  LONG_Z)),  # back-left
    ('vertical_post_4', ( SHORT_X, POST_Y,  LONG_Z)),  # back-right
]
for name, pos in corners:
    placements[name] = {'pos': pos, 'rot': ROT_IDENTITY, 'scale': scale_post}

# Top ring (mirrors base, at top Y)
placements['top_tube_long_1'] = {
    'pos': (0.0, TOP_Y, -LONG_Z),
    'rot': ROT_IDENTITY,
    'scale': (real['base_tube_long'][0]/native['base_tube_long'][0],
              real['base_tube_long'][1]/native['base_tube_long'][1],
              real['base_tube_long'][2]/native['base_tube_long'][2]),
}
placements['top_tube_long_2'] = {
    'pos': (0.0, TOP_Y, LONG_Z),
    'rot': ROT_IDENTITY,
    'scale': placements['top_tube_long_1']['scale'],
}
placements['top_tube_short_1'] = {
    'pos': (-SHORT_X, TOP_Y, 0.0),
    'rot': ROT_90Y,
    'scale': scale_short,
}
placements['top_tube_short_2'] = {
    'pos': (SHORT_X, TOP_Y, 0.0),
    'rot': ROT_90Y,
    'scale': scale_short,
}

# Engine mount plate (on top of top ring)
n = native['engine_mount_plate']
r = real['engine_mount_plate']
scale_plate = (r[0]/n[0], r[1]/n[1], r[2]/n[2])
placements['engine_mount_plate'] = {
    'pos': (0.0, PLATE_Y, 0.0),
    'rot': ROT_IDENTITY,
    'scale': scale_plate,
}

# Print all positions
print("=== Computed Play Positions ===")
for name, pl in placements.items():
    p = pl['pos']
    r = pl['rot']
    s = pl['scale']
    rot_label = "90Y" if r == ROT_90Y else "id"
    print(f"  {name}: pos=({p[0]:.4f},{p[1]:.4f},{p[2]:.4f}) rot={rot_label} scale=({s[0]:.4f},{s[1]:.4f},{s[2]:.4f})")

# Now update machine.json
json_path = r'Assets\_Project\Data\Packages\power_cube_frame\machine.json'
with open(json_path, 'r') as f:
    data = json.load(f)

count = 0
for pp in data.get('previewConfig', {}).get('partPlacements', []):
    pid = pp.get('partId', '')
    if pid in placements:
        pl = placements[pid]
        p, r, s = pl['pos'], pl['rot'], pl['scale']
        
        # Update play position
        pp['assembledPosition'] = {'x': p[0], 'y': p[1], 'z': p[2]}
        pp['assembledRotation'] = {'x': r[0], 'y': r[1], 'z': r[2], 'w': r[3]}
        pp['assembledScale'] = {'x': s[0], 'y': s[1], 'z': s[2]}
        
        # Update start scale and rotation to match play (same model, same scale)
        pp['startScale'] = {'x': s[0], 'y': s[1], 'z': s[2]}
        pp['startRotation'] = {'x': r[0], 'y': r[1], 'z': r[2], 'w': r[3]}
        
        count += 1
        print(f"  Updated {pid}")

with open(json_path, 'w') as f:
    json.dump(data, f, indent=2)

print(f"\nTotal updated: {count} placements")
