using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    public enum SeatedArmReachStatus
    {
        InvalidMeasurements,
        Reachable,
        ClampedTooNear,
        ClampedTooFar
    }

    /// <summary>
    /// Stateless two-bone arm solver. Positions, pole directions, and lengths use metres in a body-space
    /// frame where +X points to the seated person's right. The measured wrist is always returned intact;
    /// only the endpoint used to solve the elbow is clamped.
    /// </summary>
    public static class SeatedArmSolver
    {
        private const float MinimumArmLengthMeters = 0.0001f;
        private const float DirectionEpsilonMeters = 0.000001f;
        private const float PoleProjectionEpsilonSquared = 0.000001f;
        private const float ReachToleranceMeters = 0.00001f;

        public struct Solution
        {
            public SeatedArmReachStatus ReachStatus;
            public Vector3 ShoulderPositionMeters;
            public Vector3 ElbowPositionMeters;
            /// <summary>The measured wrist position. This is never replaced with the clamped solve endpoint.</summary>
            public Vector3 WristPositionMeters;
            /// <summary>The reachable endpoint used for the two-bone elbow calculation.</summary>
            public Vector3 SolvedWristPositionMeters;
            public float ShoulderToWristMeters;
            public float SolvedReachMeters;
            /// <summary>Distance between the measured wrist and the clamped solve endpoint.</summary>
            public float DisplayWristSeparationMeters;

            public bool IsValid { get { return ReachStatus != SeatedArmReachStatus.InvalidMeasurements; } }
            public bool IsReachable { get { return ReachStatus == SeatedArmReachStatus.Reachable; } }
        }

        /// <summary>Solves using the stable pole alone. Use the continuity overload for animated targets.</summary>
        public static bool TrySolve(
            Vector3 shoulderPositionMeters,
            Vector3 measuredWristPositionMeters,
            float upperArmLengthMeters,
            float lowerArmLengthMeters,
            SeatedHandSide handedness,
            Vector3 stableBodySpaceElbowPole,
            out Solution solution)
        {
            return TrySolve(
                shoulderPositionMeters,
                measuredWristPositionMeters,
                upperArmLengthMeters,
                lowerArmLengthMeters,
                handedness,
                stableBodySpaceElbowPole,
                Vector3.zero,
                false,
                out solution);
        }

        /// <summary>
        /// Solves the elbow while using the previous elbow position to keep the bend hemisphere stable
        /// near collinear poses and while a wrist crosses the body's midline. The method remains pure:
        /// the continuity hint is an explicit input and identical inputs always produce identical output.
        /// </summary>
        public static bool TrySolve(
            Vector3 shoulderPositionMeters,
            Vector3 measuredWristPositionMeters,
            float upperArmLengthMeters,
            float lowerArmLengthMeters,
            SeatedHandSide handedness,
            Vector3 stableBodySpaceElbowPole,
            Vector3 previousElbowPositionMeters,
            bool hasPreviousElbow,
            out Solution solution)
        {
            solution = InvalidSolution();

            if (!IsFinite(shoulderPositionMeters) ||
                !IsFinite(measuredWristPositionMeters) ||
                !IsFinite(stableBodySpaceElbowPole) ||
                !IsFinite(upperArmLengthMeters) || upperArmLengthMeters <= MinimumArmLengthMeters ||
                !IsFinite(lowerArmLengthMeters) || lowerArmLengthMeters <= MinimumArmLengthMeters ||
                (handedness != SeatedHandSide.Left && handedness != SeatedHandSide.Right))
                return false;

            if (hasPreviousElbow &&
                (!IsFinite(previousElbowPositionMeters) ||
                 !IsFinite(previousElbowPositionMeters - shoulderPositionMeters)))
                return false;

            if (!TryGetDistance(shoulderPositionMeters, measuredWristPositionMeters, out float measuredDistanceMeters))
                return false;

            double maximumReachDouble = (double)upperArmLengthMeters + lowerArmLengthMeters;
            double minimumReachDouble = Math.Abs((double)upperArmLengthMeters - lowerArmLengthMeters);
            if (double.IsNaN(maximumReachDouble) || double.IsInfinity(maximumReachDouble) ||
                maximumReachDouble > float.MaxValue || minimumReachDouble > float.MaxValue)
                return false;

            float maximumReachMeters = (float)maximumReachDouble;
            float minimumReachMeters = (float)minimumReachDouble;
            SeatedArmReachStatus reachStatus = SeatedArmReachStatus.Reachable;
            if ((double)measuredDistanceMeters > maximumReachDouble + ReachToleranceMeters)
                reachStatus = SeatedArmReachStatus.ClampedTooFar;
            else if ((double)measuredDistanceMeters + ReachToleranceMeters < minimumReachDouble)
                reachStatus = SeatedArmReachStatus.ClampedTooNear;

            Vector3 normalizedPole = Vector3.zero;
            TryNormalize(stableBodySpaceElbowPole, out normalizedPole);

            Vector3 measuredDelta = measuredWristPositionMeters - shoulderPositionMeters;
            Vector3 axis;
            if (measuredDistanceMeters > DirectionEpsilonMeters)
            {
                axis = measuredDelta / measuredDistanceMeters;
                if (!IsFinite(axis))
                    return false;
            }
            else
            {
                axis = GetStableAxis(normalizedPole, handedness);
            }

            float solvedReachMeters = Mathf.Clamp(measuredDistanceMeters, minimumReachMeters, maximumReachMeters);

            // Equal-length arms can fold to a zero-distance target. At that singularity, any bend
            // direction is geometrically valid, so keep the previous elbow direction when available.
            if (solvedReachMeters <= DirectionEpsilonMeters &&
                Math.Abs((double)upperArmLengthMeters - lowerArmLengthMeters) <= DirectionEpsilonMeters)
            {
                Vector3 elbowDirection;
                if (hasPreviousElbow && TryNormalize(previousElbowPositionMeters - shoulderPositionMeters, out elbowDirection))
                {
                    // The hint directly selects the continuous solution at this zero-reach singularity.
                }
                else
                {
                    Vector3 degenerateAxis = GetStableAxis(normalizedPole, handedness);
                    elbowDirection = SelectBendDirection(
                        shoulderPositionMeters, degenerateAxis, normalizedPole,
                        previousElbowPositionMeters, hasPreviousElbow, handedness);
                }

                Vector3 foldedElbow = shoulderPositionMeters + elbowDirection * upperArmLengthMeters;
                return TryFinish(
                    shoulderPositionMeters, foldedElbow, measuredWristPositionMeters,
                    shoulderPositionMeters, measuredDistanceMeters, 0f, reachStatus, out solution);
            }

            if (solvedReachMeters <= DirectionEpsilonMeters)
                return false;

            double upperSquared = (double)upperArmLengthMeters * upperArmLengthMeters;
            double lowerSquared = (double)lowerArmLengthMeters * lowerArmLengthMeters;
            double reachSquared = (double)solvedReachMeters * solvedReachMeters;
            double alongDouble = (upperSquared - lowerSquared + reachSquared) / (2.0 * solvedReachMeters);
            if (double.IsNaN(alongDouble) || double.IsInfinity(alongDouble) || Math.Abs(alongDouble) > float.MaxValue)
                return false;

            float alongMeters = (float)alongDouble;
            double heightSquared = upperSquared - alongDouble * alongDouble;
            if (heightSquared < 0.0)
                heightSquared = 0.0;
            double heightDouble = Math.Sqrt(heightSquared);
            if (double.IsNaN(heightDouble) || double.IsInfinity(heightDouble) || heightDouble > float.MaxValue)
                return false;

            Vector3 bendDirection = SelectBendDirection(
                shoulderPositionMeters, axis, normalizedPole,
                previousElbowPositionMeters, hasPreviousElbow, handedness);
            Vector3 elbowPositionMeters = shoulderPositionMeters + axis * alongMeters + bendDirection * (float)heightDouble;
            Vector3 solvedWristPositionMeters = shoulderPositionMeters + axis * solvedReachMeters;

            return TryFinish(
                shoulderPositionMeters, elbowPositionMeters, measuredWristPositionMeters,
                solvedWristPositionMeters, measuredDistanceMeters, solvedReachMeters,
                reachStatus, out solution);
        }

        private static bool TryFinish(
            Vector3 shoulder,
            Vector3 elbow,
            Vector3 measuredWrist,
            Vector3 solvedWrist,
            float measuredDistance,
            float solvedReach,
            SeatedArmReachStatus reachStatus,
            out Solution solution)
        {
            solution = InvalidSolution();
            if (!IsFinite(elbow) || !IsFinite(solvedWrist) ||
                !TryGetDistance(measuredWrist, solvedWrist, out float wristSeparation))
                return false;

            solution = new Solution
            {
                ReachStatus = reachStatus,
                ShoulderPositionMeters = shoulder,
                ElbowPositionMeters = elbow,
                WristPositionMeters = measuredWrist,
                SolvedWristPositionMeters = solvedWrist,
                ShoulderToWristMeters = measuredDistance,
                SolvedReachMeters = solvedReach,
                DisplayWristSeparationMeters = wristSeparation
            };
            return true;
        }

        private static Vector3 SelectBendDirection(
            Vector3 shoulder,
            Vector3 axis,
            Vector3 normalizedPole,
            Vector3 previousElbow,
            bool hasPreviousElbow,
            SeatedHandSide handedness)
        {
            Vector3 projectedPole = ProjectOntoPlane(normalizedPole, axis);
            bool hasPoleBend = projectedPole.sqrMagnitude > PoleProjectionEpsilonSquared &&
                               TryNormalize(projectedPole, out projectedPole);

            Vector3 previousRadial = Vector3.zero;
            bool hasPreviousRadial = false;
            if (hasPreviousElbow)
            {
                previousRadial = ProjectOntoPlane(previousElbow - shoulder, axis);
                hasPreviousRadial = TryNormalize(previousRadial, out previousRadial);
            }

            // Near collinearity, the pole projection is ill-conditioned. The explicit prior elbow
            // direction is the stable branch choice there; otherwise it only chooses the pole's sign.
            if (hasPreviousRadial && !hasPoleBend)
                return previousRadial;
            if (hasPoleBend)
            {
                if (hasPreviousRadial && Vector3.Dot(projectedPole, previousRadial) < 0f)
                    projectedPole = -projectedPole;
                return projectedPole;
            }

            Vector3 outward = handedness == SeatedHandSide.Left ? Vector3.left : Vector3.right;
            Vector3 fallback = ProjectOntoPlane(outward, axis);
            if (TryNormalize(fallback, out fallback))
                return fallback;

            fallback = ProjectOntoPlane(Vector3.forward, axis);
            if (TryNormalize(fallback, out fallback))
                return fallback;

            fallback = ProjectOntoPlane(Vector3.up, axis);
            if (TryNormalize(fallback, out fallback))
                return fallback;

            // A unit axis cannot be parallel to all three body-space basis vectors.
            return Vector3.forward;
        }

        private static Vector3 GetStableAxis(Vector3 normalizedPole, SeatedHandSide handedness)
        {
            if (TryNormalize(normalizedPole, out Vector3 poleAxis))
                return poleAxis;
            return handedness == SeatedHandSide.Left ? Vector3.left : Vector3.right;
        }

        private static Vector3 ProjectOntoPlane(Vector3 value, Vector3 planeNormal)
        {
            return value - planeNormal * Vector3.Dot(value, planeNormal);
        }

        private static bool TryGetDistance(Vector3 a, Vector3 b, out float distance)
        {
            distance = 0f;
            Vector3 delta = b - a;
            if (!IsFinite(delta))
                return false;

            double squared = (double)delta.x * delta.x + (double)delta.y * delta.y + (double)delta.z * delta.z;
            double value = Math.Sqrt(squared);
            if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue)
                return false;

            distance = (float)value;
            return IsFinite(distance);
        }

        private static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value))
                return false;

            float largest = Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
            if (largest <= 0f)
                return false;

            float x = value.x / largest;
            float y = value.y / largest;
            float z = value.z / largest;
            float scaledLength = Mathf.Sqrt(x * x + y * y + z * z);
            if (!IsFinite(scaledLength) || scaledLength <= 0f)
                return false;

            normalized = new Vector3(x / scaledLength, y / scaledLength, z / scaledLength);
            return IsFinite(normalized);
        }

        private static Solution InvalidSolution()
        {
            return new Solution { ReachStatus = SeatedArmReachStatus.InvalidMeasurements };
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

}
