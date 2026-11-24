#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// Dependency-free Edit Mode checks for projects without a dedicated Unity Test Framework assembly.
    /// Run from Tools > Neuralife > Run Seated Arm Solver Checks.
    /// </summary>
    public static class SeatedArmSolverEditModeChecks
    {
        [MenuItem("Tools/Neuralife/Run Seated Arm Solver Checks")]
        public static void RunAll()
        {
            CheckReachableBoneLengths();
            CheckUnreachableTargetKeepsMeasuredWrist();
            CheckMirroredHands();
            CheckMidlineAndCollinearContinuity();
            CheckInvalidMeasurements();
            Debug.Log("Seated arm solver checks passed.");
        }

        private static void CheckReachableBoneLengths()
        {
            Vector3 shoulder = new Vector3(0.2f, 0.4f, 0f);
            Vector3 wrist = new Vector3(0.52f, 0.12f, 0.24f);
            Require(SeatedArmSolver.TrySolve(
                shoulder, wrist, 0.40f, 0.34f, SeatedHandSide.Right, Vector3.forward,
                out SeatedArmSolver.Solution solution), "A reachable target should solve.");
            Require(solution.ReachStatus == SeatedArmReachStatus.Reachable, "Reachable target status was incorrect.");
            Near(0.40f, Vector3.Distance(solution.ShoulderPositionMeters, solution.ElbowPositionMeters), 0.0001f,
                "Upper arm length was not preserved.");
            Near(0.34f, Vector3.Distance(solution.ElbowPositionMeters, solution.SolvedWristPositionMeters), 0.0001f,
                "Lower arm length was not preserved.");
            Near(0f, solution.DisplayWristSeparationMeters, 0.0001f,
                "A reachable target should have no display wrist separation.");
        }

        private static void CheckUnreachableTargetKeepsMeasuredWrist()
        {
            Vector3 shoulder = Vector3.zero;
            Vector3 measuredWrist = new Vector3(2f, 0f, 0f);
            Require(SeatedArmSolver.TrySolve(
                shoulder, measuredWrist, 0.30f, 0.25f, SeatedHandSide.Right, Vector3.forward,
                out SeatedArmSolver.Solution solution), "An unreachable target should still produce a clamped elbow.");
            Require(solution.ReachStatus == SeatedArmReachStatus.ClampedTooFar,
                "The unreachable target should report ClampedTooFar.");
            Near(0f, Vector3.Distance(measuredWrist, solution.WristPositionMeters), 0f,
                "The measured wrist must remain unchanged.");
            Near(0.55f, solution.SolvedReachMeters, 0.0001f, "The solve endpoint was not clamped to arm reach.");
            Near(1.45f, solution.DisplayWristSeparationMeters, 0.0001f,
                "The gap between the measured wrist and solve endpoint was not reported.");
        }

        private static void CheckMirroredHands()
        {
            Vector3 rightShoulder = new Vector3(0.20f, 0.30f, 0f);
            Vector3 rightWrist = new Vector3(0.51f, 0.08f, 0.22f);
            Vector3 leftShoulder = new Vector3(-rightShoulder.x, rightShoulder.y, rightShoulder.z);
            Vector3 leftWrist = new Vector3(-rightWrist.x, rightWrist.y, rightWrist.z);
            Require(SeatedArmSolver.TrySolve(
                rightShoulder, rightWrist, 0.38f, 0.33f, SeatedHandSide.Right, Vector3.forward,
                out SeatedArmSolver.Solution right), "Right arm should solve.");
            Require(SeatedArmSolver.TrySolve(
                leftShoulder, leftWrist, 0.38f, 0.33f, SeatedHandSide.Left, Vector3.forward,
                out SeatedArmSolver.Solution left), "Left arm should solve.");
            Near(-right.ElbowPositionMeters.x, left.ElbowPositionMeters.x, 0.0001f,
                "Mirrored elbow X coordinates did not mirror.");
            Near(right.ElbowPositionMeters.y, left.ElbowPositionMeters.y, 0.0001f,
                "Mirrored elbow Y coordinates did not match.");
            Near(right.ElbowPositionMeters.z, left.ElbowPositionMeters.z, 0.0001f,
                "Mirrored elbow Z coordinates did not match.");
        }

        private static void CheckMidlineAndCollinearContinuity()
        {
            CheckContinuousSequence(
                new Vector3(0.22f, 0.18f, 0f),
                new[]
                {
                    new Vector3(0.48f, -0.02f, 0.12f),
                    new Vector3(0.30f, -0.02f, 0.12f),
                    new Vector3(0.10f, -0.02f, 0.12f),
                    new Vector3(-0.10f, -0.02f, 0.12f),
                    new Vector3(-0.30f, -0.02f, 0.12f),
                    new Vector3(-0.40f, -0.02f, 0.12f)
                },
                0.38f, 0.34f, Vector3.forward, "midline");

            CheckContinuousSequence(
                new Vector3(0.20f, 0f, 0f),
                new[]
                {
                    new Vector3(0.55f, 0f, 0f),
                    new Vector3(0.35f, 0f, 0f),
                    new Vector3(0.20f, 0f, 0f),
                    new Vector3(0.05f, 0f, 0f),
                    new Vector3(-0.15f, 0f, 0f)
                },
                0.35f, 0.35f, Vector3.right, "collinear");
        }

        private static void CheckContinuousSequence(
            Vector3 shoulder,
            Vector3[] wristPositions,
            float upperLength,
            float lowerLength,
            Vector3 pole,
            string label)
        {
            bool hasPrevious = false;
            Vector3 previousElbow = Vector3.zero;
            for (int i = 0; i < wristPositions.Length; i++)
            {
                bool solved;
                SeatedArmSolver.Solution solution;
                if (hasPrevious)
                {
                    solved = SeatedArmSolver.TrySolve(
                        shoulder, wristPositions[i], upperLength, lowerLength,
                        SeatedHandSide.Right, pole, previousElbow, true, out solution);
                }
                else
                {
                    solved = SeatedArmSolver.TrySolve(
                        shoulder, wristPositions[i], upperLength, lowerLength,
                        SeatedHandSide.Right, pole, out solution);
                }

                Require(solved, "The " + label + " sequence failed to solve at sample " + i + ".");
                if (hasPrevious)
                    Require(Vector3.Distance(previousElbow, solution.ElbowPositionMeters) < 0.30f,
                        "The elbow jumped during the " + label + " sequence at sample " + i + ".");

                Vector3 radial = solution.ElbowPositionMeters - shoulder;
                if (solution.SolvedReachMeters > 0.0001f)
                {
                    Vector3 axis = (solution.SolvedWristPositionMeters - shoulder).normalized;
                    float along = (upperLength * upperLength - lowerLength * lowerLength +
                                   solution.SolvedReachMeters * solution.SolvedReachMeters) /
                                  (2f * solution.SolvedReachMeters);
                    radial -= axis * along;
                }
                Require(Vector3.Dot(radial, Vector3.forward) > 0f,
                    "The elbow bend hemisphere flipped during the " + label + " sequence at sample " + i + ".");

                previousElbow = solution.ElbowPositionMeters;
                hasPrevious = true;
            }
        }

        private static void CheckInvalidMeasurements()
        {
            Require(!SeatedArmSolver.TrySolve(
                Vector3.zero, Vector3.one, 0f, 0.30f, SeatedHandSide.Right, Vector3.forward,
                out SeatedArmSolver.Solution zeroLength), "A zero arm length should be rejected.");
            Require(zeroLength.ReachStatus == SeatedArmReachStatus.InvalidMeasurements,
                "Invalid length should report InvalidMeasurements.");

            Require(!SeatedArmSolver.TrySolve(
                Vector3.zero, new Vector3(float.NaN, 0f, 0f), 0.30f, 0.30f,
                SeatedHandSide.Right, Vector3.forward, out SeatedArmSolver.Solution nonFiniteWrist),
                "A non-finite wrist measurement should be rejected.");
            Require(nonFiniteWrist.ReachStatus == SeatedArmReachStatus.InvalidMeasurements,
                "Invalid wrist should report InvalidMeasurements.");

            Require(!SeatedArmSolver.TrySolve(
                Vector3.zero, Vector3.one, 0.30f, 0.30f, SeatedHandSide.Unspecified, Vector3.forward,
                out SeatedArmSolver.Solution unspecifiedHand), "An unspecified hand should be rejected.");
            Require(unspecifiedHand.ReachStatus == SeatedArmReachStatus.InvalidMeasurements,
                "Invalid handedness should report InvalidMeasurements.");
        }

        private static void Near(float expected, float actual, float tolerance, string message)
        {
            Require(!float.IsNaN(actual) && !float.IsInfinity(actual) &&
                    Mathf.Abs(expected - actual) <= tolerance, message + " Expected " + expected + ", got " + actual + ".");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
#endif
