namespace StreetCrossing.Core
{
    /// <summary>
    /// §4.1 physics responsibility layers, occupying the free user slots 8-13 of
    /// ProjectSettings/TagManager.asset. The project collision matrix keeps every pair
    /// enabled (DynamicsManager m_LayerCollisionMatrix is all ones), so assigning these
    /// layers is additive-permissive: it can never change what collides with what today.
    /// LayerMask consumers opt into narrower masks explicitly, each with its own
    /// regression test. Keep the names in TagManager.asset in sync with these constants;
    /// StreetPhysicsLayerTests locks the mapping.
    /// </summary>
    public static class StreetPhysicsLayers
    {
        /// <summary>Surfaces a body stands and walks on: road, sidewalk, kerb ramps, aprons.</summary>
        public const int WalkableGround = 8;
        /// <summary>Furniture and structures that block the pedestrian capsule.</summary>
        public const int PlayerObstacle = 9;
        /// <summary>Vehicle bodies; assigned when vehicle colliders move off the Default layer.</summary>
        public const int VehicleObstacle = 10;
        /// <summary>Objects intended to block observation sightlines (mission occlusion design).</summary>
        public const int TrafficVisibilityOccluder = 11;
        /// <summary>Render-only objects whose colliders (if any) must never interact.</summary>
        public const int VisualOnly = 12;
        /// <summary>Non-blocking trigger volumes.</summary>
        public const int TriggerZone = 13;

        public const int WalkableGroundMask = 1 << WalkableGround;
        public const int PlayerObstacleMask = 1 << PlayerObstacle;
        public const int VehicleObstacleMask = 1 << VehicleObstacle;
        public const int TrafficVisibilityOccluderMask = 1 << TrafficVisibilityOccluder;
        public const int VisualOnlyMask = 1 << VisualOnly;
        public const int TriggerZoneMask = 1 << TriggerZone;
    }
}
