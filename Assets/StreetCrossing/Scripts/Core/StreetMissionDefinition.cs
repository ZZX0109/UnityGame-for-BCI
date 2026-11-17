using System;

namespace StreetCrossing.Core
{
    public enum StreetAssistance { Guided, RiskOnly, OnDemand }
    public enum StreetDestinationKind { Convenience, Residence, Bookshop, Pharmacy, Cafe }

    /// <summary>Immutable task configuration, independent of the shared scenario assets.</summary>
    public sealed class StreetMissionDefinition
    {
        private const uint SeedStep = 104729u;

        public string missionId { get; }
        public string districtId { get; }
        public string title { get; }
        public string description { get; }
        public int tier { get; }
        public int revision { get; }
        public StreetAssistance assistance { get; }
        public bool reverse { get; }
        public bool roundTrip { get; }
        public StreetDestinationKind destinationKind { get; }
        public float prewarmSeconds { get; }

        public StreetMissionDefinition(string missionId, string districtId, string title, string description,
            int tier, int revision, StreetAssistance assistance, bool reverse, bool roundTrip,
            StreetDestinationKind destinationKind, float prewarmSeconds)
        {
            this.missionId = missionId;
            this.districtId = districtId;
            this.title = title;
            this.description = description;
            this.tier = tier;
            this.revision = revision;
            this.assistance = assistance;
            this.reverse = reverse;
            this.roundTrip = roundTrip;
            this.destinationKind = destinationKind;
            this.prewarmSeconds = prewarmSeconds;
            Validate();
        }

        public void Validate()
        {
            if (districtId != "S01" && districtId != "S02" && districtId != "S03")
                throw new ArgumentException("Mission district must be S01, S02 or S03.", nameof(districtId));
            if (tier < 1 || tier > 3)
                throw new ArgumentOutOfRangeException(nameof(tier), "Mission tier must be 1, 2 or 3.");
            if (missionId != districtId + "-T" + tier)
                throw new ArgumentException("Mission ID must match its stable district and tier.", nameof(missionId));
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Mission title and description are required.");
            if (!Enum.IsDefined(typeof(StreetAssistance), assistance))
                throw new ArgumentOutOfRangeException(nameof(assistance));
            if (!Enum.IsDefined(typeof(StreetDestinationKind), destinationKind))
                throw new ArgumentOutOfRangeException(nameof(destinationKind));
            if (reverse && roundTrip)
                throw new ArgumentException("A round trip starts on the normal side; its return leg is handled separately.");
            if (float.IsNaN(prewarmSeconds) || float.IsInfinity(prewarmSeconds) || prewarmSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(prewarmSeconds));
        }

        /// <summary>
        /// Three deterministic configurations per mission revision. This never
        /// consumes global random state or changes the scenario's base seed.
        /// Explicit uint arithmetic avoids process-dependent string hashing.
        /// </summary>
        public int SeedForVariant(int baseSeed, int variant)
        {
            if (variant < 0 || variant >= 3) throw new ArgumentOutOfRangeException(nameof(variant));
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in missionId) hash = (hash ^ character) * 16777619u;
                for (int shift = 0; shift < 32; shift += 8)
                    hash = (hash ^ (((uint)revision >> shift) & 255u)) * 16777619u;
                for (int shift = 0; shift < 32; shift += 8)
                    hash = (hash ^ (((uint)baseSeed >> shift) & 255u)) * 16777619u;
                // Reserve room for both offsets, guaranteeing three distinct,
                // nonnegative System.Random-compatible seeds even at int limits.
                uint range = (uint)int.MaxValue - SeedStep * 2u + 1u;
                return (int)(hash % range + SeedStep * (uint)variant);
            }
        }
    }
}
