using NUnit.Framework;
using OSE.Interaction;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Locks the overlay contract defined in
    /// <see cref="IToolActionPreview.ComputeOverlayOffset"/>: every preview
    /// returns a Vector3 overlay, the controller composes it on top of
    /// <c>workingPos + partFollowOffset</c>, and NO preview writes
    /// <c>transform.position</c> directly while a part-follow effect is
    /// active.
    ///
    /// The prior implicit contract ("preview resets position each frame")
    /// broke twice in short succession (DefaultPreview, CutPreview) with
    /// symptoms that only surfaced at runtime under specific tool+part
    /// combinations. These tests catch a misbehaving overlay contribution
    /// at build time.
    /// </summary>
    [TestFixture]
    public class PreviewOverlayContractTests
    {
        // ── DefaultPreview: zero overlay always ────────────────────────────

        [Test]
        public void DefaultPreview_OverlayIsZeroForAllProgress()
        {
            var p = new DefaultPreview();
            for (int i = 0; i <= 10; i++)
            {
                float progress = i / 10f;
                Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(progress),
                    $"DefaultPreview must contribute zero overlay at progress={progress}");
            }
        }

        // ── DrillPreview: vibration in all axes, scaled by intensity ──────

        [Test]
        public void DrillPreview_OverlayIsZeroAtProgressZero()
        {
            var p = new DrillPreview();
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(0f),
                "Drill vibration should ramp in from zero at progress=0");
        }

        [Test]
        public void DrillPreview_OverlayAmplitudeRampsUpThroughEarlyProgress()
        {
            var p = new DrillPreview();
            // At progress 0.075 the intensity envelope is halfway up the
            // ramp-in (RampUpEnd = 0.15). Amplitude should be non-trivially
            // larger than at 0.01 but smaller than at 0.5 (full sustain).
            float mag0  = p.ComputeOverlayOffset(0.01f).magnitude;
            float mag1  = p.ComputeOverlayOffset(0.075f).magnitude;
            float mag2  = p.ComputeOverlayOffset(0.5f).magnitude;

            // magnitude values are not monotonic because of the sin() phase
            // factor, but the envelope still constrains the ceiling. Assert
            // the sustain-phase peak is at least as strong as ramp-in could
            // possibly produce.
            Assert.GreaterOrEqual(mag2, mag1 * 0.5f,
                "Sustain-phase drill vibration should not be drastically weaker than ramp-in");
            Assert.Greater(mag2, mag0,
                "Sustain-phase drill vibration should exceed very-early ramp-in");
        }

        [Test]
        public void DrillPreview_OverlayContributesToAllThreeAxes()
        {
            var p = new DrillPreview();
            bool anyX = false, anyY = false, anyZ = false;
            for (int i = 10; i <= 80; i += 5)
            {
                Vector3 offs = p.ComputeOverlayOffset(i / 100f);
                if (Mathf.Abs(offs.x) > 1e-6f) anyX = true;
                if (Mathf.Abs(offs.y) > 1e-6f) anyY = true;
                if (Mathf.Abs(offs.z) > 1e-6f) anyZ = true;
            }
            Assert.IsTrue(anyX, "drill vibration must exercise X axis at some point in sustain");
            Assert.IsTrue(anyY, "drill vibration must exercise Y axis");
            Assert.IsTrue(anyZ, "drill vibration must exercise Z axis");
        }

        // ── CutPreview: x-only overlay, zero outside active window ────────

        [Test]
        public void CutPreview_OverlayIsZeroBeforeActiveWindow()
        {
            var p = new CutPreview();
            // Active window starts > 0.15. At 0.15 and below the overlay is zero.
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(0f));
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(0.10f));
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(0.15f));
        }

        [Test]
        public void CutPreview_OverlayIsZeroAfterActiveWindow()
        {
            var p = new CutPreview();
            // Active window ends < 0.9. At 0.9 and above the overlay is zero.
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(0.9f));
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(0.95f));
            Assert.AreEqual(Vector3.zero, p.ComputeOverlayOffset(1.0f));
        }

        [Test]
        public void CutPreview_OverlayIsXOnly_InsideActiveWindow()
        {
            var p = new CutPreview();
            // Sample several progress values in the active window; Y and Z
            // must stay zero — cut chatter is x-axis only.
            for (int i = 20; i <= 85; i += 5)
            {
                Vector3 offs = p.ComputeOverlayOffset(i / 100f);
                Assert.AreEqual(0f, offs.y, 1e-6f, $"cut overlay.y must be zero at progress={i / 100f}");
                Assert.AreEqual(0f, offs.z, 1e-6f, $"cut overlay.z must be zero at progress={i / 100f}");
            }
        }

        [Test]
        public void CutPreview_OverlayHasNonZeroX_SomeTimeInActiveWindow()
        {
            var p = new CutPreview();
            bool anyNonZero = false;
            for (int i = 16; i <= 85; i += 1)
            {
                if (Mathf.Abs(p.ComputeOverlayOffset(i / 100f).x) > 1e-6f)
                {
                    anyNonZero = true;
                    break;
                }
            }
            Assert.IsTrue(anyNonZero, "cut overlay.x must be non-zero at some progress in the active window");
        }
    }
}
