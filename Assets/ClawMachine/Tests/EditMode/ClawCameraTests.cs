using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawCameraTests
    {
        [Test]
        public void OrbitGeometry_WrapsFullCircleAndKeepsElevatedViewsOutsideCabinet()
        {
            Assert.AreEqual(new Vector2(-80, -8), ClawCameraOrbit.ClampAngles(new Vector2(1000, -1000)));
            Assert.AreEqual(new Vector2(1, 45), ClawCameraOrbit.ClampAngles(new Vector2(721, 1000)));
            Vector3[] presets = { new Vector3(0, 0.39f, -3.45f), new Vector3(-3.45f, 0.39f, 0), new Vector3(3.45f, 0.39f, 0) };
            foreach (Vector3 preset in presets)
            {
                foreach (float scale in new[] { 0.85f, 1f, 1.2f })
                {
                    for (int yaw = -360; yaw <= 360; yaw += 15)
                    {
                        Vector3 offset = ClawCameraOrbit.Offset(preset, new Vector2(yaw, 45), scale, 1.75f, Vector3.up);
                        Assert.IsTrue(ClawCameraOrbit.IsFinite(offset));
                        Assert.GreaterOrEqual(offset.magnitude, 1.75f);
                        Assert.AreEqual(preset.magnitude * scale, offset.magnitude, 0.00001f);
                    }
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
        public void Orbit_CrossesFrontSidesAndBackUsingOneMainCameraAndFixedMachineAxes()
        {
            using (var fixture = new CameraFixture())
            {
                CraneCameraController camera = fixture.Controller;
                camera.Smooth = false;
                camera.ResetOrbit();
                for (int quarter = 0; quarter < 8; quarter++)
                {
                    camera.OrbitBy(new Vector2(90, 0));
                    Invoke(camera, "LateUpdate");
                    Assert.AreSame(fixture.Views[0], camera.CurrentCamera);
                    Vector3 pose = camera.CurrentCamera.transform.position;
                    Assert.GreaterOrEqual(pose.magnitude, 1.75f);
                    float expectedYaw = (quarter + 1) % 4 * 90;
                    Vector3 expected = ClawCameraOrbit.Offset(new Vector3(0, 0.39f, -3.45f), new Vector2(expectedYaw, 0), 1, 1.75f, Vector3.up);
                    Assert.Less(Vector3.Distance(expected, pose), 0.0001f);
                    for (int i = 0; i < fixture.Views.Length; i++)
                    {
                        Assert.AreEqual(i == 0, fixture.Views[i].enabled);
                        Assert.IsNull(fixture.Views[i].targetTexture, "Observation never redirects a main camera into a PIP target.");
                    }
                    Assert.AreEqual(Vector3.forward, ClawMotion.MachineDirection(Vector2.up));
                }
                camera.OrbitBy(new Vector2(15, 200));
                Assert.AreEqual(new Vector2(15, 45), camera.OrbitAngles);
                camera.OrbitBy(new Vector2(float.NaN, 1));
                Assert.AreEqual(new Vector2(15, 45), camera.OrbitAngles);
                camera.ResetOrbit(); Assert.AreEqual(Vector2.zero, camera.OrbitAngles);
            }
        }

        [Test]
        public void ObservationViewport_ReservesMenusAndRestoresWholeGameViewWithoutChangingAxes()
        {
            using (var fixture = new CameraFixture())
            {
                CraneCameraController camera = fixture.Controller;
                camera.SetObservationViewport(new Rect(0, 0, 0.74f, 1));
                foreach (Camera view in fixture.Views) Assert.AreEqual(new Rect(0, 0, 0.74f, 1), view.rect);
                camera.Select(3);
                Assert.AreEqual(new Rect(0, 0, 0.74f, 1), camera.CurrentCamera.rect);
                camera.SetObservationViewport(new Rect(float.NaN, 0, 1, 1));
                Assert.AreEqual(new Rect(0, 0, 0.74f, 1), camera.ObservationViewport);
                camera.SetObservationViewport(new Rect(0, 0, 1, 1));
                foreach (Camera view in fixture.Views) Assert.AreEqual(new Rect(0, 0, 1, 1), view.rect);
                Assert.AreEqual(Vector3.forward, ClawMotion.MachineDirection(Vector2.up));
            }
        }

        [Test]
        public void Wheel_ChangesDistanceContinuouslyWithinSafeRangeAndRejectsNonFiniteZoom()
        {
            using (var fixture = new CameraFixture())
            {
                CraneCameraController camera = fixture.Controller;
                Assert.AreEqual(1, camera.DistanceScale);
                camera.Zoom(float.NaN); camera.Zoom(float.PositiveInfinity);
                Assert.AreEqual(1, camera.DistanceScale);
                camera.Zoom(0.25f); Assert.Less(camera.DistanceScale, 1);
                Assert.Greater(camera.DistanceScale, 0.95f, "Fractional wheel motion should not jump a whole distance preset.");
                camera.Zoom(-0.25f); Assert.AreEqual(1, camera.DistanceScale, 0.0001f);
                for (int i = 0; i < 20; i++) camera.Zoom(1);
                Assert.AreEqual(0.7f, camera.DistanceScale, 0.0001f);
                for (int i = 0; i < 20; i++) camera.Zoom(-1);
                Assert.AreEqual(1.35f, camera.DistanceScale, 0.0001f);
            }
        }

        [Test]
        public void SmoothOrbit_LargeDragNeverCutsThroughTheCabinet()
        {
            using (var fixture = new CameraFixture())
            {
                CraneCameraController camera = fixture.Controller;
                camera.Smooth = true;
                for (int i = 0; i < 80; i++)
                {
                    camera.OrbitBy(new Vector2(179, i % 2 == 0 ? 45 : -45));
                    Invoke(camera, "LateUpdate");
                    Assert.GreaterOrEqual(camera.CurrentCamera.transform.position.magnitude, 1.75f - 0.00001f);
                }
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
