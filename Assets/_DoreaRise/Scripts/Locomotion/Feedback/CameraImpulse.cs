using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Additive camera feel on top of whatever drives the camera (CameraControl, Timeline, a static shot):
    /// noise shakes, FOV punches and dolly pushes that snap in and settle with critically damped springs.
    /// The previous frame's offset is removed in Update, so nothing accumulates on cameras nobody resets.
    /// Runs on unscaled time so impacts still read during hit-stop.
    /// </summary>
    [DefaultExecutionOrder(1200)]
    [RequireComponent(typeof(Camera))]
    public class CameraImpulse : MonoBehaviour
    {
        private class ActiveShake
        {
            public float positionAmplitude;
            public float rotationAmplitude;
            public float duration;
            public float time;
            public float seed;
        }

        #region [Variables/Fields]
        [Header("Shake")]
        [Tooltip("Noise frequency of shakes (Hz)")]
        [SerializeField] private float shakeFrequency = 22f;
        [Range(0f, 2f)] [SerializeField] private float shakeScale = 1f;

        [Header("Springs (half-life, s)")]
        [SerializeField] private float fovHalfLife = 0.12f;
        [SerializeField] private float dollyHalfLife = 0.15f;

        private Camera targetCamera;
        private readonly List<ActiveShake> shakes = new List<ActiveShake>();
        private float nextSeed = 1f;

        private float fovOffset;
        private float fovVelocity;
        private float dollyOffset;
        private float dollyVelocity;
        private float dollyTarget;

        private Vector3 appliedPosition;
        private Quaternion appliedRotation = Quaternion.identity;
        private float appliedFov;
        #endregion

        #region [Properties]
        public float ShakeScale
        {
            get => shakeScale;
            set => shakeScale = Mathf.Max(0f, value);
        }
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            // Undo last frame's offset before the camera driver runs (LateUpdate).
            transform.position -= appliedPosition;
            transform.rotation = Quaternion.Inverse(appliedRotation) * transform.rotation;
            if (targetCamera != null) targetCamera.fieldOfView -= appliedFov;
            appliedPosition = Vector3.zero;
            appliedRotation = Quaternion.identity;
            appliedFov = 0f;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            Spring.Float(ref fovOffset, ref fovVelocity, 0f, fovHalfLife, dt);
            Spring.Float(ref dollyOffset, ref dollyVelocity, dollyTarget, dollyHalfLife, dt);

            Vector3 shakePosition = Vector3.zero;
            Vector3 shakeEuler = Vector3.zero;
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                ActiveShake shake = shakes[i];
                shake.time += dt;
                if (shake.time >= shake.duration)
                {
                    shakes.RemoveAt(i);
                    continue;
                }

                float envelope = 1f - shake.time / shake.duration;
                envelope *= envelope;
                float t = shake.time * shakeFrequency;
                Vector3 noise = new Vector3(
                    Mathf.PerlinNoise(shake.seed, t) * 2f - 1f,
                    Mathf.PerlinNoise(shake.seed + 31.7f, t) * 2f - 1f,
                    Mathf.PerlinNoise(shake.seed + 63.1f, t) * 2f - 1f);
                shakePosition += noise * (shake.positionAmplitude * envelope);
                shakeEuler += new Vector3(noise.y, noise.x, noise.z * 0.5f) * (shake.rotationAmplitude * envelope);
            }

            appliedPosition = transform.rotation * (shakePosition * shakeScale + Vector3.back * dollyOffset);
            appliedRotation = transform.rotation * Quaternion.Euler(shakeEuler * shakeScale) * Quaternion.Inverse(transform.rotation);
            appliedFov = fovOffset;

            transform.position += appliedPosition;
            transform.rotation = appliedRotation * transform.rotation;
            if (targetCamera != null) targetCamera.fieldOfView += appliedFov;
        }
        #endregion

        #region [Public Methods]
        /// <summary>Noise shake that fades out over <paramref name="duration"/> seconds.</summary>
        public void Shake(float positionAmplitude, float rotationAmplitude, float duration)
        {
            if (duration <= 0f) return;
            shakes.Add(new ActiveShake
            {
                positionAmplitude = positionAmplitude,
                rotationAmplitude = rotationAmplitude,
                duration = duration,
                seed = nextSeed
            });
            nextSeed += 7.31f;
        }

        /// <summary>FOV punch: peaks at <paramref name="degrees"/> almost instantly, then settles back.</summary>
        public void KickFov(float degrees)
        {
            fovVelocity += degrees * PeakImpulse(fovHalfLife);
        }

        /// <summary>Camera dolly punch along its view axis. Positive = pulls back (sense of speed).</summary>
        public void PushBack(float metres)
        {
            dollyVelocity += metres * PeakImpulse(dollyHalfLife);
        }

        /// <summary>Continuous dolly offset the camera eases towards (e.g. pull back while sprinting).</summary>
        public void SetDollyTarget(float metres)
        {
            dollyTarget = metres;
        }
        #endregion

        #region [Private Methods]
        /// <summary>Initial velocity that makes a critically damped spring at rest peak at 1.</summary>
        private static float PeakImpulse(float halfLife)
        {
            float y = Spring.HalfLifeToDamping(halfLife) * 0.5f;
            return y * 2.7182818f;
        }
        #endregion
    }
}
