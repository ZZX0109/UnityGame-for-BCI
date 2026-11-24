using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AvatarBlueOutlineRenderTests
{
    [UnityTest]
    public IEnumerator BlueStyleRendersSceneOcclusionAndRestoresOriginalMaterial()
    {
        yield return new EnterPlayMode();
        Exception failure = null;
        try
        {
            Neuralife.VirtualHand.EditorTests.AvatarBlueOutlineRenderTestHelper.Run();
        }
        catch (Exception error)
        {
            failure = error;
        }

        // Allow deferred UnityEngine.Object.Destroy calls and AvatarVisualStyle.OnDestroy to finish.
        yield return null;
        yield return new ExitPlayMode();
        if (failure != null)
            throw failure;
    }
}
