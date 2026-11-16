using NUnit.Framework;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawPreferencesTests
    {
        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"version\":99,\"volume\":1}")]
        public void MissingCorruptOrFuturePreferences_UseSafeDefaults(string json)
        {
            ClawPreferences value = ClawPreferences.Decode(json);
            Assert.AreEqual(0.25f, value.volume); Assert.AreEqual(1, value.textScale);
            Assert.IsTrue(value.positionAssist); Assert.IsTrue(value.centerOfMassAssist);
            Assert.IsTrue(value.smooth); Assert.IsTrue(value.pipLowQuality);
        }
        [Test]
        public void Preferences_RoundTripAllOptionsAndClampUnsafeNumericValues()
        {
            var original = new ClawPreferences { volume = 0.6f, textScale = 1.15f, positionAssist = false, centerOfMassAssist = false, smooth = false, pipLowQuality = false };
            ClawPreferences value = ClawPreferences.Decode(JsonUtility.ToJson(original));
            Assert.AreEqual(0.6f, value.volume); Assert.AreEqual(1.15f, value.textScale);
            Assert.IsFalse(value.positionAssist); Assert.IsFalse(value.centerOfMassAssist); Assert.IsFalse(value.smooth); Assert.IsFalse(value.pipLowQuality);
            value = ClawPreferences.Decode("{\"volume\":-10,\"textScale\":100}");
            Assert.AreEqual(0, value.volume); Assert.AreEqual(1.2f, value.textScale);
            Assert.AreEqual(0.25f, ClawPreferences.Sanitize(float.NaN, 0.25f, 0, 1));
            Assert.AreEqual(1, ClawPreferences.Sanitize(float.PositiveInfinity, 1, 0.9f, 1.2f));
        }
        [Test]
        public void EmptyRecoveryAndSlippedDelivery_KeepDistinctAwardRules()
        {
            var session = new ClawSession(); session.Reset(1, true); Assert.IsTrue(session.BeginAttempt());
            Assert.IsTrue(session.Advance(GraspState.ContactDescent)); Assert.IsTrue(session.Advance(GraspState.Closing));
            session.RecordFailure(GraspFailure.NoContact); Assert.IsTrue(session.Advance(GraspState.Returning));
            Assert.IsFalse(session.Advance(GraspState.Settling)); session.MarkReleased(); Assert.IsFalse(session.Released);
            Assert.IsTrue(session.Advance(GraspState.Result)); Assert.IsTrue(session.Retry()); Assert.AreEqual(GraspState.End, session.State);
            session.Reset(1, true); Assert.IsTrue(session.BeginAttempt());
            foreach (GraspState next in new[] { GraspState.ContactDescent, GraspState.Closing, GraspState.Lifting, GraspState.Transporting, GraspState.Releasing }) Assert.IsTrue(session.Advance(next));
            session.RecordFailure(GraspFailure.Slipped); session.MarkReleased(); Assert.IsTrue(session.Advance(GraspState.Settling));
            Assert.IsTrue(session.TryAward(session.SessionId, 1, "target", "target", true));
            Assert.IsFalse(session.TryAward(session.SessionId, 1, "target", "target", true));
            Assert.AreEqual(GraspFailure.None, session.Failure);
        }
    }
}
