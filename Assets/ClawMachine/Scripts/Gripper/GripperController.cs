using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClawMachine
{
    public sealed class GripperController : MonoBehaviour
    {
        [SerializeField] private Rigidbody toolBody;
        [SerializeField] private Transform[] fingers;
        [SerializeField] private Transform[] contactPads;
        [SerializeField] private SphereCollider[] contactColliders;
        [SerializeField] private Collider[] physicalColliders;
        [SerializeField] private Collider[] environmentColliders;
        private System.Collections.Generic.IReadOnlyList<PrizePhysics> collisionContext;
        [SerializeField] private float openRadius = 0.205f;
        [SerializeField] private float closedRadius = 0.065f;
        private FixedJoint grip;
        private PrizePhysics held;
        private Vector3 initialGripOffset;
        private float openness = 1;
        private struct CollisionPairState
        {
            public Collider A, B;
            public bool PreviouslyIgnored;
        }
        private readonly List<CollisionPairState> heldCollisionPairs = new List<CollisionPairState>();
        private bool restoringReleasedPairs;
        public event Action SlippedEvent;
        public bool IsConfigured => toolBody && fingers != null && fingers.Length == 3 && fingers[0] && fingers[1] && fingers[2] && contactColliders != null && contactColliders.Length == 3 &&
            contactColliders[0] && contactColliders[1] && contactColliders[2] && PhysicalGeometryReady;
        private bool PhysicalGeometryReady
        {
            get
            {
                if (physicalColliders == null || physicalColliders.Length < 3) return false;
                foreach (Collider collider in physicalColliders) if (!collider || collider.isTrigger) return false;
                if (environmentColliders != null) foreach (Collider collider in environmentColliders) if (!collider) return false;
                return true;
            }
        }
        public bool IsHolding => held && grip;
        public PrizePhysics HeldPrize => held;
        public SphereCollider[] ContactColliders => contactColliders;
        public int LastContacts { get; private set; }
        public bool Slipped { get; private set; }
        public bool LastJointBroke { get; private set; }
        public float LastGripTorque { get; private set; }
        public float LastGripDrift { get; private set; }
        public float PeakConstraintForce { get; private set; }
        public float PeakConstraintTorque { get; private set; }
        public float LowestPointOffset => -0.025f;
        private Vector3 ToolPhysicsOffset => toolBody ? toolBody.position - toolBody.transform.position : Vector3.zero;
        private static Vector3 SurfacePhysicsPosition(Collider collider) => collider.transform.position + (collider.attachedRigidbody ? collider.attachedRigidbody.position - collider.attachedRigidbody.transform.position : Vector3.zero);
        private void SetRadius(int i, float radius)
        {
            if (!fingers[i]) return;
            float angle = (90 + i * 120) * Mathf.Deg2Rad;
            fingers[i].localPosition = new Vector3(Mathf.Cos(angle) * radius, 0.13f, Mathf.Sin(angle) * radius);
        }
        public void SetCollisionContext(System.Collections.Generic.IReadOnlyList<PrizePhysics> prizes) { collisionContext = prizes; }
        public void SetOpenness(float amount) { TrySetOpenness(amount); }
        public bool TrySetOpenness(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || fingers == null) return false;
            float requested = Mathf.Clamp01(amount);
            if (requested < openness)
            {
                if (collisionContext == null || !IsConfigured) return false;
                CloseTowards(requested, collisionContext); return true;
            }
            openness = requested;
            float radius = Mathf.Lerp(closedRadius, openRadius, openness);
            for (int i = 0; i < fingers.Length; i++)
            {
                float current = new Vector2(fingers[i].localPosition.x, fingers[i].localPosition.z).magnitude;
                SetRadius(i, Mathf.Max(current, radius));
            }
            return true;
        }
        public void CloseTowards(float amount, System.Collections.Generic.IReadOnlyList<PrizePhysics> prizes)
        {
            if (!IsConfigured) return;
            openness = Mathf.Clamp01(amount);
            float desired = Mathf.Lerp(closedRadius, openRadius, openness);
            for (int i = 0; i < fingers.Length; i++)
            {
                float current = new Vector2(fingers[i].localPosition.x, fingers[i].localPosition.z).magnitude;
                float low = Mathf.Min(desired, current), high = current;
                SetRadius(i, low);
                if (!FingerPenetrates(i, prizes, 0.001f)) continue;
                // Bounded continuous closure: retain the last nonpenetrating contact pose.
                for (int iteration = 0; iteration < 9; iteration++)
                {
                    float middle = (low + high) * 0.5f; SetRadius(i, middle);
                    if (FingerPenetrates(i, prizes, 0.001f)) low = middle; else high = middle;
                }
                SetRadius(i, high);
            }
        }
        private bool FingerPenetrates(int index, System.Collections.Generic.IReadOnlyList<PrizePhysics> prizes, float allowed)
        {
            foreach (Collider physical in physicalColliders)
            {
                if (!physical || !physical.transform.IsChildOf(fingers[index])) continue;
                foreach (PrizePhysics prize in prizes)
                {
                    if (!prize || !prize.gameObject.activeInHierarchy) continue;
                    foreach (Collider surface in prize.ContactColliders)
                        if (surface && Physics.ComputePenetration(physical, physical.transform.position + ToolPhysicsOffset, physical.transform.rotation,
                            surface, SurfacePhysicsPosition(surface), surface.transform.rotation, out _, out float depth) && depth > allowed) return true;
                }
            }
            if (environmentColliders != null) foreach (Collider physical in physicalColliders)
                if (physical && physical.transform.IsChildOf(fingers[index])) foreach (Collider surface in environmentColliders)
                    if (surface && surface.enabled && surface.gameObject.activeInHierarchy && !surface.isTrigger && Physics.ComputePenetration(physical, physical.transform.position + ToolPhysicsOffset, physical.transform.rotation,
                        surface, SurfacePhysicsPosition(surface), surface.transform.rotation, out _, out float depth) && depth > allowed) return true;
            return false;
        }
        public bool CanDescend(Vector3 delta, System.Collections.Generic.IReadOnlyList<PrizePhysics> prizes)
        {
            if (!IsConfigured) return false;
            foreach (Collider physical in physicalColliders)
                if (physical) foreach (PrizePhysics prize in prizes)
                {
                    if (!prize || !prize.gameObject.activeInHierarchy) continue;
                    foreach (Collider surface in prize.ContactColliders)
                        if (surface && Physics.ComputePenetration(physical, physical.transform.position + ToolPhysicsOffset + delta, physical.transform.rotation,
                            surface, SurfacePhysicsPosition(surface), surface.transform.rotation, out _, out float depth) && depth > 0.001f) return false;
                }
            if (environmentColliders != null) foreach (Collider physical in physicalColliders)
                if (physical) foreach (Collider surface in environmentColliders)
                    if (surface && surface.enabled && surface.gameObject.activeInHierarchy && !surface.isTrigger && Physics.ComputePenetration(physical, physical.transform.position + ToolPhysicsOffset + delta, physical.transform.rotation,
                        surface, SurfacePhysicsPosition(surface), surface.transform.rotation, out _, out float depth) && depth > 0.001f) return false;
            return true;
        }
        public GraspFailure TryGrip(PrizePhysics prize)
        {
            Release(false); Slipped = false;
            LastJointBroke = false; LastGripTorque = LastGripDrift = PeakConstraintForce = PeakConstraintTorque = 0;
            if (!IsConfigured) return GraspFailure.Configuration;
            GraspFailure result = GraspContactEvaluator.Evaluate(toolBody.position, prize, contactColliders, out int contacts);
            LastContacts = contacts;
            if (result != GraspFailure.None) return result;
            grip = toolBody.gameObject.AddComponent<FixedJoint>();
            grip.connectedBody = prize.Body; grip.autoConfigureConnectedAnchor = false; grip.anchor = Vector3.zero;
            grip.connectedAnchor = Quaternion.Inverse(prize.Body.rotation) * (toolBody.position - prize.Body.position);
            grip.breakForce = prize.Profile.breakForce; grip.breakTorque = prize.Profile.breakTorque; grip.enableCollision = false;
            held = prize; initialGripOffset = Quaternion.Inverse(toolBody.rotation) * (prize.Body.worldCenterOfMass - toolBody.position);
            SuppressHeldCollisionPairs(prize);
            return GraspFailure.None;
        }
        private void SuppressHeldCollisionPairs(PrizePhysics prize)
        {
            restoringReleasedPairs = false;
            foreach (Collider physical in physicalColliders) foreach (Collider surface in prize.ContactColliders)
            {
                if (!ActivePair(physical, surface)) continue;
                bool recorded = false;
                foreach (CollisionPairState previous in heldCollisionPairs)
                    if (previous.A == physical && previous.B == surface) { recorded = true; break; }
                if (recorded) continue;
                heldCollisionPairs.Add(new CollisionPairState {
                    A = physical, B = surface, PreviouslyIgnored = Physics.GetIgnoreCollision(physical, surface)
                });
                // Native joint collision suppression does not reliably remove
                // existing compound-finger contact constraints in this scene.
                Physics.IgnoreCollision(physical, surface, true);
            }
        }
        private static bool ActivePair(Collider a, Collider b) =>
            a && b && a.gameObject.activeInHierarchy && b.gameObject.activeInHierarchy;

        private void RestoreCollisionPairs(bool immediately)
        {
            for (int index = heldCollisionPairs.Count - 1; index >= 0; index--)
            {
                CollisionPairState pair = heldCollisionPairs[index];
                if (ActivePair(pair.A, pair.B))
                {
                    // Release can occur with closed fingers touching the prize.
                    // Keep only penetrating pairs suppressed until gravity/opening
                    // separates them, rather than inject a depenetration impulse.
                    if (!immediately && !pair.PreviouslyIgnored && pair.A.enabled && pair.B.enabled &&
                        Physics.ComputePenetration(pair.A, SurfacePhysicsPosition(pair.A), pair.A.transform.rotation,
                            pair.B, SurfacePhysicsPosition(pair.B), pair.B.transform.rotation, out _, out float depth) && depth > 0.00001f)
                        continue;
                    Physics.IgnoreCollision(pair.A, pair.B, pair.PreviouslyIgnored);
                }
                // Destroyed/inactive shapes have no live collision pair in PhysX.
                heldCollisionPairs.RemoveAt(index);
            }
            if (heldCollisionPairs.Count == 0) restoringReleasedPairs = false;
        }
        private void FixedUpdate()
        {
            if (held && !held.gameObject.activeInHierarchy) Release(false);
            if (held && !grip) CheckGrip();
            if (!held && grip) Release(false);
            if (!held && heldCollisionPairs.Count > 0) RestoreCollisionPairs(!restoringReleasedPairs);
        }
        private void OnJointBreak(float breakForce)
        {
            if (!held) return;
            LastJointBroke = true; Slipped = true;
            Release(true); SlippedEvent?.Invoke();
        }
        public void CheckGrip()
        {
            if (!held) return;
            bool broken = !grip;
            Vector3 offset = held.Body.worldCenterOfMass - toolBody.position;
            float torque = Vector3.Cross(offset, Physics.gravity * held.Body.mass).magnitude;
            float drift = Vector3.Distance(Quaternion.Inverse(toolBody.rotation) * (held.Body.worldCenterOfMass - toolBody.position), initialGripOffset);
            LastJointBroke = broken; LastGripTorque = torque; LastGripDrift = drift;
            if (grip) { PeakConstraintForce = Mathf.Max(PeakConstraintForce, grip.currentForce.magnitude); PeakConstraintTorque = Mathf.Max(PeakConstraintTorque, grip.currentTorque.magnitude); }
            if (broken || torque > held.Profile.breakTorque || drift > held.Profile.maximumGripDrift)
            {
                Slipped = true; Release(true); SlippedEvent?.Invoke();
            }
        }
        public void Release(bool released)
        {
            if (grip) { grip.connectedBody = null; if (Application.isPlaying) Destroy(grip); else DestroyImmediate(grip); grip = null; }
            if (held)
            {
                if (released)
                {
                    held.MarkReleased();
                    // A jointed prize can fall asleep during the stationary
                    // release. Removing the joint must wake the original body
                    // so gravity resumes; never award a suspended sleeping prize.
                    held.Body.WakeUp();
                }
                held = null;
            }
            if (released && Application.isPlaying && isActiveAndEnabled)
                restoringReleasedPairs = heldCollisionPairs.Count > 0;
            else RestoreCollisionPairs(true);
        }
        public void ResetGrip() { Release(false); Slipped = false; LastContacts = 0; SetOpenness(1); }
        private void OnDisable() { Release(false); SetOpenness(1); }
        private void OnDestroy() { Release(false); }
        private void OnApplicationQuit() { Release(false); }
    }
}
