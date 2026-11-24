#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using System.Collections;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class ContinuousArmSleevePlayModeTests
    {
        [UnityTest]
        public IEnumerator RealHandPrefabSleevesUpdateSmoothlyAndHideWhenLost()
        {
            GameObject handPrefab = ContinuousArmSleeveStreetHostAssembler.FindActualHandPrefab();
            Assert.IsNotNull(handPrefab, "A prefab containing FirstPersonWrist is required.");
            Material weave = ContinuousArmSleeveStreetHostAssembler.FindGeneratedWeaveMaterial(handPrefab);
            Assert.IsNotNull(weave, "The generated First Person Woven Cuff material is required.");

            ContinuousArmSleeveStreetHostAssembler.Host host =
                ContinuousArmSleeveStreetHostAssembler.CreateIsolatedStreetHost(handPrefab, weave);
            try
            {
                Vector3 leftHandPosition = host.leftHandVisual.transform.position;
                Vector3 rightHandPosition = host.rightHandVisual.transform.position;
                host.leftShoulder.position = new Vector3(-0.18f, 0.12f, 0f);
                host.leftElbow.position = new Vector3(-0.36f, -0.08f, 0.04f);
                host.leftWrist.position = new Vector3(-0.44f, -0.27f, 0.07f);
                host.rightShoulder.position = new Vector3(0.18f, 0.12f, 0f);
                host.rightElbow.position = new Vector3(0.36f, -0.08f, 0.04f);
                host.rightWrist.position = new Vector3(0.44f, -0.27f, 0.07f);

                SetArmState(host.preview, true, true);
                SetArmState(host.preview, false, true);
                host.sleeve.Refresh();
                yield return null;

                Assert.IsTrue(host.sleeve.LeftRenderer.enabled);
                Assert.IsTrue(host.sleeve.RightRenderer.enabled);
                Mesh originalLeftMesh = host.sleeve.LeftMesh;
                Assert.IsNotNull(originalLeftMesh);
                Vector3[] before = originalLeftMesh.vertices;
                int meshId = originalLeftMesh.GetInstanceID();

                host.leftElbow.position += new Vector3(-0.035f, 0.015f, 0.06f);
                host.sleeve.Refresh();
                yield return null;

                Assert.AreEqual(meshId, host.sleeve.LeftMesh.GetInstanceID(), "Refresh should reuse the generated mesh.");
                Vector3[] after = host.sleeve.LeftMesh.vertices;
                bool changed = false;
                for (int i = 0; i < before.Length; i++)
                {
                    if ((after[i] - before[i]).sqrMagnitude > 0.0000001f)
                    {
                        changed = true;
                        break;
                    }
                }
                Assert.IsTrue(changed, "Moving the elbow should deform the continuous sleeve mesh.");
                Assert.AreEqual(leftHandPosition, host.leftHandVisual.transform.position,
                    "The sleeve must not move the left hand prefab.");
                Assert.AreEqual(rightHandPosition, host.rightHandVisual.transform.position,
                    "The sleeve must not move the right hand prefab.");

                SetArmState(host.preview, true, false);
                host.sleeve.Refresh();
                Assert.IsFalse(host.sleeve.LeftRenderer.enabled, "A lost arm should hide its sleeve.");
                Assert.IsTrue(host.sleeve.RightRenderer.enabled, "The other arm should remain visible.");
            }
            finally
            {
                if (host.root) Object.Destroy(host.root);
            }
            yield return null;
        }

        private static void SetArmState(SeatedArmPreview preview, bool left, bool visible)
        {
            string property = left ? "<LeftArm>k__BackingField" : "<RightArm>k__BackingField";
            FieldInfo backingField = typeof(SeatedArmPreview).GetField(property,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(backingField, "SeatedArmPreview arm-state backing field was not found.");
            ControllerHandPair.PoseSource source = visible
                ? ControllerHandPair.PoseSource.Controller
                : ControllerHandPair.PoseSource.Hidden;
            SeatedArmPreview.ArmState state = new SeatedArmPreview.ArmState
            {
                visible = visible,
                source = source,
                isMirrored = false,
                reachStatus = visible ? SeatedArmReachStatus.Reachable : SeatedArmReachStatus.InvalidMeasurements
            };
            backingField.SetValue(preview, state);
        }
    }
}
#endif
