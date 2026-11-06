using UnityEngine;
using UnityEngine.Serialization;

namespace DoreaRise.System.Controller
{
    [DefaultExecutionOrder(700)]
    public class SpineToHeadLookAt : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Activation & Weight")]
        [SerializeField] private bool enableLookAt = true;
        [Range(0f, 1f)] [SerializeField] private float globalWeight = 1f;
        [SerializeField] private float smoothSpeed = 6f;

        [Header("Front View Cutoff")]
        [SerializeField] private float frontCutoffAngle = 90f;
        [SerializeField] private float cutoffSmoothRange = 20f;

        [Header("Dynamic Movement Damping")]
        [SerializeField] private bool dampOnSprint = true;
        [SerializeField] private float sprintDampWeight = 0.25f;
        [SerializeField] private float deadZoneAngle = 2f;

        [Header("Bone Weights (Distribution)")]
        [Range(0f, 1f)] [SerializeField] private float spineWeight = 0.12f;
        [Range(0f, 1f)] [SerializeField] private float chestWeight = 0.20f;
        [Range(0f, 1f)] [SerializeField] private float neckWeight = 0.30f;
        [Range(0f, 1f)] [SerializeField] private float headWeight = 0.45f;

        [Header("Rotation Angle Limit (Y-Axis Only)")]
        [SerializeField] private float maxYawAngle = 55f;

        [Header("Bone References (Auto-Detect if Humanoid)")]
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;

        [Header("Camera Reference")]
        [SerializeField] private Camera targetCamera;

        [Header("Look Target (cinematic)")]
        [Tooltip("When set, the torso and head turn towards this instead of following the camera")]
        [SerializeField] private Transform lookTarget;

        public Animator animator;
        [FormerlySerializedAs("characterControl")]
        [Tooltip("CharacterControl or LocomotionMotor (auto-found on parents when empty)")]
        public MonoBehaviour locomotionSource;
        private ILocomotionState locomotion;
        private float currentWeight = 0f;

        // Current smoothed bone rotations for seamless blending
        private float currentSpineYaw;
        private float currentChestYaw;
        private float currentNeckYaw;
        private float currentHeadYaw;
        #endregion

        #region [Properties]
        public bool EnableLookAt
        {
            get => enableLookAt;
            set => enableLookAt = value;
        }

        public float GlobalWeight
        {
            get => globalWeight;
            set => globalWeight = value;
        }

        public Transform LookTarget
        {
            get => lookTarget;
            set => lookTarget = value;
        }
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            AutoDetectBones();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (!enableLookAt) return;
            if (targetCamera == null && Camera.main != null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            CalculateTargetWeight();
            if (currentWeight <= 0.001f) return;

            BlendTorsoTowardsCamera();
        }
        #endregion

        #region [Public Methods]
        public void SetTargetCamera(Camera cam)
        {
            targetCamera = cam;
        }
        #endregion

        #region [Private Methods]

        private void AutoDetectBones()
        {
            if (animator == null || !animator.isHuman) return;

            if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (head == null) head = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        private void CalculateTargetWeight()
        {
            float targetW = globalWeight;

            // Automatically damp look-at weight when sprinting to preserve athletic movement animation
            ILocomotionState state = LocomotionStateResolver.ResolveCached(ref locomotion, locomotionSource, this);
            if (dampOnSprint && state != null && state.IsSprinting)
            {
                targetW *= sprintDampWeight;
            }

            currentWeight = Mathf.Lerp(currentWeight, targetW, Time.deltaTime * smoothSpeed);
        }

        private void BlendTorsoTowardsCamera()
        {
            Vector3 camPos = targetCamera.transform.position;
            Vector3 charPos = transform.position;

            Vector3 camForward = targetCamera.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude > 0.001f) camForward.Normalize();
            else camForward = transform.forward;

            Vector3 charForward = transform.forward;
            charForward.y = 0f;
            if (charForward.sqrMagnitude > 0.001f) charForward.Normalize();

            Vector3 dirToCam = camPos - charPos;
            dirToCam.y = 0f;
            if (dirToCam.sqrMagnitude > 0.001f) dirToCam.Normalize();
            else dirToCam = charForward;

            float targetYaw;
            if (lookTarget != null)
            {
                Vector3 dirToTarget = lookTarget.position - charPos;
                dirToTarget.y = 0f;
                targetYaw = dirToTarget.sqrMagnitude > 0.001f ? Vector3.SignedAngle(charForward, dirToTarget, Vector3.up) : 0f;
            }
            else
            {
                float yawFromCamForward = Vector3.SignedAngle(charForward, camForward, Vector3.up);
                float cameraPosAngle = Vector3.SignedAngle(charForward, dirToCam, Vector3.up);

                float targetLookWeight = Mathf.InverseLerp(frontCutoffAngle - cutoffSmoothRange, frontCutoffAngle + cutoffSmoothRange, Mathf.Abs(cameraPosAngle));
                targetYaw = yawFromCamForward * targetLookWeight;
            }

            targetYaw = Mathf.Sign(targetYaw) * Mathf.Max(0f, Mathf.Abs(targetYaw) - deadZoneAngle);
            targetYaw = Mathf.Clamp(targetYaw, -maxYawAngle, maxYawAngle);

            ApplyBoneYaw(spine, ref currentSpineYaw, targetYaw * spineWeight, smoothSpeed * 0.8f);
            ApplyBoneYaw(chest, ref currentChestYaw, targetYaw * chestWeight, smoothSpeed * 0.9f);
            ApplyBoneYaw(neck, ref currentNeckYaw, targetYaw * neckWeight, smoothSpeed * 1.1f);
            ApplyBoneYaw(head, ref currentHeadYaw, targetYaw * headWeight, smoothSpeed * 1.2f);
        }

        private void ApplyBoneYaw(Transform bone, ref float currentYaw, float targetYaw, float boneSmoothSpeed)
        {
            if (bone == null) return;

            currentYaw = Mathf.Lerp(currentYaw, targetYaw * currentWeight, Time.deltaTime * boneSmoothSpeed);
            bone.rotation = Quaternion.AngleAxis(currentYaw, Vector3.up) * bone.rotation;
        }
        #endregion
    }
}
