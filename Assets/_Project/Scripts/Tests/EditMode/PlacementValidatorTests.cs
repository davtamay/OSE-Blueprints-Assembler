using NUnit.Framework;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Unit tests for <see cref="PlacementValidator"/>.
    /// Verifies position/rotation tolerance boundaries and auto-snap logic.
    /// </summary>
    [TestFixture]
    public sealed class PlacementValidatorTests
    {
        private PlacementValidator _validator;

        [SetUp]
        public void SetUp()
        {
            _validator = new PlacementValidator();
        }

        // ── Exact match ───────────────────────────────────────────────────────

        [Test]
        public void Validate_ExactMatch_IsValid()
        {
            var request = BuildRequest(
                candidatePos: Vector3.zero,
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.identity,
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_ExactMatch_AutoSnapAllowed_IsFalse()
        {
            var request = BuildRequest(
                candidatePos: Vector3.zero,
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.identity,
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            // No snap needed when error is zero.
            Assert.IsFalse(result.AutoSnapAllowed);
        }

        // ── Position tolerance boundary ───────────────────────────────────────

        [Test]
        public void Validate_PositionWithinTolerance_IsValid()
        {
            // 3 mm error, 5 mm tolerance → valid
            var request = BuildRequest(
                candidatePos: new Vector3(0.003f, 0f, 0f),  // 3 mm offset
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.identity,
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_PositionExceedsTolerance_IsInvalid()
        {
            // 10 mm error, 5 mm tolerance → invalid
            var request = BuildRequest(
                candidatePos: new Vector3(0.01f, 0f, 0f),   // 10 mm offset
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.identity,
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationFailureReason.PositionOutOfTolerance, result.FailureReason);
        }

        [Test]
        public void Validate_PositionExceedsTolerance_AutoSnapAllowed_IsFalse()
        {
            var request = BuildRequest(
                candidatePos: new Vector3(0.1f, 0f, 0f),
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.identity,
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsFalse(result.AutoSnapAllowed);
        }

        // ── Rotation tolerance boundary ───────────────────────────────────────

        [Test]
        public void Validate_RotationWithinTolerance_IsValid()
        {
            // 3° error, 5° tolerance → valid
            var request = BuildRequest(
                candidatePos: Vector3.zero,
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.Euler(3f, 0f, 0f),
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_RotationExceedsTolerance_IsInvalid()
        {
            // 15° error, 5° tolerance → invalid
            var request = BuildRequest(
                candidatePos: Vector3.zero,
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.Euler(15f, 0f, 0f),
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationFailureReason.RotationOutOfTolerance, result.FailureReason);
        }

        // ── AutoSnap flag ─────────────────────────────────────────────────────

        [Test]
        public void Validate_WithinTolerance_ButNonZeroError_AutoSnapAllowed_IsTrue()
        {
            // 2 mm off, within 5 mm tolerance → valid but snap should be applied
            var request = BuildRequest(
                candidatePos: new Vector3(0.002f, 0f, 0f),
                expectedPos:  Vector3.zero,
                candidateRot: Quaternion.identity,
                expectedRot:  Quaternion.identity,
                posToleranceMm: 5f,
                rotToleranceDeg: 5f);

            var result = _validator.Validate(request);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.AutoSnapAllowed);
        }

        // ── ValidateExact convenience method ─────────────────────────────────

        [Test]
        public void ValidateExact_IsValid()
        {
            var result = PlacementValidator.ValidateExact();

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void ValidateExact_AutoSnapAllowed_IsTrue()
        {
            var result = PlacementValidator.ValidateExact();

            Assert.IsTrue(result.AutoSnapAllowed);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static PlacementValidationRequest BuildRequest(
            Vector3    candidatePos,
            Vector3    expectedPos,
            Quaternion candidateRot,
            Quaternion expectedRot,
            float      posToleranceMm,
            float      rotToleranceDeg)
        {
            return new PlacementValidationRequest(
                expectedPartId:              "test_part",
                expectedTargetAnchorId:      "test_target",
                candidatePosition:           candidatePos,
                candidateRotation:           candidateRot,
                expectedPosition:            expectedPos,
                expectedRotation:            expectedRot,
                positionTolerance:           posToleranceMm,
                rotationToleranceDegrees:    rotToleranceDeg,
                requiredPrerequisiteStepIds: System.Array.Empty<string>(),
                physicalSubstitutionAllowed: false,
                validationMode:              ValidationMode.GuidedStandard);
        }
    }
}
