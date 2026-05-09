# Normalized Step Audit — d3d_v18_10

Total steps: 321

## Summary

- Steps analyzed: 321
- Steps with lint flags: 133
- TaskOrder entries normalizer would add: 169

### TaskOrder count: authored vs normalized (by family)

| Family | Authored | After normalize | Δ |
|---|---:|---:|---:|
| Confirm | 36 | 92 | +56 |
| Connect | 0 | 0 | +0 |
| Place | 241 | 241 | +0 |
| Use | 40 | 153 | +113 |

## Flagged steps (133)


### seq 2: `step_bottom_verify_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the bottom frame side is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_bottom_square_check' not in taskOrder; normalizer adds

### seq 3: `step_bottom_clamp_panel`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Clamp the bottom frame side
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_bottom_clamp_panel' not in taskOrder; normalizer adds

### seq 6: `step_top_verify_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the top frame side is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_top_square_check' not in taskOrder; normalizer adds

### seq 7: `step_top_clamp_panel`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Clamp the top frame side
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_top_clamp_panel' not in taskOrder; normalizer adds

### seq 8: `step_top_tack_weld_corners`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld the top frame side corners
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_top_weld_upper_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_top_weld_lower_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_top_weld_upper_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_top_weld_lower_left' not in taskOrder; normalizer adds

### seq 10: `step_left_verify_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the left frame side is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_left_square_check' not in taskOrder; normalizer adds

### seq 11: `step_left_clamp_panel`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Clamp the left frame side
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_left_clamp_panel' not in taskOrder; normalizer adds

### seq 12: `step_left_tack_weld_corners`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld the left frame side corners
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_left_weld_upper_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_left_weld_lower_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_left_weld_upper_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_left_weld_lower_left' not in taskOrder; normalizer adds

### seq 14: `step_right_verify_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the right frame side is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_right_square_check' not in taskOrder; normalizer adds

### seq 15: `step_right_clamp_panel`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Clamp the right frame side
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_right_clamp_panel' not in taskOrder; normalizer adds

### seq 16: `step_right_tack_weld_corners`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld the right frame side corners
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_right_weld_upper_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_right_weld_lower_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_right_weld_upper_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_right_weld_lower_left' not in taskOrder; normalizer adds

### seq 18: `step_front_verify_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the front frame side is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_front_square_check' not in taskOrder; normalizer adds

### seq 19: `step_front_clamp_panel`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Clamp the front frame side
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_front_clamp_panel' not in taskOrder; normalizer adds

### seq 20: `step_front_tack_weld_corners`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld the front frame side corners
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_front_weld_upper_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_front_weld_lower_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_front_weld_upper_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_front_weld_lower_left' not in taskOrder; normalizer adds

### seq 22: `step_rear_verify_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the rear frame side is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_rear_square_check' not in taskOrder; normalizer adds

### seq 23: `step_rear_clamp_panel`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Clamp the rear frame side
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_rear_clamp_panel' not in taskOrder; normalizer adds

### seq 24: `step_rear_tack_weld_corners`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld the rear frame side corners
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_rear_weld_upper_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_rear_weld_lower_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_rear_weld_upper_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_rear_weld_lower_left' not in taskOrder; normalizer adds

### seq 27: `step_tack_left_to_bottom`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld left panel to bottom
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_tack_left_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_left_rear_left' not in taskOrder; normalizer adds

### seq 29: `step_tack_right_to_bottom`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld right panel to bottom
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_tack_right_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_right_rear_right' not in taskOrder; normalizer adds

### seq 31: `step_tack_front_to_sides`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld front panel to side panels
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_tack_front_left_edge' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_front_right_edge' not in taskOrder; normalizer adds

### seq 33: `step_tack_rear_to_sides`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld rear panel to side panels
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_tack_rear_left_edge' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_rear_right_edge' not in taskOrder; normalizer adds

### seq 35: `step_tack_top_to_verticals`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Tack-weld top panel to vertical panels
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_tack_top_upper_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_top_upper_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_top_upper_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tack_top_upper_rear_left' not in taskOrder; normalizer adds

### seq 36: `step_verify_stacked_cube_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify the tacked cube is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_cube_square_check_front_left' not in taskOrder; normalizer adds

### seq 37: `step_seam_weld_lower_cube_edges`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Seam-weld the 4 lower cube edges
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_cube_seam_lower_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_seam_lower_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_seam_lower_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_seam_lower_rear_left' not in taskOrder; normalizer adds

### seq 38: `step_seam_weld_upper_cube_edges`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Weld`
- Name: Seam-weld the 4 upper cube edges
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_cube_seam_upper_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_seam_upper_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_seam_upper_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_seam_upper_rear_left' not in taskOrder; normalizer adds

### seq 39: `step_grind_upper_cube_joints`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Cut`
- Name: Grind the 4 upper cube seam joints
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_cube_cleanup_upper_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_cleanup_upper_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_cleanup_upper_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_cleanup_upper_rear_left' not in taskOrder; normalizer adds

### seq 40: `step_verify_cube_post_weld`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify cube is square after welding
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_cube_square_post_weld_fl' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_cube_square_post_weld_rr' not in taskOrder; normalizer adds

### seq 42: `step_snug_lower_bracket_bolts`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Snug lower bracket bolts with allen key
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_snug_lower_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_snug_lower_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_snug_lower_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_snug_lower_rear_left' not in taskOrder; normalizer adds

### seq 44: `step_snug_upper_bracket_bolts`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Snug upper bracket bolts with allen key
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 4 entries
  - ⚠ requiredToolAction 'action_snug_upper_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_snug_upper_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_snug_upper_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_snug_upper_rear_left' not in taskOrder; normalizer adds

### seq 45: `step_verify_bracketed_cube_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify bracketed cube is square
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_bracket_square_check' not in taskOrder; normalizer adds

### seq 46: `step_final_tighten_all_bracket_bolts`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `Torque`
- Name: Final-tighten all bracket bolts
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 8 entries
  - ⚠ requiredToolAction 'action_tighten_lower_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_lower_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_lower_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_lower_rear_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_upper_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_upper_rear_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_upper_front_right' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_tighten_upper_rear_left' not in taskOrder; normalizer adds

### seq 47: `step_verify_tightened_cube_square`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Verify cube after final tightening
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_post_tighten_square_check' not in taskOrder; normalizer adds

### seq 48: `step_recheck_cube_corners`
- File: `assembly_d3d_frame.json`
- Family: `Use` | Profile: `SquareCheck`
- Name: Recheck opposite cube corners
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_recheck_front_left' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_recheck_rear_right' not in taskOrder; normalizer adds

### seq 49: `step_accept_frame_for_motion_hardware`
- File: `assembly_d3d_frame.json`
- Family: `Confirm` | Profile: `-`
- Name: Accept the frame for motion hardware
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 52: `step_batch_carriage_qc_plastic`
- File: `assembly_d3d_batch_carriage_build.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: verify all carriage holes are clean
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 82: `step_verify_y_left_axis_motion`
- File: `assembly_d3d_axes_mount.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm the Y-left axis seats cleanly on the frame
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 84: `step_verify_y_axis_pair_alignment`
- File: `assembly_d3d_axes_mount.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm the Y-axis pair establishes the X-axis span
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 85: `step_tighten_x_axis_idler_screws`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten the X-axis idler screws
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 3 entries
  - ⚠ requiredToolAction 'action_x_axis_idler_screw_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_idler_screw_2' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_idler_screw_3' not in taskOrder; normalizer adds

### seq 87: `step_snug_x_axis_idler_mount_bolts`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Snug the Y-right M6x30 anchor bolts
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_x_axis_idler_mount_y_right_a' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_idler_mount_y_right_b' not in taskOrder; normalizer adds

### seq 90: `step_lock_x_axis_motor_holder_screws`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Lock the Y-left mount bolts and motor-holder screws
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 9 entries
  - ⚠ requiredToolAction 'action_x_axis_motor_mount_y_left_a' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_mount_y_left_b' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m6x18_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m6x18_2' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m6x18_3' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m3_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m3_2' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m3_3' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_x_axis_motor_holder_m3_4' not in taskOrder; normalizer adds

### seq 91: `step_tension_x_axis_belt`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Tension the X-axis belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_x_axis_belt_tension' not in taskOrder; normalizer adds

### seq 93: `step_check_x_axis_tightness_and_travel`
- File: `assembly_d3d_axes_mount.json`
- Family: `Confirm` | Profile: `-`
- Name: Check X-axis tightness and travel
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 97: `step_secure_extruder_blower_attachment`
- File: `assembly_d3d_extruder_stage_01.json`
- Family: `Use` | Profile: `Torque`
- Name: Secure the blower attachment
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_extruder_blower_mount_a' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_extruder_blower_mount_b' not in taskOrder; normalizer adds

### seq 99: `step_secure_extruder_sensor_holder`
- File: `assembly_d3d_extruder_stage_01.json`
- Family: `Use` | Profile: `Torque`
- Name: Secure the sensor holder
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_extruder_sensor_holder_fastener_a' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_extruder_sensor_holder_fastener_b' not in taskOrder; normalizer adds

### seq 101: `step_check_extruder_nozzle_module_clearance`
- File: `assembly_d3d_extruder_stage_01.json`
- Family: `Confirm` | Profile: `-`
- Name: Check nozzle-module clearance
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 104: `step_secure_titan_aero_mount_bracket`
- File: `assembly_d3d_extruder_stage_02.json`
- Family: `Use` | Profile: `Torque`
- Name: Secure the mount bracket
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_extruder_mount_bracket_fastener_a' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_extruder_mount_bracket_fastener_b' not in taskOrder; normalizer adds

### seq 106: `step_secure_titan_aero_mount_top_plate`
- File: `assembly_d3d_extruder_stage_02.json`
- Family: `Use` | Profile: `Torque`
- Name: Secure the mount top plate
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_extruder_mount_top_plate_fastener_a' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_extruder_mount_top_plate_fastener_b' not in taskOrder; normalizer adds

### seq 108: `step_check_extruder_carriage_mount_clearance`
- File: `assembly_d3d_extruder_stage_02.json`
- Family: `Confirm` | Profile: `-`
- Name: Check carriage-mount clearance and access
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 112: `step_check_extruder_x_axis_travel_clearance`
- File: `assembly_d3d_extruder_stage_03.json`
- Family: `Confirm` | Profile: `-`
- Name: Check extruder X-axis travel clearance
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 116: `step_check_heatbed_envelope_clearance`
- File: `assembly_d3d_heatbed_stage_01.json`
- Family: `Confirm` | Profile: `-`
- Name: Check heated-bed envelope clearance
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 117: `step_assembly_complete`
- File: `assembly_d3d_heatbed_stage_01.json`
- Family: `Confirm` | Profile: `-`
- Name: Assembly complete
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 121: `step_y_left_idler_tighten_inner`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten M6x18 inner bolt against bearings
- Authored taskOrder: 1 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 125: `step_y_left_motor_pulley_pop`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock pulley set screw until it pops
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 129: `step_y_left_motor_belt_test`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt runs smoothly on pulley
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 132: `step_y_left_motor_m6_bolts_tighten`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten motor holder M6x18 bolts with drill
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 133: `step_y_left_motor_dangle_test`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Dangle test: motor holder hangs securely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 135: `step_y_left_idler_tighten_rods`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Use` | Profile: `-`
- Name: Tighten short idler bolts onto rods
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 138: `step_y_left_rods_qc`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: rods flush, carriage slides freely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 142: `step_y_left_belt_peg_orient`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt peg orientation before locking
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 143: `step_y_left_belt_first_peg`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock first belt peg
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 145: `step_y_left_belt_travel_test`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Travel test: carriage moves smoothly with belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 146: `step_y_left_label_axis`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Label completed Y-Left Axis and set aside
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 149: `step_y_left_endstop_glue_board`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Super glue endstop into holder
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 150: `step_y_left_endstop_prep_motor`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Prep motor holder holes for endstop
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 152: `step_y_left_endstop_reinforce`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Reinforce endstop joint with super glue
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 154: `step_y_right_idler_tighten_rods`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Use` | Profile: `-`
- Name: Tighten short idler bolts onto rods
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 157: `step_y_right_rods_qc`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: rods flush, carriage slides freely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 158: `step_x_axis_prep_layout`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Lay out axis pieces
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 159: `step_x_axis_prep_clean_holes`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Clean excess plastic from holes
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 160: `step_x_axis_prep_qc_plastic`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: verify all plastic pieces are clean
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 162: `step_x_axis_carriage_shake_test`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Shake test: bearings must not rattle
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 163: `step_x_axis_carriage_rod_slide_test`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Rod slide test: slight resistance, not too free
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 164: `step_x_axis_carriage_bolt`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Bolt carriage halves together
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 167: `step_x_axis_idler_align_halves`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Align idler rod holes before closing
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 168: `step_x_axis_idler_bolt`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Bolt idler halves and secure bearing
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 170: `step_x_axis_motor_pulley_pop`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock pulley set screw until it pops
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 174: `step_x_axis_motor_belt_test`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt runs smoothly on pulley
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 175: `step_x_axis_motor_screws`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Attach motor with 4 M3x25 screws
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 176: `step_x_axis_motor_m6_bolts`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Insert M6x18 bolts into motor holder
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 177: `step_x_axis_motor_dangle_test`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Dangle test: motor holder hangs securely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 179: `step_x_axis_idler_tighten_rods`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Tighten short idler bolts onto rods
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 182: `step_x_axis_rods_qc`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: rods flush, carriage slides freely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 187: `step_x_axis_belt_first_peg`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock first belt peg
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 189: `step_x_axis_belt_travel_test`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Travel test: carriage moves smoothly with belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 190: `step_x_axis_label_axis`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Label completed X Axis and set aside
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 193: `step_x_axis_endstop_glue_board`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Super glue endstop into holder
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 194: `step_x_axis_endstop_prep_motor`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Prep motor holder holes for endstop
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 196: `step_x_axis_endstop_reinforce`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Reinforce endstop joint with super glue
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 198: `step_z_front_idler_tighten_rods`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Use` | Profile: `-`
- Name: Tighten short idler bolts onto rods
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 201: `step_z_front_rods_qc`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: rods flush, carriage slides freely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 205: `step_z_back_idler_tighten_inner`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten M6x18 inner bolt against bearings
- Authored taskOrder: 1 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 209: `step_z_back_idler_tighten_rods`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Use` | Profile: `-`
- Name: Tighten short idler bolts onto rods
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 212: `step_z_back_rods_qc`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: QC: rods flush, carriage slides freely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 214: `step_qc_y_left_axis_bench`
- File: `assembly_d3d_y_left_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Y-left axis bench QC and label
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 219: `step_y_right_idler_tighten_inner`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten M6x18 inner bolt against bearings
- Authored taskOrder: 1 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 223: `step_y_right_motor_pulley_pop`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock pulley set screw until it pops
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 227: `step_y_right_motor_belt_test`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt runs smoothly on pulley
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 230: `step_y_right_motor_m6_bolts_tighten`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten motor holder M6x18 bolts with drill
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 231: `step_y_right_motor_dangle_test`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Dangle test: motor holder hangs securely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 235: `step_y_right_belt_peg_orient`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt peg orientation before locking
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 236: `step_y_right_belt_first_peg`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock first belt peg
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 238: `step_y_right_belt_travel_test`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Travel test: carriage moves smoothly with belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 239: `step_y_right_label_axis`
- File: `assembly_d3d_y_right_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Label completed Y-Right Axis and set aside
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 241: `step_qc_x_axis_bench`
- File: `assembly_d3d_x_axis_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: X-axis bench QC, dry-fit to Y axes, and label
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 246: `step_z_front_idler_tighten_inner`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten M6x18 inner bolt against bearings
- Authored taskOrder: 1 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 250: `step_z_front_motor_pulley_pop`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock pulley set screw until it pops
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 254: `step_z_front_motor_belt_test`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt runs smoothly on pulley
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 257: `step_z_front_motor_m6_bolts_tighten`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten motor holder M6x18 bolts with drill
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 258: `step_z_front_motor_dangle_test`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Dangle test: motor holder hangs securely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 262: `step_z_front_belt_peg_orient`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt peg orientation before locking
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 263: `step_z_front_belt_first_peg`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock first belt peg
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 265: `step_z_front_belt_travel_test`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Travel test: carriage moves smoothly with belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 266: `step_z_front_label_axis`
- File: `assembly_d3d_z_front_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Label completed Z-Front Axis and set aside
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 269: `step_z_back_motor_pulley_pop`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock pulley set screw until it pops
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 273: `step_z_back_motor_belt_test`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt runs smoothly on pulley
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 276: `step_z_back_motor_m6_bolts_tighten`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten motor holder M6x18 bolts with drill
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with relevantToolIds=['tool_power_drill'] but no toolAction taskOrder; may stall unless requiredToolActions also set

### seq 277: `step_z_back_motor_dangle_test`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Dangle test: motor holder hangs securely
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 281: `step_z_back_belt_peg_orient`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Confirm belt peg orientation before locking
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 282: `step_z_back_belt_first_peg`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Use` | Profile: `-`
- Name: Lock first belt peg
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

### seq 284: `step_z_back_belt_travel_test`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Travel test: carriage moves smoothly with belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 285: `step_z_back_label_axis`
- File: `assembly_d3d_z_back_bench.json`
- Family: `Confirm` | Profile: `-`
- Name: Label completed Z-Back Axis and set aside
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 286: `step_tighten_z_front_idler_screws`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten the Z-front idler screws
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_z_front_idler_screw_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_z_front_idler_screw_2' not in taskOrder; normalizer adds

### seq 288: `step_lock_z_front_motor_holder`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Lock the Z-front motor holder bolts
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_z_front_motor_holder_bolt_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_z_front_motor_holder_bolt_2' not in taskOrder; normalizer adds

### seq 289: `step_tension_z_front_belt`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Tension the Z-front axis belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_z_front_belt_tension' not in taskOrder; normalizer adds

### seq 291: `step_check_z_front_axis_travel`
- File: `assembly_d3d_axes_mount.json`
- Family: `Confirm` | Profile: `-`
- Name: Check Z-front axis travel and QC
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 292: `step_tighten_z_back_idler_screws`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Tighten the Z-back idler screws
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_z_back_idler_screw_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_z_back_idler_screw_2' not in taskOrder; normalizer adds

### seq 294: `step_lock_z_back_motor_holder`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Lock the Z-back motor holder bolts
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 2 entries
  - ⚠ requiredToolAction 'action_z_back_motor_holder_bolt_1' not in taskOrder; normalizer adds
  - ⚠ requiredToolAction 'action_z_back_motor_holder_bolt_2' not in taskOrder; normalizer adds

### seq 295: `step_tension_z_back_belt`
- File: `assembly_d3d_axes_mount.json`
- Family: `Use` | Profile: `Torque`
- Name: Tension the Z-back axis belt
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_z_back_belt_tension' not in taskOrder; normalizer adds

### seq 297: `step_check_z_back_axis_travel`
- File: `assembly_d3d_axes_mount.json`
- Family: `Confirm` | Profile: `-`
- Name: Check Z-back axis travel and QC
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 300: `step_check_bed_platform_alignment`
- File: `assembly_d3d_heatbed_stage_02.json`
- Family: `Confirm` | Profile: `-`
- Name: Check bed platform alignment and rod seating
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 305: `step_secure_control_panel`
- File: `assembly_d3d_electronics.json`
- Family: `Use` | Profile: `-`
- Name: Secure the control panel with M3 screws
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ requiredToolAction 'action_secure_control_panel' not in taskOrder; normalizer adds

### seq 309: `step_verify_psu_connections`
- File: `assembly_d3d_electronics.json`
- Family: `Confirm` | Profile: `-`
- Name: Verify all PSU connections to RAMPS
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 1 entries
  - ⚠ Confirm step had no taskOrder; normalizer adds confirm_action

### seq 321: `step_route_cables_chain`
- File: `assembly_d3d_electronics.json`
- Family: `Use` | Profile: `-`
- Name: Route all axis cables through the cable chain
- Authored taskOrder: 0 entries
- Predicted (post-normalize): 0 entries
  - ⚠ Use family with empty taskOrder AND no relevantToolIds — possible cursor stall

## Per-assembly summary

| Assembly | Steps | Flagged |
|---|---:|---:|
| `assembly_d3d_axes_mount.json` | 25 | 15 |
| `assembly_d3d_batch_carriage_build.json` | 31 | 1 |
| `assembly_d3d_cable_stage_01.json` | 1 | 0 |
| `assembly_d3d_electronics.json` | 19 | 3 |
| `assembly_d3d_extruder_stage_01.json` | 8 | 3 |
| `assembly_d3d_extruder_stage_02.json` | 7 | 3 |
| `assembly_d3d_extruder_stage_03.json` | 4 | 1 |
| `assembly_d3d_frame.json` | 49 | 34 |
| `assembly_d3d_heatbed_stage_01.json` | 5 | 2 |
| `assembly_d3d_heatbed_stage_02.json` | 4 | 1 |
| `assembly_d3d_x_axis_bench.json` | 41 | 22 |
| `assembly_d3d_y_left_bench.json` | 37 | 15 |
| `assembly_d3d_y_right_bench.json` | 30 | 11 |
| `assembly_d3d_z_back_bench.json` | 30 | 11 |
| `assembly_d3d_z_front_bench.json` | 30 | 11 |