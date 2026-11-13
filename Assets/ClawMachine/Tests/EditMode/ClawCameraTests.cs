using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawCameraTests
    {
        [Test]
        public void OrbitGeometry_ClampsAnglesAndKeepsAllViewsOutsideCabinet()
        {
            Assert.AreEqual(new Vector2(12, -10), ClawCameraOrbit.ClampAngles(new Vector2(1000, -1000)));
            Vector3[] presets = { new Vector3(0, 0.39f, -3.45f), new Vector3(-3.45f, 0.39f, 0), new Vector3(3.45f, 0.39f, 0) };
            foreach (Vector3 preset in presets)
            {
                foreach (float scale in new[] { 0.85f, 1f, 1.2f })
                {
                    Vector3 offset = ClawCameraOrbit.Offset(preset, new Vector2(12, 10), scale, 1.75f, Vector3.up);
                    Assert.IsTrue(ClawCameraOrbit.IsFinite(offset));
                    Assert.GreaterOrEqual(offset.magnitude, 1.75f);
                    Assert.AreEqual(preset.magnitude * scale, offset.magnitude, 0.00001f);
                }
            }
            Vector3 tooClose = ClawCameraOrbit.Offset(Vector3.back * 0.1f, Vector2.zero, 0.85f, 1.75f, Vector3.up);
            Assert.AreEqual(1.75f, tooClose.magnitude, 0.00001f);
        }

        [Test]
        public void OrbitGeometry_RejectsNonFiniteValuesAndAlwaysReturnsFinitePosition()
        {
            Assert.AreEqual(Vector2.zero, ClawCameraOrbit.ClampAngles(new Vector2(float.NaN, 2)));
            Assert.AreEqual(Vector2.zero, ClawCameraOrbit.ClampAngles(new Vector2(2, float.PositiveInfinity)));
            Vector3 offset = ClawCameraOrbit.Offset(new Vector3(float.NaN, 0, 0), Vector2.one,
                float.PositiveInfinity, float.NaN, Vector3.zero);
            Assert.IsTrue(ClawCameraOrbit.IsFinite(offset));
            Assert.GreaterOrEqual(offset.magnitude, 1.75f);
            Vector3 extreme = ClawCameraOrbit.Offset(Vector3.one * float.MaxValue, Vector2.one * float.MaxValue,
                float.MaxValue, float.MaxValue, Vector3.up * float.MaxValue);
            Assert.IsTrue(ClawCameraOrbit.IsFinite(extreme));
            Assert.LessOrEqual(extreme.magnitude, 20.00001f);
        }

        [Test]
        public void Presets_KeepIndependentBoundedAnglesAndOneMainCamera()
        {
            using (var fixture = new CameraFixture())
            {
                CraneCameraController camera = fixture.Controller;
                camera.Select(1); camera.OrbitBy(new Vector2(8, 6));
                camera.Select(2); Assert.AreEqual(Vector2.zero, camera.OrbitAngles);
                camera.OrbitBy(new Vector2(-100, 100));
                Assert.AreEqual(new Vector2(-12, 10), camera.OrbitAngles);
                camera.OrbitBy(new Vector2(float.NaN, 1));
                Assert.AreEqual(new Vector2(-12, 10), camera.OrbitAngles);
                camera.Select(1); Assert.AreEqual(new Vector2(8, 6), camera.OrbitAngles);
                for (int selected = 1; selected <= 3; selected++)
                {
                    camera.Select(selected);
                    Assert.AreEqual(fixture.Views[selected - 1], camera.CurrentCamera);
                    for (int i = 0; i < fixture.Views.Length; i++)
                    {
                        Assert.AreEqual(i == selected - 1, fixture.Views[i].enabled);
                        Assert.IsNull(fixture.Views[i].targetTexture, "Observation never redirects a main preset into a PIP target.");
                    }
                    Assert.AreEqual(Vector3.forward, ClawMotion.MachineDirection(Vector2.up));
                }
                camera.ResetOrbit(); Assert.AreEqual(Vector2.zero, camera.OrbitAngles);
            }
        }

        [Test]
        public void Distances_CycleThreeOptionsAndIgnoreNonFiniteZoom()
        {
            using (var fixture = new CameraFixture())
            {
                CraneCameraController camera = fixture.Controller;
                Assert.AreEqual(1, camera.ObservationDistance);
                camera.CycleDistance(); Assert.AreEqual(2, camera.ObservationDistance);
                camera.CycleDistance(); Assert.AreEqual(0, camera.ObservationDistance);
                camera.CycleDistance(); Assert.AreEqual(1, camera.ObservationDistance);
                camera.Zoom(float.NaN); camera.Zoom(float.PositiveInfinity);
                Assert.AreEqual(1, camera.ObservationDistance);
                camera.Zoom(1); camera.Zoom(1); Assert.AreEqual(0, camera.ObservationDistance);
                camera.Zoom(-1); camera.Zoom(-1); camera.Zoom(-1); Assert.AreEqual(2, camera.ObservationDistance);
            }
        }

        [Test]
        public void ExternalObstacle_PullsCameraForwardWithoutEnteringCabinetRadius()
        {
            using (var fixture = new CameraFixture())
            {
                var obstacle = new GameObject("External camera obstruction");
                try
                {
                    var collider = obstacle.AddComponent<BoxCollider>();
                    obstacle.layer = 30;
                    Set(fixture.Controller, "obstructionLayers", (LayerMask)(1 << 30));
                    collider.size = new Vector3(1, 2, 0.2f);
                    obstacle.transform.position = new Vector3(0, 0.3f, -2.4f);
                    Physics.SyncTransforms();
                    fixture.Controller.Smooth = false;
                    Invoke(fixture.Controller, "LateUpdate");
                    float radius = fixture.Controller.CurrentCamera.transform.position.magnitude;
                    Assert.GreaterOrEqual(radius, 1.75f);
                    Assert.Less(radius, 2.4f, "The camera must stay in front of the external wall.");
                    Assert.IsFalse(collider.bounds.Contains(fixture.Controller.CurrentCamera.transform.position));
                }
                finally { Object.DestroyImmediate(obstacle); }
            }
        }

        private sealed class CameraFixture : System.IDisposable
        {
            public readonly GameObject Root = new GameObject("Camera fixture");
            public readonly Camera[] Views = new Camera[3];
            public readonly CraneCameraController Controller;
            public CameraFixture()
            {
                var target = new GameObject("Look target"); target.transform.SetParent(Root.transform, false);
                Vector3[] positions = { new Vector3(0, 0.39f, -3.45f), new Vector3(-3.45f, 0.39f, 0), new Vector3(3.45f, 0.39f, 0) };
                for (int i = 0; i < Views.Length; i++)
                {
                    var view = new GameObject("Preset " + i); view.transform.SetParent(Root.transform, false);
                    view.transform.position = positions[i]; Views[i] = view.AddComponent<Camera>();
                }
                Controller = Root.AddComponent<CraneCameraController>();
                Set(Controller, "views", Views); Set(Controller, "lookTarget", target.transform);
                Set(Controller, "obstructionLayers", (LayerMask)0);
                Controller.Select(1);
            }
            public void Dispose() => Object.DestroyImmediate(Root);
        }
        private static void Set(object owner, string field, object value)
        {
            FieldInfo info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, field); info.SetValue(owner, value);
        }
        private static void Invoke(object owner, string method)
        {
            MethodInfo info = owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, method); info.Invoke(owner, null);
        }
    }
}
