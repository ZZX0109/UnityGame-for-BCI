using System.Collections.Generic;
using StreetCrossing.Traffic;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Presentation only. Driven by the deterministic vehicle tick, never by an independent clock.</summary>
    public sealed class StreetVehiclePresentation : MonoBehaviour
    {
        private readonly List<Transform> wheels = new List<Transform>();
        private readonly List<Quaternion> wheelRestRotations = new List<Quaternion>();
        private readonly List<Renderer> rearLamps = new List<Renderer>();
        private readonly List<Renderer> leftIndicators = new List<Renderer>();
        private readonly List<Renderer> rightIndicators = new List<Renderer>();
        private MaterialPropertyBlock properties;
        private bool initialized;
        public float WheelDegrees { get; private set; }
        public bool BrakeLightsOn { get; private set; }
        public bool LeftIndicatorOn { get; private set; }
        public bool RightIndicatorOn { get; private set; }

        public void ResetVisuals()
        {
            if (!initialized)
            {
                properties = new MaterialPropertyBlock();
                foreach (Transform part in GetComponentsInChildren<Transform>(true))
                {
                    if (part.name == "Tyre" || part.name == "Wheel alloy" || part.name == "Hub")
                    { wheels.Add(part); wheelRestRotations.Add(part.localRotation); }
                    Renderer renderer = part.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    if (part.name == "Tail lamp") rearLamps.Add(renderer);
                    else if (part.name == "Turn indicator left") leftIndicators.Add(renderer);
                    else if (part.name == "Turn indicator right") rightIndicators.Add(renderer);
                }
                initialized = true;
            }
            WheelDegrees = 0;
            for (int i = 0; i < wheels.Count; i++) wheels[i].localRotation = wheelRestRotations[i];
            TickVisuals(0, 1, false, VehicleRouteKind.StraightNear, 0, 0, 0);
        }

        public void TickVisuals(float travelledMetres, float speed, bool braking, VehicleRouteKind route, float routeDistance, float junctionExit, float simulationTime)
        {
            if (!initialized) ResetVisuals();
            WheelDegrees = Mathf.Repeat(WheelDegrees + travelledMetres / .34f * Mathf.Rad2Deg, 360);
            for (int i = 0; i < wheels.Count; i++) wheels[i].localRotation = wheelRestRotations[i] * Quaternion.AngleAxis(WheelDegrees, Vector3.right);
            BrakeLightsOn = braking || speed < .08f;
            bool turning = route == VehicleRouteKind.LeftTurn || route == VehicleRouteKind.RightTurn;
            bool flashing = turning && routeDistance < junctionExit && ((int)(simulationTime * 2) & 1) == 0;
            LeftIndicatorOn = flashing && route == VehicleRouteKind.LeftTurn;
            RightIndicatorOn = flashing && route == VehicleRouteKind.RightTurn;
            Apply(rearLamps, BrakeLightsOn ? new Color(.9f, .07f, .035f) : new Color(.35f, .045f, .025f), BrakeLightsOn ? 1.6f : .15f);
            Apply(leftIndicators, LeftIndicatorOn ? new Color(1, .52f, .04f) : new Color(.32f, .18f, .035f), LeftIndicatorOn ? 1.8f : 0);
            Apply(rightIndicators, RightIndicatorOn ? new Color(1, .52f, .04f) : new Color(.32f, .18f, .035f), RightIndicatorOn ? 1.8f : 0);
        }

        private void Apply(List<Renderer> renderers, Color colour, float emission)
        {
            properties.Clear(); properties.SetColor("_Color", colour); properties.SetColor("_EmissionColor", colour * emission);
            foreach (Renderer renderer in renderers) renderer.SetPropertyBlock(properties);
        }
    }
}
