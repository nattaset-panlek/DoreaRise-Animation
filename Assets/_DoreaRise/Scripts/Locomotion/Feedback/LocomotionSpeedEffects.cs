using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Fades speed effects (speed-line dome, particles) in and out with movement speed instead of popping
    /// them on/off. Thresholds follow the gait speeds, so retuning Sprint / SprintFast moves them too.
    /// </summary>
    public class LocomotionSpeedEffects : MonoBehaviour
    {
        [Serializable]
        public class SpeedEffect
        {
            public GameObject target;
            [Tooltip("Starts to appear at this gait's speed x Fade In Fraction")]
            public Gait fadeInGait = Gait.Sprint;
            public float fadeInFraction = 0.9f;
            [Tooltip("Fully visible at this gait's speed x Full Fraction")]
            public Gait fullGait = Gait.SprintFast;
            public float fullFraction = 0.9f;
            [Tooltip("Shader float driving opacity (_Fade on Dorea/ScrollingTextureTransparent). Empty = on/off only")]
            public string opacityProperty = "_Fade";
            [Range(0f, 1f)] public float maxOpacity = 1f;
            [Tooltip("Scale particle emission with the intensity")]
            public bool scaleParticles = true;

            [NonSerialized] public Renderer[] renderers;
            [NonSerialized] public ParticleSystem[] particles;
            [NonSerialized] public float[] baseRates;
            [NonSerialized] public int propertyId;
            [NonSerialized] public float intensity;
            [NonSerialized] public float intensityVelocity;
            [NonSerialized] public bool prepared;
        }

        #region [Variables/Fields]
        [SerializeField] private LocomotionMotor motor;
        [Tooltip("Fade response (half-life, s)")]
        [SerializeField] private float fadeHalfLife = 0.12f;
        [Tooltip("Used when Effects is empty: children with these names fade in towards SprintFast")]
        [SerializeField] private string[] autoEffectNames = { "Mesh_SpeedDome" };
        [SerializeField] private List<SpeedEffect> effects = new List<SpeedEffect>();

        private MaterialPropertyBlock block;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<LocomotionMotor>();
            block = new MaterialPropertyBlock();

            if (effects.Count == 0)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (Array.IndexOf(autoEffectNames, child.name) >= 0)
                        effects.Add(new SpeedEffect { target = child.gameObject });
                }
            }

            foreach (SpeedEffect effect in effects) Prepare(effect);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || motor == null) return;

            float speed = motor.CurrentSpeed;
            foreach (SpeedEffect effect in effects)
            {
                if (effect == null || effect.target == null) continue;
                if (!effect.prepared) Prepare(effect);

                float from = motor.GetGaitSpeed(effect.fadeInGait) * effect.fadeInFraction;
                float to = Mathf.Max(motor.GetGaitSpeed(effect.fullGait) * effect.fullFraction, from + 0.01f);
                float goal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, speed));

                Spring.Float(ref effect.intensity, ref effect.intensityVelocity, goal, fadeHalfLife, dt);
                effect.intensity = Mathf.Clamp01(effect.intensity);

                bool visible = effect.intensity > 0.01f;
                if (effect.target.activeSelf != visible) effect.target.SetActive(visible);
                if (visible) ApplyIntensity(effect);
            }
        }
        #endregion

        #region [Private Methods]
        private static void Prepare(SpeedEffect effect)
        {
            if (effect == null || effect.target == null) return;

            effect.renderers = effect.target.GetComponentsInChildren<Renderer>(true);
            effect.particles = effect.target.GetComponentsInChildren<ParticleSystem>(true);
            effect.baseRates = new float[effect.particles.Length];
            for (int i = 0; i < effect.particles.Length; i++)
                effect.baseRates[i] = effect.particles[i].emission.rateOverTimeMultiplier;
            effect.propertyId = string.IsNullOrEmpty(effect.opacityProperty) ? 0 : Shader.PropertyToID(effect.opacityProperty);
            effect.prepared = true;
        }

        private void ApplyIntensity(SpeedEffect effect)
        {
            if (effect.propertyId != 0)
            {
                foreach (Renderer renderer in effect.renderers)
                {
                    if (renderer == null || renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty(effect.propertyId))
                        continue;
                    renderer.GetPropertyBlock(block);
                    block.SetFloat(effect.propertyId, effect.intensity * effect.maxOpacity);
                    renderer.SetPropertyBlock(block);
                }
            }

            if (!effect.scaleParticles) return;
            for (int i = 0; i < effect.particles.Length; i++)
            {
                if (effect.particles[i] == null) continue;
                ParticleSystem.EmissionModule emission = effect.particles[i].emission;
                emission.rateOverTimeMultiplier = effect.baseRates[i] * effect.intensity;
            }
        }
        #endregion
    }
}
