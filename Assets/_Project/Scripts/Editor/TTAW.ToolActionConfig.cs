// TTAW.ToolActionConfig.cs — Consolidated "Tool Action Config" authoring section.
// ──────────────────────────────────────────────────────────────────────────────
// Renders four collapsible subsections for a selected tool-action task:
//   • TOOL           — orientationEuler, scaleOverride, persistent, primaryActionType
//   • TOOL POSE      — gripPoint, tipPoint, actionAxis, handedness, poseHint, cursorOffset
//   • PROFILE        — read-only snapshot of FramingDistance/WorkingDistance/ApproachTiltDegrees/PreviewStyle
//   • PREVIEW OVRRDE — ToolActionPreviewConfig fields, filtered by step.profile
//
// The TARGET subsection (position/rotation/scale/weldAxis/weldLength/rotation
// lock) is still rendered by the existing DrawDetailPanel above this block
// — no change to target authoring today. Over time the target fields can
// migrate into a "TARGET" foldout here to fully consolidate.

using System;
using OSE.Content;
using OSE.Interaction;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // Persistent foldout state per subsection.
        [SerializeField] private bool _cfgTargetFoldout      = true;
        [SerializeField] private bool _cfgToolFoldout        = false;
        [SerializeField] private bool _cfgToolPoseFoldout    = false;
        [SerializeField] private bool _cfgProfileFoldout     = false;
        [SerializeField] private bool _cfgOverridesFoldout   = true;

        private static readonly string[] _handednessOptions = { "", "right", "left", "either" };
        private static readonly string[] _poseHintOptions   = { "", "power_grip", "pinch", "precision", "two_hand" };

        /// <summary>
        /// Draws the consolidated Target / Tool / Tool Pose / Profile /
        /// Preview Overrides section for the given tool action. Call from
        /// the "TOOL CONTEXT" branch of DrawTaskInspectorBody.
        /// </summary>
        private void DrawToolActionConfigSection(StepDefinition step, ToolActionDefinition action)
        {
            if (step == null || action == null) return;

            EditorGUILayout.Space(4);
            var headerStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = new Color(0.95f, 0.65f, 0.20f) },
            };
            EditorGUILayout.LabelField("TOOL ACTION CONFIG", headerStyle);

            // Locate the tool definition for the action so we can edit tool-
            // level fields inline (matches what ToolPoseGizmoEditor exposes
            // in its own window).
            ToolDefinition toolDef = null;
            if (!string.IsNullOrEmpty(action.toolId) && _pkg?.tools != null)
            {
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == action.toolId) { toolDef = td; break; }
            }

            DrawTargetSection(action);
            DrawToolSection(toolDef);
            DrawToolPoseSection(toolDef);
            DrawProfileSection(step);
            DrawPreviewOverridesSection(step, action);
        }

        // ── TARGET (direction + distance of the tool's travel) ───────────

        private void DrawTargetSection(ToolActionDefinition action)
        {
            _cfgTargetFoldout = EditorGUILayout.Foldout(_cfgTargetFoldout, "TARGET", true, EditorStyles.foldoutHeader);
            if (!_cfgTargetFoldout) return;
            EditorGUI.indentLevel++;

            // Locate the TargetEditState for this action's target. It owns
            // the live weldAxis / weldLength values shown as "Action
            // Direction" + "Action Distance" — the vector defines where
            // the tool travels during the action and its magnitude.
            int tIdx = -1;
            if (_targets != null && !string.IsNullOrEmpty(action.targetId))
            {
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i].def != null && string.Equals(_targets[i].def.id, action.targetId, System.StringComparison.Ordinal))
                    { tIdx = i; break; }
                }
            }

            if (tIdx < 0)
            {
                EditorGUILayout.HelpBox("No target registered for this action.", MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            ref var t = ref _targets[tIdx];

            EditorGUILayout.HelpBox(
                "Action Direction + Action Distance describe where the tool travels " +
                "during the action. For a weld: the seam direction × length. For a cut: " +
                "the scorch line vector. For drill/torque/tap: leave as zero (the tool " +
                "doesn't translate — it spins or plunges in-place).",
                MessageType.None);

            // Direction — vector of the action path (world-independent, in
            // target-local frame like weldAxis).
            EditorGUI.BeginChangeCheck();
            Vector3 newAxis = EditorGUILayout.Vector3Field(
                new GUIContent("Action Direction (axis)",
                    "Unit vector defining where the tool travels during the action. " +
                    "Normalised on save. Zero = point target (no translation)."),
                t.weldAxis);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                t.weldAxis = newAxis.sqrMagnitude > 0.001f ? newAxis.normalized : newAxis;
                t.isDirty  = true;
                EndEdit();
                SceneView.RepaintAll();
            }

            // Magnitude — distance travelled along the direction.
            EditorGUI.BeginChangeCheck();
            float newLen = EditorGUILayout.FloatField(
                new GUIContent("Action Distance (m)",
                    "Magnitude of the action path in metres. For a weld, the seam length. " +
                    "For point actions (drill, torque), 0."),
                t.weldLength);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                t.weldLength = Mathf.Max(0f, newLen);
                t.isDirty    = true;
                EndEdit();
            }

            // Scene-gizmo toggle (two draggable handles in SceneView — A/B
            // drop points define direction + distance visually).
            EditorGUI.BeginChangeCheck();
            bool newGizmo = EditorGUILayout.ToggleLeft(
                new GUIContent("Use scene gizmo (drag two handles)",
                    "Drops an orange (A) and yellow (B) handle in SceneView. " +
                    "The A→B vector becomes the action direction; its length becomes the action distance."),
                t.weldGizmoActive);
            if (EditorGUI.EndChangeCheck() && newGizmo != t.weldGizmoActive)
            {
                t.weldGizmoActive = newGizmo;
                if (newGizmo) InitWeldGizmo(ref t);
                SceneView.RepaintAll();
            }

            // Tool-rotation lock — freezes the tool's orientation at this
            // target. Previously authored via useToolActionRotation +
            // toolActionRotation; renamed here for clarity.
            EditorGUI.BeginChangeCheck();
            bool newLock = EditorGUILayout.Toggle(
                new GUIContent("Lock Tool Rotation at Target",
                    "When on, the tool snaps to the authored rotation at this target instead of the profile's computed orientation."),
                t.useToolActionRotation);
            if (EditorGUI.EndChangeCheck() && newLock != t.useToolActionRotation)
            {
                BeginEdit();
                t.useToolActionRotation = newLock;
                t.isDirty = true;
                EndEdit();
            }
            if (t.useToolActionRotation)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newEuler = EditorGUILayout.Vector3Field(
                    new GUIContent("Locked Rotation (Euler °)",
                        "Euler angles the tool is rotated to at this target when Lock Tool Rotation is on."),
                    t.toolActionRotationEuler);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    t.toolActionRotationEuler = newEuler;
                    t.isDirty = true;
                    EndEdit();
                }
            }

            EditorGUI.indentLevel--;
        }

        // ── TOOL ─────────────────────────────────────────────────────────

        private void DrawToolSection(ToolDefinition tool)
        {
            _cfgToolFoldout = EditorGUILayout.Foldout(_cfgToolFoldout, "TOOL", true, EditorStyles.foldoutHeader);
            if (!_cfgToolFoldout) return;
            EditorGUI.indentLevel++;

            if (tool == null)
            {
                EditorGUILayout.HelpBox("No tool mapped to this action.", MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUI.BeginChangeCheck();

            bool useOri = EditorGUILayout.Toggle(new GUIContent("Use Orientation Override",
                "When on, the tool preview uses authored Euler angles instead of auto-detected upright."),
                tool.useOrientationOverride);
            Vector3 euler = EditorGUILayout.Vector3Field(new GUIContent("Orientation (Euler °)",
                "Pitch / yaw / roll applied when Use Orientation Override is on."),
                tool.orientationEuler);
            float scaleOv = EditorGUILayout.FloatField(new GUIContent("Scale Override (× cursor)",
                "0 or 1 = no override. Higher = larger cursor preview for this tool."),
                tool.scaleOverride);
            bool persist = EditorGUILayout.Toggle(new GUIContent("Persistent (stays after use)",
                "Clamps, fixtures, vises stay on the workpiece after action completes."),
                tool.persistent);
            string primaryAction = EditorGUILayout.TextField(new GUIContent("Primary Action Type",
                "Default actionType inherited by new tool-target tasks using this tool."),
                tool.primaryActionType ?? "");

            if (EditorGUI.EndChangeCheck())
            {
                tool.useOrientationOverride = useOri;
                tool.orientationEuler       = euler;
                tool.scaleOverride          = scaleOv;
                tool.persistent             = persist;
                tool.primaryActionType      = string.IsNullOrEmpty(primaryAction) ? null : primaryAction;
                _dirtyToolIds.Add(tool.id);
            }

            EditorGUI.indentLevel--;
        }

        // ── TOOL POSE ─────────────────────────────────────────────────────

        private void DrawToolPoseSection(ToolDefinition tool)
        {
            _cfgToolPoseFoldout = EditorGUILayout.Foldout(_cfgToolPoseFoldout, "TOOL POSE", true, EditorStyles.foldoutHeader);
            if (!_cfgToolPoseFoldout) return;
            EditorGUI.indentLevel++;

            if (tool == null)
            {
                EditorGUILayout.HelpBox("No tool mapped to this action.", MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            // Initialise ToolPoseConfig lazily so authors can start authoring
            // without having to open the separate Grab Pose Editor first.
            if (tool.toolPose == null) tool.toolPose = new ToolPoseConfig();
            var pose = tool.toolPose;

            EditorGUILayout.HelpBox(
                "Grip / tip / action-axis live here and round-trip with the Grab Pose Editor window. " +
                "Visual gizmos for these points still live in that editor.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();

            Vector3 grip = EditorGUILayout.Vector3Field("Grip Point (local)", ToVector3(pose.gripPoint));
            Vector3 gripRot = EditorGUILayout.Vector3Field("Grip Rotation (Euler °)", ToVector3(pose.gripRotation));
            Vector3 tip = EditorGUILayout.Vector3Field("Tip Point (local)", ToVector3(pose.tipPoint));
            Vector3 tipAxis = EditorGUILayout.Vector3Field("Tip Axis (local)", ToVector3(pose.tipAxis));
            Vector3 actionAxis = EditorGUILayout.Vector3Field(new GUIContent("Action Axis (local)",
                "Direction the tool travels during the action. Torque uses this as rotation axis."),
                ToVector3(pose.actionAxis));
            Vector3 cursorOff = EditorGUILayout.Vector3Field("Cursor Offset (from grip)", ToVector3(pose.cursorOffset));
            Vector3 cursorRot = EditorGUILayout.Vector3Field("Cursor Rotation (Euler °)", ToVector3(pose.cursorRotation));

            int handIdx = Mathf.Max(0, Array.IndexOf(_handednessOptions, pose.handedness ?? ""));
            int newHandIdx = EditorGUILayout.Popup("Handedness", handIdx, _handednessOptions);

            int hintIdx = Mathf.Max(0, Array.IndexOf(_poseHintOptions, pose.poseHint ?? ""));
            int newHintIdx = EditorGUILayout.Popup("Pose Hint", hintIdx, _poseHintOptions);

            if (EditorGUI.EndChangeCheck())
            {
                pose.gripPoint      = FromVector3(grip);
                pose.gripRotation   = FromVector3(gripRot);
                pose.tipPoint       = FromVector3(tip);
                pose.tipAxis        = FromVector3(tipAxis);
                pose.actionAxis     = FromVector3(actionAxis);
                pose.cursorOffset   = FromVector3(cursorOff);
                pose.cursorRotation = FromVector3(cursorRot);
                pose.handedness     = _handednessOptions[newHandIdx];
                pose.poseHint       = _poseHintOptions[newHintIdx];
                _dirtyToolIds.Add(tool.id);
            }

            EditorGUI.indentLevel--;
        }

        // ── PROFILE (read-only) ──────────────────────────────────────────

        private void DrawProfileSection(StepDefinition step)
        {
            _cfgProfileFoldout = EditorGUILayout.Foldout(_cfgProfileFoldout, "PROFILE (read-only)", true, EditorStyles.foldoutHeader);
            if (!_cfgProfileFoldout) return;
            EditorGUI.indentLevel++;

            string profile = step?.profile ?? "";
            if (string.IsNullOrEmpty(profile))
            {
                EditorGUILayout.LabelField("Step has no profile.", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                return;
            }

            var desc = ToolProfileRegistry.Get(profile);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("Profile", profile);
            EditorGUILayout.FloatField("Framing Distance (m)",  desc.FramingDistance);
            EditorGUILayout.FloatField("Working Distance (m)",  desc.WorkingDistance);
            EditorGUILayout.FloatField("Approach Tilt (°)",     desc.ApproachTiltDegrees);
            EditorGUILayout.LabelField("Preview Style",         desc.PreviewStyle.ToString());
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.LabelField("Override any of these in PREVIEW OVERRIDES below.", EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }

        // ── PREVIEW OVERRIDES ─────────────────────────────────────────────

        private void DrawPreviewOverridesSection(StepDefinition step, ToolActionDefinition action)
        {
            _cfgOverridesFoldout = EditorGUILayout.Foldout(_cfgOverridesFoldout, "PREVIEW OVERRIDES", true, EditorStyles.foldoutHeader);
            if (!_cfgOverridesFoldout) return;
            EditorGUI.indentLevel++;

            if (action.previewConfig == null) action.previewConfig = new ToolActionPreviewConfig();
            var cfg = action.previewConfig;
            string profile = step?.profile ?? "";

            EditorGUILayout.HelpBox(
                "Sentinel convention: leave a field at 0 (or alpha = 0 for colours) to use the preview's hardcoded default. " +
                "Any non-zero value overrides the default for this specific tool action.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();

            DrawUniversalOverrides(cfg);

            switch (profile)
            {
                case "Weld":
                case "solder":
                    DrawWeldOverrides(cfg); break;
                case "Cut":
                case "grind":
                    DrawCutOverrides(cfg); break;
                case "Drill":
                    DrawDrillOverrides(cfg); break;
                case "Torque":
                    DrawTorqueOverrides(cfg); break;
                case "SquareCheck":
                    DrawSquareOverrides(cfg); break;
                default:
                    EditorGUILayout.LabelField($"No profile-specific overrides for '{profile}'.", EditorStyles.miniLabel);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                _dirtyStepIds.Add(step.id);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawUniversalOverrides(ToolActionPreviewConfig c)
        {
            EditorGUILayout.LabelField("Universal", EditorStyles.miniBoldLabel);
            c.duration           = OverrideFloatField("Duration (s)",            c.duration,           "preview default");
            c.approachDuration   = OverrideFloatField("Approach Duration (s)",   c.approachDuration,   "0.5");
            c.returnDuration     = OverrideFloatField("Return Duration (s)",     c.returnDuration,     "0.35");
            c.guidedDragScale    = OverrideFloatField("Guided Drag Scale",       c.guidedDragScale,    "preview default");
            c.autoAssistDelay    = OverrideFloatField("Auto-Assist Delay (s)",   c.autoAssistDelay,    "preview default");
            c.autoAssistRate     = OverrideFloatField("Auto-Assist Rate (/s)",   c.autoAssistRate,     "preview default");
            c.framingDistance    = OverrideFloatField("Framing Distance (m)",    c.framingDistance,    "profile default");
            c.workingDistance    = OverrideFloatField("Working Distance (m)",    c.workingDistance,    "profile default");
            c.approachTiltDegrees = OverrideFloatField("Approach Tilt (°)",       c.approachTiltDegrees, "profile default");
        }

        private static void DrawWeldOverrides(ToolActionPreviewConfig c)
        {
            EditorGUILayout.LabelField("Weld", EditorStyles.miniBoldLabel);
            c.weldArcSpawnThreshold   = OverrideFloatField("Arc Spawn Threshold (0..1)",  c.weldArcSpawnThreshold,   "0.1");
            c.weldBeadSpawnThreshold  = OverrideFloatField("Bead Spawn Threshold (0..1)", c.weldBeadSpawnThreshold,  "0.2");
            c.weldBeadWindowStart     = OverrideFloatField("Bead Window Start (0..1)",     c.weldBeadWindowStart,     "0.15");
            c.weldBeadWindowEnd       = OverrideFloatField("Bead Window End (0..1)",       c.weldBeadWindowEnd,       "0.9");
            c.weldBeadWidth           = OverrideFloatField("Bead Width (m)",               c.weldBeadWidth,           "0.004");
            c.weldWobbleAmplitude     = OverrideFloatField("Wobble Amplitude (rad)",       c.weldWobbleAmplitude,     "0.12");
            c.weldWobbleFrequency     = OverrideFloatField("Wobble Frequency (rad/s)",     c.weldWobbleFrequency,     "40");
            c.weldBeadHotColor        = OverrideColorField("Bead Hot Color",               c.weldBeadHotColor,        "silver-tan");
            c.weldBeadCoolColor       = OverrideColorField("Bead Cool Color",              c.weldBeadCoolColor,       "grey");
            c.weldCoolerDuration      = OverrideFloatField("Cooler Duration (s)",          c.weldCoolerDuration,      "2.0");
        }

        private static void DrawDrillOverrides(ToolActionPreviewConfig c)
        {
            EditorGUILayout.LabelField("Drill", EditorStyles.miniBoldLabel);
            c.drillShakeFrequency   = OverrideFloatField("Shake Frequency (Hz)",          c.drillShakeFrequency,   "55");
            c.drillShakeAmplitude   = OverrideFloatField("Shake Amplitude (m)",           c.drillShakeAmplitude,   "0.0004");
            c.drillRampUpEnd        = OverrideFloatField("Ramp-Up End (0..1)",            c.drillRampUpEnd,        "0.15");
            c.drillRampDownStart    = OverrideFloatField("Ramp-Down Start (0..1)",        c.drillRampDownStart,    "0.85");
            c.drillSpark1Threshold  = OverrideFloatField("Spark 1 Threshold (0..1)",      c.drillSpark1Threshold,  "0.4");
            c.drillSpark1Scale      = OverrideFloatField("Spark 1 Scale",                 c.drillSpark1Scale,      "0.06");
            c.drillSpark2Threshold  = OverrideFloatField("Spark 2 Threshold (0..1)",      c.drillSpark2Threshold,  "0.8");
            c.drillSpark2Scale      = OverrideFloatField("Spark 2 Scale",                 c.drillSpark2Scale,      "0.04");
            c.drillGlowIntensity    = OverrideFloatField("Glow Intensity (×)",            c.drillGlowIntensity,    "0.5");
            c.drillGlowColor        = OverrideColorField("Glow Color",                    c.drillGlowColor,        "blue");
        }

        private static void DrawTorqueOverrides(ToolActionPreviewConfig c)
        {
            EditorGUILayout.LabelField("Torque", EditorStyles.miniBoldLabel);
            c.torqueTargetAngle        = OverrideFloatField("Target Angle (°)",           c.torqueTargetAngle,        "120");
            c.torqueGestureArc         = OverrideFloatField("Gesture Arc (°)",            c.torqueGestureArc,         "180");
            c.torqueMinRadius          = OverrideFloatField("Min Radius (px)",            c.torqueMinRadius,          "10");
            c.torqueDragFallbackScale  = OverrideFloatField("Drag Fallback Scale",        c.torqueDragFallbackScale,  "0.008");
            c.torqueSparkThreshold     = OverrideFloatField("Spark Threshold (0..1)",     c.torqueSparkThreshold,     "0.5");
            c.torqueSparkScale         = OverrideFloatField("Spark Scale",                c.torqueSparkScale,         "0.1");
        }

        private static void DrawCutOverrides(ToolActionPreviewConfig c)
        {
            EditorGUILayout.LabelField("Cut", EditorStyles.miniBoldLabel);
            c.cutEmissionThreshold       = OverrideFloatField("Emission Threshold (0..1)",  c.cutEmissionThreshold,      "0.15");
            c.cutGlowColor               = OverrideColorField("Glow Color",                 c.cutGlowColor,              "orange");
            c.cutSpark1Threshold         = OverrideFloatField("Spark 1 Threshold (0..1)",   c.cutSpark1Threshold,        "0.2");
            c.cutSpark1Scale             = OverrideFloatField("Spark 1 Scale",              c.cutSpark1Scale,            "0.12");
            c.cutSpark2Threshold         = OverrideFloatField("Spark 2 Threshold (0..1)",   c.cutSpark2Threshold,        "0.6");
            c.cutSpark2Scale             = OverrideFloatField("Spark 2 Scale",              c.cutSpark2Scale,            "0.08");
            c.cutLineSpawn               = OverrideFloatField("Line Spawn (0..1)",          c.cutLineSpawn,              "0.25");
            c.cutLineWindowEnd           = OverrideFloatField("Line Window End (0..1)",     c.cutLineWindowEnd,          "0.9");
            c.cutVibrationWindowStart    = OverrideFloatField("Vibration Start (0..1)",     c.cutVibrationWindowStart,   "0.15");
            c.cutVibrationWindowEnd      = OverrideFloatField("Vibration End (0..1)",       c.cutVibrationWindowEnd,     "0.9");
            c.cutVibrationFrequency      = OverrideFloatField("Vibration Frequency (Hz)",   c.cutVibrationFrequency,     "80");
            c.cutVibrationAmplitude      = OverrideFloatField("Vibration Amplitude (m)",    c.cutVibrationAmplitude,     "0.00015");
            c.cutLineColor               = OverrideColorField("Line Color",                 c.cutLineColor,              "dark scorch");
        }

        private static void DrawSquareOverrides(ToolActionPreviewConfig c)
        {
            EditorGUILayout.LabelField("Square-Check", EditorStyles.miniBoldLabel);
            c.squareSettleDistance    = OverrideFloatField("Settle Distance (m)",       c.squareSettleDistance,    "0.015");
            c.squareSettleEnd         = OverrideFloatField("Settle End (0..1)",         c.squareSettleEnd,         "0.4");
            c.squareHoldEnd           = OverrideFloatField("Hold End (0..1)",           c.squareHoldEnd,           "0.7");
            c.squareGlowColor         = OverrideColorField("Glow Color",                c.squareGlowColor,         "green");
            c.squarePulseFrequency    = OverrideFloatField("Pulse Frequency (cycles)",  c.squarePulseFrequency,    "1.0");
        }

        // ── Field helpers ────────────────────────────────────────────────

        private static float OverrideFloatField(string label, float value, string defaultHint)
        {
            var content = new GUIContent(label,
                value > 0f ? $"Authored value (overrides default)" : $"Default = {defaultHint}. Set to any non-zero value to override.");
            var prevStyle = EditorStyles.numberField.fontStyle;
            // Indicate unauthored fields with italic style for quick scan.
            var style = new GUIStyle(EditorStyles.numberField);
            if (value <= 0f) style.fontStyle = FontStyle.Italic;
            return EditorGUILayout.FloatField(content, value, style);
        }

        private static SceneFloat4 OverrideColorField(string label, SceneFloat4 value, string defaultHint)
        {
            bool authored = value.a > 0f;
            Color color = authored
                ? new Color(value.r, value.g, value.b, value.a)
                : Color.clear;
            var content = new GUIContent(label,
                authored ? "Authored colour (overrides default)" : $"Default = {defaultHint}. Set alpha &gt; 0 to override.");
            Color newColor = EditorGUILayout.ColorField(content, color);
            return new SceneFloat4 { r = newColor.r, g = newColor.g, b = newColor.b, a = newColor.a };
        }

        private static Vector3 ToVector3(SceneFloat3 v) => new Vector3(v.x, v.y, v.z);
        private static SceneFloat3 FromVector3(Vector3 v) => new SceneFloat3 { x = v.x, y = v.y, z = v.z };
    }
}
