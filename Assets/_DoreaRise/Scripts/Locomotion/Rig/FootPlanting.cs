using System;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Foot locking + ground adaptation on top of the animation. A foot that the clip puts on the ground
    /// stays pinned to its world position (no sliding while turning, accelerating or at clamped playback),
    /// then releases when the clip lifts it or it drifts too far. The leg reaches the target with two-bone IK.
    /// Skids (stop / pivot / dash) are left alone by default — sliding is the point there.
    /// Works with a clean chain (foot under the shin) and with the current export (foot under Foot.IK).
    /// </summary>
    [DefaultExecutionOrder(950)]
    public class FootPlanting : MonoBehaviour
    {
        [Serializable]
        public class Leg
        {
            [Tooltip("Thigh (e.g. Hip.L)")] public Transform upper;
            [Tooltip("Shin (e.g. Leg.L)")] public Transform lower;
            [Tooltip("Ankle point at the end of the shin (e.g. Leg.end.L)")] public Transform end;
            [Tooltip("Foot bone (e.g. Foot.L)")] public Transform foot;

            [NonSerialized] public Transform footDriver;
            [NonSerialized] public bool locked;
            [NonSerialized] public Vector3 lockPosition;
            [NonSerialized] public float lockWeight;
            [NonSerialized] public float lockWeightVelocity;
            [NonSerialized] public Vector3 previousAnimated;
            [NonSerialized] public bool hasPrevious;
            [NonSerialized] public float restHeight = float.PositiveInfinity;
            [NonSerialized] public bool prepared;
            [NonSerialized] public Vector3 animated;
            [NonSerialized] public Vector3 target;
            [NonSerialized] public float pelvisDemand;
            [NonSerialized] public float grounded;
            [NonSerialized] public Vector3 groundNormal = Vector3.up;
            [NonSerialized] public float groundOffset;
            [NonSerialized] public float appliedTilt;
            [NonSerialized] public float groundY;
            [NonSerialized] public bool hasGround;
            [NonSerialized] public int contactFrames;
            [NonSerialized] public float relockTimer;
            [NonSerialized] public int lockCount;
            [NonSerialized] public Vector3 lastBend;
            [NonSerialized] public bool hasLastBend;
            [NonSerialized] public Vector3 animatedShin;
            [NonSerialized] public Vector3 finalShin;
            [NonSerialized] public float smoothedGroundOffset;
            [NonSerialized] public float groundOffsetVelocity;

            public bool IsValid => upper != null && lower != null && end != null;
        }

        #region [Variables/Fields]
        [Header("References")]
        [SerializeField] private LocomotionMotor motor;

        [Header("Legs (found by name when empty)")]
        [SerializeField] private Leg left = new Leg();
        [SerializeField] private Leg right = new Leg();
        [Tooltip("Thigh, shin, ankle end, foot. When the ankle end is not found, the shin's first child is used")]
        [SerializeField] private string[] leftBoneNames = { "Hip.L", "Leg.L", "Leg.L_end", "Foot.L" };
        [SerializeField] private string[] rightBoneNames = { "Hip.R", "Leg.R", "Leg.R_end", "Foot.R" };

        [Header("Planting")]
        [Range(0f, 1f)] [SerializeField] private float weight = 1f;
        [Tooltip("A foot can plant while its ankle is within this height (m) of its lowest animated height")]
        [SerializeField] private float plantHeight = 0.035f;
        [Tooltip("...and the animated ankle moves slower than this over the ground (m/s)")]
        [SerializeField] private float plantSpeed = 1.2f;
        [Tooltip("Release the lock when the animated foot is this far from it (m) — avoids over-stretched legs")]
        [SerializeField] private float releaseDistance = 0.12f;
        [Tooltip("Release the lock when the animated foot moves faster than this over the ground (m/s): its swing has begun")]
        [SerializeField] private float releaseSpeed = 1.5f;
        [SerializeField] private float lockBlendHalfLife = 0.04f;
        [Tooltip("Planting, ground offsets and tilt fade out this fast (half-life, s) while the character is airborne " +
                 "(jumps, falls) and back in on landing")]
        [SerializeField] private float airborneFadeHalfLife = 0.03f;
        [Tooltip("Fastest the knee's bend direction may swing (deg/s). Stops one-frame knee flips")]
        [SerializeField] private float maxKneeTurnSpeed = 720f;
        [Tooltip("Frames of continuous contact before a foot locks (filters one-frame touches)")]
        [SerializeField] private int lockAfterContactFrames = 2;
        [Tooltip("After lifting off, a foot cannot lock again for this long (s)")]
        [SerializeField] private float relockDelay = 0.1f;
        [Tooltip("How fast (m/s) the measured rest height of the ankle creeps back up after an unusually low frame")]
        [SerializeField] private float restHeightRecovery = 0.01f;
        [Tooltip("Off = stops, pivots and dashes keep their authored skid")]
        [SerializeField] private bool lockDuringActions;

        [Header("Leg IK")]
        [Tooltip("When the animated leg is almost straight its knee direction is noise; bias it this much (x thigh " +
                 "length) towards the character's forward so the knee never flips side")]
        [SerializeField] private float kneeForwardBias = 0.15f;
        [Tooltip("Above max(animated extension, this fraction of full leg length) the reach eases in instead of snapping straight")]
        [Range(0.8f, 1f)] [SerializeField] private float softReachStart = 0.97f;
        [Tooltip("Corrections smaller than this (m) are skipped so the animation stays untouched")]
        [SerializeField] private float minCorrection = 0.0005f;

        [Header("Ground")]
        [SerializeField] private bool adaptToGround = true;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float maxGroundOffset = 0.25f;
        [Tooltip("Smoothing of each foot's ground offset (s) — step edges change it by a whole step in one frame")]
        [SerializeField] private float groundOffsetHalfLife = 0.04f;

        [Header("Foot Rotation")]
        [Tooltip("Tilt feet near the ground to match the slope under them")]
        [SerializeField] private bool alignToGround = true;
        [Range(0f, 1f)] [SerializeField] private float alignWeight = 1f;
        [SerializeField] private float maxFootTilt = 35f;
        [Tooltip("Smoothing of the ground normal (s) — avoids snapping at step edges")]
        [SerializeField] private float normalHalfLife = 0.05f;

        [Header("Pelvis")]
        [Tooltip("Lower the hips so the foot on the lower ground can reach it (slopes, stairs)")]
        [SerializeField] private bool adjustPelvis = true;
        [Tooltip("Bone both thighs hang from. Empty = their common parent (e.g. Spine)")]
        [SerializeField] private Transform pelvis;
        [SerializeField] private float maxPelvisDrop = 0.25f;
        [SerializeField] private float pelvisHalfLife = 0.08f;
        [Tooltip("Hip dip on landing (m per m/s of fall speed) — the legs absorb the impact")]
        [SerializeField] private float landingDipPerSpeed = 0.02f;
        [SerializeField] private float maxLandingDip = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private float pelvisOffset;
        private float groundedWeight = 1f;
        private float groundedWeightVelocity;
        private float pelvisVelocity;
        #endregion

        #region [Properties]
        /// <summary>Debug: how far (m, planar) a locked foot is held away from where the animation puts it.</summary>
        public float LockStretch
        {
            get
            {
                float stretch = 0f;
                foreach (Leg leg in new[] { left, right })
                {
                    if (!leg.locked || !leg.hasPrevious) continue;
                    Vector3 d = leg.lockPosition - leg.previousAnimated;
                    d.y = 0f;
                    stretch = Mathf.Max(stretch, d.magnitude * leg.lockWeight);
                }
                return stretch;
            }
        }

        public float Weight
        {
            get => weight;
            set => weight = Mathf.Clamp01(value);
        }

        public bool LeftLocked => left.locked;
        public bool RightLocked => right.locked;
        /// <summary>Current hip drop (m, ≤ 0).</summary>
        public float PelvisOffset => pelvisOffset;
        public Leg LeftLeg => left;
        public Leg RightLeg => right;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<LocomotionMotor>();
            if (!left.IsValid) FindLeg(left, leftBoneNames);
            if (!right.IsValid) FindLeg(right, rightBoneNames);
            ResolveFootDriver(left);
            ResolveFootDriver(right);

            if (pelvis == null && left.IsValid && right.IsValid && left.upper.parent == right.upper.parent)
                pelvis = left.upper.parent;

            if (!left.IsValid || !right.IsValid)
                Debug.LogWarning($"[FootPlanting] Leg bones not found (left ok: {left.IsValid}, right ok: {right.IsValid}) " +
                                 "— check the bone names on " + name, this);
        }

        private void OnEnable()
        {
            if (motor == null) return;
            motor.Landed += OnLanded;
            motor.Teleported += ResetState;
        }

        private void OnDisable()
        {
            if (motor == null) return;
            motor.Landed -= OnLanded;
            motor.Teleported -= ResetState;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || motor == null) return;

            // In the air there is no ground to plant on: the real floor is metres below the animated feet
            // ...and a slide is authored body-on-the-floor: its folded leg must keep the clip's knee and foot
            bool authored = motor.CurrentActionKind == LocomotionActionKind.Slide;
            Spring.Float(ref groundedWeight, ref groundedWeightVelocity, motor.IsGrounded && !authored ? 1f : 0f,
                authored ? 0.05f : airborneFadeHalfLife, dt);
            groundedWeight = Mathf.Clamp01(groundedWeight);
            bool canLock = weight > 0f && motor.IsGrounded && !authored && (lockDuringActions || !IsSkidding());
            float rootGround = GroundHeight(motor.transform.position + Vector3.up * 0.3f, out bool rootHasGround, out _);

            // 1) Where each foot should be. 2) Drop the hips for the lower foot. 3) Reach with IK.
            PrepareLeg(left, canLock, dt, rootGround, rootHasGround);
            PrepareLeg(right, canLock, dt, rootGround, rootHasGround);
            ApplyPelvis(dt);
            SolveLeg(left);
            SolveLeg(right);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos || !Application.isPlaying) return;
            DrawLegGizmo(left);
            DrawLegGizmo(right);
        }
        #endregion

        #region [Public Methods]
        /// <summary>Drop both locks (after a cut or teleport).</summary>
        public void ReleaseAll()
        {
            left.locked = right.locked = false;
            left.lockWeight = right.lockWeight = 0f;
            left.hasPrevious = right.hasPrevious = false;
            left.contactFrames = right.contactFrames = 0;
            left.relockTimer = right.relockTimer = 0f;
            left.hasLastBend = right.hasLastBend = false;
        }

        /// <summary>Full reset for a cut: locks, hip offset (incl. a pending landing dip) and ground normals.</summary>
        public void ResetState()
        {
            ReleaseAll();
            pelvisOffset = pelvisVelocity = 0f;
            left.groundNormal = right.groundNormal = Vector3.up;
            left.smoothedGroundOffset = right.smoothedGroundOffset = 0f;
            left.groundOffsetVelocity = right.groundOffsetVelocity = 0f;
        }
        #endregion

        #region [Private Methods]
        /// <summary>Kick the pelvis spring downwards so it dips and recovers (peak ≈ requested dip).</summary>
        private void OnLanded(float impactSpeed)
        {
            if (pelvis == null || weight <= 0f) return;
            float dip = Mathf.Min(impactSpeed * landingDipPerSpeed, maxLandingDip) * weight;
            float y = Spring.HalfLifeToDamping(pelvisHalfLife) * 0.5f;
            pelvisVelocity -= dip * y * 2.7182818f;
        }

        private bool IsSkidding()
        {
            if (motor.IsDrifting) return true;

            switch (motor.CurrentActionKind)
            {
                case LocomotionActionKind.Stop:
                case LocomotionActionKind.Pivot:
                case LocomotionActionKind.Dash:
                case LocomotionActionKind.Jump:
                case LocomotionActionKind.Land:
                case LocomotionActionKind.Fall:
                case LocomotionActionKind.Slide:
                    LocomotionAnimator anim = motor.LocomotionAnimator;
                    if (anim == null || anim.IsInFollowUp) return false;
                    return anim.CurrentAction == null || !anim.CurrentAction.lockFeet;
                default:
                    return false;
            }
        }

        private void PrepareLeg(Leg leg, bool canLock, float dt, float rootGround, bool rootHasGround)
        {
            leg.prepared = false;
            leg.pelvisDemand = 0f;
            leg.groundOffset = 0f;
            leg.appliedTilt = 0f;
            if (!leg.IsValid) return;

            Vector3 animated = leg.end.position;
            leg.animatedShin = (animated - leg.lower.position).normalized;

            // Contact is judged in animation space: against the floor the clip was authored on, which is the
            // model's own origin (the motor keeps that on the real ground under the character, not on the capsule).
            // Real terrain under the foot therefore never corrupts the measured rest height. The rest height is a
            // slowly recovering minimum, so one unusually low frame cannot disable planting for the session.
            float floorY = motor.VisualRoot != null
                ? motor.VisualRoot.position.y
                : (rootHasGround ? rootGround : motor.transform.position.y);
            float height = animated.y - floorY;
            leg.restHeight = Mathf.Min(float.IsInfinity(leg.restHeight) ? height : leg.restHeight + restHeightRecovery * dt, height);

            Vector3 planar = animated - (leg.hasPrevious ? leg.previousAnimated : animated);
            planar.y = 0f;
            float speed = planar.magnitude / dt;
            leg.previousAnimated = animated;
            leg.hasPrevious = true;

            bool low = height <= leg.restHeight + plantHeight;
            bool contact = low && speed <= plantSpeed;
            leg.contactFrames = contact ? leg.contactFrames + 1 : 0;
            if (leg.relockTimer > 0f) leg.relockTimer -= dt;

            if (!leg.locked)
            {
                if (canLock && leg.contactFrames >= lockAfterContactFrames && leg.relockTimer <= 0f)
                {
                    leg.locked = true;
                    leg.lockPosition = animated;
                    leg.lockCount++;
                }
            }
            else
            {
                // Release at lift-off, when the animated foot starts its swing (the heel rolls the ankle forward
                // before it rises), or when it has drifted too far. The relock delay + contact frames stop it from
                // re-locking straight away, and by the time it may, the target is back on the animated foot — so a
                // release never snaps (that snap was the walking flicker).
                Vector3 drift = leg.lockPosition - animated;
                drift.y = 0f;
                bool lifted = height > leg.restHeight + plantHeight * 2f;
                bool swinging = speed > releaseSpeed;
                if (lifted || swinging || !canLock || drift.magnitude > releaseDistance)
                {
                    leg.locked = false;
                    leg.relockTimer = relockDelay;
                }
            }

            Spring.Float(ref leg.lockWeight, ref leg.lockWeightVelocity, leg.locked ? 1f : 0f, lockBlendHalfLife, dt);
            leg.lockWeight = Mathf.Clamp01(leg.lockWeight);

            // The lock pins the foot in the ground plane only; its height always comes from the animation plus
            // the ground offset, so walking up a slope / stairs does not drag a planted foot into the ground.
            float lockBlend = leg.lockWeight * weight * groundedWeight;
            Vector3 target = new Vector3(
                Mathf.Lerp(animated.x, leg.lockPosition.x, lockBlend),
                animated.y,
                Mathf.Lerp(animated.z, leg.lockPosition.z, lockBlend));

            // Ground under where the foot will actually be (the lock position while planted).
            float groundY = GroundHeight(new Vector3(target.x, animated.y, target.z), out bool hasGround, out Vector3 groundNormal);
            leg.groundY = groundY;
            leg.hasGround = hasGround;

            // 1 while the foot is on (or just above) the ground, 0 high in its swing.
            leg.grounded = 1f - Mathf.InverseLerp(leg.restHeight + plantHeight, leg.restHeight + plantHeight * 4f, height);

            // Uneven ground: offset by how much higher/lower the ground under the foot is than under the
            // character (0 on flat ground, whatever the capsule's skin width does to the root height). Smoothed,
            // because the ray jumps a whole step height in one frame when the foot crosses a step edge.
            float rawOffset = adaptToGround && hasGround && rootHasGround
                ? Mathf.Clamp(groundY - floorY, -maxGroundOffset, maxGroundOffset) * weight * groundedWeight
                : 0f;
            Spring.Float(ref leg.smoothedGroundOffset, ref leg.groundOffsetVelocity, rawOffset, groundOffsetHalfLife, dt);
            float groundOffset = leg.smoothedGroundOffset;
            target.y += groundOffset;
            leg.groundOffset = groundOffset;

            // A foot high in its swing should not pull the hips down; a foot near the ground should.
            leg.pelvisDemand = Mathf.Min(0f, groundOffset) * leg.grounded;

            Vector3 normalGoal = hasGround ? groundNormal : Vector3.up;
            float normalBlend = 1f - Mathf.Exp(-0.6931472f * dt / Mathf.Max(normalHalfLife, 1e-4f));
            leg.groundNormal = Vector3.Slerp(leg.groundNormal, normalGoal, normalBlend).normalized;

            leg.animated = animated;
            leg.target = target;
            leg.prepared = true;
        }

        /// <summary>Lowers the hips just enough for the lower foot; the other leg bends more.</summary>
        private void ApplyPelvis(float dt)
        {
            float goal = 0f;
            if (adjustPelvis && pelvis != null)
                goal = Mathf.Max(-maxPelvisDrop, Mathf.Min(left.pelvisDemand, right.pelvisDemand));

            Spring.Float(ref pelvisOffset, ref pelvisVelocity, goal, pelvisHalfLife, dt);
            if (pelvis != null && Mathf.Abs(pelvisOffset) > 1e-5f)
                pelvis.position += Vector3.up * pelvisOffset;
        }

        private void SolveLeg(Leg leg)
        {
            if (!leg.prepared) return;

            // Compare against the current ankle: the pelvis may have moved it even when the target did not change.
            if ((leg.target - leg.end.position).sqrMagnitude > minCorrection * minCorrection)
            {
                SolveTwoBone(leg, leg.target);
                if (leg.footDriver != null)
                    leg.footDriver.position += leg.end.position - leg.animated;
            }

            AlignFoot(leg);
            leg.finalShin = (leg.end.position - leg.lower.position).normalized;
        }

        /// <summary>Tilts the foot by the slope under it (keeps the animated foot pose relative to the ground).</summary>
        private void AlignFoot(Leg leg)
        {
            if (!alignToGround) return;

            float blend = leg.grounded * weight * groundedWeight * alignWeight;
            if (blend <= 0.001f) return;

            Transform rotated = leg.footDriver != null ? leg.footDriver : leg.foot;
            if (rotated == null) return;

            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, leg.groundNormal);
            tilt.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) < 0.01f) return;
            angle = Mathf.Clamp(angle, -maxFootTilt, maxFootTilt) * blend;

            rotated.rotation = Quaternion.AngleAxis(angle, axis) * rotated.rotation;
            leg.appliedTilt = Mathf.Abs(angle);
        }

        /// <summary>
        /// Two-bone IK that keeps the animated knee bend plane. Stable for nearly straight legs: the knee side is
        /// biased forward when the animated bend is too small to trust, never flips between frames, and the reach
        /// eases in instead of snapping the leg straight.
        /// </summary>
        private void SolveTwoBone(Leg leg, Vector3 target)
        {
            Vector3 hip = leg.upper.position;
            Vector3 knee = leg.lower.position;
            Vector3 ankle = leg.end.position;

            float upperLength = Vector3.Distance(hip, knee);
            float lowerLength = Vector3.Distance(knee, ankle);
            if (upperLength < 1e-5f || lowerLength < 1e-5f) return;

            Vector3 toTarget = target - hip;
            float rawDistance = toTarget.magnitude;
            if (rawDistance < 1e-5f) return;
            Vector3 direction = toTarget / rawDistance;

            float maxReach = upperLength + lowerLength;
            float animatedDistance = Vector3.Distance(hip, ankle);
            float distance = SoftReach(rawDistance, Mathf.Max(maxReach * softReachStart, animatedDistance), maxReach);
            distance = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + 1e-4f, maxReach - 1e-4f);

            // Bend towards where the animated knee points; when the leg is so straight that this is noise, lean on
            // the character's forward (knees bend forward) and on last frame's bend.
            Vector3 legAxis = ankle - hip;
            Vector3 animatedBend = legAxis.sqrMagnitude > 1e-10f
                ? knee - (hip + Vector3.Project(knee - hip, legAxis.normalized))
                : Vector3.zero;
            float trust = Mathf.SmoothStep(0f, 1f, animatedBend.magnitude / (upperLength * 0.05f));
            Vector3 pole = animatedBend + motor.transform.forward * (upperLength * kneeForwardBias * (1f - trust));
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 1e-10f && leg.hasLastBend) bend = Vector3.ProjectOnPlane(leg.lastBend, direction);
            if (bend.sqrMagnitude < 1e-10f) bend = Vector3.ProjectOnPlane(motor.transform.forward, direction);
            if (bend.sqrMagnitude < 1e-10f) return;
            bend.Normalize();

            // Knees bend forward: a bend behind the character (seen after jump tucks and fast turns) is mirrored
            // to the front.
            Vector3 front = Vector3.ProjectOnPlane(motor.transform.forward, direction);
            if (front.sqrMagnitude > 1e-6f)
            {
                front.Normalize();
                float back = Vector3.Dot(bend, front);
                if (back < -0.1f) bend = (bend - front * (2f * back)).normalized;
            }

            // A real knee never swings to the other side in one frame: turn toward the new bend at a limited rate.
            // (Keeping last frame's bend whenever the new one disagreed latched a wrong side for good: the leg
            // stayed broken while standing.)
            if (leg.hasLastBend)
            {
                Vector3 last = Vector3.ProjectOnPlane(leg.lastBend, direction);
                if (last.sqrMagnitude > 1e-10f)
                    bend = Vector3.RotateTowards(last.normalized, bend, maxKneeTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
            }
            leg.lastBend = bend;
            leg.hasLastBend = true;

            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            float across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 newKnee = hip + direction * along + bend * across;
            Vector3 newAnkle = hip + direction * distance;

            leg.upper.rotation = Quaternion.FromToRotation(knee - hip, newKnee - hip) * leg.upper.rotation;
            Vector3 currentLower = leg.end.position - leg.lower.position;
            leg.lower.rotation = Quaternion.FromToRotation(currentLower, newAnkle - leg.lower.position) * leg.lower.rotation;
        }

        /// <summary>Distance that approaches <paramref name="max"/> asymptotically beyond <paramref name="start"/>.</summary>
        private static float SoftReach(float distance, float start, float max)
        {
            if (distance <= start || start >= max) return distance;
            float range = max - start;
            return start + range * (1f - Mathf.Exp(-(distance - start) / range));
        }

        private float GroundHeight(Vector3 ankle, out bool hasGround, out Vector3 normal)
        {
            hasGround = false;
            normal = Vector3.up;
            float fallback = motor.transform.position.y;
            if (!adaptToGround && !alignToGround) return fallback;

            Vector3 origin = ankle + Vector3.up * 0.5f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 1.5f, groundLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(motor.transform)) continue;
                if (hits[i].point.y <= best) continue;
                best = hits[i].point.y;
                normal = hits[i].normal;
            }

            if (float.IsNegativeInfinity(best)) return fallback;
            hasGround = true;
            return best;
        }

        private void FindLeg(Leg leg, string[] names)
        {
            if (names == null || names.Length < 3) return;
            Transform[] all = GetComponentsInChildren<Transform>(true);
            leg.upper = Find(all, names[0]);
            leg.lower = Find(all, names[1]);
            leg.end = Find(all, names[2]);
            leg.foot = names.Length > 3 ? Find(all, names[3]) : null;

            // Exporters name the shin's end bone differently (Leg.L_end, Leg.end.L, ...): fall back to its child.
            if (leg.end == null && leg.lower != null && leg.lower.childCount > 0)
                leg.end = leg.lower.GetChild(0);
        }

        private static Transform Find(Transform[] all, string boneName)
        {
            foreach (Transform t in all)
                if (t.name == boneName) return t;
            return null;
        }

        /// <summary>When the foot is not under the shin (Foot.IK export), its parent must be moved with the ankle.</summary>
        private static void ResolveFootDriver(Leg leg)
        {
            leg.footDriver = null;
            if (!leg.IsValid || leg.foot == null) return;
            if (leg.foot.IsChildOf(leg.lower)) return;
            leg.footDriver = leg.foot.parent != null && !leg.lower.IsChildOf(leg.foot.parent) ? leg.foot.parent : leg.foot;
        }

        private void DrawLegGizmo(Leg leg)
        {
            if (!leg.IsValid) return;
            Gizmos.color = leg.locked ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.4f, 0.3f);
            Gizmos.DrawWireSphere(leg.locked ? leg.lockPosition : leg.end.position, 0.05f);
        }
        #endregion
    }
}
