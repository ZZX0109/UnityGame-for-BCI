using System;
using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetMissionCatalogTests
    {
        private static StreetMissionDefinition Definition(string id = "S01-T1", string district = "S01",
            string title = "Mission", string description = "Description", int tier = 1, int revision = 1,
            StreetAssistance assistance = StreetAssistance.Guided, bool reverse = false, bool roundTrip = false,
            StreetDestinationKind destination = StreetDestinationKind.Convenience, float prewarm = 0)
            => new StreetMissionDefinition(id, district, title, description, tier, revision, assistance,
                reverse, roundTrip, destination, prewarm);

        [Test]
        public void CatalogHasNineStableTasksAndThreeOrderedTiersPerDistrict()
        {
            Assert.AreEqual(9, StreetMissionCatalog.All.Count);
            var ids = new HashSet<string>();
            foreach (string district in new[] { "S01", "S02", "S03" })
            {
                IReadOnlyList<StreetMissionDefinition> missions = StreetMissionCatalog.ForDistrict(district);
                Assert.AreEqual(3, missions.Count);
                for (int index = 0; index < missions.Count; index++)
                {
                    StreetMissionDefinition mission = missions[index];
                    Assert.AreEqual(district + "-T" + (index + 1), mission.missionId);
                    Assert.AreEqual(district, mission.districtId);
                    Assert.AreEqual(index + 1, mission.tier);
                    Assert.AreEqual(1, mission.revision);
                    Assert.IsTrue(ids.Add(mission.missionId));
                    Assert.Contains(mission, (System.Collections.ICollection)StreetMissionCatalog.All);
                    Assert.DoesNotThrow(mission.Validate);
                }
            }
        }

        [TestCase("S01", 0, StreetAssistance.Guided, StreetDestinationKind.Convenience, false, false, 0f)]
        [TestCase("S01", 1, StreetAssistance.RiskOnly, StreetDestinationKind.Convenience, false, false, 20f)]
        [TestCase("S01", 2, StreetAssistance.OnDemand, StreetDestinationKind.Residence, true, false, 0f)]
        [TestCase("S02", 0, StreetAssistance.Guided, StreetDestinationKind.Bookshop, false, false, 0f)]
        [TestCase("S02", 1, StreetAssistance.RiskOnly, StreetDestinationKind.Bookshop, false, false, 20f)]
        [TestCase("S02", 2, StreetAssistance.OnDemand, StreetDestinationKind.Pharmacy, false, false, 0f)]
        [TestCase("S03", 0, StreetAssistance.Guided, StreetDestinationKind.Cafe, false, false, 0f)]
        [TestCase("S03", 1, StreetAssistance.RiskOnly, StreetDestinationKind.Cafe, false, false, 8f)]
        [TestCase("S03", 2, StreetAssistance.OnDemand, StreetDestinationKind.Cafe, false, true, 0f)]
        public void TasksRetainTheirDistinctPurposeDirectionAssistanceAndFullPrewarmRequest(string district, int index,
            StreetAssistance assistance, StreetDestinationKind destination, bool reverse, bool roundTrip, float prewarm)
        {
            StreetMissionDefinition mission = StreetMissionCatalog.ForDistrict(district)[index];
            Assert.AreEqual(assistance, mission.assistance);
            Assert.AreEqual(destination, mission.destinationKind);
            Assert.AreEqual(reverse, mission.reverse);
            Assert.AreEqual(roundTrip, mission.roundTrip);
            Assert.AreEqual(prewarm, mission.prewarmSeconds);
        }

        [Test]
        public void CatalogCollectionsCannotBeModifiedByASelector()
        {
            var all = (IList<StreetMissionDefinition>)StreetMissionCatalog.All;
            var district = (IList<StreetMissionDefinition>)StreetMissionCatalog.ForDistrict("S01");
            Assert.Throws<NotSupportedException>(() => all.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => district[0] = district[1]);
            foreach (var property in typeof(StreetMissionDefinition).GetProperties())
                Assert.IsFalse(property.CanWrite, property.Name + " must remain read-only.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("s01")]
        [TestCase("S04")]
        public void UnknownDistrictDoesNotSilentlySelectAnotherTask(string district)
            => Assert.That(() => StreetMissionCatalog.ForDistrict(district), Throws.InstanceOf<ArgumentException>());

        [TestCase("S01", 3101, 825830647, 825935376, 826040105)]
        [TestCase("S02", 3202, 563227597, 563332326, 563437055)]
        [TestCase("S03", 3303, 359948297, 360053026, 360157755)]
        public void ConfigurationsHavePinnedRepeatableSeeds(string district, int baseSeed, int first, int second, int third)
        {
            StreetMissionDefinition mission = StreetMissionCatalog.ForDistrict(district)[0];
            CollectionAssert.AreEqual(new[] { first, second, third }, new[]
                { mission.SeedForVariant(baseSeed, 0), mission.SeedForVariant(baseSeed, 1), mission.SeedForVariant(baseSeed, 2) });
        }

        [Test]
        public void AllTwentySevenConfigurationsAreDistinctAndRevisionChangesDoNotReuseAnOpening()
        {
            var seeds = new HashSet<int>();
            foreach (StreetMissionDefinition mission in StreetMissionCatalog.All)
            {
                int baseSeed = mission.districtId == "S01" ? 3101 : mission.districtId == "S02" ? 3202 : 3303;
                for (int variant = 0; variant < 3; variant++)
                {
                    int value = mission.SeedForVariant(baseSeed, variant);
                    Assert.AreEqual(value, mission.SeedForVariant(baseSeed, variant));
                    Assert.IsTrue(seeds.Add(value), "Task configurations must not accidentally share the reviewed seed pool.");
                }
            }
            Assert.AreNotEqual(Definition().SeedForVariant(3101, 0), Definition(revision: 2).SeedForVariant(3101, 0));
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void SeedIntegerBoundariesStillGiveThreeDistinctNonnegativeValues(int baseSeed)
        {
            var values = new HashSet<int>();
            for (int variant = 0; variant < 3; variant++)
            {
                int value = Definition().SeedForVariant(baseSeed, variant);
                Assert.GreaterOrEqual(value, 0);
                Assert.IsTrue(values.Add(value));
            }
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void FourthOrNegativeConfigurationCannotBeRequested(int variant)
            => Assert.Throws<ArgumentOutOfRangeException>(() => Definition().SeedForVariant(3101, variant));

        [Test]
        public void InvalidIdentityAndMissingInstructionsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => Definition(id: "S02-T1"));
            Assert.Throws<ArgumentException>(() => Definition(district: "S04"));
            Assert.Throws<ArgumentException>(() => Definition(title: " "));
            Assert.Throws<ArgumentException>(() => Definition(description: null));
            Assert.Throws<ArgumentException>(() => Definition(reverse: true, roundTrip: true));
        }

        [TestCase(0)]
        [TestCase(4)]
        public void InvalidTierIsRejected(int tier)
            => Assert.Throws<ArgumentOutOfRangeException>(() => Definition(tier: tier));

        [Test]
        public void InvalidRevisionEnumsAndPrewarmAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Definition(revision: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Definition(assistance: (StreetAssistance)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => Definition(destination: (StreetDestinationKind)99));
            foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => Definition(prewarm: value));
        }
    }
}
