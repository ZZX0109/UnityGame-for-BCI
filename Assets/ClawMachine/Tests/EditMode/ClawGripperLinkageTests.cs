using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawGripperLinkageTests
    {
        [Test]
        public void LinkEndpoints_StayAttachedThroughoutClosureWithoutMovingFingerPhysics()
        {
            var root = new GameObject("Linkage fixture");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(3, 2, -1), Quaternion.Euler(15, 37, 8));
                root.transform.localScale = new Vector3(1.3f, 0.8f, 1.1f);
                var fingers = new Transform[3];
                var links = new Transform[3];
                var colliders = new BoxCollider[3];
                for (int index = 0; index < 3; index++)
                {
                    fingers[index] = new GameObject("Finger " + index).transform;
                    fingers[index].SetParent(root.transform, false);
                    colliders[index] = fingers[index].gameObject.AddComponent<BoxCollider>();
                    colliders[index].size = new Vector3(0.024f, 0.28f, 0.024f);
                    links[index] = new GameObject("Visual cylinder " + index).transform;
                    links[index].SetParent(root.transform, false);
                    links[index].localScale = new Vector3(0.018f, 0.01f, 0.018f);
                }
                ClawGripperLinkage linkage = root.AddComponent<ClawGripperLinkage>();
                Set(linkage, "hubOrigin", root.transform); Set(linkage, "fingers", fingers); Set(linkage, "links", links);
                foreach (float radius in new[] { 0.205f, 0.12f, 0.065f })
                {
                    for (int index = 0; index < fingers.Length; index++)
                    {
                        float angle = (90 + index * 120) * Mathf.Deg2Rad;
                        fingers[index].localPosition = new Vector3(Mathf.Cos(angle) * radius, 0.13f, Mathf.Sin(angle) * radius);
                    }
                    linkage.RefreshNow();
                    for (int index = 0; index < fingers.Length; index++)
                    {
                        Vector3 radial = new Vector3(fingers[index].localPosition.x, 0, fingers[index].localPosition.z).normalized;
                        Vector3 wrist = root.transform.TransformPoint(radial * 0.045f + Vector3.up * 0.30f);
                        Vector3 knuckle = fingers[index].TransformPoint(Vector3.up * 0.10f);
                        Assert.Less(Vector3.Distance(wrist, links[index].TransformPoint(Vector3.down)), 0.00001f, "Cylinder bottom must meet the hub.");
                        Assert.Less(Vector3.Distance(knuckle, links[index].TransformPoint(Vector3.up)), 0.00001f, "Cylinder top must meet its moving knuckle.");
                        Assert.AreEqual(0.23f, root.transform.InverseTransformPoint(knuckle).y, 0.00001f, "Struts stay above the finger/contact area.");
                        Assert.AreEqual(radius, new Vector2(fingers[index].localPosition.x, fingers[index].localPosition.z).magnitude, 0.00001f);
                        Assert.AreEqual(0.13f, fingers[index].localPosition.y);
                        Assert.AreEqual(Quaternion.identity, fingers[index].localRotation);
                        Assert.AreEqual(new Vector3(0.024f, 0.28f, 0.024f), colliders[index].size);
                        Assert.AreEqual(Vector3.zero, colliders[index].center);
                        Assert.AreEqual(0.018f, links[index].localScale.x);
                        Assert.AreEqual(0.018f, links[index].localScale.z);
                    }
                }
                // An anchor in the prize area must hide its visual link instead of drawing through a prize.
                fingers[0].localPosition = new Vector3(0, -0.05f, 0.205f);
                linkage.RefreshNow(); Assert.AreEqual(0, links[0].localScale.y);
                fingers[1] = null;
                Assert.DoesNotThrow(linkage.RefreshNow);
                Assert.AreEqual(0, links[1].localScale.y, "A missing finger must not leave a floating strut.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name); field.SetValue(target, value);
        }
    }
}
