using System;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    public enum StreetVisualIdentity { Residential, Commercial, Corner }

    /// <summary>Art direction only. This asset cannot change traffic, routes, observation or collision.</summary>
    [CreateAssetMenu(menuName = "Street Crossing/Visual Profile")]
    public sealed class StreetVisualProfile : ScriptableObject
    {
        public string profileId;
        public StreetVisualIdentity identity;
        public string facade = "E3D6BF";
        public string accent = "537A67";
        public string secondary = "CBA06D";
        public string metal = "364B4C";
        public string foliage = "708C60";
        public string shopCaption = "CORNER MARKET";
        public string districtCaption = "GARDEN WALK";

        public void Validate(string scenarioId)
        {
            if (profileId != scenarioId || (int)identity != int.Parse(scenarioId.Substring(1)) - 1)
                throw new ArgumentException("Visual profile must match the scenario identity.");
            foreach (string hex in new[] { facade, accent, secondary, metal, foliage })
                if (!ColorUtility.TryParseHtmlString("#" + hex, out Color _))
                    throw new ArgumentException("Visual profile contains an invalid colour.");
            if (string.IsNullOrWhiteSpace(shopCaption) || string.IsNullOrWhiteSpace(districtCaption))
                throw new ArgumentException("Visual profile needs readable district and shop labels.");
        }
    }
}
