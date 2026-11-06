using UnityEngine;
using UnityEngine.Serialization;

namespace DoreaRise.System.Controller
{
    public class ProceduralLegTurn : MonoBehaviour
    {
        [Header("References")]
        [FormerlySerializedAs("characterControl")]
        [Tooltip("CharacterControl or LocomotionMotor (auto-found on parents when empty)")]
        [SerializeField] private MonoBehaviour locomotionSource;
        [SerializeField] private Animator animator;

        [Header("Turn Settings")]
        [SerializeField] private float smoothSpeed = 10f;
        [SerializeField] private float maxTurnAngle = 45f;
        [SerializeField] private float turnSensitivity = 0.15f;

        [Header("Bone Weights")]
        [Range(0f, 1f)][SerializeField] private float hipWeight = 0.3f;
        [Range(0f, 1f)][SerializeField] private float legWeight = 0.5f;
        [Range(0f, 1f)][SerializeField] private float footWeight = 0.8f;

        [Header("Bone References")]
        [SerializeField] private Transform leftHip;
        [SerializeField] private Transform rightHip;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;
        [SerializeField] private Transform leftLegEnd;
        [SerializeField] private Transform rightLegEnd;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;

        private ILocomotionState locomotion;
        private float previousYaw;
        private float currentYawOffset;
        private float currentBlendWeight;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void Start()
        {
            previousYaw = transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            ILocomotionState state = LocomotionStateResolver.ResolveCached(ref locomotion, locomotionSource, this);
            if (state == null || animator == null) return;

            float targetWeight = state.CurrentState == MovementState.Walk ? 1f : 0f;
            currentBlendWeight = Mathf.Lerp(currentBlendWeight, targetWeight, Time.deltaTime * smoothSpeed);

            float currentYaw = transform.eulerAngles.y;
            float turnDelta = Mathf.DeltaAngle(previousYaw, currentYaw) / Time.deltaTime;
            previousYaw = currentYaw;

            if (currentBlendWeight <= 0.001f)
            {
                currentYawOffset = 0f;
                SnapFootPosition(leftFoot, leftLegEnd);
                SnapFootPosition(rightFoot, rightLegEnd);
                return;
            }

            float targetYawOffset = Mathf.Clamp(turnDelta * turnSensitivity, -maxTurnAngle, maxTurnAngle);
            currentYawOffset = Mathf.Lerp(currentYawOffset, targetYawOffset, Time.deltaTime * smoothSpeed);

            float activeYaw = currentYawOffset * currentBlendWeight;

            ApplyYawToBone(leftHip, activeYaw * hipWeight);
            ApplyYawToBone(rightHip, activeYaw * hipWeight);
            ApplyYawToBone(leftLeg, activeYaw * legWeight);
            ApplyYawToBone(rightLeg, activeYaw * legWeight);

            SnapFootPosition(leftFoot, leftLegEnd);
            SnapFootPosition(rightFoot, rightLegEnd);

            ApplyYawToBone(leftFoot, activeYaw * footWeight);
            ApplyYawToBone(rightFoot, activeYaw * footWeight);
        }

        private void ApplyYawToBone(Transform bone, float angle)
        {
            if (bone == null) return;
            bone.rotation = Quaternion.AngleAxis(angle, Vector3.up) * bone.rotation;
        }

        private void SnapFootPosition(Transform foot, Transform legEnd)
        {
            if (foot == null || legEnd == null) return;
            foot.position = legEnd.position;
        }
    }
}