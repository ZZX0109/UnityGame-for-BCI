using System;
using System.Globalization;
using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Save
{
    public interface IStreetMissionProgressStore
    {
        int GetInt(string key, int defaultValue);
        void SetInt(string key, int value);
        void Save();
    }

    /// <summary>Mission/revision/variant results; legacy district stars remain independent.</summary>
    public static class StreetMissionProgress
    {
        private const string Prefix = "StreetCrossing.MissionProgress.v1.";
        private sealed class PlayerPrefsStore : IStreetMissionProgressStore
        {
            public int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);
            public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
            public void Save() => PlayerPrefs.Save();
        }

        private static IStreetMissionProgressStore store = new PlayerPrefsStore();
        public static IStreetMissionProgressStore Store
        {
            get => store;
            set => store = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static int BestStars(StreetMissionDefinition definition)
        {
            RequireDefinition(definition);
            int best = 0;
            for (int variant = 0; variant < 3; variant++) best = Math.Max(best, ReadStars(definition, variant));
            return best;
        }

        public static int VariantStars(StreetMissionDefinition definition, int variant)
        {
            RequireDefinition(definition);
            if (variant < 0 || variant >= 3) throw new ArgumentOutOfRangeException(nameof(variant));
            return ReadStars(definition, variant);
        }

        public static int CompletedVariantCount(StreetMissionDefinition definition)
        {
            RequireDefinition(definition);
            int count = 0;
            for (int variant = 0; variant < 3; variant++) if (ReadStars(definition, variant) > 0) count++;
            return count;
        }

        /// <summary>Call only after the entire mission, including every return leg, succeeds safely.</summary>
        public static void RecordSuccess(StreetMissionDefinition definition, int variant, int stars)
        {
            RequireDefinition(definition);
            if (variant < 0 || variant >= 3) throw new ArgumentOutOfRangeException(nameof(variant));
            if (stars < 1 || stars > 3) throw new ArgumentOutOfRangeException(nameof(stars));
            if (stars <= ReadStars(definition, variant)) return;
            store.SetInt(Key(definition, variant), stars);
            store.Save();
        }

        /// <summary>A recommendation, not a lock; only three distinct successful configurations advance it.</summary>
        public static int RecommendedTier(string districtId)
        {
            foreach (StreetMissionDefinition mission in StreetMissionCatalog.ForDistrict(districtId))
            {
                if (CompletedVariantCount(mission) < 3) return mission.tier;
            }
            return 3;
        }

        private static int ReadStars(StreetMissionDefinition definition, int variant)
        {
            int stars = store.GetInt(Key(definition, variant), 0);
            return stars >= 1 && stars <= 3 ? stars : 0;
        }

        private static string Key(StreetMissionDefinition definition, int variant)
            => Prefix + definition.missionId + ".r" + definition.revision.ToString(CultureInfo.InvariantCulture) +
               ".variant." + variant.ToString(CultureInfo.InvariantCulture);

        private static void RequireDefinition(StreetMissionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            definition.Validate();
        }
    }
}
