using System;

namespace StreetCrossing.Traffic
{
    /// <summary>Distance is measured along the lane; stop targets constrain the FRONT of the vehicle.</summary>
    public sealed class VehicleKinematics
    {
        public const float Length = 4.2f;
        public const float HalfLength = Length / 2;
        public float Distance { get; private set; }
        public float Speed { get; private set; }
        public float CruiseSpeed { get; private set; }
        public bool Committed { get; private set; }
        public VehicleMotionState State { get; private set; }
        public float Front => Distance + HalfLength;
        public float Rear => Distance - HalfLength;
        public static float StoppingDistance(float speed, float braking, float reactionTime)
            => speed * speed / (2 * Math.Max(0.01f, braking)) + speed * reactionTime;
        public void Reset(float distance, float speed) { Distance = distance; Speed = CruiseSpeed = speed; Committed = false; State = VehicleMotionState.Cruising; }
        public void DecideAmber(float stopLine, float braking, float reactionTime)
        {
            if (Front >= stopLine || stopLine - Front <= StoppingDistance(Speed, braking, reactionTime)) Committed = true;
        }
        public void Step(float dt, float stopLine, bool mayEnter, float leaderRear, float gap, float acceleration, float braking, float reaction)
        {
            if (dt <= 0) return;
            if (Front >= stopLine - 0.001f && mayEnter) Committed = true;
            float frontLimit = leaderRear - gap;
            if (!mayEnter && !Committed) frontLimit = Math.Min(frontLimit, stopLine - 0.12f);
            float clearance = frontLimit - Front;
            // Reaction margin is conservative and acts before comfortable braking is needed.
            float available = Math.Max(0, clearance - Speed * reaction);
            float target = Math.Min(CruiseSpeed, (float)Math.Sqrt(2 * braking * available));
            float oldSpeed = Speed;
            float rate = target < Speed ? braking : acceleration;
            Speed = MoveTowards(Speed, target, rate * dt);
            float travel = (oldSpeed + Speed) * 0.5f * dt;
            // Last-centimetre constraint prevents numerical creep across a stop line or into a queue.
            if (travel > Math.Max(0, clearance)) { travel = Math.Max(0, clearance); Speed = 0; }
            Distance += travel;
            // A queued amber follower may brake behind a stopping leader. Release its unused reservation before the line.
            if (!mayEnter && Committed && Speed < 0.04f && Front < stopLine - 0.15f) Committed = false;
            if (mayEnter && Front >= stopLine) Committed = true;
            if (Speed < 0.04f) State = VehicleMotionState.Stopped;
            else if (Speed < oldSpeed - 0.001f) State = VehicleMotionState.Braking;
            else if (Speed < CruiseSpeed - 0.1f) State = VehicleMotionState.Starting;
            else State = stopLine - Front < 18 && !Committed ? VehicleMotionState.Approaching : VehicleMotionState.Cruising;
        }
        private static float MoveTowards(float value, float target, float amount)
            => value < target ? Math.Min(value + amount, target) : Math.Max(value - amount, target);
    }
}
