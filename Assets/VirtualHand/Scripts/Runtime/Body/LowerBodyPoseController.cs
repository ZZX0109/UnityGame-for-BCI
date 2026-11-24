using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// Stores the locomotion input for the lower-body walk cycle and integrates it into a
    /// planar position and yaw over time.  Planar velocity is metres per second in body space
    /// (x the wearer's right, y forward); yaw rate is degrees per second, matching
    /// <c>GameIntegrationAdapter.FeedLocomotion</c>.  Integration only happens when
    /// <see cref="Integrate"/> is called (or in Update during play mode) so edit-mode tests
    /// and host games control the clock explicitly.  Disclaimer: this integrates the avatar
    /// root position and yaw only — no leg bones or meshes are animated, so there is no
    /// visible walk cycle yet.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Body/Lower Body Pose Controller")]
    [DisallowMultipleComponent]
    public sealed class LowerBodyPoseController : MonoBehaviour
    {
        Vector2 _planarVelocityMetersPerSecond;
        float _yawDegreesPerSecond;
        Vector2 _planarPositionMeters;
        float _yawDegrees;

        /// <summary>Most recent planar velocity input, metres per second.</summary>
        public Vector2 PlanarVelocityMetersPerSecond { get { return _planarVelocityMetersPerSecond; } }

        /// <summary>Most recent yaw rate input, degrees per second.</summary>
        public float YawDegreesPerSecond { get { return _yawDegreesPerSecond; } }

        /// <summary>Integrated planar position, metres.</summary>
        public Vector2 PlanarPositionMeters { get { return _planarPositionMeters; } }

        /// <summary>Integrated yaw, degrees.</summary>
        public float YawDegrees { get { return _yawDegrees; } }

        /// <summary>Feeds the per-frame locomotion input; no integration happens here.</summary>
        public void SetLocomotionInput(Vector2 planarVelocity, float yawDeltaPerSecond)
        {
            _planarVelocityMetersPerSecond = planarVelocity;
            _yawDegreesPerSecond = yawDeltaPerSecond;
        }

        /// <summary>Advances position and yaw by <paramref name="deltaTime"/> seconds; non-positive steps are ignored.</summary>
        public void Integrate(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;
            _planarPositionMeters += _planarVelocityMetersPerSecond * deltaTime;
            _yawDegrees += _yawDegreesPerSecond * deltaTime;
        }

        void Update()
        {
            Integrate(Time.deltaTime);
        }
    }
}
