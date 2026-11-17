using System;
using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Save;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetMissionProgressTests
    {
        private sealed class MemoryStore : IStreetMissionProgressStore
        {
            public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
            public int Saves;
            public int GetInt(string key, int defaultValue) => Values.TryGetValue(key, out int value) ? value : defaultValue;
            public void SetInt(string key, int value) => Values[key] = value;
            public void Save() => Saves++;
        }

        private IStreetMissionProgressStore previousStore;
        private MemoryStore store;
        private static StreetMissionDefinition Mission(string district = "S01", int tier = 1)
            => StreetMissionCatalog.ForDistrict(district)[tier - 1];

        [SetUp]
        public void IsolateProgress()
        {
            previousStore = StreetMissionProgress.Store;
            store = new MemoryStore();
            StreetMissionProgress.Store = store;
        }

        [TearDown]
        public void RestoreProgress() => StreetMissionProgress.Store = previousStore;

        private static void Complete(StreetMissionDefinition mission, int stars = 1)
        {
            for (int variant = 0; variant < 3; variant++) StreetMissionProgress.RecordSuccess(mission, variant, stars);
        }

        [Test]
        public void MissingProgressStartsAtTierOneAndReadsDoNotSave()
        {
            Assert.AreEqual(0, StreetMissionProgress.BestStars(Mission()));
            Assert.AreEqual(0, StreetMissionProgress.CompletedVariantCount(Mission()));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S01"));
            Assert.AreEqual(0, store.Values.Count);
            Assert.AreEqual(0, store.Saves);
        }

        [Test]
        public void BestStarsNeverRegressAndReplayingOneConfigurationDoesNotAdvanceRecommendation()
        {
            StreetMissionDefinition mission = Mission();
            StreetMissionProgress.RecordSuccess(mission, 0, 1);
            StreetMissionProgress.RecordSuccess(mission, 0, 3);
            for (int retry = 0; retry < 20; retry++) StreetMissionProgress.RecordSuccess(mission, 0, 2);
            Assert.AreEqual(3, StreetMissionProgress.BestStars(mission));
            Assert.AreEqual(1, StreetMissionProgress.CompletedVariantCount(mission));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S01"));
            Assert.AreEqual(1, store.Values.Count);
            Assert.AreEqual(2, store.Saves);
        }

        [Test]
        public void ExactlyThreeDistinctSafeConfigurationsRecommendTheNextTierEvenWithOneStar()
        {
            StreetMissionProgress.RecordSuccess(Mission(), 2, 1);
            StreetMissionProgress.RecordSuccess(Mission(), 0, 1);
            Assert.AreEqual(2, StreetMissionProgress.CompletedVariantCount(Mission()));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S01"));
            StreetMissionProgress.RecordSuccess(Mission(), 1, 1);
            Assert.AreEqual(3, StreetMissionProgress.CompletedVariantCount(Mission()));
            Assert.AreEqual(2, StreetMissionProgress.RecommendedTier("S01"));
        }

        [Test]
        public void CompletingAHigherTierDoesNotSkipUnpractisedPrerequisitesAndRecommendationCapsAtThree()
        {
            Complete(Mission(tier: 3));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S01"));
            Complete(Mission());
            Assert.AreEqual(2, StreetMissionProgress.RecommendedTier("S01"));
            Complete(Mission(tier: 2));
            Assert.AreEqual(3, StreetMissionProgress.RecommendedTier("S01"));
            Assert.AreEqual(3, StreetMissionProgress.CompletedVariantCount(Mission(tier: 3)));
        }

        [Test]
        public void DistrictsAndNeighbouringTaskResultsDoNotShareProgress()
        {
            Complete(Mission(), 2);
            Assert.AreEqual(0, StreetMissionProgress.BestStars(Mission(tier: 2)));
            Assert.AreEqual(0, StreetMissionProgress.CompletedVariantCount(Mission("S02")));
            Assert.AreEqual(0, StreetMissionProgress.BestStars(Mission("S03")));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S02"));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S03"));
        }

        [Test]
        public void VariantStarsReportsIndependentBestValuesWithoutWriting()
        {
            StreetMissionProgress.RecordSuccess(Mission(), 1, 2);
            StreetMissionProgress.RecordSuccess(Mission(), 2, 3);
            Assert.AreEqual(0, StreetMissionProgress.VariantStars(Mission(), 0));
            Assert.AreEqual(2, StreetMissionProgress.VariantStars(Mission(), 1));
            Assert.AreEqual(3, StreetMissionProgress.VariantStars(Mission(), 2));
            Assert.AreEqual(0, StreetMissionProgress.VariantStars(Mission("S02"), 1));
            Assert.AreEqual(0, StreetMissionProgress.VariantStars(Mission(tier: 2), 2));
            Assert.AreEqual(2, store.Saves);
        }

        [Test]
        public void NewRevisionCannotReuseOldCompletionsOrOverwriteTheirBestStars()
        {
            StreetMissionDefinition old = Mission();
            Complete(old, 3);
            var revised = new StreetMissionDefinition(old.missionId, old.districtId, old.title, old.description,
                old.tier, 2, old.assistance, old.reverse, old.roundTrip, old.destinationKind, old.prewarmSeconds);
            Assert.AreEqual(0, StreetMissionProgress.BestStars(revised));
            Assert.AreEqual(0, StreetMissionProgress.CompletedVariantCount(revised));
            Assert.AreEqual(0, StreetMissionProgress.VariantStars(revised, 0));
            StreetMissionProgress.RecordSuccess(revised, 0, 1);
            Assert.AreEqual(1, StreetMissionProgress.BestStars(revised));
            Assert.AreEqual(3, StreetMissionProgress.BestStars(old));
            Assert.AreEqual(3, StreetMissionProgress.VariantStars(old, 0));
            Assert.AreEqual(3, StreetMissionProgress.CompletedVariantCount(old));
        }

        [Test]
        public void LegacyDistrictStarsAndSettingsNeitherCountNorChange()
        {
            store.Values["StreetCrossing.Progress.S01"] = 3;
            store.Values["StreetCrossing.Settings.LargeText"] = 1;
            Assert.AreEqual(0, StreetMissionProgress.BestStars(Mission()));
            Assert.AreEqual(1, StreetMissionProgress.RecommendedTier("S01"));
            StreetMissionProgress.RecordSuccess(Mission(), 1, 2);
            Assert.AreEqual(3, store.Values["StreetCrossing.Progress.S01"]);
            Assert.AreEqual(1, store.Values["StreetCrossing.Settings.LargeText"]);
            Assert.IsTrue(store.Values.ContainsKey("StreetCrossing.MissionProgress.v1.S01-T1.r1.variant.1"));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void InvalidVariantDoesNotWriteOrSave(int variant)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StreetMissionProgress.RecordSuccess(Mission(), variant, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => StreetMissionProgress.VariantStars(Mission(), variant));
            Assert.AreEqual(0, store.Values.Count);
            Assert.AreEqual(0, store.Saves);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(4)]
        public void InvalidStarsCannotFabricateASafeCompletion(int stars)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StreetMissionProgress.RecordSuccess(Mission(), 0, stars));
            Assert.AreEqual(0, StreetMissionProgress.CompletedVariantCount(Mission()));
            Assert.AreEqual(0, store.Values.Count);
            Assert.AreEqual(0, store.Saves);
        }

        [Test]
        public void CorruptStoredValuesAreNotCountedAsSuccessfulConfigurations()
        {
            string prefix = "StreetCrossing.MissionProgress.v1.S01-T1.r1.variant.";
            store.Values[prefix + "0"] = -1;
            store.Values[prefix + "1"] = 4;
            store.Values[prefix + "2"] = int.MaxValue;
            Assert.AreEqual(0, StreetMissionProgress.BestStars(Mission()));
            Assert.AreEqual(0, StreetMissionProgress.CompletedVariantCount(Mission()));
            StreetMissionProgress.RecordSuccess(Mission(), 2, 1);
            Assert.AreEqual(1, StreetMissionProgress.CompletedVariantCount(Mission()));
            Assert.AreEqual(1, StreetMissionProgress.BestStars(Mission()));
        }

        [Test]
        public void NullDefinitionsAndNullStoreAreRejectedWithoutReplacingTheInjectedStore()
        {
            Assert.Throws<ArgumentNullException>(() => StreetMissionProgress.BestStars(null));
            Assert.Throws<ArgumentNullException>(() => StreetMissionProgress.CompletedVariantCount(null));
            Assert.Throws<ArgumentNullException>(() => StreetMissionProgress.VariantStars(null, 0));
            Assert.Throws<ArgumentNullException>(() => StreetMissionProgress.RecordSuccess(null, 0, 1));
            Assert.Throws<ArgumentNullException>(() => StreetMissionProgress.Store = null);
            Assert.AreSame(store, StreetMissionProgress.Store);
            Assert.AreEqual(0, store.Saves);
        }
    }
}
