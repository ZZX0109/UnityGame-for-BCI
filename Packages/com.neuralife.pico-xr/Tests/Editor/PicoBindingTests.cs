using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Neuralife.XR.Tests
{
    public sealed class PicoBindingTests : InputTestFixture
    {
        [Test]
        public void OfficialPicoControllerUsageBindingsResolveButtonsAndStick()
        {
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>("Haptic");
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.PoseControl>("Pose");
            InputSystem.RegisterLayout<PICO4ControllerProfile.PICO4TouchController>();
            var controller = InputSystem.AddDevice<PICO4ControllerProfile.PICO4TouchController>();
            InputSystem.SetDeviceUsage(controller, "RightHand");
            try
            {
                using (var press = new InputAction("test", InputActionType.Value, "<XRController>{RightHand}/{PrimaryButton}"))
                using (var axis = new InputAction("test axis", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}"))
                using (var trigger = new InputAction("test trigger", InputActionType.Value, "<XRController>{RightHand}/{TriggerButton}"))
                {
                    press.Enable(); axis.Enable(); trigger.Enable();
                    Assert.That(press.controls, Does.Contain(controller.primaryButton), "PICO right primary binding: " + string.Join(",", controller.usages));
                    Assert.That(trigger.controls, Does.Contain(controller.triggerPressed));
                    Assert.That(axis.controls, Does.Contain(controller.thumbstick));
                    Press(controller.primaryButton);
                    Press(controller.triggerPressed);
                    Set(controller.thumbstick, new Vector2(0.6f, -0.8f));
                    Assert.AreEqual(1, press.ReadValue<float>(), "Action did not receive the primary button state.");
                    Assert.AreEqual(1, trigger.ReadValue<float>());
                    Assert.AreEqual(new Vector2(0.6f, -0.8f), axis.ReadValue<Vector2>());
                }
            }
            finally { InputSystem.RemoveDevice(controller); }
        }
    }
}
