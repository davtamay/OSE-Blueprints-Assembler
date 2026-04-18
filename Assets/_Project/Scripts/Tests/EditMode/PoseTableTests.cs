using System.Reflection;
using NUnit.Framework;
using OSE.Content;
using OSE.Content.Loading;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Exercises the <see cref="PoseResolver"/> decision tree by constructing
    /// tiny synthetic packages that isolate each source branch. These tests
    /// are the safety net for Steps 4 &amp; 5 of the pose rewrite — if a
    /// consumer migration regresses resolver behaviour, one of these fires.
    /// The scenarios mirror the real-world bug classes this session hit:
    /// NO-TASK-first parts reaching their task step, author spans with
    /// bounded ranges, integrated subassembly stacking, and the Start vs
    /// Assembled toggle on Required steps.
    /// </summary>
    [TestFixture]
    public class PoseTableTests
    {
        // ──────────────────────────────────────────────────────────────────
        // Scenario builders
        // ──────────────────────────────────────────────────────────────────

        private static SceneFloat3     V(float x, float y, float z) => new SceneFloat3    { x = x, y = y, z = z };
        private static SceneQuaternion Q(float x, float y, float z, float w) => new SceneQuaternion { x = x, y = y, z = z, w = w };

        private static PartPreviewPlacement PP(string id, SceneFloat3 start, SceneFloat3 assembled)
            => new PartPreviewPlacement {
                partId            = id,
                startPosition     = start,     startRotation     = Q(0,0,0,1), startScale     = V(1,1,1),
                assembledPosition = assembled, assembledRotation = Q(0,0,0,1), assembledScale = V(1,1,1),
            };

        private static StepDefinition Step(int seq, string id, string[] required = null, string[] visual = null,
            string requiredSub = null, string[] targetIds = null)
            => new StepDefinition {
                id = id, sequenceIndex = seq,
                requiredPartIds = required, visualPartIds = visual,
                requiredSubassemblyId = requiredSub, targetIds = targetIds,
            };

        // ──────────────────────────────────────────────────────────────────
        // Hidden branch
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_BeforeFirstVisible_ReturnsHidden()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "s1"),
                    Step(2, "s2", required: new[] { "bolt" }),
                },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("bolt", V(1,0,0), V(2,0,0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);
            Assert.IsFalse(pkg.poseTable.TryGet("bolt", 1, out _), "bolt should not appear at seq 1");
            Assert.AreEqual(2, pkg.poseTable.FirstVisibleSeq("bolt"));
        }

        // ──────────────────────────────────────────────────────────────────
        // Start / Assembled on the part's task step
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_TaskStep_CommittedMode_ReturnsAssembled()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] { Step(1, "s1", required: new[] { "bolt" }) },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("bolt", V(10,0,0), V(20,0,0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            Assert.IsTrue(pkg.poseTable.TryGet("bolt", 1, out var r));
            Assert.AreEqual(PoseSource.Assembled, r.source, "Committed mode at task step -> Assembled");
            Assert.AreEqual(new Vector3(20, 0, 0), r.pos);
        }

        [Test]
        public void Resolve_TaskStep_StartPreview_DirectCall_ReturnsStart()
        {
            // The bake always commits to Assembled on task steps. Editor-only
            // Start toggle is a direct Resolve() call, not a table lookup —
            // verify the resolver returns Start when asked explicitly.
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] { Step(1, "s1", required: new[] { "bolt" }) },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("bolt", V(10,0,0), V(20,0,0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            var idx = new PoseResolverIndex(pkg);
            var r = PoseResolver.Resolve("bolt", 1, pkg, idx, PoseMode.StartPreview);
            Assert.AreEqual(PoseSource.Start, r.source);
            Assert.AreEqual(new Vector3(10, 0, 0), r.pos);

            var r2 = PoseResolver.Resolve("bolt", 1, pkg, idx, PoseMode.AssembledPreview);
            Assert.AreEqual(PoseSource.Assembled, r2.source);
            Assert.AreEqual(new Vector3(20, 0, 0), r2.pos);
        }

        // ──────────────────────────────────────────────────────────────────
        // NO-TASK intro: the bug class from Step 50 / Step 54 in d3d_v18_10
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_NoTaskIntroBeforeTask_ShowsStartPositionUntilTaskStep()
        {
            // Step 1: NO TASK intro (visualPartIds)
            // Step 2: idle
            // Step 3: Required task on the part
            // Part must read startPosition at steps 1 & 2, then Committed → Assembled at step 3.
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "layout", visual: new[] { "half_b" }),
                    Step(2, "idle"),
                    Step(3, "close",  required: new[] { "half_b" }),
                },
                parts = new[] { new PartDefinition { id = "half_b" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("half_b", V(0.5228f, 0.55f, 0), V(0.82f, 0.55f, 0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            Assert.IsTrue(pkg.poseTable.TryGet("half_b", 1, out var r1));
            Assert.AreEqual(PoseSource.Start, r1.source);
            Assert.AreEqual(new Vector3(0.5228f, 0.55f, 0), r1.pos);

            Assert.IsTrue(pkg.poseTable.TryGet("half_b", 2, out var r2));
            Assert.AreEqual(PoseSource.Start, r2.source, "non-task step between intro and first task -> still start");
            Assert.AreEqual(new Vector3(0.5228f, 0.55f, 0), r2.pos);

            Assert.IsTrue(pkg.poseTable.TryGet("half_b", 3, out var r3));
            Assert.AreEqual(PoseSource.Assembled, r3.source);
            Assert.AreEqual(new Vector3(0.82f, 0.55f, 0), r3.pos);
        }

        [Test]
        public void Resolve_NoTaskOnly_PartNeverGetsTask_StaysAtStart()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "layout", visual: new[] { "decor" }),
                    Step(2, "other",  required: new[] { "bolt" }),
                },
                parts = new[] {
                    new PartDefinition { id = "decor" },
                    new PartDefinition { id = "bolt" },
                },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] {
                        PP("decor", V(3,0,0), V(9,0,0)),
                        PP("bolt",  V(0,0,0), V(1,0,0)),
                    }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            Assert.IsTrue(pkg.poseTable.TryGet("decor", 1, out var r1));
            Assert.AreEqual(PoseSource.NoTaskStart, r1.source);
            Assert.AreEqual(new Vector3(3,0,0), r1.pos);

            Assert.IsTrue(pkg.poseTable.TryGet("decor", 2, out var r2));
            Assert.AreEqual(PoseSource.NoTaskStart, r2.source, "no task ever claims decor");
            Assert.AreEqual(new Vector3(3,0,0), r2.pos);
        }

        [Test]
        public void Resolve_AfterLastTask_StaysAtAssembled()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "place", required: new[] { "bolt" }),
                    Step(2, "idle"),
                    Step(3, "idle2"),
                },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("bolt", V(0,0,0), V(5,0,0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            Assert.IsTrue(pkg.poseTable.TryGet("bolt", 3, out var r3));
            Assert.AreEqual(PoseSource.Assembled, r3.source);
            Assert.AreEqual(new Vector3(5,0,0), r3.pos);
        }

        // ──────────────────────────────────────────────────────────────────
        // Explicit author-written stepPose spans
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_ExplicitSpan_CoversInsideRange_WinsOverDefault()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "s1", required: new[] { "bolt" }),
                    Step(2, "s2"),
                    Step(3, "s3"),
                },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] {
                        new PartPreviewPlacement {
                            partId            = "bolt",
                            startPosition     = V(0,0,0), startRotation = Q(0,0,0,1), startScale = V(1,1,1),
                            assembledPosition = V(5,0,0), assembledRotation = Q(0,0,0,1), assembledScale = V(1,1,1),
                            stepPoses = new[] {
                                new StepPoseEntry {
                                    stepId = "s2", label = "mid",
                                    position = V(99,0,0), rotation = Q(0,0,0,1), scale = V(1,1,1),
                                    propagateFromStep = "s2", propagateThroughStep = "s2",
                                }
                            }
                        }
                    }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            // Step 1: task step -> Assembled (Committed mode).
            Assert.AreEqual(PoseSource.Assembled, pkg.poseTable.GetOrHidden("bolt", 1).source);
            // Step 2: explicit span wins over Assembled fallthrough.
            Assert.IsTrue(pkg.poseTable.TryGet("bolt", 2, out var r2));
            Assert.AreEqual(PoseSource.ExplicitSpan, r2.source);
            Assert.AreEqual(new Vector3(99,0,0), r2.pos);
            // Step 3: span doesn't reach — back to Assembled.
            Assert.AreEqual(PoseSource.Assembled, pkg.poseTable.GetOrHidden("bolt", 3).source);
        }

        [Test]
        public void Resolve_ExplicitSpan_BoundedThrough_StopsCovering()
        {
            // Mirrors the d3d_v18_10 step 54 bug: a synthetic NO-TASK waypoint
            // at step 50 must NOT cover step 54. With the rewrite, the bake's
            // NO-TASK source is direct and bounded — no synthetic needed.
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(50, "layout", visual: new[] { "half_b" }),
                    Step(51, "a"),
                    Step(52, "b"),
                    Step(53, "c"),
                    Step(54, "close", required: new[] { "half_b" }),
                },
                parts = new[] { new PartDefinition { id = "half_b" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("half_b", V(0.5228f, 0.55f, 0), V(0.82f, 0.55f, 0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            for (int seq = 50; seq <= 53; seq++)
            {
                Assert.IsTrue(pkg.poseTable.TryGet("half_b", seq, out var r), $"half_b missing at {seq}");
                Assert.AreEqual(PoseSource.Start, r.source, $"half_b @{seq} should be Start");
                Assert.AreEqual(new Vector3(0.5228f, 0.55f, 0), r.pos);
            }
            Assert.IsTrue(pkg.poseTable.TryGet("half_b", 54, out var r54));
            Assert.AreEqual(PoseSource.Assembled, r54.source, "task step 54 -> Assembled");
            Assert.AreEqual(new Vector3(0.82f, 0.55f, 0), r54.pos);
        }

        // ──────────────────────────────────────────────────────────────────
        // Integrated subassembly member
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_IntegratedMember_AfterStackingStep_UsesMemberPlacement()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "fab", required: new[] { "half_a" }),
                    Step(2, "stack", requiredSub: "carriage", targetIds: new[] { "cube_top" }),
                    Step(3, "idle"),
                },
                parts = new[] { new PartDefinition { id = "half_a" } },
                subassemblies = new[] {
                    new SubassemblyDefinition {
                        id = "carriage",
                        partIds = new[] { "half_a" },
                    },
                },
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("half_a", V(0,0,0), V(1,0,0)) },
                    integratedSubassemblyPlacements = new[] {
                        new IntegratedSubassemblyPreviewPlacement {
                            subassemblyId = "carriage",
                            targetId = "cube_top",
                            memberPlacements = new[] {
                                new IntegratedMemberPreviewPlacement {
                                    partId = "half_a",
                                    position = V(7,7,7), rotation = Q(0,0,0,1), scale = V(1,1,1),
                                },
                            },
                        },
                    },
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            // Before stacking: own task step -> Assembled (step 1).
            Assert.AreEqual(PoseSource.Assembled, pkg.poseTable.GetOrHidden("half_a", 1).source);

            // At and after stacking step -> IntegratedMember at authored pose.
            Assert.IsTrue(pkg.poseTable.TryGet("half_a", 2, out var r2));
            Assert.AreEqual(PoseSource.IntegratedMember, r2.source);
            Assert.AreEqual(new Vector3(7,7,7), r2.pos);

            Assert.IsTrue(pkg.poseTable.TryGet("half_a", 3, out var r3));
            Assert.AreEqual(PoseSource.IntegratedMember, r3.source);
            Assert.AreEqual(new Vector3(7,7,7), r3.pos);
        }

        // ──────────────────────────────────────────────────────────────────
        // Clone safety — CloneStepPoseEntry used to silently drop
        // propagateFromStep / propagateThroughStep
        // ──────────────────────────────────────────────────────────────────

        // ──────────────────────────────────────────────────────────────────
        // StepPoseEntry.Clone must preserve EVERY serialized field.
        // Guards against the bug that silently dropped
        // propagateFromStep/propagateThroughStep from hand-rolled clones and
        // kicked off the whole pose rewrite.
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void StepPoseEntry_Clone_PreservesEverySerializedField()
        {
            var original = new StepPoseEntry {
                stepId               = "s_anchor",
                label                = "authored",
                position             = V(1.1f, 2.2f, 3.3f),
                rotation             = Q(0.1f, 0.2f, 0.3f, 0.4f),
                scale                = V(4.4f, 5.5f, 6.6f),
                propagateFromStep    = "s_from",
                propagateThroughStep = "s_through",
            };
            var clone = original.Clone();

            Assert.AreNotSame(original, clone, "Clone must return a new instance");

            // Reflection-checked so adding a new field to StepPoseEntry later
            // and forgetting to update Clone fails loudly.
            foreach (var f in typeof(StepPoseEntry).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object originalValue = f.GetValue(original);
                object cloneValue    = f.GetValue(clone);
                Assert.AreEqual(originalValue, cloneValue, $"StepPoseEntry.Clone dropped field '{f.Name}'");
            }
        }

        [Test]
        public void ResolverIndex_PreservesSpanBoundsFromRawEntry()
        {
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "a"),
                    Step(2, "b"),
                    Step(3, "c"),
                },
                parts = new[] { new PartDefinition { id = "p" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] {
                        new PartPreviewPlacement {
                            partId            = "p",
                            startPosition     = V(0,0,0), startRotation = Q(0,0,0,1), startScale = V(1,1,1),
                            assembledPosition = V(5,0,0), assembledRotation = Q(0,0,0,1), assembledScale = V(1,1,1),
                            stepPoses = new[] {
                                new StepPoseEntry {
                                    stepId = "a", label = "authored",
                                    position = V(9,0,0), rotation = Q(0,0,0,1), scale = V(1,1,1),
                                    propagateFromStep = "a", propagateThroughStep = "b",
                                }
                            }
                        }
                    }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            // seq 3 is outside [1..2] — must fall through to non-ExplicitSpan.
            Assert.AreNotEqual(PoseSource.ExplicitSpan, pkg.poseTable.GetOrHidden("p", 3).source,
                "span bounds must be respected — if they leaked, seq 3 would still be ExplicitSpan");
        }

        // ──────────────────────────────────────────────────────────────────
        // holdAtEnd synthesis — authored spans win, synth skips, no
        // invariant violation. Guards the class of bugs the three G.2.5.b
        // attempts all hit.
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_HoldAtEnd_AuthoredSpanCoveringAnchor_Wins()
        {
            // Sub with one member part. A poseTransition cue with holdAtEnd=true
            // at step 1 would synthesize a stepPose on member at step 2 (the
            // next step). An authored stepPose on the member covering step 2
            // must win — the synth is skipped with a warning, no invariant
            // throw.
            var cue = new AnimationCueEntry {
                type      = "poseTransition",
                stepIds   = new[] { "s1" },
                holdAtEnd = true,
                fromPose  = new AnimationPose { rotation = Q(0, 0, 0, 1) },
                toPose    = new AnimationPose {
                    position = V(0,0,0),
                    rotation = Q(0, 0.7071f, 0, 0.7071f),   // 90° about Y
                    scale    = V(1,1,1),
                },
            };
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] {
                    Step(1, "s1", required: new[] { "half_a" }),
                    Step(2, "s2"),
                    Step(3, "s3"),
                },
                parts = new[] { new PartDefinition { id = "half_a" } },
                subassemblies = new[] {
                    new SubassemblyDefinition {
                        id            = "carriage",
                        partIds       = new[] { "half_a" },
                        animationCues = new[] { cue },
                    }
                },
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] {
                        new PartPreviewPlacement {
                            partId            = "half_a",
                            startPosition     = V(0,0,0),        startRotation     = Q(0,0,0,1), startScale     = V(1,1,1),
                            assembledPosition = V(1,0,0),        assembledRotation = Q(0,0,0,1), assembledScale = V(1,1,1),
                            stepPoses = new[] {
                                new StepPoseEntry {
                                    stepId = "s2", label = "authored_after_cue",
                                    position = V(42, 0, 0), rotation = Q(0, 0, 0, 1), scale = V(1,1,1),
                                    propagateFromStep = "s2", propagateThroughStep = "s2",
                                }
                            }
                        }
                    }
                }
            };

            // Must not throw invariant violation (synth-vs-authored overlap
            // is the false-positive class fixed in commit 212f381).
            Assert.DoesNotThrow(() => MachinePackageNormalizer.Normalize(pkg),
                "holdAtEnd + authored span covering same anchor must not throw");

            Assert.IsTrue(pkg.poseTable.TryGet("half_a", 2, out var r2));
            Assert.AreEqual(PoseSource.ExplicitSpan, r2.source,
                "authored span at s2 wins over synthesized holdAtEnd");
            Assert.AreEqual(new Vector3(42, 0, 0), r2.pos,
                "authored pose wins — synth must be skipped, not merged");
        }

        // ──────────────────────────────────────────────────────────────────
        // Tool-part effect self-step lookup (H.4 guard).
        // ToolActionExecutor.ResolveEndPose currently calls
        // spawner.FindPartStepPose(partId, currentStepId). H.4 replaces
        // both call sites with poseTable.TryGet(partId, currentStepSeq).
        // This test fixes the contract: at the tool's own step, the
        // resolver must return the same pose either way —
        //   • explicit stepPose covering the self-step → ExplicitSpan pose
        //   • no stepPose at the self-step → Assembled pose
        // ──────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_ToolActionSelfStep_ExplicitSpan_WinsOverAssembled()
        {
            // Bolt is assembled at seq 1 (Committed → Assembled).
            // An explicit stepPose on seq 1 covering itself overrides.
            // H.4: poseTable.TryGet("bolt", 1) must return the authored
            //   (99,0,0), not the assembled (20,0,0).
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] { Step(1, "s1", required: new[] { "bolt" }) },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] {
                        new PartPreviewPlacement {
                            partId            = "bolt",
                            startPosition     = V(10,0,0), startRotation = Q(0,0,0,1), startScale = V(1,1,1),
                            assembledPosition = V(20,0,0), assembledRotation = Q(0,0,0,1), assembledScale = V(1,1,1),
                            stepPoses = new[] {
                                new StepPoseEntry {
                                    stepId = "s1", label = "intermediate",
                                    position = V(99, 0, 0), rotation = Q(0,0,0,1), scale = V(1,1,1),
                                    propagateFromStep = "s1", propagateThroughStep = "s1",
                                }
                            }
                        }
                    }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            Assert.IsTrue(pkg.poseTable.TryGet("bolt", 1, out var r1));
            Assert.AreEqual(PoseSource.ExplicitSpan, r1.source,
                "self-step explicit span must win over Committed→Assembled " +
                "(H.4 contract: poseTable.TryGet at the tool's step preserves " +
                "the legacy FindPartStepPose(partId, currentStepId) result).");
            Assert.AreEqual(new Vector3(99, 0, 0), r1.pos);
        }

        [Test]
        public void Resolve_ToolActionSelfStep_NoStepPose_ReturnsAssembled()
        {
            // Bolt is assembled at seq 1 with no authored stepPose.
            // H.4: poseTable.TryGet("bolt", 1) must return Assembled — this
            // is the fallback branch of the legacy implicit chain
            // (FindPartStepPose returns null → assembledPosition).
            var pkg = new MachinePackageDefinition {
                machine = new MachineDefinition { id = "m" },
                steps = new[] { Step(1, "s1", required: new[] { "bolt" }) },
                parts = new[] { new PartDefinition { id = "bolt" } },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                previewConfig = new PackagePreviewConfig {
                    partPlacements = new[] { PP("bolt", V(10, 0, 0), V(20, 0, 0)) }
                }
            };
            MachinePackageNormalizer.Normalize(pkg);

            Assert.IsTrue(pkg.poseTable.TryGet("bolt", 1, out var r1));
            Assert.AreEqual(PoseSource.Assembled, r1.source,
                "no stepPose on self-step → Assembled (legacy fallback contract).");
            Assert.AreEqual(new Vector3(20, 0, 0), r1.pos);
        }
    }
}
