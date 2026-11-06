using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Critically damped springs parameterised by half-life (time to close half the gap).
    /// Exact integration, so results do not depend on frame rate.
    /// Reference: Daniel Holden, "Spring-It-On: The Game Developer's Spring-Roll-Call".
    /// </summary>
    public static class Spring
    {
        private const float FourLn2 = 2.7725887f;

        public static float HalfLifeToDamping(float halfLife)
        {
            return FourLn2 / Mathf.Max(halfLife, 1e-5f);
        }

        /// <summary>Moves x towards goal. v is the spring velocity.</summary>
        public static void Float(ref float x, ref float v, float goal, float halfLife, float dt)
        {
            float y = HalfLifeToDamping(halfLife) * 0.5f;
            float j0 = x - goal;
            float j1 = v + j0 * y;
            float eydt = Mathf.Exp(-y * dt);
            x = eydt * (j0 + j1 * dt) + goal;
            v = eydt * (v - j1 * y * dt);
        }

        /// <summary>Same as Float but for angles in degrees (takes the shortest way round).</summary>
        public static void Angle(ref float angle, ref float angularVelocity, float goal, float halfLife, float dt)
        {
            float y = HalfLifeToDamping(halfLife) * 0.5f;
            float j0 = Mathf.DeltaAngle(goal, angle);
            float j1 = angularVelocity + j0 * y;
            float eydt = Mathf.Exp(-y * dt);
            angle = goal + eydt * (j0 + j1 * dt);
            angularVelocity = eydt * (angularVelocity - j1 * y * dt);
        }

        /// <summary>
        /// Character velocity spring: velocity and acceleration are driven towards goalVelocity.
        /// Returns the exact position delta travelled during dt.
        /// </summary>
        public static Vector3 CharacterVelocity(ref Vector3 velocity, ref Vector3 acceleration,
            Vector3 goalVelocity, float halfLife, float dt)
        {
            float y = HalfLifeToDamping(halfLife) * 0.5f;
            Vector3 j0 = velocity - goalVelocity;
            Vector3 j1 = acceleration + j0 * y;
            float eydt = Mathf.Exp(-y * dt);
            float yy = y * y;

            Vector3 delta = eydt * ((-j1) / yy + (-j0 - j1 * dt) / y) + j1 / yy + j0 / y + goalVelocity * dt;
            velocity = eydt * (j0 + j1 * dt) + goalVelocity;
            acceleration = eydt * (acceleration - j1 * y * dt);
            return delta;
        }

        /// <summary>Distance a CharacterVelocity spring still travels if its goal becomes zero now.</summary>
        public static Vector3 PredictStopOffset(Vector3 velocity, Vector3 acceleration, float halfLife)
        {
            float y = HalfLifeToDamping(halfLife) * 0.5f;
            Vector3 j1 = acceleration + velocity * y;
            return j1 / (y * y) + velocity / y;
        }
    }
}
