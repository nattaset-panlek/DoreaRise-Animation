using UnityEngine;
using UnityEngine.Serialization;

namespace DoreaRise.System.Controller
{
    [DefaultExecutionOrder(900)]
    public class FootIK : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("References")]
        [FormerlySerializedAs("characterControl")]
        [Tooltip("CharacterControl or LocomotionMotor (auto-found on parents when empty)")]
        [SerializeField] private MonoBehaviour locomotionSource;
        [Tooltip("Root/Pelvis bone above both hips — used for body height adjustment")]
        [SerializeField] private Transform bodyBone;

        [Header("Left Leg Bones")]
        [Tooltip("Hip.L — upper leg bone, pivot at hip joint")]
        [SerializeField] private Transform hipL;
        [Tooltip("Leg.L — lower leg / knee (child of Hip.L)")]
        [SerializeField] private Transform legL;
        [Tooltip("Leg.end.L — ankle marker (child of Leg.L, no mesh weight)")]
        [SerializeField] private Transform legEndL;
        [Tooltip("Foot.L — foot bone (disconnected from leg hierarchy)")]
        [SerializeField] private Transform footL;

        [Header("Right Leg Bones")]
        [Tooltip("Hip.R — upper leg bone, pivot at hip joint")]
        [SerializeField] private Transform hipR;
        [Tooltip("Leg.R — lower leg / knee (child of Hip.R)")]
        [SerializeField] private Transform legR;
        [Tooltip("Leg.end.R — ankle marker (child of Leg.R, no mesh weight)")]
        [SerializeField] private Transform legEndR;
        [Tooltip("Foot.R — foot bone (disconnected from leg hierarchy)")]
        [SerializeField] private Transform footR;

        [Header("Activation")]
        [SerializeField] private bool enableFootIK = true;
        [SerializeField] private bool activeOnWalk = true;
        [SerializeField] private bool activeOnSprint = false;
        [SerializeField] private bool activeOnSprintFast = false;

        [Header("IK Mode")]
        [Tooltip("true = fully replace leg animation with IK\nfalse = blend IK on top of animation")]
        [SerializeField] private bool overrideLegAnimation = true;
        [Tooltip("Max IK weight when overrideLegAnimation is off")]
        [Range(0f, 1f)]
        [SerializeField] private float blendWeight = 0.7f;

        [Header("Raycast")]
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private float raycastDistance = 1.5f;
        [SerializeField] private float raycastOriginHeight = 0.5f;
        [SerializeField] private float footOffset = 0.02f;

        [Header("Smoothing")]
        [SerializeField] private float ikSmoothSpeed = 12f;
        [SerializeField] private float ikBlendSpeed = 8f;

        [Header("Knee Hint")]
        [Tooltip("Distance forward from knee for auto pole-target (uses the character root forward)")]
        [SerializeField] private float kneeHintOffset = 0.3f;

        [Header("Foot Rotation Offset")]
        [Tooltip("Euler offset applied to both feet after surface alignment")]
        [SerializeField] private Vector3 footRotationOffset = Vector3.zero;

        [Header("Step Height")]
        [Tooltip("Extra lift during foot swing phase to prevent dragging")]
        [SerializeField] private float stepHeight = 0.08f;
        [Tooltip("Animated ankle height above ground to start detecting swing")]
        [SerializeField] private float stepSwingThreshold = 0.04f;
        [Tooltip("Animated ankle height at which step lift peaks")]
        [SerializeField] private float stepPeakHeight = 0.15f;

        [Header("Body Adjustment")]
        [SerializeField] private bool enableBodyAdjust = true;
        [SerializeField] private float bodyOffsetSmooth = 8f;
        [SerializeField] private float maxBodyOffset = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private bool showFootTrail = true;
        [SerializeField] private bool showVelocityArrow = true;

        private ILocomotionState locomotion;
        private float currentIKWeight;
        private float currentBodyOffset;

        private Vector3 smoothTargetL;
        private Vector3 smoothTargetR;
        private Vector3 hitNormalL = Vector3.up;
        private Vector3 hitNormalR = Vector3.up;
        private bool hasHitL;
        private bool hasHitR;
        private float groundYL;
        private float groundYR;

        private Vector3 kneeHintPosL;
        private Vector3 kneeHintPosR;

        private float upperLenL, lowerLenL;
        private float upperLenR, lowerLenR;
        private bool boneLengthsCached;

        private const int TrailCapacity = 40;
        private Vector3[] trailL = new Vector3[TrailCapacity];
        private Vector3[] trailR = new Vector3[TrailCapacity];
        private int trailWriteL;
        private int trailWriteR;
        private int trailCountL;
        private int trailCountR;
        private float trailTimer;
        private const float TrailInterval = 0.025f;

        private Vector3 prevFootPosL;
        private Vector3 prevFootPosR;
        private Vector3 footVelocityL;
        private Vector3 footVelocityR;
        #endregion

        #region [Properties]
        public bool EnableFootIK { get => enableFootIK; set => enableFootIK = value; }
        public bool OverrideLegAnimation { get => overrideLegAnimation; set => overrideLegAnimation = value; }
        public float CurrentIKWeight => currentIKWeight;
        public bool LeftHasHit => hasHitL;
        public bool RightHasHit => hasHitR;
        public float UpperLengthL => upperLenL;
        public float LowerLengthL => lowerLenL;
        public float UpperLengthR => upperLenR;
        public float LowerLengthR => lowerLenR;
        #endregion

        #region [Unity Methods]
        private void OnValidate()
        {
            CacheBoneLengths();
        }

        private void Start()
        {
            CacheBoneLengths();

            if (legEndL) smoothTargetL = legEndL.position;
            if (legEndR) smoothTargetR = legEndR.position;
        }

        private void LateUpdate()
        {
            if (!enableFootIK) return;
            if (LocomotionStateResolver.ResolveCached(ref locomotion, locomotionSource, this) == null) return;
            if (!AreBoneRefsValid()) return;
            if (!boneLengthsCached) CacheBoneLengths();

            UpdateIKWeight();

            if (currentIKWeight > 0.001f)
            {
                RaycastGround(legEndL, ref smoothTargetL, ref hitNormalL, ref hasHitL, ref groundYL);
                RaycastGround(legEndR, ref smoothTargetR, ref hitNormalR, ref hasHitR, ref groundYR);

                ApplyStepLift(legEndL, ref smoothTargetL, hasHitL, groundYL);
                ApplyStepLift(legEndR, ref smoothTargetR, hasHitR, groundYR);

                if (enableBodyAdjust && bodyBone != null)
                    AdjustBodyHeight();

                ProcessLeg(hipL, legL, legEndL, footL,
                    smoothTargetL, hitNormalL, hasHitL,
                    ref kneeHintPosL, upperLenL, lowerLenL);

                ProcessLeg(hipR, legR, legEndR, footR,
                    smoothTargetR, hitNormalR, hasHitR,
                    ref kneeHintPosR, upperLenR, lowerLenR);
            }

            SnapFoot(footL, legEndL);
            SnapFoot(footR, legEndR);

            RecordTrail();
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos) return;

            Color leftColor = new Color(0.2f, 1f, 0.3f);
            Color rightColor = new Color(0.3f, 0.5f, 1f);

            if (hipL && legL && legEndL && footL)
                DrawLegGizmos(hipL, legL, legEndL, footL,
                    smoothTargetL, kneeHintPosL, hitNormalL, hasHitL, leftColor);

            if (hipR && legR && legEndR && footR)
                DrawLegGizmos(hipR, legR, legEndR, footR,
                    smoothTargetR, kneeHintPosR, hitNormalR, hasHitR, rightColor);

            if (Application.isPlaying && currentIKWeight > 0.001f)
            {
                DrawFootTrail(trailL, trailWriteL, trailCountL, footVelocityL, leftColor);
                DrawFootTrail(trailR, trailWriteR, trailCountR, footVelocityR, rightColor);
            }
        }
        #endregion

        #region [Public Methods]
        public void RecalculateBoneLengths()
        {
            boneLengthsCached = false;
            CacheBoneLengths();
        }
        #endregion

        #region [Private Methods]
        private void CacheBoneLengths()
        {
            if (hipL && legL && legEndL)
            {
                upperLenL = Vector3.Distance(hipL.position, legL.position);
                lowerLenL = Vector3.Distance(legL.position, legEndL.position);
            }
            if (hipR && legR && legEndR)
            {
                upperLenR = Vector3.Distance(hipR.position, legR.position);
                lowerLenR = Vector3.Distance(legR.position, legEndR.position);
            }
            boneLengthsCached = upperLenL > 0.001f && lowerLenL > 0.001f
                             && upperLenR > 0.001f && lowerLenR > 0.001f;
        }

        private void UpdateIKWeight()
        {
            MovementState state = locomotion.CurrentState;
            bool shouldBeActive = (activeOnWalk && state == MovementState.Walk)
                               || (activeOnSprint && state == MovementState.Sprint)
                               || (activeOnSprintFast && state == MovementState.SprintFast);

            float targetWeight = shouldBeActive
                ? (overrideLegAnimation ? 1f : blendWeight)
                : 0f;

            currentIKWeight = Mathf.MoveTowards(currentIKWeight, targetWeight, Time.deltaTime * ikBlendSpeed);
        }

        private void RaycastGround(Transform legEnd, ref Vector3 smoothTarget,
            ref Vector3 hitNormal, ref bool hasHit, ref float groundY)
        {
            Vector3 anklePos = legEnd.position;
            Vector3 rayOrigin = anklePos + Vector3.up * raycastOriginHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                raycastDistance + raycastOriginHeight, groundLayer))
            {
                hasHit = true;
                groundY = hit.point.y;
                Vector3 rawTarget = hit.point + Vector3.up * footOffset;
                smoothTarget = Vector3.Lerp(smoothTarget, rawTarget, Time.deltaTime * ikSmoothSpeed);
                hitNormal = Vector3.Slerp(hitNormal, hit.normal, Time.deltaTime * ikSmoothSpeed);
            }
            else
            {
                hasHit = false;
                smoothTarget = Vector3.Lerp(smoothTarget, anklePos, Time.deltaTime * ikSmoothSpeed);
                hitNormal = Vector3.Slerp(hitNormal, Vector3.up, Time.deltaTime * ikSmoothSpeed);
            }
        }

        private void ApplyStepLift(Transform legEnd, ref Vector3 smoothTarget, bool hasHit, float groundY)
        {
            if (!hasHit || stepHeight <= 0f) return;

            float animatedHeight = legEnd.position.y - groundY;
            if (animatedHeight <= stepSwingThreshold) return;

            float t = Mathf.Clamp01((animatedHeight - stepSwingThreshold) / Mathf.Max(0.001f, stepPeakHeight - stepSwingThreshold));
            float lift = stepHeight * Mathf.Sin(t * Mathf.PI);
            smoothTarget.y += lift;
        }

        private void RecordTrail()
        {
            if (!showGizmos || !showFootTrail) return;

            if (footL)
            {
                footVelocityL = (footL.position - prevFootPosL) / Mathf.Max(Time.deltaTime, 0.0001f);
                prevFootPosL = footL.position;
            }
            if (footR)
            {
                footVelocityR = (footR.position - prevFootPosR) / Mathf.Max(Time.deltaTime, 0.0001f);
                prevFootPosR = footR.position;
            }

            trailTimer += Time.deltaTime;
            if (trailTimer < TrailInterval) return;
            trailTimer = 0f;

            if (footL)
            {
                trailL[trailWriteL % TrailCapacity] = footL.position;
                trailWriteL++;
                if (trailCountL < TrailCapacity) trailCountL++;
            }
            if (footR)
            {
                trailR[trailWriteR % TrailCapacity] = footR.position;
                trailWriteR++;
                if (trailCountR < TrailCapacity) trailCountR++;
            }
        }

        private void ProcessLeg(Transform hip, Transform leg, Transform legEnd, Transform foot,
            Vector3 target, Vector3 surfaceNormal, bool hasHit,
            ref Vector3 kneeHintPos, float upperLen, float lowerLen)
        {
            Vector3 forward = locomotion.transform.forward;
            kneeHintPos = leg.position + forward * kneeHintOffset;

            Quaternion animHipRot = hip.rotation;
            Quaternion animLegRot = leg.rotation;

            SolveTwoBoneIK(hip, leg, legEnd, target, kneeHintPos, upperLen, lowerLen);

            Quaternion ikHipRot = hip.rotation;
            Quaternion ikLegRot = leg.rotation;

            hip.rotation = Quaternion.Slerp(animHipRot, ikHipRot, currentIKWeight);
            leg.rotation = Quaternion.Slerp(animLegRot, ikLegRot, currentIKWeight);

            if (foot != null)
            {
                Quaternion offsetRot = Quaternion.Euler(footRotationOffset);

                if (hasHit)
                {
                    Quaternion correction = Quaternion.FromToRotation(foot.up, surfaceNormal);
                    Quaternion alignedRot = correction * foot.rotation * offsetRot;
                    foot.rotation = Quaternion.Slerp(foot.rotation, alignedRot, currentIKWeight);
                }
                else
                {
                    foot.rotation = foot.rotation * Quaternion.Slerp(Quaternion.identity, offsetRot, currentIKWeight);
                }
            }
        }

        private void SolveTwoBoneIK(Transform upper, Transform lower, Transform end,
            Vector3 target, Vector3 poleTarget, float upperLen, float lowerLen)
        {
            Vector3 upperPos = upper.position;
            Vector3 toTarget = target - upperPos;
            float targetDist = toTarget.magnitude;

            if (targetDist < 0.0001f) return;

            float maxReach = upperLen + lowerLen - 0.001f;
            float minReach = Mathf.Abs(upperLen - lowerLen) + 0.001f;
            targetDist = Mathf.Clamp(targetDist, minReach, maxReach);
            target = upperPos + toTarget.normalized * targetDist;

            float projDist = (upperLen * upperLen + targetDist * targetDist - lowerLen * lowerLen)
                           / (2f * targetDist);
            float perpDistSq = upperLen * upperLen - projDist * projDist;
            float perpDist = perpDistSq > 0f ? Mathf.Sqrt(perpDistSq) : 0f;

            Vector3 targetDir = (target - upperPos).normalized;

            Vector3 toPole = poleTarget - upperPos;
            Vector3 bendDir = (toPole - Vector3.Dot(toPole, targetDir) * targetDir).normalized;

            if (bendDir.sqrMagnitude < 0.001f)
            {
                bendDir = Vector3.Cross(targetDir, Vector3.up).normalized;
                if (bendDir.sqrMagnitude < 0.001f)
                    bendDir = Vector3.Cross(targetDir, Vector3.right).normalized;
            }

            Vector3 kneePos = upperPos + targetDir * projDist + bendDir * perpDist;

            Vector3 currentUpperDir = lower.position - upper.position;
            Vector3 desiredUpperDir = kneePos - upperPos;
            if (currentUpperDir.sqrMagnitude > 0.0001f && desiredUpperDir.sqrMagnitude > 0.0001f)
                upper.rotation = Quaternion.FromToRotation(currentUpperDir, desiredUpperDir) * upper.rotation;

            Vector3 currentLowerDir = end.position - lower.position;
            Vector3 desiredLowerDir = target - lower.position;
            if (currentLowerDir.sqrMagnitude > 0.0001f && desiredLowerDir.sqrMagnitude > 0.0001f)
                lower.rotation = Quaternion.FromToRotation(currentLowerDir, desiredLowerDir) * lower.rotation;
        }

        private void SnapFoot(Transform foot, Transform legEnd)
        {
            if (foot == null || legEnd == null) return;
            foot.position = legEnd.position;
        }

        private void AdjustBodyHeight()
        {
            float baseY = locomotion.transform.position.y;
            float leftY = hasHitL ? smoothTargetL.y : baseY;
            float rightY = hasHitR ? smoothTargetR.y : baseY;

            float lowestOffset = Mathf.Min(leftY, rightY) - baseY;
            lowestOffset = Mathf.Clamp(lowestOffset, -maxBodyOffset, 0f);

            float targetOffset = lowestOffset * currentIKWeight;
            currentBodyOffset = Mathf.Lerp(currentBodyOffset, targetOffset, Time.deltaTime * bodyOffsetSmooth);

            bodyBone.position += Vector3.up * currentBodyOffset;
        }

        private bool AreBoneRefsValid()
        {
            return hipL && legL && legEndL && footL
                && hipR && legR && legEndR && footR;
        }

        private void DrawLegGizmos(Transform hip, Transform leg, Transform legEnd, Transform foot,
            Vector3 targetPos, Vector3 kneeHintPos, Vector3 normal, bool hasHit, Color legColor)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(hip.position, leg.position);
            Gizmos.DrawLine(leg.position, legEnd.position);

            Gizmos.color = legColor;
            Gizmos.DrawWireSphere(hip.position, 0.02f);
            Gizmos.DrawWireSphere(leg.position, 0.025f);
            Gizmos.DrawWireSphere(legEnd.position, 0.02f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(foot.position, legEnd.position);
            Gizmos.DrawWireSphere(foot.position, 0.015f);

            if (!Application.isPlaying || currentIKWeight <= 0.001f) return;

            Gizmos.color = hasHit ? legColor : Color.red;
            Gizmos.DrawSphere(targetPos, 0.02f);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(kneeHintPos, 0.012f);
            Gizmos.DrawLine(leg.position, kneeHintPos);

            if (hasHit)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f);
                Gizmos.DrawRay(targetPos, normal * 0.15f);
            }

            Gizmos.color = Color.white;
            Vector3 rayOrigin = legEnd.position + Vector3.up * raycastOriginHeight;
            Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * (raycastDistance + raycastOriginHeight));
        }

        private void DrawFootTrail(Vector3[] trail, int writeIdx, int count,
            Vector3 velocity, Color color)
        {
            if (count < 2) return;

            if (showFootTrail)
            {
                for (int i = 1; i < count; i++)
                {
                    int idxA = (writeIdx - count + i - 1 + TrailCapacity * 2) % TrailCapacity;
                    int idxB = (writeIdx - count + i + TrailCapacity * 2) % TrailCapacity;

                    float t = (float)i / count;
                    Gizmos.color = new Color(color.r, color.g, color.b, t * 0.8f);
                    Gizmos.DrawLine(trail[idxA], trail[idxB]);
                }

                int newest = (writeIdx - 1 + TrailCapacity) % TrailCapacity;
                Gizmos.color = color;
                Gizmos.DrawWireSphere(trail[newest], 0.012f);
            }

            if (showVelocityArrow && velocity.sqrMagnitude > 0.01f)
            {
                Vector3 footPos = trail[(writeIdx - 1 + TrailCapacity) % TrailCapacity];
                Vector3 dir = velocity.normalized;
                float len = Mathf.Clamp(velocity.magnitude * 0.04f, 0.05f, 0.3f);

                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(footPos, dir * len);

                Vector3 arrowEnd = footPos + dir * len;
                Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;
                if (right.sqrMagnitude < 0.001f)
                    right = Vector3.Cross(dir, Vector3.forward).normalized;
                float arrowSize = len * 0.3f;
                Gizmos.DrawLine(arrowEnd, arrowEnd - dir * arrowSize + right * arrowSize * 0.5f);
                Gizmos.DrawLine(arrowEnd, arrowEnd - dir * arrowSize - right * arrowSize * 0.5f);
            }
        }
        #endregion
    }
}
