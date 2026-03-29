using System.Collections.Generic;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Real-world dimensions (in meters) for machine package parts.
    /// Used by <see cref="PackageModelNormalizer"/> to compute correct scale factors
    /// for AI-generated 3D models.
    ///
    /// Dimensions represent the part's approximate bounding box (width x height x depth)
    /// based on OSE Power Cube BOM specifications.
    ///
    /// To add a new part: add an entry to the dictionary below.
    /// To update dimensions: modify the Vector3 values (all in meters).
    /// </summary>
    internal static class PartDimensionCatalog
    {
        // ── Power Cube Frame Parts ──────────────────────────────────────
        // Source: OSE Power Cube Workshop Manual
        // All dimensions in meters (W x H x D bounding box)

        private static readonly Dictionary<string, Vector3> Dimensions =
            new Dictionary<string, Vector3>(System.StringComparer.OrdinalIgnoreCase)
        {
            // ─── Frame / Structure ───
            { "base_tube_long_1",     new Vector3(1.22f,  0.05f, 0.05f) },  // 4-ft steel tube (1.22m x 2" x 2")
            { "base_tube_long_2",     new Vector3(1.22f,  0.05f, 0.05f) },
            { "base_tube_short_1",    new Vector3(0.05f,  0.05f, 0.61f) },  // 2-ft steel tube
            { "base_tube_short_2",    new Vector3(0.05f,  0.05f, 0.61f) },
            { "vertical_post",        new Vector3(0.05f,  0.61f, 0.05f) },  // 2-ft vertical post
            { "engine_mount_plate",   new Vector3(0.30f,  0.006f, 0.30f) }, // 12" x 12" steel plate, 1/4" thick
            { "engine",               new Vector3(0.45f,  0.35f, 0.40f) },  // 16-HP Lifan engine

            // ─── Hydraulic System ───
            { "hydraulic_pump",       new Vector3(0.20f,  0.15f, 0.15f) },  // 11 GPM gear pump
            { "pump_coupling",        new Vector3(0.10f,  0.08f, 0.08f) },  // Lovejoy jaw coupling
            { "reservoir",            new Vector3(0.30f,  0.25f, 0.20f) },  // 5-gallon hydraulic reservoir
            { "pressure_hose",        new Vector3(0.03f,  0.03f, 0.90f) },  // SAE 100R2 hose, ~3ft
            { "return_hose",          new Vector3(0.03f,  0.03f, 0.90f) },  // SAE 100R1 return line
            { "oil_cooler",           new Vector3(0.25f,  0.20f, 0.05f) },  // Aluminum oil cooler
            { "pressure_gauge",       new Vector3(0.06f,  0.06f, 0.04f) },  // 2.5" dial gauge

            // ─── Fuel System ───
            { "fuel_tank",            new Vector3(0.30f,  0.25f, 0.20f) },  // 7-gallon steel tank
            { "fuel_line",            new Vector3(0.01f,  0.01f, 0.60f) },  // 3/8" rubber hose
            { "fuel_shutoff_valve",   new Vector3(0.04f,  0.03f, 0.03f) },  // 1/4-turn brass valve

            // ─── Electrical System ───
            { "battery",              new Vector3(0.21f,  0.18f, 0.17f) },  // Group 26 lead-acid battery
            { "battery_cables",       new Vector3(0.01f,  0.01f, 0.45f) },  // 2-gauge copper cables
            { "key_switch",           new Vector3(0.04f,  0.05f, 0.04f) },  // 4-position ignition switch
            { "starter_wiring",       new Vector3(0.005f, 0.005f, 0.50f) }, // 14-gauge wiring harness
            { "choke_cable",          new Vector3(0.005f, 0.005f, 0.40f) }, // Bowden cable
            { "throttle_cable",       new Vector3(0.005f, 0.005f, 0.40f) }, // Bowden cable

            // ─── Tools ───
            { "tool_tape_measure",    new Vector3(0.08f,  0.08f, 0.04f) },
            { "tool_framing_square",  new Vector3(0.40f,  0.30f, 0.005f) },
            { "tool_clamp",           new Vector3(0.25f,  0.10f, 0.05f) },
            { "tool_welder",          new Vector3(0.40f,  0.30f, 0.25f) },
            { "tool_angle_grinder",   new Vector3(0.30f,  0.10f, 0.10f) },
            { "tool_torque_wrench",   new Vector3(0.45f,  0.05f, 0.05f) },
            { "tool_socket_set",      new Vector3(0.30f,  0.08f, 0.15f) },
            { "tool_line_wrench",     new Vector3(0.20f,  0.03f, 0.02f) },
            { "tool_allen_key_metric", new Vector3(0.005f, 0.08789f, 0.03589f) },
            { "tool_hex_driver_metric_small", new Vector3(0.0025f, 0.09244f, 0.02244f) },
            { "tool_wire_crimper",    new Vector3(0.22f,  0.06f, 0.02f) },
            { "tool_multimeter",      new Vector3(0.08f,  0.15f, 0.04f) },

            // --- D3D v18.10 starter slice ---
            // Source: OSE Frame Construction Set / D3D frame notes
            // 1/8" x 1" mild steel flat bar, cut to 13" lengths.
            // Four bars overlap at the corners to form one 14" square frame side.
            { "d3d_frame_bottom_top_bar",    new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_bottom_right_bar",  new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_bottom_bottom_bar", new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_bottom_left_bar",   new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_top_top_bar",       new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_top_right_bar",     new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_top_bottom_bar",    new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_top_left_bar",      new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_left_top_bar",      new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_left_right_bar",    new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_left_bottom_bar",   new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_left_left_bar",     new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_right_top_bar",     new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_right_right_bar",   new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_right_bottom_bar",  new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_right_left_bar",    new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_front_top_bar",     new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_front_right_bar",   new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_front_bottom_bar",  new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_front_left_bar",    new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_rear_top_bar",      new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_rear_right_bar",    new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_rear_bottom_bar",   new Vector3(0.3302f, 0.0254f, 0.003175f) },
            { "d3d_frame_rear_left_bar",     new Vector3(0.3302f, 0.0254f, 0.003175f) },

            // --- D3D v18.10 axes hardening batch 01 ---
            // Source: OSE Universal Axis CAD exports captured in
            // source_cad/axes_stage01/exported/reports/*.json
            { "d3d_y_left_axis_unit",         new Vector3(0.0195f, 0.2400f, 0.08142f) },
            { "d3d_y_right_axis_unit",        new Vector3(0.0120f, 0.2400f, 0.06383f) },
            { "d3d_x_axis_motor_holder_unit", new Vector3(0.0595f, 0.0660f, 0.0120f) },
            { "d3d_x_axis_idler_unit",        new Vector3(0.0260f, 0.0660f, 0.0120f) },
            { "d3d_x_axis_rod_pair",          new Vector3(0.2032f, 0.009823826f, 0.065658255f) },
            { "d3d_extruder_simplified_carriage", new Vector3(0.0520f, 0.0740f, 0.0120f) },
            { "d3d_x_axis_carriage_side",     new Vector3(0.0520f, 0.0740f, 0.0120f) },
            { "d3d_x_axis_half_carriage",     new Vector3(0.04612f, 0.07359f, 0.02364f) },
            { "d3d_heatbed_body",             new Vector3(0.20320f, 0.04134f, 0.20320f) },
            { "d3d_heatbed_snapbuckle_left",  new Vector3(0.01204f, 0.03390f, 0.01000f) },
            { "d3d_heatbed_snapbuckle_right", new Vector3(0.01204f, 0.03390f, 0.01000f) },
            { "fastener_x_axis_belt_peg",     new Vector3(0.0240f, 0.01147f, 0.0100f) },
            { "fastener_x_axis_idler_mount_m6x30_a", new Vector3(0.0360f, 0.0100f, 0.0100f) },
            { "fastener_x_axis_idler_mount_m6x30_b", new Vector3(0.0360f, 0.0100f, 0.0100f) },
            { "fastener_x_axis_motor_mount_m6x30_a", new Vector3(0.0360f, 0.0100f, 0.0100f) },
            { "fastener_x_axis_motor_mount_m6x30_b", new Vector3(0.0360f, 0.0100f, 0.0100f) },
        };

        /// <summary>
        /// Returns the real-world bounding box dimensions (meters) for a part id.
        /// Returns Vector3.zero if the part is not in the catalog.
        /// </summary>
        internal static Vector3 GetDimensions(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return Vector3.zero;
            return Dimensions.TryGetValue(partId, out var dims) ? dims : Vector3.zero;
        }

        /// <summary>
        /// Returns true if the catalog has dimensions for the given part.
        /// </summary>
        internal static bool HasDimensions(string partId)
        {
            return !string.IsNullOrEmpty(partId) && Dimensions.ContainsKey(partId);
        }
    }
}
