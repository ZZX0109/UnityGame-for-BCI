#if UNITY_EDITOR
using NUnit.Framework;
namespace Neuralife.VirtualHand.Tests
{
    public class SeatedArmSolverTests
    {
        [Test] public void GeometryReachAndContinuity()
        {
            SeatedArmSolverEditModeChecks.RunAll();
        }
    }
}
#endif
