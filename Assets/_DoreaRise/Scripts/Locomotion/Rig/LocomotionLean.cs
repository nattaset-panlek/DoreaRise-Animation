using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Leans the upper body into turns and against acceleration, distributed over a spine chain.
    /// Only bones above the hips are touched, so the legs and feet keep their authored contacts
    /// (unlike rotating the whole model). Runs after the animation, before foot/hand IK.
    /// </summary>
    [DefaultExecutionOrder(800)]
    public class LocomotionLean : MonoBehaviour
    {
        [Serializable]
        public class LeanBone
        {
            public Transform bone;
            [Range(0f, 1f)] public float weight = 0.33f;
        }

        #region [Variables/Fields]
        [Header("References")]
        [SerializeField] private LocomotionMotor motor;

        [Header("Bones (upper body only — not the bone the hips hang from)")]
        [SerializeField] private LeanBone[] bones = new LeanBone[0];
        [Tooltip("Used when Bones is empty: found by name under the character at startup (survives rig re-exports)")]
        [SerializeField] private string[] autoBoneNames = { "Spine.001", "Spine.002", "Spine.003" };
        [SerializeField] private float[] autoBoneWeights = { 0.3f, 0.35f, 0.35f };

        [Header("Roll (lean into turns)")]
        [Tooltip("Degrees of roll per m/s² of sideways acceleration")]
        [SerializeField] private float rollPerAcceleration = 1f;
        [Tooltip("Max roll at walking speed")]
        [SerializeField] private float maxRoll = 15f;
        [Tooltip("Max roll at SprintFast speed — carving into high-speed turns")]
        [SerializeField] private float maxRollAtTopSpeed = 28f;

        [Header("Pitch (lean forward when speeding up, back when braking)")]
        [Tooltip("Degrees of pitch per m/s² of forward acceleration")]
        [SerializeField] private float pitchPerAcceleration = 0.5f;
        [SerializeField] private float maxPitch = 10f;

        [Header("Twist (upper body leads the turn)")]
        [Tooltip("Degrees of spine twist per degree/second of turning")]
        [SerializeField] private float twistPerYawRate = 0.04f;
        [SerializeField] private float maxTwist = 25f;
        [SerializeField] private float twistHalfLife = 0.06f;

        [Header("Feel")]
        [Tooltip("Lean response time (half-life, s)")]
        [SerializeField] private float halfLife = 0.1f;
        [Range(0f, 1f)] [SerializeField] private float weight = 1f;
        [Tooltip("Fade the lean out while an action clip (stop, dash...) is playing — authored clips carry their own lean")]
        [Range(0f, 1f)] [SerializeField] private float weightDuringActions = 0.3f;
        private float fullBodyBlend = 1f;
        private float fullBodyBlendVelocity;

        private float roll;
        private float rollVelocity;
        private float pitch;
        private float pitchVelocity;
        private float actionBlend = 1f;
        private float actionBlendVelocity;
        private float twist;
        private float twistVelocity;
        #endregion

        #region [Properties]
        public float Weight
        {
            get => weight;
            set => weight = Mathf.Clamp01(value);
        }

        public float CurrentRoll => roll;
        public float CurrentPitch => pitch;
        public float CurrentTwist => twist;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<LocomotionMotor>();
            if (!HasAnyBone()) FindBonesByName();
        }

        private void OnEnable()
        {
            if (motor != null) motor.Teleported += ResetState;
        }

        private void OnDisable()
        {
            if (motor != null) motor.Teleported -= ResetState;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (motor == null || dt <= 0f || weight <= 0f) return;

            Vector3 local = motor.LocalAcceleration;
            float speedT = Mathf.InverseLerp(motor.GetGaitSpeed(Gait.Walk), motor.GetGaitSpeed(Gait.SprintFast), motor.CurrentSpeed);
            float rollLimit = Mathf.Lerp(maxRoll, maxRollAtTopSpeed, speedT);
            float targetRoll = Mathf.Clamp(-local.x * rollPerAcceleration, -rollLimit, rollLimit);
            float targetPitch = Mathf.Clamp(local.z * pitchPerAcceleration, -maxPitch, maxPitch);

            LocomotionActionKind kind = motor.CurrentActionKind;
            bool inAction = kind != LocomotionActionKind.None;
            // whole-body authored moves (slide, jumps, falls, landings) get no lean or twist on top of the clip
            bool fullBody = kind == LocomotionActionKind.Slide || kind == LocomotionActionKind.Jump
                || kind == LocomotionActionKind.Fall || kind == LocomotionActionKind.Land;
            Spring.Float(ref actionBlend, ref actionBlendVelocity, fullBody ? 0f : inAction ? weightDuringActions : 1f, halfLife, dt);
            Spring.Float(ref fullBodyBlend, ref fullBodyBlendVelocity, fullBody ? 0f : 1f, halfLife, dt);
            Spring.Float(ref roll, ref rollVelocity, targetRoll, halfLife, dt);
            Spring.Float(ref pitch, ref pitchVelocity, targetPitch, halfLife, dt);

            // Twist is not faded during actions: a pivot is exactly where the chest should lead the hips.
            float targetTwist = Mathf.Clamp(motor.YawRate * twistPerYawRate, -maxTwist, maxTwist);
            Spring.Float(ref twist, ref twistVelocity, targetTwist, twistHalfLife, dt);

            float scale = weight * actionBlend;
            float twistScale = weight * Mathf.Clamp01(fullBodyBlend);
            if (Mathf.Abs(roll * scale) < 0.01f && Mathf.Abs(pitch * scale) < 0.01f && Mathf.Abs(twist * twistScale) < 0.01f)
                return;

            Vector3 up = motor.transform.up;
            Vector3 forward = motor.transform.forward;
            Vector3 right = motor.transform.right;

            foreach (LeanBone lean in bones)
            {
                if (lean == null || lean.bone == null) continue;
                float w = lean.weight * scale;
                lean.bone.rotation = Quaternion.AngleAxis(twist * lean.weight * twistScale, up)
                                   * Quaternion.AngleAxis(roll * w, forward)
                                   * Quaternion.AngleAxis(pitch * w, right)
                                   * lean.bone.rotation;
            }
        }
        #endregion

        #region [Public Methods]
        /// <summary>Drop all lean springs (after a cut).</summary>
        public void ResetState()
        {
            roll = rollVelocity = pitch = pitchVelocity = twist = twistVelocity = 0f;
            actionBlend = 1f;
            actionBlendVelocity = 0f;
        }

        public void SetBones(Transform[] chain, float[] weights)
        {
            bones = new LeanBone[chain.Length];
            for (int i = 0; i < chain.Length; i++)
                bones[i] = new LeanBone { bone = chain[i], weight = i < weights.Length ? weights[i] : 0.33f };
        }
        #endregion

        #region [Private Methods]
        private bool HasAnyBone()
        {
            foreach (LeanBone lean in bones)
                if (lean != null && lean.bone != null) return true;
            return false;
        }

        private void FindBonesByName()
        {
            Transform searchRoot = motor != null ? motor.transform : transform;
            Transform[] all = searchRoot.GetComponentsInChildren<Transform>(true);
            List<Transform> chain = new List<Transform>();
            List<float> weights = new List<float>();

            for (int i = 0; i < autoBoneNames.Length; i++)
            {
                foreach (Transform candidate in all)
                {
                    if (candidate.name != autoBoneNames[i]) continue;
                    chain.Add(candidate);
                    weights.Add(i < autoBoneWeights.Length ? autoBoneWeights[i] : 0.33f);
                    break;
                }
            }

            if (chain.Count == 0)
                Debug.LogWarning($"[LocomotionLean] No lean bones found by name under {searchRoot.name}", this);
            SetBones(chain.ToArray(), weights.ToArray());
        }
        #endregion
    }
}
