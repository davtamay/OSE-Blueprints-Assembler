using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Static utility that positions spawned parts in the preview scene.
    /// Extracted from <see cref="PackagePartSpawner"/> for single-responsibility.
    ///
    /// In play mode, parts are arranged on an arc; in edit mode they use the
    /// authored <c>startPosition</c> from <c>previewConfig</c>, falling back to
    /// a linear grid when no position is authored.
    /// </summary>
    internal static class PartPositionResolver
    {
        // ── Layout constants ──────────────────────────────────────────────────

        /// <summary>Distance from arc center to part centers.</summary>
        internal const float LayoutRadius = 3.8f;

        /// <summary>Total arc spread in degrees.</summary>
        internal const float LayoutArcDegrees = 220f;

        /// <summary>Arc start angle, centered on the negative-Z (camera-facing) side.</summary>
        internal const float LayoutArcStartDeg = -110f;

        /// <summary>Height above the floor plane.</summary>
        internal const float LayoutY = 0.55f;

        /// <summary>Gap between parts within the same asset group.</summary>
        internal const float LayoutPadding = 0.15f;

        /// <summary>Extra gap between different asset groups on the arc.</summary>
        internal const float LayoutGroupGap = 0.3f;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Positions parts along an arc (play mode) or at authored start positions
        /// (edit mode). Delegates to <see cref="PositionPartsFallback"/> when running
        /// in edit mode or when the package lacks part definitions.
        /// </summary>
        internal static void PositionParts(
            IReadOnlyList<GameObject>          parts,
            MachinePackageDefinition           pkg,
            bool                               isPlaying,
            bool                               showGeometryPreview,
            Func<string, PartPreviewPlacement> findPartPlacement,
            Func<string, bool>                 shouldPreserveTransform)
        {
            if (!showGeometryPreview)
                return;

            if (!isPlaying || pkg?.parts == null)
            {
                PositionPartsFallback(parts, isPlaying, findPartPlacement, shouldPreserveTransform);
                return;
            }

            // Group parts by assetRef so identical parts cluster on the arc.
            var groups      = new List<List<int>>();
            var assetToGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < parts.Count; i++)
            {
                var partGo = parts[i];
                if (partGo == null) continue;

                string assetRef = null;
                foreach (var part in pkg.parts)
                {
                    if (string.Equals(part.id, partGo.name, StringComparison.OrdinalIgnoreCase))
                    {
                        assetRef = part.assetRef;
                        break;
                    }
                }

                string groupKey = assetRef ?? partGo.name;
                if (assetToGroup.TryGetValue(groupKey, out int groupIdx))
                    groups[groupIdx].Add(i);
                else
                {
                    assetToGroup[groupKey] = groups.Count;
                    groups.Add(new List<int> { i });
                }
            }

            int groupCount = groups.Count;
            if (groupCount == 0) return;

            // Pre-resolve scale for every part.
            var partScales = new Vector3[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                var go = parts[i];
                if (go == null) { partScales[i] = Vector3.one; continue; }
                PartPreviewPlacement pp = findPartPlacement(go.name);
                partScales[i] = pp != null
                    ? new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z)
                    : Vector3.one;
            }

            // Estimate tangent spans using rough even-spaced angles (first pass).
            float[] groupSpans = new float[groupCount];
            float   totalSpan  = 0f;
            float   roughStep  = groupCount > 1 ? LayoutArcDegrees / (groupCount - 1) : 0f;

            for (int g = 0; g < groupCount; g++)
            {
                float angle = (LayoutArcStartDeg + roughStep * g) * Mathf.Deg2Rad;
                float tanX  = Mathf.Abs(Mathf.Cos(angle));
                float tanZ  = Mathf.Abs(Mathf.Sin(angle));

                var   members = groups[g];
                float span    = 0f;
                for (int m = 0; m < members.Count; m++)
                {
                    Vector3 s = partScales[members[m]];
                    span += s.x * tanX + s.z * tanZ;
                    if (m < members.Count - 1) span += LayoutPadding;
                }
                groupSpans[g] = span;
                totalSpan    += span;
            }

            totalSpan += (groupCount - 1) * LayoutGroupGap;

            // Scale radius up if spans exceed available arc length.
            float effectiveRadius = LayoutRadius;
            if (totalSpan / (LayoutRadius * Mathf.Deg2Rad) * (180f / Mathf.PI) > LayoutArcDegrees
                && totalSpan > 0f)
            {
                float arcLengthAvailable = LayoutArcDegrees * Mathf.Deg2Rad * LayoutRadius;
                if (totalSpan > arcLengthAvailable)
                    effectiveRadius = totalSpan / (LayoutArcDegrees * Mathf.Deg2Rad);
            }

            float cursor = 0f;

            for (int g = 0; g < groupCount; g++)
            {
                float groupCenter   = cursor + groupSpans[g] * 0.5f;
                float groupAngleRad = (LayoutArcStartDeg * Mathf.Deg2Rad) + (groupCenter / effectiveRadius);

                float cx       = Mathf.Sin(groupAngleRad) * effectiveRadius;
                float cz       = -Mathf.Cos(groupAngleRad) * effectiveRadius;
                float tangentX = Mathf.Cos(groupAngleRad);
                float tangentZ = Mathf.Sin(groupAngleRad);
                float absTanX  = Mathf.Abs(tangentX);
                float absTanZ  = Mathf.Abs(tangentZ);

                var     members        = groups[g];
                float[] memberWidths   = new float[members.Count];
                float   groupTotalWidth = 0f;

                for (int m = 0; m < members.Count; m++)
                {
                    Vector3 s = partScales[members[m]];
                    memberWidths[m]  = s.x * absTanX + s.z * absTanZ;
                    groupTotalWidth += memberWidths[m];
                    if (m < members.Count - 1) groupTotalWidth += LayoutPadding;
                }

                float memberCursor = -groupTotalWidth * 0.5f;

                for (int m = 0; m < members.Count; m++)
                {
                    int        partIdx = members[m];
                    GameObject partGo  = parts[partIdx];
                    if (partGo == null) continue;

                    PartPreviewPlacement spCheck = findPartPlacement(partGo.name);
                    if (SplinePartFactory.HasSplineData(spCheck)) continue;

                    float offset = memberCursor + memberWidths[m] * 0.5f;
                    float px     = cx + tangentX * offset;
                    float pz     = cz + tangentZ * offset;

                    PartPreviewPlacement pp  = findPartPlacement(partGo.name);
                    Vector3              scale = partScales[partIdx];

                    Color col = pp != null
                        ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                        : new Color(0.94f, 0.55f, 0.18f, 1f);
                    Quaternion rot = pp != null && !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;

                    Vector3 pos = (pp != null && HasAuthoredStartPosition(pp))
                        ? ResolvePresentationStartPosition(pp)
                        : new Vector3(px, LayoutY, pz);

                    if (!shouldPreserveTransform(partGo.name))
                        partGo.transform.SetLocalPositionAndRotation(pos, rot);
                    partGo.transform.localScale = scale;

                    if (!MaterialHelper.IsImportedModel(partGo))
                        MaterialHelper.Apply(partGo, "Preview Part Material", col);

                    ClearPropertyBlocks(partGo);

                    memberCursor += memberWidths[m] + LayoutPadding;
                }

                cursor += groupSpans[g] + LayoutGroupGap;
            }
        }

        /// <summary>
        /// Edit-mode fallback: positions parts using authored <c>startPosition</c>
        /// from <c>previewConfig</c>, or a linear grid when no position is authored.
        /// </summary>
        internal static void PositionPartsFallback(
            IReadOnlyList<GameObject>          parts,
            bool                               isPlaying,
            Func<string, PartPreviewPlacement> findPartPlacement,
            Func<string, bool>                 shouldPreserveTransform)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var partGo = parts[i];
                if (partGo == null) continue;

                PartPreviewPlacement pp = findPartPlacement(partGo.name);

                if (SplinePartFactory.HasSplineData(pp)) continue;

                Vector3    pos;
                Vector3    scale;
                Color      col;
                Quaternion rot;

                if (pp != null)
                {
                    pos   = HasAuthoredStartPosition(pp)
                        ? ResolvePresentationStartPosition(pp)
                        : new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    scale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                    col   = new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a);
                    rot   = !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;
                }
                else
                {
                    pos   = new Vector3(-2f + i * 1.5f, 0.55f, 0f);
                    scale = Vector3.one * 0.5f;
                    col   = new Color(0.94f, 0.55f, 0.18f, 1f);
                    rot   = Quaternion.identity;
                }

                if (!shouldPreserveTransform(partGo.name))
                    partGo.transform.SetLocalPositionAndRotation(pos, rot);
                partGo.transform.localScale = scale;

                if (!MaterialHelper.IsImportedModel(partGo))
                    MaterialHelper.Apply(partGo, "Preview Part Material", col);

                ClearPropertyBlocks(partGo);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static Vector3 ResolvePresentationStartPosition(PartPreviewPlacement pp) =>
            new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);

        private static bool HasAuthoredStartPosition(PartPreviewPlacement pp)
        {
            if (pp == null) return false;
            var v = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
            return !Mathf.Approximately(v.x, 0f) || !Mathf.Approximately(v.y, 0f) || !Mathf.Approximately(v.z, 0f);
        }

        private static void ClearPropertyBlocks(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                r.SetPropertyBlock(null);
        }
    }
}
