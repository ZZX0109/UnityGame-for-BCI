using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Traffic;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetMissionScoringTests
    {
        private static CrossingEvaluator Arrive(bool nearMiss, bool stationary, bool legal = true)
        {
            var e = new CrossingEvaluator();
            e.Tick(CrossingRegion.Approach | CrossingRegion.Kerb | CrossingRegion.Entry, TrafficPhase.VehicleGreen, true, false, 10);
            e.Observe(true); e.Observe(false);
            if (nearMiss) e.NearMiss(1);
            if (stationary) e.Contact(2, 0);
            e.Tick(CrossingRegion.RoadwayHazard, legal ? TrafficPhase.Walk : TrafficPhase.VehicleGreen, true, false, .02f);
            e.Tick(CrossingRegion.Exit, TrafficPhase.Walk, true, false, .02f);
            e.Tick(CrossingRegion.Destination, TrafficPhase.Walk, true, false, .02f);
            return e;
        }
        [TestCase(false, false, 3)]
        [TestCase(true, false, 2)]
        [TestCase(false, true, 2)]
        [TestCase(true, true, 1)]
        public void IndependentSafetyEventsGiveReachableOneTwoAndThreeStarResults(bool nearMiss, bool stationary, int stars)
        {
            var result = new StreetSegmentResult(0, Arrive(nearMiss, stationary));
            Assert.IsTrue(result.SafetySuccess); Assert.AreEqual(stars, result.Stars);
            Assert.AreEqual(10, result.WaitingSeconds);
        }
        [Test]
        public void WaitingAndCompleteObservationCannotOffsetIllegalEntry()
        {
            var result = new StreetSegmentResult(0, Arrive(false, false, false));
            Assert.IsFalse(result.SafetySuccess); Assert.AreEqual(0, result.Stars);
        }
    }
}
