using NUnit.Framework;
using OSE.Content;
using OSE.Editor;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Regression coverage for the load-time stepPose heal pass —
    /// see <see cref="StepPoseLoadHealer.RescueEmptyLabelStepPoses"/>.
    /// Born from a 10-chat debug where empty-label entries auto-captured
    /// by <c>CaptureCurrentPoseAsStepPose</c> were silently wiped on every
    /// recompile because the legacy strip pass treated empty-label as
    /// "legacy artifact, delete." See
    /// <c>feedback_steppose_label_required_for_persistence.md</c> and
    /// <c>feedback_load_time_mutations_must_be_additive.md</c>.
    ///
    /// Three scenarios pinned here:
    /// 1. Empty-label entry whose (partId, stepId) backs a current
    ///    visualPartIds membership → rescued (label promoted to "Custom").
    /// 2. Empty-label entry that DOES NOT back any visualPartIds → kept
    ///    on disk + counted as orphan (no destructive delete; previous
    ///    impl deleted, which lost author data).
    /// 3. Already-labelled entry (label="Custom" or any non-empty) → left
    ///    untouched.
    ///
    /// If anyone in the future adds a stripping branch back, scenario 2
    /// will fail and surface the regression before it hits authoring.
    /// </summary>
    [TestFixture]
    public class StepPoseLoadHealerTests
    {
        private static SceneFloat3     V(float x, float y, float z) => new SceneFloat3 { x = x, y = y, z = z };
        private static SceneQuaternion Q() => new SceneQuaternion { x = 0, y = 0, z = 0, w = 1 };

        private static MachinePackageDefinition BuildPackage(
            string partId,
            string stepId,
            string entryLabel,
            bool partInVisualPartIds)
        {
            var pkg = new MachinePackageDefinition
            {
                parts = new[] { new PartDefinition { id = partId } },
                steps = new[]
                {
                    new StepDefinition
                    {
                        id = stepId,
                        sequenceIndex = 1,
                        visualPartIds = partInVisualPartIds ? new[] { partId } : new string[0],
                    },
                },
                previewConfig = new PackagePreviewConfig
                {
                    partPlacements = new[]
                    {
                        new PartPreviewPlacement
                        {
                            partId = partId,
                            startPosition = V(0,0,0), startRotation = Q(), startScale = V(1,1,1),
                            assembledPosition = V(0,0,0), assembledRotation = Q(), assembledScale = V(1,1,1),
                            stepPoses = new[]
                            {
                                new StepPoseEntry
                                {
                                    stepId = stepId,
                                    label  = entryLabel,
                                    position = V(1,2,3), rotation = Q(), scale = V(1,1,1),
                                },
                            },
                        },
                    },
                },
            };
            return pkg;
        }

        [Test]
        public void RescueEmptyLabel_BackedByVisualPartIds_PromotesToCustom()
        {
            var pkg = BuildPackage("idler001", "step_layout", entryLabel: "", partInVisualPartIds: true);

            var (rescued, orphans) = StepPoseLoadHealer.RescueEmptyLabelStepPoses(pkg);

            Assert.AreEqual(1, rescued, "Should rescue the empty-label entry that backs visualPartIds.");
            Assert.AreEqual(0, orphans, "No orphans expected — the only entry has visualPartIds backing.");

            var entry = pkg.previewConfig.partPlacements[0].stepPoses[0];
            Assert.AreEqual("Custom", entry.label, "Rescued entry's label should be promoted to 'Custom'.");
            Assert.AreEqual(1, pkg.previewConfig.partPlacements[0].stepPoses.Length,
                "Entry must remain in the array; rescue is in-place promotion, not delete-and-readd.");
        }

        [Test]
        public void RescueEmptyLabel_NotBackedByVisualPartIds_KeptOnDiskAsOrphan()
        {
            var pkg = BuildPackage("orphan_part", "step_layout", entryLabel: "", partInVisualPartIds: false);

            var (rescued, orphans) = StepPoseLoadHealer.RescueEmptyLabelStepPoses(pkg);

            Assert.AreEqual(0, rescued, "Nothing to rescue — entry has no visualPartIds backing.");
            Assert.AreEqual(1, orphans, "Orphan entry should be counted (logged at call site).");

            // Critical invariant: orphan entries are NOT deleted. Previous
            // implementations stripped them, which destroyed legitimate
            // auto-captures whose label happened to be empty.
            Assert.AreEqual(1, pkg.previewConfig.partPlacements[0].stepPoses.Length,
                "Orphan entries must be PRESERVED on disk. Load-time mutations must be additive, not destructive.");
            Assert.AreEqual("", pkg.previewConfig.partPlacements[0].stepPoses[0].label,
                "Orphan entry's label should remain empty (not be promoted) — only NoTask-backed entries get the rescue treatment.");
        }

        [Test]
        public void RescueEmptyLabel_AlreadyLabelled_Untouched()
        {
            var pkg = BuildPackage("idler001", "step_layout", entryLabel: "Custom", partInVisualPartIds: true);

            var (rescued, orphans) = StepPoseLoadHealer.RescueEmptyLabelStepPoses(pkg);

            Assert.AreEqual(0, rescued, "Already-labelled entry should not be counted as rescued.");
            Assert.AreEqual(0, orphans, "Already-labelled entry is not an orphan.");

            Assert.AreEqual("Custom", pkg.previewConfig.partPlacements[0].stepPoses[0].label,
                "Existing label must be preserved verbatim — no overwrite.");
        }

        [Test]
        public void RescueEmptyLabel_NullPackage_NoOp()
        {
            // Defensive: caller might invoke before _pkg is loaded.
            var (rescued, orphans) = StepPoseLoadHealer.RescueEmptyLabelStepPoses(null);
            Assert.AreEqual(0, rescued);
            Assert.AreEqual(0, orphans);
        }

        [Test]
        public void RescueEmptyLabel_PackageWithoutPreviewConfig_NoOp()
        {
            var pkg = new MachinePackageDefinition { parts = new PartDefinition[0], steps = new StepDefinition[0] };
            var (rescued, orphans) = StepPoseLoadHealer.RescueEmptyLabelStepPoses(pkg);
            Assert.AreEqual(0, rescued);
            Assert.AreEqual(0, orphans);
        }
    }
}
