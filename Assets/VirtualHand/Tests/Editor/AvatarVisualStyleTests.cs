using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;

public class AvatarVisualStyleTests
{
    [UnityTest]
    public IEnumerator BlueModeRestoresEveryOriginalMaterialSlot()
    {
        yield return new EnterPlayMode();
        System.Exception failure = null;
        UnityEngine.Material released = null;
        try { released = Neuralife.VirtualHand.EditorTests.AvatarVisualStyleRoundTripTest.Run(); }
        catch (System.Exception error) { failure = error; }
        yield return null;
        bool wasReleased = !released;
        yield return new ExitPlayMode();
        if (failure != null) throw failure;
        Assert.IsTrue(wasReleased, "Generated material must be released after the frame completes.");
    }
}
