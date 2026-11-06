using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Offset inertialization: when the source pose switches instantly, the difference between the last
    /// displayed pose and the new pose (plus its velocity) is kept as an offset that decays with a
    /// critically damped spring. Gives snappy transitions without the "mushy" look of long crossfades.
    /// Reference: David Bollo, "Inertialization: High-Performance Animation Transitions in Gears of War";
    /// Daniel Holden, "Spring-It-On".
    /// </summary>
    public struct InertializationJob : IAnimationJob
    {
        public NativeArray<TransformStreamHandle> handles;

        public NativeArray<Vector3> lastPosition;
        public NativeArray<Quaternion> lastRotation;
        public NativeArray<Vector3> lastScale;
        public NativeArray<Vector3> lastPositionVelocity;
        public NativeArray<Vector3> lastRotationVelocity;

        public NativeArray<Vector3> offsetPosition;
        public NativeArray<Vector3> offsetPositionVelocity;
        public NativeArray<Vector3> offsetRotation;
        public NativeArray<Vector3> offsetRotationVelocity;
        public NativeArray<Vector3> offsetScale;
        public NativeArray<Vector3> offsetScaleVelocity;

        /// <summary>[0] processed request id, [1] has pose history, [2] offsets active.</summary>
        public NativeArray<int> state;
        public NativeArray<float> elapsed;

        public int requestId;
        public float halfLife;

        public static InertializationJob Create(Animator animator, IList<Transform> bones)
        {
            int count = bones.Count;
            InertializationJob job = new InertializationJob
            {
                handles = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent),
                lastPosition = new NativeArray<Vector3>(count, Allocator.Persistent),
                lastRotation = new NativeArray<Quaternion>(count, Allocator.Persistent),
                lastScale = new NativeArray<Vector3>(count, Allocator.Persistent),
                lastPositionVelocity = new NativeArray<Vector3>(count, Allocator.Persistent),
                lastRotationVelocity = new NativeArray<Vector3>(count, Allocator.Persistent),
                offsetPosition = new NativeArray<Vector3>(count, Allocator.Persistent),
                offsetPositionVelocity = new NativeArray<Vector3>(count, Allocator.Persistent),
                offsetRotation = new NativeArray<Vector3>(count, Allocator.Persistent),
                offsetRotationVelocity = new NativeArray<Vector3>(count, Allocator.Persistent),
                offsetScale = new NativeArray<Vector3>(count, Allocator.Persistent),
                offsetScaleVelocity = new NativeArray<Vector3>(count, Allocator.Persistent),
                state = new NativeArray<int>(3, Allocator.Persistent),
                elapsed = new NativeArray<float>(1, Allocator.Persistent),
                halfLife = 0.1f
            };

            for (int i = 0; i < count; i++)
            {
                job.handles[i] = animator.BindStreamTransform(bones[i]);
                job.lastRotation[i] = Quaternion.identity;
                job.lastScale[i] = Vector3.one;
            }

            return job;
        }

        public void Dispose()
        {
            if (handles.IsCreated) handles.Dispose();
            if (lastPosition.IsCreated) lastPosition.Dispose();
            if (lastRotation.IsCreated) lastRotation.Dispose();
            if (lastScale.IsCreated) lastScale.Dispose();
            if (lastPositionVelocity.IsCreated) lastPositionVelocity.Dispose();
            if (lastRotationVelocity.IsCreated) lastRotationVelocity.Dispose();
            if (offsetPosition.IsCreated) offsetPosition.Dispose();
            if (offsetPositionVelocity.IsCreated) offsetPositionVelocity.Dispose();
            if (offsetRotation.IsCreated) offsetRotation.Dispose();
            if (offsetRotationVelocity.IsCreated) offsetRotationVelocity.Dispose();
            if (offsetScale.IsCreated) offsetScale.Dispose();
            if (offsetScaleVelocity.IsCreated) offsetScaleVelocity.Dispose();
            if (state.IsCreated) state.Dispose();
            if (elapsed.IsCreated) elapsed.Dispose();
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            float dt = stream.deltaTime;
            bool hasHistory = state[1] != 0;
            bool request = requestId != state[0];
            if (request) state[0] = requestId;

            bool begin = request && hasHistory;
            if (begin)
            {
                state[2] = 1;
                elapsed[0] = 0f;
            }

            bool active = state[2] != 0 && dt > 0f;
            float y = Spring.HalfLifeToDamping(Mathf.Max(halfLife, 0.001f)) * 0.5f;
            float eydt = Mathf.Exp(-y * dt);

            for (int i = 0; i < handles.Length; i++)
            {
                TransformStreamHandle handle = handles[i];
                if (!handle.IsValid(stream)) continue;

                Vector3 position = handle.GetLocalPosition(stream);
                Quaternion rotation = handle.GetLocalRotation(stream);
                Vector3 scale = handle.GetLocalScale(stream);

                if (begin)
                {
                    Vector3 posOffset = lastPosition[i] - position;
                    Vector3 rotOffset = ToScaledAngleAxis(Shortest(lastRotation[i] * Quaternion.Inverse(rotation)));
                    offsetPosition[i] = posOffset;
                    offsetPositionVelocity[i] = DecayVelocity(lastPositionVelocity[i], posOffset, y);
                    offsetRotation[i] = rotOffset;
                    offsetRotationVelocity[i] = DecayVelocity(lastRotationVelocity[i], rotOffset, y);
                    offsetScale[i] = lastScale[i] - scale;
                    offsetScaleVelocity[i] = Vector3.zero;
                }

                if (active)
                {
                    Vector3 posOffset = offsetPosition[i], posOffsetVel = offsetPositionVelocity[i];
                    Vector3 rotOffset = offsetRotation[i], rotOffsetVel = offsetRotationVelocity[i];
                    Vector3 sclOffset = offsetScale[i], sclOffsetVel = offsetScaleVelocity[i];
                    Decay(ref posOffset, ref posOffsetVel, y, eydt, dt);
                    Decay(ref rotOffset, ref rotOffsetVel, y, eydt, dt);
                    Decay(ref sclOffset, ref sclOffsetVel, y, eydt, dt);
                    offsetPosition[i] = posOffset;
                    offsetPositionVelocity[i] = posOffsetVel;
                    offsetRotation[i] = rotOffset;
                    offsetRotationVelocity[i] = rotOffsetVel;
                    offsetScale[i] = sclOffset;
                    offsetScaleVelocity[i] = sclOffsetVel;

                    position += posOffset;
                    rotation = FromScaledAngleAxis(rotOffset) * rotation;
                    scale += sclOffset;

                    handle.SetLocalPosition(stream, position);
                    handle.SetLocalRotation(stream, rotation);
                    handle.SetLocalScale(stream, scale);
                }

                if (hasHistory && dt > 1e-6f)
                {
                    lastPositionVelocity[i] = (position - lastPosition[i]) / dt;
                    lastRotationVelocity[i] = ToScaledAngleAxis(Shortest(rotation * Quaternion.Inverse(lastRotation[i]))) / dt;
                }

                lastPosition[i] = position;
                lastRotation[i] = rotation;
                lastScale[i] = scale;
            }

            state[1] = 1;

            if (active)
            {
                elapsed[0] += dt;
                if (elapsed[0] > halfLife * 10f) state[2] = 0;
            }
        }

        /// <summary>
        /// Offset velocity at the start of a transition. The new pose's own velocity is unknown, so the old output
        /// velocity cannot be used as-is (the new clip's motion would be counted twice and overshoot). Keep only
        /// its component that closes the gap, capped so a critically damped decay never crosses the target
        /// (Bollo-style clamp).
        /// </summary>
        private static Vector3 DecayVelocity(Vector3 velocity, Vector3 offset, float y)
        {
            float distance = offset.magnitude;
            if (distance < 1e-6f) return Vector3.zero;

            Vector3 axis = offset / distance;
            float along = Vector3.Dot(velocity, axis);
            if (along >= 0f) return Vector3.zero;
            return axis * Mathf.Max(along, -distance * y);
        }

        private static void Decay(ref Vector3 x, ref Vector3 v, float y, float eydt, float dt)
        {
            Vector3 j1 = v + x * y;
            x = eydt * (x + j1 * dt);
            v = eydt * (v - j1 * y * dt);
        }

        private static Quaternion Shortest(Quaternion q)
        {
            return q.w < 0f ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : q;
        }

        private static Vector3 ToScaledAngleAxis(Quaternion q)
        {
            Vector3 v = new Vector3(q.x, q.y, q.z);
            float length = v.magnitude;
            if (length < 1e-8f) return v * 2f;
            float angle = 2f * Mathf.Atan2(length, q.w);
            return v * (angle / length);
        }

        private static Quaternion FromScaledAngleAxis(Vector3 v)
        {
            float angle = v.magnitude;
            if (angle < 1e-8f) return new Quaternion(v.x * 0.5f, v.y * 0.5f, v.z * 0.5f, 1f).normalized;
            float half = angle * 0.5f;
            float s = Mathf.Sin(half) / angle;
            return new Quaternion(v.x * s, v.y * s, v.z * s, Mathf.Cos(half));
        }
    }
}
