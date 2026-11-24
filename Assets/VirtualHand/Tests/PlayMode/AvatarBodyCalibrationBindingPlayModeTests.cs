using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Neuralife.VirtualHand;

public sealed class AvatarBodyCalibrationBindingPlayModeTests
{
    private readonly List<GameObject> createdObjects = new List<GameObject>();

    [UnityTest]
    public IEnumerator AppliesValidatedDimensionsAndRetainsMeasurementSources()
    {
        GameObject previewObject = CreateObject("Preview");
        SeatedArmPreview preview = previewObject.AddComponent<SeatedArmPreview>();
        AvatarBodyCalibrationBinding binding = previewObject.AddComponent<AvatarBodyCalibrationBinding>();
        GameObject head = CreateObject("Tracked head");
        GameObject origin = CreateObject("Body origin");

        AvatarBodyCalibration calibration = CreateCalibration();
        binding.preview = preview;
        binding.trackedHead = head.transform;
        binding.headTrackingValid = true;
        binding.bodyOrigin = origin.transform;
        binding.calibration = calibration;

        Assert.That(binding.ApplyCalibration(), Is.True, binding.LastError);
        Assert.That(preview.upperArmLengthMeters, Is.EqualTo(0.33f).Within(0.0001f));
        Assert.That(preview.lowerArmLengthMeters, Is.EqualTo(0.29f).Within(0.0001f));
        Assert.That(preview.leftShoulderBodyPositionMeters.x, Is.EqualTo(-0.22f).Within(0.0001f));
        Assert.That(preview.rightShoulderBodyPositionMeters.x, Is.EqualTo(0.22f).Within(0.0001f));
        Assert.That(preview.leftShoulderBodyPositionMeters.y, Is.EqualTo(-0.04f).Within(0.0001f));
        Assert.That(preview.rightShoulderBodyPositionMeters.y, Is.EqualTo(-0.04f).Within(0.0001f));
        Assert.That(binding.AppliedCalibration, Is.Not.Null);
        Assert.That(binding.AppliedCalibration.shoulderWidth.source, Is.EqualTo(AvatarBodyMeasurementSource.Measured));
        Assert.That(binding.AppliedCalibration.upperArmLength.source, Is.EqualTo(AvatarBodyMeasurementSource.ManualEdit));
        Assert.That(binding.AppliedCalibration.forearmLength.source, Is.EqualTo(AvatarBodyMeasurementSource.Measured));

        yield return null;
    }

    [UnityTest]
    public IEnumerator HeadNodsKeepBodyOriginAndShoulderLineUprightThroughVerticalGaze()
    {
        GameObject previewObject = CreateObject("Preview");
        SeatedArmPreview preview = previewObject.AddComponent<SeatedArmPreview>();
        AvatarBodyCalibrationBinding binding = previewObject.AddComponent<AvatarBodyCalibrationBinding>();
        GameObject head = CreateObject("Tracked head");
        GameObject origin = CreateObject("Body origin");

        const float yawDegrees = 37f;
        Vector3 offsetMeters = new Vector3(0.03f, -0.04f, 0.02f);
        Vector3 headPosition = new Vector3(-0.8f, 1.55f, 2.1f);
        head.transform.position = headPosition;
        binding.preview = preview;
        binding.trackedHead = head.transform;
        binding.headTrackingValid = true;
        binding.bodyOrigin = origin.transform;
        binding.bodyOriginOffsetFromTrackedHeadMeters = offsetMeters;
        binding.calibration = CreateCalibration();
        Assert.That(binding.ApplyCalibration(), Is.True, binding.LastError);

        Quaternion expectedYaw = Quaternion.Euler(0f, yawDegrees, 0f);
        float[] pitches = { -80f, 80f, -90f, 90f };
        foreach (float pitch in pitches)
        {
            head.transform.rotation = Quaternion.Euler(pitch, yawDegrees, 0f);
            Assert.That(binding.RefreshBinding(), Is.True, binding.LastError);
            Assert.That(Vector3.Angle(origin.transform.up, Vector3.up), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(origin.transform.forward, expectedYaw * Vector3.forward), Is.LessThan(0.01f));

            Vector3 expectedPosition = headPosition + expectedYaw * offsetMeters;
            Assert.That(Vector3.Distance(origin.transform.position, expectedPosition), Is.LessThan(0.0001f));

            Vector3 shoulderLine = origin.transform.rotation *
                                   (preview.rightShoulderBodyPositionMeters - preview.leftShoulderBodyPositionMeters);
            Assert.That(Mathf.Abs(Vector3.Dot(shoulderLine, Vector3.up)), Is.LessThan(0.0001f));
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator MissingHeadHidesPreviewOutputs()
    {
        GameObject previewObject = CreateObject("Preview");
        SeatedArmPreview preview = previewObject.AddComponent<SeatedArmPreview>();
        AvatarBodyCalibrationBinding binding = previewObject.AddComponent<AvatarBodyCalibrationBinding>();
        GameObject origin = CreateObject("Body origin");
        GameObject leftOutput = CreateObject("Left output");
        GameObject rightOutput = CreateObject("Right output");

        preview.leftOutputRoot = leftOutput;
        preview.rightOutputRoot = rightOutput;
        binding.preview = preview;
        binding.bodyOrigin = origin.transform;
        binding.calibration = CreateCalibration();

        Assert.That(binding.ApplyCalibration(), Is.True, binding.LastError);
        Assert.That(binding.RefreshBinding(), Is.False);
        Assert.That(preview.bodyOrigin, Is.Null);
        Assert.That(leftOutput.activeSelf, Is.False);
        Assert.That(rightOutput.activeSelf, Is.False);

        yield return null;
    }

    [UnityTest]
    public IEnumerator ExistingHeadTransformIsRejectedWhenTrackingIsLost()
    {
        var preview = CreateObject("Preview").AddComponent<SeatedArmPreview>();
        var binding = preview.gameObject.AddComponent<AvatarBodyCalibrationBinding>();
        binding.preview = preview;
        binding.trackedHead = CreateObject("Head").transform;
        binding.bodyOrigin = CreateObject("Body origin").transform;
        binding.calibration = CreateCalibration();
        binding.headTrackingValid = true;
        Assert.IsTrue(binding.ApplyCalibration());
        Assert.IsTrue(binding.RefreshBinding());
        binding.headTrackingValid = false;
        Assert.IsFalse(binding.RefreshBinding());
        Assert.IsNull(preview.bodyOrigin, "A retained Transform must not count as valid tracking.");
        binding.headTrackingValid = true;
        Assert.IsTrue(binding.RefreshBinding());
        Assert.AreSame(binding.bodyOrigin, preview.bodyOrigin);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = 0; i < createdObjects.Count; i++)
        {
            if (createdObjects[i])
                Object.Destroy(createdObjects[i]);
        }

        yield return null;
        createdObjects.Clear();
    }

    private GameObject CreateObject(string objectName)
    {
        GameObject instance = new GameObject(objectName);
        createdObjects.Add(instance);
        return instance;
    }

    private static AvatarBodyCalibration CreateCalibration()
    {
        return new AvatarBodyCalibration
        {
            shoulderWidth = new AvatarBodyMeasurement(0.44f, AvatarBodyMeasurementSource.Measured),
            upperArmLength = new AvatarBodyMeasurement(0.33f, AvatarBodyMeasurementSource.ManualEdit),
            forearmLength = new AvatarBodyMeasurement(0.29f, AvatarBodyMeasurementSource.Measured),
            torsoHeight = new AvatarBodyMeasurement(0.58f, AvatarBodyMeasurementSource.Measured),
            eyeHeightFromPelvis = new AvatarBodyMeasurement(0.62f, AvatarBodyMeasurementSource.ManualEdit),
            seatedPelvisHeightFromFloor = new AvatarBodyMeasurement(0.49f, AvatarBodyMeasurementSource.Measured)
        };
    }
}
