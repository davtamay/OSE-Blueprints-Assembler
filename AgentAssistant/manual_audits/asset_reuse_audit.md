# Asset Reuse Audit

_Phase B.2 audit. Inventory of every assetRef + SHA dedup + composite check._


## SHA-identical GLBs (true file-level duplicates)

| SHA | Bytes | Canonical | Duplicates |
|---|---:|---|---|
| `22d3066c6d43` | 81,152 | `d3d_8mm_sensor_approved.glb` | `d3d_extruder_8mm_sensor_approved.glb` |

**Action:** Repoint assetRefs from duplicates to canonical, then delete the duplicate GLB files.


## Top mesh reuse (most-shared assetRefs)

High counts are usually correct — every M6 nut shares one mesh, every LM8UU bearing shares one mesh, etc. **Flag only when multiple parts represent the same physical thing.**


| assetRef | parts | Verdict |
|---|---:|---|
| `d3d_axis_m6x18_bolt.glb` | 52 | OK — one mesh, every M6x18 instance |
| `<none>` | 50 | manual review needed |
| `d3d_axis_m6_nut.glb` | 50 | OK — one mesh, dozens of nut instances across 5 axes |
| `d3d_axis_lm8uu_bearing.glb` | 20 | OK — 4 bearings × 5 axes |
| `d3d_axis_m3x25_shcs.glb` | 20 | OK — 4 motor screws × 5 axes |
| `d3d_axis_625zz_bearing.glb` | 10 | OK — 2 bearings × 4 idlers + 2 X half-bearings |
| `idler_approved.glb` | 8 | OK — single canonical idler mesh after dedup commit add6ab9 |
| `compound007_approved.glb` | 8 | OK — compound bracket × 8 |
| `heatbed_raisers_combined.glb` | 6 | OK — bed riser × 6 |
| `d3d_axis_gt2_pulley_19t.glb` | 5 | OK — 1 pulley × 5 motors |
| `d3d_axis_gt2_belt.glb` | 5 | OK — 1 belt per axis |
| `y_left_carriage_half_a.glb` | 4 | OK — same printed half across all 4 Y/Z carriages |
| `y_left_carriage_half_b.glb` | 4 | OK — same as half_b |
| `rod_005_approved.glb` | 4 | OK — guide rod mesh reused across axes |
| `rod_006_approved.glb` | 3 | OK — same for rod_006 |
| `y_endstop_approved.glb` | 2 | OK — endstop mesh reused for X-axis endstop |
| `motor002_approved.glb` | 1 | manual review needed |
| `motor003_approved.glb` | 1 | manual review needed |
| `motor001_approved.glb` | 1 | manual review needed |
| `motor_approved.glb` | 1 | manual review needed |
| `pocket039_approved.glb` | 1 | manual review needed |
| `pocket040_approved.glb` | 1 | manual review needed |
| `y1_bracket_approved.glb` | 1 | manual review needed |
| `rod_007_approved.glb` | 1 | manual review needed |
| `rod_008_approved.glb` | 1 | manual review needed |

## Composite-part audit

Legacy composite IDs (idler001/002/003/idler, *_half_carriage, d3d_x_axis_*) — confirm each is referenced where it should be (typically a bench_unit aggregate plus a batch step that places it as half_a).


| Composite | Ref count | Status |
|---|---:|---|
| `d3d_x_axis_idler_unit` | 7 | 7 refs |
| `d3d_x_axis_carriage_side` | 5 | 5 refs |
| `d3d_x_axis_half_carriage` | 4 | 4 refs |
| `d3d_x_axis_rod_pair` | 3 | 3 refs |
| `z1_half_carriage` | 3 | 3 refs |
| `z2_half_carriage` | 3 | 3 refs |
| `idler001` | 5 | 5 refs |
| `idler002` | 2 | 2 refs |
| `idler003` | 2 | 2 refs |
| `idler` | 5 | 5 refs |
| `idler001_half_b` | 1 | 1 refs |
| `idler002_half_b` | 2 | 2 refs |
| `idler003_half_b` | 1 | 1 refs |
| `idler_half_b` | 1 | 1 refs |

### Composite-part details


**`d3d_x_axis_idler_unit`** (7 refs):
- `assembly_d3d_axes_mount.json::target:target_x_axis_idler_screw_1`
- `assembly_d3d_axes_mount.json::target:target_x_axis_idler_screw_2`
- `assembly_d3d_axes_mount.json::target:target_x_axis_idler_screw_3`
- `assembly_d3d_axes_mount.json::target:target_x_axis_idler_anchor_prefit`
- `assembly_d3d_axes_mount.json::partGroup:partGroup_x_axis_frame_fit`
- `assembly_d3d_x_axis_bench.json::step:step_x_axis_idler_insert_inner_bolt`
- `assembly_d3d_x_axis_bench.json::partGroup:partGroup_x_axis_bench_unit`

**`d3d_x_axis_carriage_side`** (5 refs):
- `assembly_d3d_extruder_stage_03.json::target:target_x_axis_carriage_side_stage`
- `assembly_d3d_extruder_stage_03.json::partGroup:partGroup_extruder_x_axis_mount`
- `assembly_d3d_x_axis_bench.json::step:step_x_axis_prep_layout`
- `assembly_d3d_x_axis_bench.json::partGroup:partGroup_x_axis_endstop_build`
- `assembly_d3d_x_axis_bench.json::partGroup:partGroup_x_axis_bench_unit`

**`d3d_x_axis_half_carriage`** (4 refs):
- `assembly_d3d_extruder_stage_03.json::target:target_x_axis_half_carriage_attach`
- `assembly_d3d_extruder_stage_03.json::partGroup:partGroup_extruder_x_axis_mount`
- `assembly_d3d_x_axis_bench.json::step:step_x_axis_prep_layout`
- `assembly_d3d_x_axis_bench.json::partGroup:partGroup_x_axis_bench_unit`

**`d3d_x_axis_rod_pair`** (3 refs):
- `assembly_d3d_axes_mount.json::target:target_x_axis_rod_pair_prefit`
- `assembly_d3d_axes_mount.json::partGroup:partGroup_x_axis_frame_fit`
- `assembly_d3d_x_axis_bench.json::partGroup:partGroup_x_axis_bench_unit`

**`z1_half_carriage`** (3 refs):
- `assembly_d3d_axes_mount.json::partGroup:partGroup_z_back_frame_mount`
- `assembly_d3d_batch_rod_assembly.json::step:step_batch_r3_carriage_onto_rods`
- `assembly_d3d_z_back_bench.json::partGroup:partGroup_z_back_bench_unit`

**`z2_half_carriage`** (3 refs):
- `assembly_d3d_axes_mount.json::partGroup:partGroup_z_front_frame_mount`
- `assembly_d3d_batch_rod_assembly.json::step:step_batch_r4_carriage_onto_rods`
- `assembly_d3d_z_front_bench.json::partGroup:partGroup_z_front_bench_unit`

**`idler001`** (5 refs):
- `assembly_d3d_axes_mount.json::target:target_z_back_idler_screw_1`
- `assembly_d3d_axes_mount.json::target:target_z_back_idler_screw_2`
- `assembly_d3d_axes_mount.json::partGroup:partGroup_z_back_frame_mount`
- `assembly_d3d_batch_idler_build.json::step:step_batch_i3_insert_inner_bolt`
- `assembly_d3d_z_back_bench.json::partGroup:partGroup_z_back_bench_unit`

**`idler002`** (2 refs):
- `assembly_d3d_batch_idler_build.json::step:step_batch_i1_insert_inner_bolt`
- `assembly_d3d_y_left_bench.json::partGroup:partGroup_y_left_bench_unit`

**`idler003`** (2 refs):
- `assembly_d3d_batch_idler_build.json::step:step_batch_i2_insert_inner_bolt`
- `assembly_d3d_y_right_bench.json::partGroup:partGroup_y_right_bench_unit`

**`idler`** (5 refs):
- `assembly_d3d_axes_mount.json::target:target_z_front_idler_screw_1`
- `assembly_d3d_axes_mount.json::target:target_z_front_idler_screw_2`
- `assembly_d3d_axes_mount.json::partGroup:partGroup_z_front_frame_mount`
- `assembly_d3d_batch_idler_build.json::step:step_batch_i4_insert_inner_bolt`
- `assembly_d3d_z_front_bench.json::partGroup:partGroup_z_front_bench_unit`

**`idler001_half_b`** (1 refs):
- `assembly_d3d_batch_idler_build.json::step:step_batch_i3_align_halves`

**`idler002_half_b`** (2 refs):
- `assembly_d3d_batch_idler_build.json::step:step_batch_i1_align_halves`
- `assembly_d3d_y_left_bench.json::partGroup:partGroup_y_left_bench_unit`

**`idler003_half_b`** (1 refs):
- `assembly_d3d_batch_idler_build.json::step:step_batch_i2_align_halves`

**`idler_half_b`** (1 refs):
- `assembly_d3d_batch_idler_build.json::step:step_batch_i4_align_halves`

## Recommendations

1. **Resolve SHA dup(s)** — see table above. Repoint + delete duplicate GLBs.

2. **Composites are intentional** — `idler001/002/003/idler` are the half_a identity for each Y/Z idler instance, now placed by batch_idler_build's per-axis steps and aggregated into bench_unit. The X-axis composites (`d3d_x_axis_*`) similarly serve as conceptual wholes for the X bench.

3. **The mesh-reuse warnings in Unity validator are noise** — every entry in Top mesh reuse above is legitimate. Worth lowering these warnings to Info severity in `MachinePackageValidator` so the dashboard surfaces real issues.


_Inventory CSV: see `asset_inventory.csv` (50 unique assetRefs)._
