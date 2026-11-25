using StreetCrossing.Core;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using UnityEngine;
using Neuralife.XR;

namespace StreetCrossing.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PedestrianPlayerController : MonoBehaviour
    {
        private CharacterController body;
        private StreetGameManager game;
        private StreetScenarioDefinition scenario;
        private IStreetInput input;
        private float yaw, pitch, gravity;
        private PicoRig xrRig;
        public Camera View { get; private set; }
        public float Heading => PicoXR.Requested && View ? View.transform.eulerAngles.y : yaw;
        public float Pitch => PicoXR.Requested && View ? Mathf.DeltaAngle(0, View.transform.eulerAngles.x) : pitch;
        public void Initialize(StreetGameManager owner, StreetScenarioDefinition definition, IStreetInput source)
        {
            game = owner; scenario = definition; input = source;
            body = GetComponent<CharacterController>();
            body.height = 1.8f; body.radius = 0.3f; body.center = new Vector3(0, 0.9f, 0);
            body.stepOffset = 0.22f; body.skinWidth = 0.025f; body.minMoveDistance = 0; body.slopeLimit = 45;
            GameObject cameraObject = new GameObject("Pedestrian eye camera"); cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.65f, 0);
            View = cameraObject.AddComponent<Camera>(); cameraObject.tag = "MainCamera";
            View.nearClipPlane = 0.06f; View.farClipPlane = 160; View.fieldOfView = StreetPreferences.FieldOfView;
            // Use the procedural sky when the world supplied one; the warm solid tone stays as the fallback.
            View.clearFlags = RenderSettings.skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            View.backgroundColor = new Color(0.74f, 0.84f, 0.88f);
            cameraObject.AddComponent<AudioListener>(); ResetPose();
            xrRig = PicoRig.Attach(View, transform);
        }
        public void SetInputSource(IStreetInput source) { input = source; }
        public void ResetPose() => ResetPose(scenario.playerSpawn, 0);
        public void ResetPose(Vector3 position, float heading)
        {
            if (float.IsNaN(heading) || float.IsInfinity(heading) || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                throw new System.ArgumentException("Player reset pose must be finite.");
            body.enabled = false; transform.position = position;
            yaw = Mathf.Repeat(heading, 360); pitch = gravity = 0;
            transform.rotation = Quaternion.Euler(0, yaw, 0); body.enabled = true;
            if (View != null && !PicoXR.Requested) View.transform.localRotation = Quaternion.identity;
            if (xrRig) xrRig.Recenter();
        }
        private void Update()
        {
            if (View == null || game == null) return;
            if (PicoXR.Requested) { UpdateVR(); return; }
            View.fieldOfView = StreetPreferences.FieldOfView;
            if (!game.IsPlaying || input == null) return;
            Vector2 look = input.Look * StreetPreferences.Sensitivity;
            yaw += look.x; pitch = Mathf.Clamp(pitch - look.y, -70, 70);
            transform.rotation = Quaternion.Euler(0, yaw, 0); View.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            Vector2 motion = Vector2.ClampMagnitude(input.Movement, 1);
            float speed = StreetPreferences.ComfortMode ? scenario.comfortableSpeed : scenario.walkingSpeed;
            if (input.FastWalk && !StreetPreferences.ComfortMode && StreetPreferences.FastWalkEnabled) speed = scenario.fastWalkingSpeed;
            if (body.isGrounded && gravity < 0) gravity = -2;
            gravity = Mathf.Max(-20, gravity - 9.81f * Time.deltaTime);
            Vector3 displacement = (transform.right * motion.x + transform.forward * motion.y) * speed;
            displacement.y = gravity; body.Move(displacement * Mathf.Min(Time.deltaTime, 0.1f));
            if (transform.position.y < -2) game.RequestRestart();
            if (input.InteractPressed) InspectFacingSign();
        }
        public void InspectFacingSign()
        {
            if (!game || !game.IsPlaying || !View) return;
            if (Physics.Raycast(View.transform.position, View.transform.forward, out RaycastHit hit, 3))
            {
                StreetInspectable sign = hit.collider.GetComponentInParent<StreetInspectable>();
                if (sign != null) game.Inspect(sign);
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            VehicleController vehicle = hit.collider.GetComponentInParent<VehicleController>();
            if (vehicle != null && game.IsPlaying) game.RegisterContact(vehicle.EncounterId, vehicle.Motion.Speed);
        }
        private void UpdateVR()
        {
            if (!game.IsPlaying || input == null || !PicoXR.MovementAllowed) return;
            if (xrRig) xrRig.SnapTurn(PicoXR.Controls.TurnDegrees);
            Vector3 displacement = PicoControls.PlanarMotion(View.transform, input.Movement) * Mathf.Min(1.2f, scenario.comfortableSpeed);
            if (body.isGrounded && gravity < 0) gravity = -2;
            gravity = Mathf.Max(-20, gravity - 9.81f * Time.deltaTime);
            displacement.y = gravity; body.Move(displacement * Mathf.Min(Time.deltaTime, 0.1f));
            if (transform.position.y < -2) game.RequestRestart();
            if (input.InteractPressed) InspectFacingSign();
        }
    }
}
