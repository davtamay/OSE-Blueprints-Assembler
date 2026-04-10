using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Editor window for visualizing and refining spatial grab metadata for
    /// both tools (toolPose) and parts (grabConfig).
    ///
    /// Two preview modes:
    ///   - Embedded: self-contained 3D preview in the window (drag orbit, scroll zoom)
    ///   - SceneView: spawns into the scene with draggable handles — use standard Unity nav
    ///
    /// Open via: OSE > Grab Pose Editor
    /// </summary>
    public sealed class ToolPoseGizmoEditor : EditorWindow
    {
        private const string MenuPath = "OSE/Authoring/Grab Pose Editor";
        private const string RightHandFbx =
            "Assets/Samples/XR Hands/1.7.3/HandVisualizer/Models/RightHand.fbx";
        private const string LeftHandFbx =
            "Assets/Samples/XR Hands/1.7.3/HandVisualizer/Models/LeftHand.fbx";
        private const float RuntimeCursorScale = 0.16f;
        private const float EmbeddedPreviewHeight = 380f;

        private enum EditorTab { Tools, Parts }
        private enum PreviewMode { Embedded, SceneView }
        private enum PreviewContent { Hand, Cursor }

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var w = GetWindow<ToolPoseGizmoEditor>("Grab Pose Editor");
            w.minSize = new Vector2(420, 500);
            w.Show();
        }

        // ── State ────────────────────────────────────────────────────────────────

        private EditorTab _tab = EditorTab.Tools;
        private PreviewMode _preview = PreviewMode.SceneView;
        private string[] _packageIds;
        private int _pkgIdx;
        private MachinePackageDefinition _pkg;
        private string _pkgId;
        private Vector2 _scroll;

        private int _toolIdx; private ToolDefinition _tool;
        private int _partIdx; private PartDefinition _part;

        // Shared model state
        private GameObject _model;
        private Bounds _bounds;
        private bool _loaded;
        /// <summary>Local-space bounding box center used as custom pivot for handles.</summary>
        private Vector3 _modelCenterLocal;

        // Embedded preview
        private PreviewRenderUtility _pu;
        private float _orbX = 20f, _orbY = -30f, _dist = 0.8f;
        private Vector3 _pivot;

        // Hand / Cursor toggle
        private PreviewContent _previewContent = PreviewContent.Hand;
        private GameObject _hand;
        private bool _showHand = true;
        private Transform[] _fingers;
        private Transform _thumbTip;
        private Material[][] _cursorOriginalMats; // stashed to restore when switching back to Hand

        // Scale
        private float _scale = 1f;
        private float _scaleOverride;

        // Editable pose — tool-on-hand model
        // Primary: user moves/rotates the tool; grip values are derived.
        private Vector3 _toolPos;                       // tool offset from HandRefPos (world)
        private Vector3 _toolRotEuler;                  // tool euler rotation (world)
        private Vector3 _tip, _tipAxis;                   // tip point + explicit aiming direction
        private Vector3 _cursorLocal, _cursorRot;          // model-local points (follow tool)
        private string _handedness = "right", _pose = "power_grip";
        private bool _dirty;
        private bool _showLabels = true;

        // Undo/redo for pose edits (in-editor, before writing to disk)
        private struct PoseSnapshot
        {
            public Vector3 toolPos, toolRotEuler, tip, tipAxis, cursorLocal, cursorRot;
            public string handedness, pose;
            public float scaleOverride;
        }
        private const int MaxUndoHistory = 50;
        private readonly List<PoseSnapshot> _undoStack = new();
        private readonly List<PoseSnapshot> _redoStack = new();
        private bool _snapshotPending;

        // File-level backup for machine.json writes
        private string _lastBackupPath;

        private bool IsTool => _tab == EditorTab.Tools;
        private bool IsEmbedded => _preview == PreviewMode.Embedded;

        private static readonly string[] Hands = { "right", "left", "either" };
        private static readonly string[] Poses = { "power_grip", "pinch", "precision", "two_hand" };

        private static readonly Color ColGrip   = new(0.2f, 0.5f, 1f, 1f);
        private static readonly Color ColTip    = new(1f, 0.15f, 0.15f, 1f);
        private static readonly Color ColTipA   = new(0.2f, 1f, 0.3f, 1f);
        private static readonly Color ColLine   = new(0.7f, 0.7f, 0.7f, 0.5f);
        private static readonly Color ColGrid   = new(0.35f, 0.35f, 0.35f, 0.4f);
        private static readonly Color ColCursor = new(1f, 0.85f, 0.1f, 0.9f);

        /// <summary>Fixed hand world position — raised above the ground plane.</summary>
        private static readonly Vector3 HandRefPos = new(0f, 1f, 0f);
        /// <summary>Identity — used for grip rotation derivation math only.</summary>
        private static readonly Quaternion HandFixedRot = Quaternion.identity;
        /// <summary>Visual rotation so the hand mesh displays in a natural side-grip pose.</summary>
        private static readonly Quaternion HandVisualRot = Quaternion.Euler(0f, 0f, -90f);

        // ── Pose Undo / Redo ────────────────────────────────────────────────────

        private PoseSnapshot CaptureSnapshot() => new()
        {
            toolPos = _toolPos, toolRotEuler = _toolRotEuler, tip = _tip, tipAxis = _tipAxis,
            cursorLocal = _cursorLocal, cursorRot = _cursorRot,
            handedness = _handedness, pose = _pose,
            scaleOverride = _scaleOverride
        };

        private void PushUndo()
        {
            _undoStack.Add(CaptureSnapshot());
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        private void UndoPose()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(CaptureSnapshot());
            var s = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            ApplySnapshot(s);
        }

        private void RedoPose()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(CaptureSnapshot());
            var s = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            ApplySnapshot(s);
        }

        private void ApplySnapshot(PoseSnapshot s)
        {
            _toolPos = s.toolPos; _toolRotEuler = s.toolRotEuler; _tip = s.tip; _tipAxis = s.tipAxis;
            _cursorLocal = s.cursorLocal; _cursorRot = s.cursorRot;
            _handedness = s.handedness; _pose = s.pose;
            _scaleOverride = s.scaleOverride;
            _dirty = true;
            ApplyToolTransform();
            Repaint();
            if (!IsEmbedded) SceneView.RepaintAll();
        }

        /// <summary>Call before any edit to record current state for undo.</summary>
        private void BeginEdit()
        {
            if (!_snapshotPending)
            {
                PushUndo();
                _snapshotPending = true;
            }
        }

        /// <summary>Call after an edit sequence completes (mouse up, field commit).</summary>
        private void EndEdit() => _snapshotPending = false;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            RefreshPackageList();
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Cleanup();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            if (!string.IsNullOrEmpty(_pkgId))
                LoadPkg(_pkgId);
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        private void Cleanup()
        {
            KillModel(); KillHand();
            if (_pu != null) { _pu.Cleanup(); _pu = null; }
        }

        private void EnsurePU()
        {
            if (_pu != null) return;
            _pu = new PreviewRenderUtility();
            _pu.camera.fieldOfView = 30f;
            _pu.camera.nearClipPlane = 0.001f;
            _pu.camera.farClipPlane = 100f;
            _pu.camera.clearFlags = CameraClearFlags.SolidColor;
            _pu.camera.backgroundColor = new Color(0.17f, 0.17f, 0.21f, 1f);
            _pu.lights[0].transform.rotation = Quaternion.Euler(30f, -45f, 0f);
            _pu.lights[0].intensity = 1.3f;
            _pu.lights[0].color = new Color(1f, 0.97f, 0.93f);
            if (_pu.lights.Length > 1)
            {
                _pu.lights[1].transform.rotation = Quaternion.Euler(-20f, 135f, 0f);
                _pu.lights[1].intensity = 0.5f;
            }
        }

        // ── Main GUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            // Tab toggle
            EditorGUI.BeginChangeCheck();
            _tab = (EditorTab)GUILayout.Toolbar((int)_tab, new[] { "Tools", "Parts" });
            if (EditorGUI.EndChangeCheck())
            {
                KillModel(); KillHand();
                _tool = null; _part = null; _dirty = false;
                if (_pkg != null) PickFirst();
            }

            EditorGUILayout.Space(2);
            DrawPkgPicker();
            if (_pkg == null) return;
            DrawItemPicker();
            if (IsTool ? _tool == null : _part == null) return;

            // Preview mode toggle
            EditorGUI.BeginChangeCheck();
            _preview = (PreviewMode)GUILayout.Toolbar((int)_preview, new[] { "Embedded Preview", "SceneView Preview" });
            if (EditorGUI.EndChangeCheck()) SwitchPreviewMode();

            // Embedded preview renders inline; SceneView mode just shows a hint
            if (IsEmbedded)
                DrawEmbeddedPreview();
            else
                EditorGUILayout.HelpBox(
                    "Preview is in the SceneView. Use standard Unity navigation.\n" +
                    "Drag the blue/red handles to move grip/tip points.",
                    MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(4);

            DrawScaleUI();
            DrawBoundsInfo();
            EditorGUILayout.Space(4);
            DrawPoseFields();
            EditorGUILayout.Space(4);
            DrawHandUI();
            EditorGUILayout.Space(8);
            DrawActions();
            EditorGUILayout.Space(4);
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        private void SwitchPreviewMode()
        {
            // Tear down current model and re-load in the new mode
            string assetRef = IsTool ? _tool?.assetRef : _part?.assetRef;
            string itemId = IsTool ? _tool?.id : _part?.id;
            KillModel(); KillHand();
            if (!string.IsNullOrEmpty(assetRef))
                LoadModel(assetRef, itemId);
        }

        // ── Package / Item pickers ───────────────────────────────────────────────

        private void DrawPkgPicker()
        {
            if (_packageIds == null || _packageIds.Length == 0)
            {
                EditorGUILayout.HelpBox("No packages found in Assets/_Project/Data/Packages/", MessageType.Warning);
                if (GUILayout.Button("Refresh")) RefreshPackageList();
                return;
            }
            int i = EditorGUILayout.Popup("Package", _pkgIdx, _packageIds);
            if (i != _pkgIdx) { _pkgIdx = i; LoadPkg(_packageIds[i]); }
            if (_pkg == null && GUILayout.Button("Load")) LoadPkg(_packageIds[_pkgIdx]);
        }

        private void DrawItemPicker()
        {
            if (IsTool)
            {
                var arr = _pkg.tools;
                if (arr == null || arr.Length == 0) { EditorGUILayout.HelpBox("No tools.", MessageType.Info); return; }
                var n = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++) n[i] = $"{arr[i].id} ({arr[i].name})";
                int idx = EditorGUILayout.Popup("Tool", _toolIdx, n);
                if (idx != _toolIdx || _tool == null) { _toolIdx = idx; PickTool(arr[idx]); }
            }
            else
            {
                var arr = _pkg.parts;
                if (arr == null || arr.Length == 0) { EditorGUILayout.HelpBox("No parts.", MessageType.Info); return; }
                var n = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++) n[i] = $"{arr[i].id} ({arr[i].GetDisplayName()})";
                int idx = EditorGUILayout.Popup("Part", _partIdx, n);
                if (idx != _partIdx || _part == null) { _partIdx = idx; PickPart(arr[idx]); }
            }
        }

        // ── Embedded Preview ─────────────────────────────────────────────────────

        private void DrawEmbeddedPreview()
        {
            Rect r = GUILayoutUtility.GetRect(10, EmbeddedPreviewHeight, GUILayout.ExpandWidth(true));
            if (r.width < 10 || r.height < 10 || _pu == null || !_loaded) return;

            HandleEmbeddedInput(r);
            UpdateEmbeddedCamera();
            ApplyToolTransform();
            UpdateHandXform();

            _pu.BeginPreview(r, GUIStyle.none);
            _pu.camera.Render();
            Handles.SetCamera(_pu.camera);
            DrawGrid();
            DrawMarkersVis();
            Texture tex = _pu.EndPreview();
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);

            var st = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(1, 1, 1, 0.35f) }, alignment = TextAnchor.LowerLeft };
            GUI.Label(new Rect(r.x + 4, r.yMax - 18, r.width, 16),
                "Drag: orbit  |  Scroll: zoom  |  Alt+Drag / Middle: pan", st);
        }

        private void HandleEmbeddedInput(Rect r)
        {
            Event e = Event.current;
            if (!r.Contains(e.mousePosition)) return;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.type)
            {
                case EventType.MouseDown when e.button is 0 or 1 or 2:
                    GUIUtility.hotControl = id; e.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        float ps = _dist * 0.002f;
                        _pivot -= _pu.camera.transform.right * e.delta.x * ps
                                + _pu.camera.transform.up    * e.delta.y * ps;
                    }
                    else
                    {
                        _orbY += e.delta.x * 0.5f;
                        _orbX = Mathf.Clamp(_orbX - e.delta.y * 0.5f, -89f, 89f);
                    }
                    e.Use(); Repaint(); break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0; e.Use(); break;
                case EventType.ScrollWheel:
                    _dist = Mathf.Clamp(_dist * (1f + e.delta.y * 0.06f), 0.005f, 30f);
                    e.Use(); Repaint(); break;
            }
        }

        private void UpdateEmbeddedCamera()
        {
            float xr = _orbX * Mathf.Deg2Rad, yr = _orbY * Mathf.Deg2Rad;
            Vector3 off = new(
                _dist * Mathf.Cos(xr) * Mathf.Sin(yr),
                _dist * Mathf.Sin(xr),
                _dist * Mathf.Cos(xr) * Mathf.Cos(yr));
            _pu.camera.transform.position = _pivot + off;
            _pu.camera.transform.LookAt(_pivot);
        }

        private void DrawGrid()
        {
            float ext = Mathf.Max(_bounds.size.magnitude * _scale, 0.2f);
            float step = SnapGrid(ext);
            float y = _pivot.y - _bounds.extents.y * _scale;
            Handles.color = ColGrid;
            for (float v = -ext; v <= ext; v += step)
            {
                Handles.DrawLine(new Vector3(_pivot.x + v, y, _pivot.z - ext),
                                 new Vector3(_pivot.x + v, y, _pivot.z + ext));
                Handles.DrawLine(new Vector3(_pivot.x - ext, y, _pivot.z + v),
                                 new Vector3(_pivot.x + ext, y, _pivot.z + v));
            }
        }

        private static float SnapGrid(float ext)
        {
            float raw = ext / 5f;
            float p = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 1e-6f))));
            float n = raw / p;
            return (n < 2f ? 1f : n < 5f ? 2f : 5f) * p;
        }

        // ── SceneView Preview ────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (IsEmbedded || _model == null || !_loaded) return;

            bool cursorMode = _previewContent == PreviewContent.Cursor && IsTool;

            // ── Cursor mode: tool tracks the SceneView mouse like play mode ──
            if (cursorMode)
            {
                DrawCursorModeSceneGUI(sceneView);
                return;
            }

            Transform root = _model.transform;
            Vector3 handW = HandRefPos;
            float baseSize = HandleUtility.GetHandleSize(handW) * 0.06f;

            // ── Hand marker (blue sphere — fixed, never moves) ──
            Handles.color = ColGrip;
            Handles.SphereHandleCap(0, handW, Quaternion.identity, baseSize * 2.5f, EventType.Repaint);
            if (_showLabels)
            {
                var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColGrip } };
                Handles.Label(handW + Vector3.up * baseSize * 3f, "HAND", s);
            }

            // ── Tool handles at bounding-box center pivot ──
            Vector3 pivotW = root.TransformPoint(_modelCenterLocal);

            // Position handle
            EditorGUI.BeginChangeCheck();
            Vector3 newPivot = Handles.PositionHandle(pivotW, root.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                _toolPos += newPivot - pivotW; // delta move
                ApplyToolTransform();
                _dirty = true; Repaint();
            }
            else EndEdit();

            // Rotation handle — rotates around center pivot
            // Tip + cursor stay in model-local space (rotate with tool).
            // cursorRot syncs to toolRotEuler so desktop preview matches.
            EditorGUI.BeginChangeCheck();
            Quaternion newToolRot = Handles.RotationHandle(root.rotation, pivotW);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                // Rotate tool around the center pivot, not the mesh origin
                Quaternion oldRot = Quaternion.Euler(_toolRotEuler);
                Quaternion delta = newToolRot * Quaternion.Inverse(oldRot);
                Vector3 oldPos = HandRefPos + _toolPos;
                Vector3 rotatedPos = delta * (oldPos - pivotW) + pivotW;
                _toolPos = rotatedPos - HandRefPos;
                _toolRotEuler = newToolRot.eulerAngles;
                ApplyToolTransform();
                _dirty = true; Repaint();
            }
            else EndEdit();

            if (IsTool)
            {
                // ── Tip handle (red, draggable on tool) ──
                Vector3 tipW = root.TransformPoint(_tip);
                Handles.color = ColTip;
                Handles.SphereHandleCap(0, tipW, Quaternion.identity, baseSize * 2.5f, EventType.Repaint);
                EditorGUI.BeginChangeCheck();
                Vector3 newTip = Handles.PositionHandle(tipW, root.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    _tip = root.InverseTransformPoint(newTip);
                    _dirty = true; Repaint();
                }
                else EndEdit();
                if (_showLabels)
                {
                    var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColTip } };
                    Handles.Label(tipW + Vector3.up * baseSize * 3f, "TIP", s);
                }

                // Grip-to-tip dashed line + direction arrow
                Handles.color = ColLine;
                Handles.DrawDottedLine(handW, tipW, 4f);
                Vector3 dir = (tipW - handW);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    float len = baseSize * 10f;
                    Vector3 end = handW + dir.normalized * len;
                    Handles.color = ColTipA;
                    Handles.DrawAAPolyLine(3f, handW, end);
                    Handles.ConeHandleCap(0, end, Quaternion.LookRotation(dir.normalized),
                        baseSize * 1.2f, EventType.Repaint);
                    if (_showLabels)
                    {
                        var s = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColTipA } };
                        Handles.Label(end + Vector3.up * baseSize, "tip dir", s);
                    }
                }

                // ── Cursor offset handle (yellow, model-local like tip) ──
                Vector3 cursorW = root.TransformPoint(_cursorLocal);
                Handles.color = ColCursor;
                Handles.SphereHandleCap(0, cursorW, Quaternion.identity, baseSize * 2f, EventType.Repaint);
                EditorGUI.BeginChangeCheck();
                Vector3 newCursor = Handles.PositionHandle(cursorW, root.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    _cursorLocal = root.InverseTransformPoint(newCursor);
                    _dirty = true; Repaint();
                }
                else EndEdit();

                // Dashed line from hand to cursor offset point
                Handles.color = ColCursor * 0.7f;
                Handles.DrawDottedLine(handW, cursorW, 3f);
                if (_showLabels)
                {
                    var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColCursor } };
                    Handles.Label(cursorW + Vector3.up * baseSize * 3f, "Cursor Offset", s);
                }

                // ── Cursor Preview: where the cursor actually appears at runtime ──
                // Uses the full tiered preview rotation (same as ToolCursorManager at runtime).
                // The orientation axes show the effect of Cursor Rotation.
                Quaternion previewRot = _tool != null
                    ? ToolPoseResolver.ResolvePreviewRotation(_tool, _model)
                    : Quaternion.Euler(_cursorRot);
                Vector3 runtimeCursorW = root.position + previewRot * (_cursorLocal * _scale);
                Color colPreview = new Color(1f, 0.5f, 0f, 0.9f);
                Handles.color = colPreview;
                Handles.SphereHandleCap(0, runtimeCursorW, Quaternion.identity, baseSize * 2f, EventType.Repaint);
                Handles.DrawDottedLine(cursorW, runtimeCursorW, 2f);
                // Orientation axes at Cursor Preview — visualizes Cursor Rotation
                float axLen = baseSize * 4f;
                Handles.color = new Color(1f, 0.3f, 0.3f, 0.8f);
                Handles.DrawLine(runtimeCursorW, runtimeCursorW + previewRot * Vector3.right   * axLen);
                Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
                Handles.DrawLine(runtimeCursorW, runtimeCursorW + previewRot * Vector3.up      * axLen);
                Handles.color = new Color(0.3f, 0.3f, 1f, 0.8f);
                Handles.DrawLine(runtimeCursorW, runtimeCursorW + previewRot * Vector3.forward * axLen);
                if (_showLabels)
                {
                    var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = colPreview } };
                    Handles.Label(runtimeCursorW + Vector3.up * baseSize * 3f, "Cursor Preview", s);
                }
            }
        }

        /// <summary>
        /// In cursor mode the tool tracks the SceneView mouse exactly like
        /// <see cref="ToolCursorManager.UpdatePosition"/> does at runtime.
        /// The tool is parented to the scene camera and positioned at a fixed
        /// depth so it floats in front of the viewport.
        /// </summary>
        private void DrawCursorModeSceneGUI(SceneView sceneView)
        {
            Camera cam = sceneView.camera;
            if (cam == null) return;

            if (Event.current.type == EventType.MouseMove)
                sceneView.Repaint();

            // ── Resolve rotation from live editor fields ──
            // Mirrors ToolPoseResolver.ResolvePreviewRotation tiers but uses
            // the unsaved editor values so changes are visible immediately.
            //   Tier 1: explicit cursorRotation (_cursorRot field)
            //   Tier 2: Inverse(gripRotation) — derived from _toolRotEuler
            //   Tier 3+: fall through to resolver for orientationEuler / auto-detect
            Quaternion previewRot;
            if (_cursorRot.sqrMagnitude > 0.001f)
                previewRot = Quaternion.Euler(_cursorRot);
            else
            {
                Vector3 gripRotEuler = DeriveGripRotation();
                if (gripRotEuler.sqrMagnitude > 0.001f)
                    previewRot = Quaternion.Inverse(Quaternion.Euler(gripRotEuler));
                else
                    previewRot = ToolPoseResolver.ResolvePreviewRotation(_tool, _model);
            }

            // ── Grip offset from live editor field ──
            // _cursorLocal = gripPoint + cursorOffset, updated live as the user
            // drags handles or types in the fields. This is the same value that
            // ToolPoseResolver.ResolveCursorOffset() returns after saving.
            Vector3 gripOffset = _cursorLocal;

            // The runtime parents the tool to the camera and sets localRotation.
            // In editor we set world rotation = sceneCamera.rotation * localRotation.
            _model.transform.rotation = cam.transform.rotation * previewRot;
            _model.transform.localScale = Vector3.one * _scale;

            // ── Position — mirrors ToolCursorManager.UpdatePosition() exactly ──
            Vector2 guiMouse = Event.current.mousePosition;
            Vector3 vp = new Vector3(
                guiMouse.x / cam.pixelWidth,
                1f - guiMouse.y / cam.pixelHeight, 0f);
            float halfH = CursorRayDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            float localX = (vp.x - 0.5f) * 2f * halfW;

            bool hasGrip = gripOffset.sqrMagnitude > 0.001f;
            float yOffset = hasGrip ? 0f : CursorVerticalOffset;
            float localY = (vp.y - 0.5f) * 2f * halfH + yOffset;
            Vector3 localPos = new Vector3(localX, localY, CursorRayDistance);

            // Runtime (line 210): adjustedPos = localPos - previewRot * (gripOffset * s)
            float s = _scale;
            Vector3 adjustedPos = localPos - previewRot * (gripOffset * s);

            _model.transform.position = cam.transform.TransformPoint(adjustedPos);

            // ── Tip contact marker ──
            if (_tip.sqrMagnitude > 0.001f)
            {
                Vector3 tipWorld = _model.transform.TransformPoint(_tip);
                float baseSize = HandleUtility.GetHandleSize(tipWorld) * 0.06f;
                Handles.color = ColTip;
                Handles.SphereHandleCap(0, tipWorld, Quaternion.identity, baseSize * 2f, EventType.Repaint);
                if (_showLabels)
                {
                    var st = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColTip } };
                    Handles.Label(tipWorld + Vector3.up * baseSize * 2f, "TIP", st);
                }
            }

            // ── Cursor Offset handle ──
            // When the user drags the cursor offset handle, only the tool's screen
            // position changes — the tip stays fixed relative to the tool mesh.
            Vector3 cursorAnchorW = _model.transform.TransformPoint(_cursorLocal);
            float dotSize = HandleUtility.GetHandleSize(cursorAnchorW) * 0.04f;
            Handles.color = ColCursor;
            Handles.SphereHandleCap(0, cursorAnchorW, Quaternion.identity, dotSize, EventType.Repaint);
            if (_showLabels)
            {
                var ls = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColCursor } };
                Handles.Label(cursorAnchorW + cam.transform.up * dotSize * 3f, "cursor anchor", ls);
            }

            // Force continuous repaint while in cursor mode so the tool tracks smoothly.
            if (Event.current.type == EventType.Repaint)
                sceneView.Repaint();
        }

        private const float CursorRayDistance    = 0.75f;  // matches ToolCursorManager
        private const float CursorVerticalOffset = 0.15f;  // matches ToolCursorManager

        // ── Shared marker drawing (embedded mode, read-only) ─────────────────────

        private void DrawMarkersVis()
        {
            if (_model == null) return;
            Transform root = _model.transform;
            float sz = Mathf.Max(0.006f, _dist * 0.018f);

            Vector3 grip = DeriveGripPoint();
            Vector3 gripRot = DeriveGripRotation();
            Vector3 gw = root.TransformPoint(grip);
            Quaternion gripRotW = root.rotation * Quaternion.Euler(gripRot);
            Handles.color = ColGrip;
            Handles.SphereHandleCap(0, gw, Quaternion.identity, sz, EventType.Repaint);

            // Grip orientation frame
            float axLen = sz * 3f;
            Handles.color = new Color(1f, 0.3f, 0.3f, 0.7f);
            Handles.DrawLine(gw, gw + gripRotW * Vector3.right * axLen);
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.7f);
            Handles.DrawLine(gw, gw + gripRotW * Vector3.up * axLen);
            Handles.color = new Color(0.3f, 0.3f, 1f, 0.7f);
            Handles.DrawLine(gw, gw + gripRotW * Vector3.forward * axLen);

            if (_showLabels) EmbLabel(gw, "HAND", ColGrip);

            if (!IsTool) return;

            Vector3 tw = root.TransformPoint(_tip);
            Handles.color = ColTip;
            Handles.SphereHandleCap(0, tw, Quaternion.identity, sz, EventType.Repaint);
            if (_showLabels) EmbLabel(tw, "TIP", ColTip);

            Handles.color = ColLine;
            Handles.DrawDottedLine(gw, tw, 3f);
            Vector3 tipDir = (tw - gw);
            if (tipDir.sqrMagnitude > 0.0001f)
            {
                float len = _bounds.size.magnitude * _scale * 0.3f;
                Vector3 end = gw + tipDir.normalized * len;
                Handles.color = ColTipA;
                Handles.DrawAAPolyLine(3f, gw, end);
                float cone = Mathf.Max(len * 0.07f, 0.003f);
                Handles.ConeHandleCap(0, end, Quaternion.LookRotation(tipDir.normalized), cone, EventType.Repaint);
                if (_showLabels) EmbLabel(end, "tip dir", ColTipA);
            }

            // Cursor offset marker (yellow sphere + dashed line from hand)
            Vector3 cw = root.TransformPoint(_cursorLocal);
            Handles.color = ColCursor;
            Handles.SphereHandleCap(0, cw, Quaternion.identity, sz, EventType.Repaint);
            Handles.color = ColCursor * 0.7f;
            Handles.DrawDottedLine(gw, cw, 2f);
            if (_showLabels) EmbLabel(cw, "Cursor Offset", ColCursor);

            // Cursor Preview (orange) — where cursor actually appears at runtime
            // Orientation axes visualize the Cursor Rotation field.
            Quaternion previewRot = IsTool && _tool != null
                ? ToolPoseResolver.ResolvePreviewRotation(_tool, _model)
                : Quaternion.Euler(_cursorRot);
            Vector3 rcw = root.position + previewRot * (_cursorLocal * _scale);
            Color colPreview = new(1f, 0.5f, 0f, 0.9f);
            Handles.color = colPreview;
            Handles.SphereHandleCap(0, rcw, Quaternion.identity, sz, EventType.Repaint);
            Handles.DrawDottedLine(cw, rcw, 2f);
            axLen = sz * 3f;
            Handles.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            Handles.DrawLine(rcw, rcw + previewRot * Vector3.right   * axLen);
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            Handles.DrawLine(rcw, rcw + previewRot * Vector3.up      * axLen);
            Handles.color = new Color(0.3f, 0.3f, 1f, 0.8f);
            Handles.DrawLine(rcw, rcw + previewRot * Vector3.forward * axLen);
            if (_showLabels) EmbLabel(rcw, "Cursor Preview", colPreview);
        }


        private static void EmbLabel(Vector3 p, string text, Color c)
        {
            Handles.Label(p + Vector3.up * 0.006f,
                text, new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = c }, fontSize = 11 });
        }

        // ── Scale ────────────────────────────────────────────────────────────────

        private void DrawScaleUI()
        {
            if (!IsTool) return;

            EditorGUI.BeginChangeCheck();
            _scaleOverride = EditorGUILayout.Slider(
                new GUIContent("Tool Scale",
                    "Controls how large the tool appears when held/previewed.\n" +
                    "This is saved and used at runtime. Drag to see it live."),
                _scaleOverride, 0.01f, 20f);
            if (EditorGUI.EndChangeCheck())
            {
                _scale = RuntimeCursorScale * _scaleOverride;
                RecomputeToolPosForScale();
                ApplyToolTransform();
                _dirty = true;
                Repaint();
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Recomputes _toolPos so the grip point stays at the hand after a scale change.
        /// Must be called BEFORE ApplyToolTransform so DeriveGripPoint reads
        /// the old transform (grip in model-local space is scale-independent).
        /// </summary>
        private void RecomputeToolPosForScale()
        {
            if (_model == null) return;
            // Grip in model-local space is independent of scale —
            // InverseTransformPoint divides out scale, so read it from current transform.
            Vector3 gripPt = DeriveGripPoint();
            Quaternion toolRot = Quaternion.Euler(_toolRotEuler);
            _toolPos = -(toolRot * (gripPt * _scale));
        }

        private void DrawBoundsInfo()
        {
            if (!_loaded) return;
            Vector3 s = _bounds.size;
            string info = $"Model bounds: {s.x:F3} × {s.y:F3} × {s.z:F3} m";
            float maxDim = Mathf.Max(s.x, s.y, s.z);
            MessageType mt = MessageType.None;
            if (maxDim > 2f) { info += $"  (!) {maxDim:F1}m — check import scale"; mt = MessageType.Warning; }
            EditorGUILayout.HelpBox(info, mt);
        }

        /// <summary>
        /// Applies the tool's position/rotation from editor state.
        /// Hand is always fixed at HandRefPos.
        /// </summary>
        private void ApplyToolTransform()
        {
            if (_model == null) return;
            if (_previewContent == PreviewContent.Cursor && IsTool)
            {
                ApplyCursorTransform();
                return;
            }
            Quaternion rot = Quaternion.Euler(_toolRotEuler);
            _model.transform.rotation = rot;
            _model.transform.localScale = Vector3.one * _scale;
            _model.transform.position = HandRefPos + _toolPos;
            UpdateHandXform();
            UpdateFingerCurl();
        }

        /// <summary>
        /// Computes gripPoint and gripRotation from the current tool transform
        /// relative to the fixed hand. These are the values written to machine.json.
        /// </summary>
        private Vector3 DeriveGripPoint()
        {
            if (_model == null) return Vector3.zero;
            return _model.transform.InverseTransformPoint(HandRefPos);
        }

        private Vector3 DeriveGripRotation()
        {
            Quaternion toolRot = Quaternion.Euler(_toolRotEuler);
            // gripRotation = hand rotation expressed in tool-local space
            return (Quaternion.Inverse(toolRot) * HandFixedRot).eulerAngles;
        }

        // ── Pose Fields ──────────────────────────────────────────────────────────

        private void DrawPoseFields()
        {
            EditorGUILayout.LabelField("Pose", EditorStyles.boldLabel);

            // Undo / Redo
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_undoStack.Count == 0);
            if (GUILayout.Button("◄ Undo", EditorStyles.miniButtonLeft, GUILayout.Width(60))) UndoPose();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_redoStack.Count == 0);
            if (GUILayout.Button("Redo ►", EditorStyles.miniButtonRight, GUILayout.Width(60))) RedoPose();
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            _showLabels = GUILayout.Toggle(_showLabels, "Labels", EditorStyles.miniButton, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();

            _toolPos = EditorGUILayout.Vector3Field("Tool Position", _toolPos);
            _toolRotEuler = EditorGUILayout.Vector3Field("Tool Rotation", _toolRotEuler);
            if (IsTool)
            {
                _tip = EditorGUILayout.Vector3Field("Tip Point", _tip);
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Cursor (Desktop / Mobile)", EditorStyles.miniLabel);
                _cursorLocal = EditorGUILayout.Vector3Field(new GUIContent("Cursor Offset", "Model-local point on the tool where the screen cursor anchors. Shown as the yellow sphere."), _cursorLocal);
                _cursorRot = EditorGUILayout.Vector3Field(new GUIContent("Cursor Rotation", "Euler override for the cursor orientation at runtime. The effect is shown by the RGB axes on the orange Cursor Preview sphere."), _cursorRot);
            }

            if (EditorGUI.EndChangeCheck())
            {
                PushUndo();
                _dirty = true;
                ApplyToolTransform();
                Repaint();
                if (!IsEmbedded) SceneView.RepaintAll();
            }

            // Show derived grip values (read-only)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Vector3Field("Grip Point (derived)", DeriveGripPoint());
            EditorGUILayout.Vector3Field("Grip Rotation (derived)", DeriveGripRotation());
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Auto-Detect (PCA from mesh)", GUILayout.Height(20)))
            {
                PushUndo();
                AutoDetect();
                _dirty = true; Repaint();
            }
        }

        // ── Hand UI ─────────────────────────────────────────────────────────────

        private void DrawHandUI()
        {
            // ── Preview content toggle: Hand vs Cursor ──
            if (IsTool)
            {
                EditorGUILayout.LabelField("Preview Content", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _previewContent = (PreviewContent)GUILayout.Toolbar((int)_previewContent,
                    new[] { "Hand (XR Grab)", "Cursor (Desktop)" });
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyPreviewContent();
                    Repaint();
                    if (!IsEmbedded) SceneView.RepaintAll();
                }
                EditorGUILayout.Space(4);
            }

            if (_previewContent == PreviewContent.Cursor && IsTool)
            {
                EditorGUILayout.HelpBox(
                    "Showing the tool as it appears on the desktop cursor.\n" +
                    "Cursor Rotation and Cursor Offset are applied.",
                    MessageType.Info);
                return; // hide hand settings in cursor mode
            }

            EditorGUILayout.LabelField("XR Hand", EditorStyles.boldLabel);

            int hi = System.Array.IndexOf(Hands, _handedness);
            hi = EditorGUILayout.Popup("Handedness", Mathf.Max(0, hi), Hands);
            if (hi >= 0 && hi < Hands.Length) _handedness = Hands[hi];

            int pi = System.Array.IndexOf(Poses, _pose);
            int newPi = EditorGUILayout.Popup("Pose Hint", Mathf.Max(0, pi), Poses);
            if (newPi != pi) { _pose = Poses[newPi]; UpdateFingerCurl(); }

            EditorGUI.BeginChangeCheck();
            _showHand = EditorGUILayout.Toggle("Show Hand at Grip", _showHand);
            if (EditorGUI.EndChangeCheck())
            {
                if (_showHand) SpawnHand(); else KillHand();
                Repaint();
            }
        }

        /// <summary>
        /// Switches the preview between hand-grip and desktop-cursor views.
        /// In cursor mode the hand is hidden and the tool is repositioned/scaled
        /// to match <see cref="ToolCursorManager"/> at runtime.
        /// </summary>
        private void ApplyPreviewContent()
        {
            if (_model == null) return;

            if (_previewContent == PreviewContent.Cursor)
            {
                KillHand();

                // Stash original materials and apply semi-transparent cursor look
                _cursorOriginalMats = StashMaterials(_model);
                MaterialHelper.MakeTransparent(_model, 0.55f);

                ApplyCursorTransform();
            }
            else
            {
                // Restore original materials
                if (_cursorOriginalMats != null)
                {
                    RestoreStashedMaterials(_model, _cursorOriginalMats);
                    _cursorOriginalMats = null;
                }

                ApplyToolTransform();
                if (_showHand) SpawnHand();
            }
        }

        /// <summary>
        /// Positions the tool model as the desktop cursor would show it at runtime.
        /// Uses the same tiered rotation from <see cref="ToolPoseResolver"/>.
        /// </summary>
        private void ApplyCursorTransform()
        {
            if (_model == null) return;

            // Use live editor fields so unsaved edits are reflected immediately.
            Quaternion previewRot;
            if (_cursorRot.sqrMagnitude > 0.001f)
                previewRot = Quaternion.Euler(_cursorRot);
            else
            {
                Vector3 gripRotEuler = DeriveGripRotation();
                if (gripRotEuler.sqrMagnitude > 0.001f)
                    previewRot = Quaternion.Inverse(Quaternion.Euler(gripRotEuler));
                else if (_tool != null)
                    previewRot = ToolPoseResolver.ResolvePreviewRotation(_tool, _model);
                else
                    previewRot = Quaternion.identity;
            }

            _model.transform.rotation = previewRot;
            _model.transform.localScale = Vector3.one * _scale;
            _model.transform.position = HandRefPos - previewRot * (_cursorLocal * _scale);
        }

        private static Material[][] StashMaterials(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var stash = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
                stash[i] = renderers[i].sharedMaterials;
            return stash;
        }

        private static void RestoreStashedMaterials(GameObject go, Material[][] stash)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length && i < stash.Length; i++)
                renderers[i].sharedMaterials = stash[i];
        }

        private void SpawnHand()
        {
            KillHand();
            if (!_loaded) return;
            // Embedded mode needs PreviewRenderUtility
            if (IsEmbedded && _pu == null) return;

            string path = _handedness == "left" ? LeftHandFbx : RightHandFbx;
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pfb == null)
            {
                Debug.LogWarning($"[GrabPoseEditor] Hand not found: {path}. Import XR Hands HandVisualizer sample.");
                return;
            }

            _hand = Instantiate(pfb);
            _hand.name = "[GrabPosePreview] Hand";
            _hand.hideFlags = HideFlags.HideAndDontSave;
            foreach (var c in _hand.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            ApplyHandMat(_hand);
            CacheBones(_hand);

            if (IsEmbedded)
                _pu.AddSingleGO(_hand);
            // SceneView mode: hand is just a scene object with HideAndDontSave

            UpdateHandXform();
            UpdateFingerCurl();
        }

        private void KillHand()
        {
            if (_hand != null) DestroyImmediate(_hand);
            _hand = null; _fingers = null; _thumbTip = null;
        }

        private void UpdateHandXform()
        {
            if (_hand == null) return;
            _hand.transform.position = HandRefPos;
            _hand.transform.rotation = HandVisualRot;
            _hand.transform.localScale = Vector3.one;
        }

        private void UpdateFingerCurl()
        {
            if (_hand == null || _fingers == null) return;
            float curl  = _pose switch { "power_grip" => 80f, "pinch" => 20f, "precision" => 45f, "two_hand" => 70f, _ => 60f };
            float thumb = _pose switch { "power_grip" => 50f, "pinch" => 40f, "precision" => 35f, "two_hand" => 45f, _ => 40f };
            for (int i = 0; i < _fingers.Length; i++)
            {
                if (_fingers[i] == null) continue;
                float c = (_pose == "pinch" && i == 0) ? 50f : curl;
                _fingers[i].localRotation = Quaternion.Euler(c, 0, 0);
            }
            if (_thumbTip?.parent != null) _thumbTip.parent.localRotation = Quaternion.Euler(0, 0, thumb);
        }

        private void CacheBones(GameObject hand)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                map[t.name.ToLowerInvariant()] = t;
            _fingers = new[]
            {
                Bone(map, "indexproximal", "index1", "b_r_index1", "b_l_index1"),
                Bone(map, "middleproximal", "middle1", "b_r_middle1", "b_l_middle1"),
                Bone(map, "ringproximal", "ring1", "b_r_ring1", "b_l_ring1"),
                Bone(map, "littleproximal", "pinky1", "b_r_pinky1", "b_l_pinky1"),
            };
            _thumbTip = Bone(map, "thumbtip", "thumb_tip", "b_r_thumb_tip", "b_l_thumb_tip", "thumb3");
        }

        private static Transform Bone(Dictionary<string, Transform> m, params string[] names)
        {
            foreach (string n in names)
                if (m.TryGetValue(n.ToLowerInvariant(), out var t)) return t;
            foreach (string n in names)
            {
                string lo = n.ToLowerInvariant();
                foreach (var kv in m) if (kv.Key.Contains(lo)) return kv.Value;
            }
            return null;
        }

        private static void ApplyHandMat(GameObject hand)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return;
            Material m = new(sh) { name = "HandPreview", hideFlags = HideFlags.HideAndDontSave };
            Color skin = new(0.85f, 0.72f, 0.6f, 0.75f);
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3100;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", skin);
            if (m.HasProperty("_Color")) m.SetColor("_Color", skin);
            foreach (var r in hand.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m;
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            if (IsEmbedded && GUILayout.Button("Reset Camera")) ResetCam();
            if (!IsEmbedded && GUILayout.Button("Frame in SceneView")) FrameInScene();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUI.BeginDisabledGroup(!_dirty);
            GUI.backgroundColor = _dirty ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            string lbl = IsTool ? "Write toolPose to machine.json" : "Write grabConfig to machine.json";
            if (GUILayout.Button(lbl, GUILayout.Height(28))) { WriteJson(); _dirty = false; }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            // Revert last file write from backup
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_lastBackupPath) || !File.Exists(_lastBackupPath));
            if (GUILayout.Button("Revert Last Write (restore backup)"))
            {
                string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
                if (jsonPath != null && !string.IsNullOrEmpty(_lastBackupPath) && File.Exists(_lastBackupPath))
                {
                    File.Copy(_lastBackupPath, jsonPath, true);
                    AssetDatabase.Refresh();
                    Debug.Log($"[GrabPoseEditor] Reverted to backup: {_lastBackupPath}");
                    string saved = IsTool ? _tool?.id : _part?.id;
                    LoadPkg(_pkgId);
                    if (IsTool && _pkg.tools != null)
                    {
                        _toolIdx = System.Array.FindIndex(_pkg.tools, t => t.id == saved);
                        if (_toolIdx >= 0) PickTool(_pkg.tools[_toolIdx]);
                    }
                    else if (!IsTool && _pkg.parts != null)
                    {
                        _partIdx = System.Array.FindIndex(_pkg.parts, p => p.id == saved);
                        if (_partIdx >= 0) PickPart(_pkg.parts[_partIdx]);
                    }
                    _lastBackupPath = null;
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawStatus()
        {
            string tier;
            if (IsTool && _tool != null)
                tier = _tool.HasToolPose ? "Tier 1 (toolPose)" : _tool.HasOrientationOverride ? "Tier 2 (orientationEuler)" : "Tier 3 (auto-detect)";
            else if (!IsTool && _part != null)
                tier = _part.HasGrabConfig ? "Authored (grabConfig)" : "None (default)";
            else return;
            string extra = IsTool ? $"  |  Grip-Tip: {(_tip - DeriveGripPoint()).magnitude:F3}m" : "";
            EditorGUILayout.HelpBox($"Resolution: {tier}{extra}", MessageType.Info);
        }

        private void ResetCam()
        {
            _orbX = 20f; _orbY = -30f;
            _dist = Mathf.Max(_bounds.size.magnitude * 1.5f * _scale, 0.1f);
            // Pivot at the hand (origin) — tool is positioned around it
            _pivot = _model != null ? _model.transform.TransformPoint(_bounds.center) : Vector3.zero;
            Repaint();
        }

        private void FrameInScene()
        {
            if (_model == null) return;
            Selection.activeGameObject = _model;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.FrameSelected(); sv.Repaint(); }
        }

        // ── Model Loading ────────────────────────────────────────────────────────

        private void LoadModel(string assetRef, string itemId)
        {
            KillModel(); KillHand();
            if (string.IsNullOrEmpty(assetRef) || string.IsNullOrEmpty(_pkgId)) return;

            string path = $"Assets/_Project/Data/Packages/{_pkgId}/{assetRef}";
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            // Fallback: try assets/tools/ prefix when assetRef is a bare filename
            if (pfb == null && !assetRef.Contains("/"))
            {
                string prefixed = $"Assets/_Project/Data/Packages/{_pkgId}/assets/tools/{assetRef}";
                pfb = AssetDatabase.LoadAssetAtPath<GameObject>(prefixed);
                if (pfb == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(prefixed))
                        if (asset is GameObject go) { pfb = go; break; }
                if (pfb != null) path = prefixed;
            }

            // Fallback: try assets/parts/ prefix
            if (pfb == null && !assetRef.Contains("/"))
            {
                string prefixed = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/{assetRef}";
                pfb = AssetDatabase.LoadAssetAtPath<GameObject>(prefixed);
                if (pfb == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(prefixed))
                        if (asset is GameObject go) { pfb = go; break; }
                if (pfb != null) path = prefixed;
            }

            if (pfb == null) { Debug.LogWarning($"[GrabPoseEditor] Not found: {path}"); return; }

            _model = Instantiate(pfb);
            _model.name = $"[GrabPosePreview] {itemId}";
            _model.hideFlags = HideFlags.HideAndDontSave;
            foreach (var c in _model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);

            _bounds = CalcBounds(_model);
            _modelCenterLocal = _bounds.center; // world ≈ local at instantiation (origin, identity)
            _loaded = true;

            if (IsEmbedded)
            {
                EnsurePU();
                _pu.AddSingleGO(_model);
            }
            // SceneView mode: model is just a scene object with HideAndDontSave

            ApplyToolTransform();
            ResetCam();
            if (_showHand) SpawnHand();
        }

        private void KillModel()
        {
            if (_model != null) DestroyImmediate(_model);
            _model = null; _loaded = false;
        }

        private static Bounds CalcBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ── Selection ────────────────────────────────────────────────────────────

        private void PickTool(ToolDefinition t)
        {
            _tool = t; _part = null;
            _previewContent = PreviewContent.Hand; // reset to hand mode
            _cursorOriginalMats = null;
            _undoStack.Clear(); _redoStack.Clear();

            // Convert authored grip data → tool position (tool at identity rotation,
            // positioned so gripPoint meets the hand at HandRefPos).
            Vector3 gripPt = Vector3.zero;
            if (t.toolPose != null)
            {
                gripPt        = t.toolPose.GetGripPoint();
                _tip          = t.toolPose.GetTipPoint();
                // Capture stable tip direction: prefer explicit tipAxis, else compute from grip→tip
                _tipAxis      = t.toolPose.HasTipAxis
                    ? t.toolPose.GetTipAxis()
                    : (t.toolPose.GetTipPoint() - gripPt).normalized;
                _cursorLocal  = gripPt + t.toolPose.GetCursorOffset();
                _cursorRot    = new Vector3(t.toolPose.cursorRotation.x, t.toolPose.cursorRotation.y, t.toolPose.cursorRotation.z);
                _handedness   = t.toolPose.handedness ?? "right";
                _pose         = t.toolPose.poseHint ?? "power_grip";
            }
            else
            {
                ResetFields();
                if (t.HasOrientationOverride)
                    _cursorRot = t.orientationEuler;
            }

            // Reverse-derive tool transform from authored grip data.
            // gripRotation = (Inverse(toolRot) * HandFixedRot).eulerAngles
            // Since HandFixedRot = identity: toolRot = Inverse(Euler(gripRotation))
            Vector3 gripRotEuler = Vector3.zero;
            if (t.toolPose != null && t.toolPose.HasGripRotation)
                gripRotEuler = new Vector3(t.toolPose.gripRotation.x, t.toolPose.gripRotation.y, t.toolPose.gripRotation.z);

            Quaternion toolRot = Quaternion.Inverse(Quaternion.Euler(gripRotEuler));
            _toolRotEuler = toolRot.eulerAngles;

            _scaleOverride = t.scaleOverride > 0f ? t.scaleOverride : 1f; // 0 in JSON = "use default 0.16"
            _scale = RuntimeCursorScale * _scaleOverride;
            _toolPos = -(toolRot * (gripPt * _scale));

            LoadModel(t.assetRef, t.id);
            _dirty = false;
        }

        private void PickPart(PartDefinition p)
        {
            _part = p; _tool = null;
            _undoStack.Clear(); _redoStack.Clear();

            Vector3 gripPt = Vector3.zero;
            Vector3 gripRotEuler = Vector3.zero;
            if (p.grabConfig != null)
            {
                gripPt      = p.grabConfig.GetGripPoint();
                if (p.grabConfig.HasGripRotation)
                    gripRotEuler = new Vector3(p.grabConfig.gripRotation.x, p.grabConfig.gripRotation.y, p.grabConfig.gripRotation.z);
                _handedness = p.grabConfig.handedness ?? "either";
                _pose       = p.grabConfig.poseHint ?? "power_grip";
            }
            else { ResetFields(); _handedness = "either"; }
            _tip = Vector3.zero;

            Quaternion toolRot = Quaternion.Inverse(Quaternion.Euler(gripRotEuler));
            _toolRotEuler = toolRot.eulerAngles;

            _scale = 1f; // parts always at import scale
            _toolPos = -(toolRot * (gripPt * _scale));

            LoadModel(p.assetRef, p.id);
            _dirty = false;
        }

        private void PickFirst()
        {
            if (IsTool && _pkg.tools is { Length: > 0 }) { _toolIdx = 0; PickTool(_pkg.tools[0]); }
            else if (!IsTool && _pkg.parts is { Length: > 0 }) { _partIdx = 0; PickPart(_pkg.parts[0]); }
        }

        private void ResetFields()
        {
            _toolPos = _toolRotEuler = _tip = _tipAxis = _cursorLocal = _cursorRot = Vector3.zero;
            _handedness = "right"; _pose = "power_grip";
            _scaleOverride = 0f;
        }

        // ── Auto-detect (PCA) ────────────────────────────────────────────────────

        private void AutoDetect()
        {
            if (_model == null) return;
            var filters = _model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return;
            Transform root = _model.transform;

            var pts = new List<Vector3>();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                    pts.Add(root.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count < 3) return;

            Vector3 cent = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) cent += pts[i];
            cent /= pts.Count;

            float cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - cent;
                cxx += d.x * d.x; cyy += d.y * d.y; czz += d.z * d.z;
                cxy += d.x * d.y; cxz += d.x * d.z; cyz += d.y * d.z;
            }
            float n = pts.Count;
            cxx /= n; cyy /= n; czz /= n; cxy /= n; cxz /= n; cyz /= n;

            Vector3 ev = new Vector3(1, 1, 1).normalized;
            for (int iter = 0; iter < 50; iter++)
            {
                Vector3 mv = new(
                    cxx * ev.x + cxy * ev.y + cxz * ev.z,
                    cxy * ev.x + cyy * ev.y + cyz * ev.z,
                    cxz * ev.x + cyz * ev.y + czz * ev.z);
                float mag = mv.magnitude;
                if (mag < 1e-10f) break;
                ev = mv / mag;
            }

            float maxP = float.NegativeInfinity, minP = float.PositiveInfinity;
            Vector3 tipPt = cent, gripPt = cent;
            for (int i = 0; i < pts.Count; i++)
            {
                float proj = Vector3.Dot(pts[i] - cent, ev);
                if (proj > maxP) { maxP = proj; tipPt = pts[i]; }
                if (proj < minP) { minP = proj; gripPt = pts[i]; }
            }
            gripPt = Vector3.Lerp(gripPt, cent, 0.3f);

            _handedness = IsTool ? "right" : "either";
            _pose = "power_grip";

            if (IsTool)
                _tip = tipPt;

            // Position tool so the detected gripPt meets the hand at HandRefPos
            _toolRotEuler = Vector3.zero;
            _toolPos = -(gripPt * _scale);

            Debug.Log($"[GrabPoseEditor] PCA: grip={gripPt}" + (IsTool ? $" tip={_tip}" : ""));
            ApplyToolTransform();
            UpdateHandXform();
            UpdateFingerCurl();
            if (!IsEmbedded) SceneView.RepaintAll();
        }

        // ── Package loading ──────────────────────────────────────────────────────

        private void RefreshPackageList()
        {
            string root = PackageJsonUtils.AuthoringRoot;
            if (!Directory.Exists(root)) { _packageIds = new string[0]; return; }
            var dirs = Directory.GetDirectories(root);
            var ids = new List<string>();
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, "machine.json"))) ids.Add(Path.GetFileName(d));
            _packageIds = ids.ToArray();
        }

        private void LoadPkg(string id)
        {
            _pkg = PackageJsonUtils.LoadPackage(id);
            _pkgId = id;
            _toolIdx = _partIdx = 0;
            _tool = null; _part = null;
            KillModel(); KillHand();
            if (_pkg != null) PickFirst();
        }

        // ── Write back ───────────────────────────────────────────────────────────

        private void WriteJson()
        {
            string itemId = IsTool ? _tool?.id : _part?.id;
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(_pkgId)) return;
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[GrabPoseEditor] machine.json not found for '{_pkgId}'"); return; }

            string blockName = IsTool ? "toolPose" : "grabConfig";
            string poseJson = BuildPoseJson();

            string json = File.ReadAllText(jsonPath);

            // Validate JSON parses before we touch it
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GrabPoseEditor] machine.json is already invalid, aborting write.\n{ex.Message}");
                return;
            }

            if (!TryInjectBlock(ref json, itemId, blockName, poseJson))
            {
                Debug.LogError($"[GrabPoseEditor] Could not find '{itemId}' in {jsonPath}");
                return;
            }

            // Write scaleOverride for tools (sibling to toolPose on the tool object)
            if (IsTool)
            {
                string soValue = _scaleOverride.ToString(System.Globalization.CultureInfo.InvariantCulture);
                TryInjectBlock(ref json, itemId, "scaleOverride", soValue);
            }

            // Validate the result parses before saving
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GrabPoseEditor] Write would produce invalid JSON, aborting.\n{ex.Message}");
                return;
            }

            // Backup before writing
            string backupDir = Path.Combine(Path.GetDirectoryName(jsonPath), ".pose_backups");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupDir, $"machine_{ts}.json");
            File.Copy(jsonPath, backupPath, true);
            _lastBackupPath = backupPath;

            File.WriteAllText(jsonPath, json);
            AssetDatabase.Refresh();
            Debug.Log($"[GrabPoseEditor] Wrote {blockName} for '{itemId}' (backup: {backupPath})");

            // Reload the package definition so status display updates (tier info),
            // but do NOT re-pick the tool/part — that would destroy the model/hand
            // and recreate from JSON, causing visual jumps from float round-trip.
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            if (_pkg != null)
            {
                if (IsTool && _pkg.tools != null)
                {
                    int idx = System.Array.FindIndex(_pkg.tools, t => t.id == itemId);
                    if (idx >= 0) { _toolIdx = idx; _tool = _pkg.tools[idx]; }
                }
                else if (!IsTool && _pkg.parts != null)
                {
                    int idx = System.Array.FindIndex(_pkg.parts, p => p.id == itemId);
                    if (idx >= 0) { _partIdx = idx; _part = _pkg.parts[idx]; }
                }
            }
        }

        private string BuildPoseJson()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            string ind = "                "; // match typical machine.json indentation

            Vector3 gp = DeriveGripPoint();
            Vector3 gr = DeriveGripRotation();
            sb.Append($"{ind}\"gripPoint\": {{ \"x\": {R(gp.x).ToString(inv)}, \"y\": {R(gp.y).ToString(inv)}, \"z\": {R(gp.z).ToString(inv)} }}");

            // Always write gripRotation — the cursor preview is camera-parented and needs
            // an explicit rotation. Zero = identity = mesh raw orientation relative to camera.
            sb.Append($",\n{ind}\"gripRotation\": {{ \"x\": {R(gr.x).ToString(inv)}, \"y\": {R(gr.y).ToString(inv)}, \"z\": {R(gr.z).ToString(inv)} }}");

            if (IsTool)
            {
                sb.Append($",\n{ind}\"tipPoint\": {{ \"x\": {R(_tip.x).ToString(inv)}, \"y\": {R(_tip.y).ToString(inv)}, \"z\": {R(_tip.z).ToString(inv)} }}");
                // Explicit tip direction — stable across grip changes from tool rotation
                if (_tipAxis.sqrMagnitude > 0.001f)
                    sb.Append($",\n{ind}\"tipAxis\": {{ \"x\": {R(_tipAxis.x).ToString(inv)}, \"y\": {R(_tipAxis.y).ToString(inv)}, \"z\": {R(_tipAxis.z).ToString(inv)} }}");
            }

            // cursorOffset in JSON = cursorLocal - gripPoint (relative to grip)
            Vector3 co = _cursorLocal - gp;
            if (co.sqrMagnitude > 0.0001f)
                sb.Append($",\n{ind}\"cursorOffset\": {{ \"x\": {R(co.x).ToString(inv)}, \"y\": {R(co.y).ToString(inv)}, \"z\": {R(co.z).ToString(inv)} }}");

            // Only write cursorRotation when non-zero
            if (_cursorRot.sqrMagnitude > 0.0001f)
                sb.Append($",\n{ind}\"cursorRotation\": {{ \"x\": {R(_cursorRot.x).ToString(inv)}, \"y\": {R(_cursorRot.y).ToString(inv)}, \"z\": {R(_cursorRot.z).ToString(inv)} }}");

            sb.Append($",\n{ind}\"handedness\": \"{_handedness}\"");
            sb.Append($",\n{ind}\"poseHint\": \"{_pose}\"");
            sb.Append($"\n            }}");
            return sb.ToString();
        }

        /// <summary>
        /// Finds the JSON object with the given "id" value, removes any existing key named
        /// <paramref name="block"/>, and appends the new block. All brace/bracket matching
        /// is string-aware (ignores braces inside JSON string literals).
        /// </summary>
        private static bool TryInjectBlock(ref string json, string id, string block, string blockJson)
        {
            // Find "id": "<id>" in the tools/parts array.
            // Strategy: search for the full pattern "id": "tool_mig_torch" as a single string
            // to avoid false positives from "toolId", sub-object "id" keys, etc.
            string fullPattern = $"\"id\": \"{id}\"";
            int idPos = json.IndexOf(fullPattern, System.StringComparison.Ordinal);

            // Also try without space after colon
            if (idPos < 0)
            {
                fullPattern = $"\"id\":\"{id}\"";
                idPos = json.IndexOf(fullPattern, System.StringComparison.Ordinal);
            }

            // Try with variable whitespace: find "id" then verify value
            if (idPos < 0)
            {
                string idNeedle = $"\"id\"";
                int searchFrom = 0;
                while (searchFrom < json.Length)
                {
                    int found = json.IndexOf(idNeedle, searchFrom, System.StringComparison.Ordinal);
                    if (found < 0) break;

                    int afterKey = found + idNeedle.Length;
                    int colonPos = SkipWhitespace(json, afterKey);
                    if (colonPos < json.Length && json[colonPos] == ':')
                    {
                        int valuePos = SkipWhitespace(json, colonPos + 1);
                        string idValue = $"\"{id}\"";
                        if (valuePos + idValue.Length <= json.Length
                            && json.Substring(valuePos, idValue.Length) == idValue)
                        {
                            idPos = found;
                            break;
                        }
                    }
                    searchFrom = found + 1;
                }
            }

            if (idPos < 0)
            {
                Debug.LogWarning($"[GrabPoseEditor] TryInjectBlock: pattern not found for id='{id}'");
                return false;
            }

            // Walk backwards (string-aware) to find the opening { of this object
            int objStart = FindObjectStart(json, idPos);
            if (objStart < 0) return false;

            // Walk forward (string-aware) to find the closing } of this object
            int objEnd = FindMatchingClose(json, objStart);
            if (objEnd < 0) return false;

            // Extract the object substring
            string obj = json.Substring(objStart, objEnd - objStart + 1);

            // Remove existing block key if present
            string cleaned = RemoveKey(obj, block);

            // Detect indentation from the "id" line
            string indent = "            "; // fallback 12 spaces
            int lineStart = json.LastIndexOf('\n', idPos);
            if (lineStart >= 0)
            {
                int firstNonSpace = lineStart + 1;
                while (firstNonSpace < json.Length && json[firstNonSpace] == ' ') firstNonSpace++;
                indent = new string(' ', firstNonSpace - lineStart - 1);
            }

            // Insert the block before the closing }
            int lastBrace = cleaned.LastIndexOf('}');
            string before = cleaned.Substring(0, lastBrace).TrimEnd();
            string after = cleaned.Substring(lastBrace);

            string injected = before + ",\n" + indent + $"\"{block}\": " + blockJson + "\n"
                + indent.Substring(0, System.Math.Max(0, indent.Length - 4)) + after;

            json = json.Substring(0, objStart) + injected + json.Substring(objEnd + 1);
            return true;
        }

        private static int SkipWhitespace(string s, int from)
        {
            while (from < s.Length && char.IsWhiteSpace(s[from])) from++;
            return from;
        }

        /// <summary>
        /// From the "id" key position, walk backward to find the opening { of
        /// this object. In well-formed machine.json, "id" is always the first key
        /// so we just need to skip whitespace + newlines back to {.
        /// </summary>
        private static int FindObjectStart(string json, int from)
        {
            for (int i = from - 1; i >= 0; i--)
            {
                char c = json[i];
                if (c == '{') return i;
                if (!char.IsWhiteSpace(c)) return -1; // unexpected char before "id"
            }
            return -1;
        }

        /// <summary>
        /// From an opening { or [, find the matching close. String-aware.
        /// </summary>
        private static int FindMatchingClose(string json, int openPos)
        {
            char open = json[openPos];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inStr = false;
            for (int i = openPos; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == open) depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>
        /// Removes a top-level key:value pair from a JSON object string.
        /// Uses string-aware scanning for nested braces/brackets.
        /// </summary>
        private static string RemoveKey(string obj, string key)
        {
            string needle = $"\"{key}\"";
            int keyIdx = obj.IndexOf(needle, System.StringComparison.Ordinal);
            if (keyIdx < 0) return obj;

            // Find colon
            int colon = obj.IndexOf(':', keyIdx + needle.Length);
            if (colon < 0) return obj;

            // Find value start
            int valStart = SkipWhitespace(obj, colon + 1);
            if (valStart >= obj.Length) return obj;

            // Find value end
            int valEnd;
            char first = obj[valStart];
            if (first == '{' || first == '[')
            {
                valEnd = FindMatchingClose(obj, valStart);
                if (valEnd < 0) return obj;
            }
            else if (first == '"')
            {
                // String value — find unescaped closing quote
                valEnd = valStart;
                for (int i = valStart + 1; i < obj.Length; i++)
                {
                    if (obj[i] == '\\') { i++; continue; }
                    if (obj[i] == '"') { valEnd = i; break; }
                }
            }
            else
            {
                // Number/bool/null — scan to next , or } or ]
                valEnd = valStart;
                for (int i = valStart; i < obj.Length; i++)
                {
                    char c = obj[i];
                    if (c == ',' || c == '}' || c == ']') { valEnd = i - 1; break; }
                    valEnd = i;
                }
            }

            // Determine removal range including comma + surrounding whitespace
            int removeStart = keyIdx;
            int removeEnd = valEnd + 1;

            // Look for leading comma
            int ls = removeStart - 1;
            while (ls >= 0 && (obj[ls] == ' ' || obj[ls] == '\t' || obj[ls] == '\r' || obj[ls] == '\n')) ls--;
            if (ls >= 0 && obj[ls] == ',')
            {
                removeStart = ls;
            }
            else
            {
                // No leading comma — eat trailing comma
                int ts = removeEnd;
                while (ts < obj.Length && (obj[ts] == ' ' || obj[ts] == '\t')) ts++;
                if (ts < obj.Length && obj[ts] == ',')
                    removeEnd = ts + 1;
            }

            // Eat trailing newline
            while (removeEnd < obj.Length && (obj[removeEnd] == ' ' || obj[removeEnd] == '\r' || obj[removeEnd] == '\n'))
                removeEnd++;

            return obj.Substring(0, removeStart) + obj.Substring(removeEnd);
        }

        private static float R(float v) => Mathf.Round(v * 100000f) / 100000f;
    }
}
