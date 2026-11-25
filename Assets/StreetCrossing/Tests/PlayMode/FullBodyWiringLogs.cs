using UnityEngine;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    internal static class FullBodyWiringLogs
    {
        internal static void ExpectWiringSequence()
        {
            LogAssert.Expect(LogType.Log, "[StreetCrossingFullBodyBinding] Elena wrist landmark alignment 2/2 hands (target <2 cm residual).");
        }
    }
}
