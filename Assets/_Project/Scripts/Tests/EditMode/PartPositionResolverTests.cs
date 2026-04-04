using System.Collections.Generic;
using NUnit.Framework;
using OSE.Content;
using OSE.UI.Root;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Contract tests for <see cref="PartPositionResolver"/> layout math.
    /// Exercises both the arc (play-mode) and fallback (edit-mode) positioning paths.
    /// </summary>
    [TestFixture]
    public class PartPositionResolverTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private GameObject MakePart(string name = "Part")
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;
            return go;
        }

        private static PartPreviewPlacement PlacementAt(float x, float y, float z) =>
            new PartPreviewPlacement
            {
                partId        = "test",
                startPosition = new SceneFloat3 { x = x, y = y, z = z },
                startScale    = new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                startRotation = new SceneQuaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                color         = new SceneFloat4 { r = 1f, g = 1f, b = 1f, a = 1f }
            };

        // ── Layout constant regression tests ─────────────────────────────────

        [Test]
        public void LayoutRadius_Is_Expected_Value()
            => Assert.AreEqual(3.8f, PartPositionResolver.LayoutRadius, 1e-6f);

        [Test]
        public void LayoutY_Is_Expected_Value()
            => Assert.AreEqual(0.55f, PartPositionResolver.LayoutY, 1e-6f);

        [Test]
        public void LayoutArcDegrees_Is_Expected_Value()
            => Assert.AreEqual(220f, PartPositionResolver.LayoutArcDegrees, 1e-6f);

        // ── PositionPartsFallback: no authored placement (linear grid) ────────

        [Test]
        public void PositionPartsFallback_NullPlacement_FirstPart_UsesLinearGrid()
        {
            var part = MakePart("PartA");
            part.transform.localPosition = new Vector3(99f, 99f, 99f);

            PartPositionResolver.PositionPartsFallback(
                new[] { part },
                isPlaying: false,
                findPartPlacement: _ => null,
                shouldPreserveTransform: _ => false);

            // Linear grid formula: x = -2 + index * 1.5, y = 0.55, z = 0
            Assert.AreEqual(-2f,  part.transform.localPosition.x, 1e-4f, "x");
            Assert.AreEqual(0.55f, part.transform.localPosition.y, 1e-4f, "y");
            Assert.AreEqual(0f,   part.transform.localPosition.z, 1e-4f, "z");
        }

        [Test]
        public void PositionPartsFallback_NullPlacement_SecondPart_OffsetByGridStep()
        {
            var p0 = MakePart("P0");
            var p1 = MakePart("P1");

            PartPositionResolver.PositionPartsFallback(
                new[] { p0, p1 },
                isPlaying: false,
                findPartPlacement: _ => null,
                shouldPreserveTransform: _ => false);

            float expected1X = -2f + 1 * 1.5f;
            Assert.AreEqual(expected1X, p1.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(0.55f,      p1.transform.localPosition.y, 1e-4f);
        }

        // ── PositionPartsFallback: authored position ──────────────────────────

        [Test]
        public void PositionPartsFallback_AuthoredPosition_UsesExactAuthoredValues()
        {
            var part = MakePart("PartB");

            PartPositionResolver.PositionPartsFallback(
                new[] { part },
                isPlaying: false,
                findPartPlacement: _ => PlacementAt(1.5f, 0.8f, -2.3f),
                shouldPreserveTransform: _ => false);

            Assert.AreEqual(1.5f,  part.transform.localPosition.x, 1e-4f, "x");
            Assert.AreEqual(0.8f,  part.transform.localPosition.y, 1e-4f, "y");
            Assert.AreEqual(-2.3f, part.transform.localPosition.z, 1e-4f, "z");
        }

        // ── shouldPreserveTransform guard ─────────────────────────────────────

        [Test]
        public void PositionPartsFallback_ShouldPreserveTransform_SkipsPositionUpdate()
        {
            var part = MakePart("Fixed");
            part.transform.localPosition = new Vector3(5f, 5f, 5f);

            PartPositionResolver.PositionPartsFallback(
                new[] { part },
                isPlaying: false,
                findPartPlacement: _ => null,          // would move part to linear grid
                shouldPreserveTransform: _ => true);   // but preserve flag blocks it

            Assert.AreEqual(new Vector3(5f, 5f, 5f), part.transform.localPosition,
                "Transform must not change when shouldPreserveTransform returns true.");
        }

        // ── showGeometryPreview early-exit ────────────────────────────────────

        [Test]
        public void PositionParts_ShowGeometryPreviewFalse_LeavesTransformsUnchanged()
        {
            var part = MakePart("Geo");
            part.transform.localPosition = new Vector3(7f, 7f, 7f);

            PartPositionResolver.PositionParts(
                new[] { part },
                pkg:                   null,
                isPlaying:             true,
                showGeometryPreview:   false,
                findPartPlacement:     _ => null,
                shouldPreserveTransform: _ => false);

            Assert.AreEqual(new Vector3(7f, 7f, 7f), part.transform.localPosition,
                "showGeometryPreview=false must skip all positioning.");
        }

        // ── Arc path (play mode) ──────────────────────────────────────────────

        [Test]
        public void PositionParts_IsPlaying_NullPkg_FallsBackToFallback()
        {
            // pkg=null forces the fallback path even when isPlaying=true.
            var part = MakePart("ArcPart");
            part.transform.localPosition = new Vector3(99f, 99f, 99f);

            PartPositionResolver.PositionParts(
                new[] { part },
                pkg:                   null,
                isPlaying:             true,
                showGeometryPreview:   true,
                findPartPlacement:     _ => null,
                shouldPreserveTransform: _ => false);

            // Fallback linear grid: index 0 → (-2, 0.55, 0)
            Assert.AreEqual(-2f,   part.transform.localPosition.x, 1e-4f, "x");
            Assert.AreEqual(0.55f, part.transform.localPosition.y, 1e-4f, "y");
        }

        [Test]
        public void PositionParts_SinglePart_PlacedAtLayoutY()
        {
            var part = MakePart("ArcSingle");

            var pkg = new MachinePackageDefinition
            {
                packageId = "test_pkg",
                parts     = new[] { new PartDefinition { id = "ArcSingle", assetRef = "foo.glb" } }
            };

            PartPositionResolver.PositionParts(
                new[] { part },
                pkg:                   pkg,
                isPlaying:             true,
                showGeometryPreview:   true,
                findPartPlacement:     _ => null,
                shouldPreserveTransform: _ => false);

            Assert.AreEqual(PartPositionResolver.LayoutY, part.transform.localPosition.y, 1e-4f,
                "Arc-placed part must sit at LayoutY.");
        }

        [Test]
        public void PositionParts_SinglePart_PlacedAtLayoutRadius()
        {
            var part = MakePart("ArcRadius");

            var pkg = new MachinePackageDefinition
            {
                packageId = "test_pkg",
                parts     = new[] { new PartDefinition { id = "ArcRadius", assetRef = "bar.glb" } }
            };

            PartPositionResolver.PositionParts(
                new[] { part },
                pkg:                   pkg,
                isPlaying:             true,
                showGeometryPreview:   true,
                findPartPlacement:     _ => null,
                shouldPreserveTransform: _ => false);

            var pos   = part.transform.localPosition;
            float xzDist = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);

            // Allow a small tolerance — scale adjustments can shift the radius slightly.
            Assert.AreEqual(PartPositionResolver.LayoutRadius, xzDist, 0.5f,
                "Single arc part XZ distance should approximate LayoutRadius.");
        }

        [Test]
        public void PositionParts_TwoParts_SameAssetRef_ClusterTogether()
        {
            var p0 = MakePart("Part_A");
            var p1 = MakePart("Part_B");

            // Both parts reference the same GLB → same group → should be close to each other.
            var pkg = new MachinePackageDefinition
            {
                packageId = "test_pkg",
                parts     = new[]
                {
                    new PartDefinition { id = "Part_A", assetRef = "shared.glb" },
                    new PartDefinition { id = "Part_B", assetRef = "shared.glb" }
                }
            };

            PartPositionResolver.PositionParts(
                new[] { p0, p1 },
                pkg:                   pkg,
                isPlaying:             true,
                showGeometryPreview:   true,
                findPartPlacement:     _ => null,
                shouldPreserveTransform: _ => false);

            float dist = Vector3.Distance(p0.transform.localPosition, p1.transform.localPosition);
            Assert.Less(dist, 2f, "Parts sharing an assetRef should cluster within ~2m.");
        }
    }
}
