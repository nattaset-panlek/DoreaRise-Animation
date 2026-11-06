using System;
using System.Collections.Generic;
using UnityEngine;
using DoreaRise.System.Controller;
using SystemRandom = System.Random;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Moves the character from LocomotionDriver intent using critically damped springs, and decides
    /// when to play authored transitions (stop, dash, start, pivot, turn in place, idle breaks).
    /// Pair with a LocomotionAnimator on the Animator object.
    /// Frame order: drivers -> transitions -> movement -> LocomotionAnimator.Animate -> (graph) -> LateUpdate rig.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public class LocomotionMotor : MonoBehaviour, ILocomotionState
    {
        #region [Variables/Fields]
        [Header("References")]
        [SerializeField] private LocomotionAnimator locomotionAnimator;

        [Header("Gait Speeds")]
        [Tooltip("Use each loop's measured ideal speed so the feet never slide. Turn off to force the speeds below")]
        [SerializeField] private bool useClipSpeeds = true;
        [SerializeField] private float walkSpeed = 1.26f;
        [SerializeField] private float runSpeed = 4.2f;
        [SerializeField] private float sprintSpeed = 10f;
        [SerializeField] private float sprintFastSpeed = 15f;

        [Header("Movement Feel (half-life in seconds, lower = snappier)")]
        [SerializeField] private float accelerationHalfLife = 0.12f;
        [Tooltip("Slowing down when no stop clip applies (e.g. walking)")]
        [SerializeField] private float decelerationHalfLife = 0.15f;
        [SerializeField] private float facingHalfLife = 0.08f;
        [SerializeField] private float idleFacingHalfLife = 0.2f;
        [Tooltip("Smoothing of the acceleration exposed to lean / secondary motion")]
        [SerializeField] private float accelerationSmoothingHalfLife = 0.06f;
        [SerializeField] private float maxReportedAcceleration = 30f;
        [SerializeField] private float gravity = -20f;

        [Header("Procedural Start / Turn (used when no start / turn clip exists)")]
        [Tooltip("Speed jump on the first frame of a start, as a fraction of the target speed (snappy push-off)")]
        [Range(0f, 1f)] [SerializeField] private float startLaunchFraction = 0.35f;
        [SerializeField] private float startLaunchMaxSpeed = 4f;
        [Tooltip("Starting towards a direction further than this (degrees) from facing: turn on the spot first, then go")]
        [SerializeField] private float startTurnAngle = 75f;
        [Tooltip("Facing error (degrees) at which the turn-first start pushes off")]
        [SerializeField] private float startTurnFinishAngle = 30f;
        [SerializeField] private float startTurnHalfLife = 0.05f;
        [Tooltip("Standing turns larger than 45 degrees use this half-life (snappy) instead of Idle Facing Half Life")]
        [SerializeField] private float turnInPlaceHalfLife = 0.08f;

        [Header("High-Speed Turning (drift)")]
        [Tooltip("Above this speed (m/s), sharp direction changes drift: the body snaps to the new direction while momentum slides")]
        [SerializeField] private float driftMinSpeed = 7f;
        [Tooltip("Angle (degrees) between momentum and the new direction that starts a drift (pivots take over above Pivot Angle)")]
        [SerializeField] private float driftAngle = 60f;
        [SerializeField] private float driftExitAngle = 12f;
        [Tooltip("How quickly momentum follows the new direction while drifting (half-life, s). Higher = longer slide")]
        [SerializeField] private float driftGripHalfLife = 0.2f;
        [SerializeField] private float driftFacingHalfLife = 0.05f;
        [Tooltip("Fraction of the target speed kept through a drift")]
        [Range(0f, 1f)] [SerializeField] private float driftSpeedRetention = 0.85f;

        [Header("Transitions")]
        [Tooltip("A brief release of movement at running speed (changing keys, a script handing over to the next " +
                 "command) is bridged for this long (s) before it counts as a stop — so a quick reverse pivots " +
                 "and a quick 90° turn drifts instead of braking")]
        [SerializeField] private float stopIntentGrace = 0.08f;
        [Tooltip("Direction change (degrees) that counts as a pivot")]
        [SerializeField] private float pivotAngle = 135f;
        [SerializeField] private float pivotMinSpeed = 2.5f;
        [Tooltip("A pivot clip authored from a given foot (entry phase) waits up to this long (s) for the stride to " +
                 "reach it, still walking straight, so the planted foot is not dragged into the turn")]
        [SerializeField] private float pivotPhaseMaxWait = 0.45f;
        [SerializeField] private float pivotPhaseTolerance = 0.08f;
        [Tooltip("Without a pivot clip, skid with the stop clip and then run the new way")]
        [SerializeField] private bool brakeOnPivotWithoutClip = true;
        [Tooltip("Max angle error (degrees) when choosing a start / pivot / turn clip")]
        [SerializeField] private float directionalClipTolerance = 50f;
        [Tooltip("Facing error (degrees) that triggers a turn-in-place clip")]
        [SerializeField] private float turnInPlaceAngle = 60f;

        [Header("Dash")]
        [SerializeField] private bool canDash = true;
        [SerializeField] private float dashCooldown = 0.6f;
        [Tooltip("When no movement is requested after a dash, skid with the stop clip")]
        [SerializeField] private bool brakeAfterDash = true;

        [Header("Slide")]
        [Tooltip("Capsule height during the slide's low part, so it passes under bars")]
        [SerializeField] private float slideCapsuleHeight = 0.7f;
        [Tooltip("Still under something when the slide would stand up: stay low and keep going at this speed (m/s)")]
        [SerializeField] private float slideUnderSpeed = 3f;

        [Header("Jump")]
        [SerializeField] private bool canJump = true;
        [Tooltip("Obstacles the running jump vaults: looked for ahead at knee height")]
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField] private float obstacleMinHeight = 0.25f;
        [SerializeField] private float obstacleMaxHeight = 1.3f;
        [Tooltip("Height the feet clear an obstacle by (m) when the vault clip has no hand plant")]
        [SerializeField] private float obstacleClearance = 0.12f;
        [Tooltip("Vault: how far past the obstacle's near edge the hand lands (m), and the wrist's height above the top")]
        [SerializeField] private float vaultHandInset = 0.15f;
        [SerializeField] private float vaultWristAboveTop = 0.05f;
        [SerializeField] private float handPlantBlend = 0.08f;
        [Tooltip("Vault pressed early: wait (running on) until the travel scale is at most this, up to vaultQueueMax s")]
        [SerializeField] private float vaultIdealScale = 1.06f;
        [SerializeField] private float vaultQueueMax = 0.8f;
        [Tooltip("Obstacles whose top is this much below the clip's planted wrist are too low for a hand plant (m)")]
        [SerializeField] private float vaultHandMinDrop = 0.3f;
        [Tooltip("Deepest obstacle the vault clears (m); deeper ones are jumped onto with the plain jump")]
        [SerializeField] private float vaultMaxDepth = 0.9f;

        [Header("Idle Breaks")]
        [SerializeField] private bool playIdleBreaks = true;
        [Tooltip("Same seed = same idle break choices and timing on every take")]
        [SerializeField] private int idleBreakSeed = 1;

        [Header("Ground & Air")]
        [Tooltip("Keep the capsule on the ground when going down slopes / stairs (search distance below the feet, m)")]
        [SerializeField] private float groundSnapDistance = 0.35f;
        [SerializeField] private LayerMask groundSnapLayers = ~0;
        [Tooltip("Airborne at least this long (s) before touching down counts as a landing")]
        [SerializeField] private float minAirTimeForLanding = 0.15f;

        [Header("Visual Ground Following")]
        [Tooltip("Model that follows the real ground under the character instead of the capsule (whose round bottom " +
                 "rides up step edges and ramp starts early). Auto = the animated model")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("How fast the model follows a change of ground height under the character (half-life, s)")]
        [SerializeField] private float stepSmoothingHalfLife = 0.07f;
        [Tooltip("Largest offset (m) between the model and the capsule")]
        [SerializeField] private float maxStepSmoothing = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        private CharacterController characterController;
        private readonly List<LocomotionDriver> drivers = new List<LocomotionDriver>();
        private LocomotionIntent intent = LocomotionIntent.Idle;
        private SystemRandom idleRandom;

        private Vector3 velocity;
        private Vector3 springAcceleration;
        private Vector3 actualVelocity;
        private Vector3 previousVelocity;
        private Vector3 smoothedAcceleration;
        private float verticalVelocity;
        private float yaw;
        private float yawVelocity;
        private float previousYaw;
        private float yawRate;

        private float dashCooldownTimer;
        private bool dashResolved;
        private bool pendingJump;
        private bool pendingSlide;
        private float standingHeight;
        private Vector3 standingCenter;
        private bool capsuleLow;
        private bool slideHeldUnder;
        private readonly Collider[] overlapHits = new Collider[8];
        private bool isJumping;
        private bool jumpLaunched;
        private float jumpGravity;
        private float idleTimer;
        private float nextIdleBreak;

        private Vector3 actionDirection = Vector3.forward;
        private float actionEntrySpeed;
        private float actionTravelScale = 1f;
        private float actionTurnAmount;
        private float pivotWaitStart = -1f;
        private float lastBlockedTime = -10f;
        private Vector3 lastBlockedDirection;
        private Vector2 actionTurnWindow = new Vector2(0f, 1f);
        /// <summary>A start authored straight ahead but chosen for an angle off it turns the rest over this
        /// part of the clip, so the body never travels sideways.</summary>
        private const float StartResidualTurnWindow = 0.3f;
        private bool isTurningToStart;
        private Vector3 lastDesiredVelocity;
        private float noMoveTime = float.PositiveInfinity;
        private bool isDrifting;

        private bool isGrounded = true;
        private float airTime;
        private float visualOffset;
        private float visualHeight;
        private float visualHeightVelocity;
        private float lastTargetHeight;
        private bool hasVisualHeight;
        private Vector3 visualBaseLocalPosition;
        private bool hasVisualBase;
        private float groundClearance;
        private bool hasGroundClearance;
        #endregion

        #region [Properties]
        /// <summary>Raised when an authored transition starts. Use for camera impulses, hit-stop, VFX, sound.</summary>
        public event Action<LocomotionActionKind> ActionStarted;

        /// <summary>Raised when a high-speed drift (sharp turn with momentum slide) begins.</summary>
        public event Action DriftStarted;

        /// <summary>Sliding through a sharp high-speed turn: facing leads, momentum lags.</summary>
        public bool IsDrifting => isDrifting;

        /// <summary>Raised on touch-down after being airborne; argument = downward impact speed (m/s).</summary>
        public event Action<float> Landed;

        /// <summary>Raised after Teleport so rig layers can drop their state (locks, springs) for the cut.</summary>
        public event Action Teleported;

        public bool IsGrounded => isGrounded;
        public float AirTime => airTime;

        /// <summary>Vertical offset (m) currently applied to the model to smooth stair steps.</summary>
        public float VisualStepOffset => visualOffset;

        /// <summary>The model transform that step smoothing moves (null when none was found).</summary>
        public Transform VisualRoot => visualRoot;

        /// <summary>Planar speed when the current/last action started (m/s).</summary>
        public float ActionEntrySpeed => actionEntrySpeed;

        /// <summary>Planar speed actually travelled last frame (m/s).</summary>
        public float CurrentSpeed => actualVelocity.magnitude;
        public Vector3 Velocity => actualVelocity;
        /// <summary>Smoothed planar acceleration in world space (m/s²).</summary>
        public Vector3 Acceleration => smoothedAcceleration;
        /// <summary>Smoothed acceleration in character space: x = right, z = forward.</summary>
        public Vector3 LocalAcceleration => Quaternion.Inverse(transform.rotation) * smoothedAcceleration;
        /// <summary>Yaw rate in degrees per second (positive = turning right).</summary>
        public float YawRate => yawRate;
        public Gait Gait => intent.Gait;
        public LocomotionIntent Intent => intent;
        public LocomotionAnimator LocomotionAnimator => locomotionAnimator;
        public LocomotionClipSet ClipSet => locomotionAnimator != null ? locomotionAnimator.ClipSet : null;

        public LocomotionActionKind CurrentActionKind =>
            locomotionAnimator != null ? locomotionAnimator.ActionKind : LocomotionActionKind.None;

        public MovementState CurrentState
        {
            get
            {
                if (CurrentSpeed < 0.1f && !intent.WantsToMove) return MovementState.Idle;
                switch (intent.Gait)
                {
                    case Gait.SprintFast: return MovementState.SprintFast;
                    case Gait.Sprint: return MovementState.Sprint;
                    default: return MovementState.Walk;
                }
            }
        }

        public bool IsSprinting => CurrentState == MovementState.Sprint || CurrentState == MovementState.SprintFast;

        public bool IsBraking => (CurrentActionKind == LocomotionActionKind.Stop || CurrentActionKind == LocomotionActionKind.Pivot)
                                 && !locomotionAnimator.IsInFollowUp;

        /// <summary>Standing still and free to take a new command (follow-ups and idle breaks count as free).</summary>
        public bool IsStopped
        {
            get
            {
                if (CurrentSpeed > 0.05f) return false;
                if (locomotionAnimator == null || !locomotionAnimator.IsActionActive) return true;
                return locomotionAnimator.IsInFollowUp || locomotionAnimator.ActionKind == LocomotionActionKind.IdleBreak;
            }
        }

        public bool CanDash
        {
            get => canDash;
            set => canDash = value;
        }

        public bool PlayIdleBreaks
        {
            get => playIdleBreaks;
            set => playIdleBreaks = value;
        }
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (locomotionAnimator == null) locomotionAnimator = GetComponentInChildren<LocomotionAnimator>();
            idleRandom = new SystemRandom(idleBreakSeed);
            RefreshDrivers();
        }

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            previousYaw = yaw;

            if (visualRoot == null && locomotionAnimator != null && locomotionAnimator.Animator != null)
            {
                Transform model = locomotionAnimator.Animator.transform;
                if (model != transform && model.IsChildOf(transform)) visualRoot = model;
            }
            if (visualRoot != null)
            {
                visualBaseLocalPosition = visualRoot.localPosition;
                hasVisualBase = true;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || characterController == null) return;

            intent = ApplyStopGrace(GatherIntent(dt), dt);
            intent.RequestJump |= pendingJump;
            pendingJump = false;
            intent.RequestSlide |= pendingSlide;
            pendingSlide = false;
            dashCooldownTimer -= dt;

            if (locomotionAnimator != null)
            {
                ActionClip running = locomotionAnimator.IsActionActive ? locomotionAnimator.CurrentAction : null;
                locomotionAnimator.TickActions();
                if (running != null && !locomotionAnimator.IsActionActive) Launch(running);
            }
            UpdateTransitions();
            UpdateJump();
            UpdateFall();
            UpdateSlideCapsule();

            bool rootMotionAction = IsRootMotionActionActive();
            Vector3 planarDelta = IsActionDrivingMotion() ? ComputeActionMotion(dt) : SimulateLocomotion(dt);

            if (isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += (isJumping && jumpLaunched && !isGrounded ? jumpGravity : gravity) * dt;

            Vector3 before = transform.position;
            CollisionFlags collisions = characterController.Move(planarDelta + Vector3.up * (verticalVelocity * dt));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            UpdateGrounding(dt);

            Vector3 planarTravel = transform.position - before;
            planarTravel.y = 0f;
            UpdateVisualSmoothing(dt);

            if (!rootMotionAction)
            {
                actualVelocity = planarTravel / dt;

                // Blocked by a wall: don't keep a velocity the character cannot have. Judge it from the side
                // collision and the commanded move — not from the spring's end-of-frame velocity, which is always
                // ahead of the frame's average while accelerating.
                Vector3 commanded = planarDelta;
                commanded.y = 0f;
                bool blocked = (collisions & CollisionFlags.Sides) != 0 && commanded.sqrMagnitude > 1e-8f
                    && planarTravel.magnitude < commanded.magnitude * 0.7f;
                if (blocked)
                {
                    lastBlockedTime = Time.time;
                    lastBlockedDirection = commanded.normalized;
                }
                if (blocked && !IsActionDrivingMotion())
                {
                    velocity = actualVelocity;
                    springAcceleration = Vector3.zero;
                }
            }

            UpdateDerived(dt);
            UpdateIdleBreaks(dt);

            if (locomotionAnimator != null) locomotionAnimator.Animate(CurrentSpeed, dt, intent.Gait, intent.WantsToMove);
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos || !Application.isPlaying) return;

            Vector3 origin = transform.position + Vector3.up * 0.05f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + actualVelocity * 0.5f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + transform.forward * 0.6f);

            Gizmos.color = new Color(1f, 0.4f, 0.2f);
            Gizmos.DrawLine(origin + Vector3.up * 1.2f, origin + Vector3.up * 1.2f + smoothedAcceleration * 0.05f);

            if (CurrentSpeed > 0.05f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(origin + actualVelocity.normalized * PredictStopDistance(), 0.08f);
            }

            if (intent.HasStopTarget)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(intent.StopTarget, 0.12f);
            }
        }
        #endregion

        #region [Public Methods]
        /// <summary>Re-collects drivers on this GameObject (call after adding/removing one at runtime).</summary>
        public void RefreshDrivers()
        {
            drivers.Clear();
            GetComponents(drivers);
            drivers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public float GetGaitSpeed(Gait gait)
        {
            if (useClipSpeeds && locomotionAnimator != null)
            {
                float clipSpeed = locomotionAnimator.GetLoopSpeed(gait);
                if (clipSpeed > 0f) return clipSpeed;
            }

            switch (gait)
            {
                case Gait.Walk: return walkSpeed;
                case Gait.Run: return runSpeed;
                case Gait.Sprint: return sprintSpeed;
                default: return sprintFastSpeed;
            }
        }

        /// <summary>Distance the character still travels if asked to stop right now.</summary>
        public float PredictStopDistance()
        {
            float speed = velocity.magnitude;
            if (speed < 0.01f) return 0f;

            LocomotionClipSet clipSet = ClipSet;
            PhasedAction stop = clipSet != null ? clipSet.FindStop(locomotionAnimator.Phase, speed) : null;
            if (stop != null)
            {
                switch (stop.motion)
                {
                    case ActionMotion.Decelerate:
                        return speed * stop.MotionDuration / (Mathf.Max(0f, stop.decelerationExponent) + 1f);
                    case ActionMotion.Travel:
                        return stop.travelDistance;
                }
            }

            return Spring.PredictStopOffset(velocity, springAcceleration, decelerationHalfLife).magnitude;
        }

        /// <summary>Planar angle (degrees) between the current facing and a world direction.</summary>
        public float FacingAngleTo(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(Forward(), worldDirection);
        }

        /// <summary>Plays a clip in place, not interruptible by movement. Returns an action id (0 = failed).</summary>
        public int PlayCustomAction(AnimationClip clip, float blendHalfLife = -1f)
        {
            if (locomotionAnimator == null || clip == null) return 0;
            ResetActionMotion();
            return Started(locomotionAnimator.PlayClip(clip, LocomotionActionKind.Custom, blendHalfLife, 1f), LocomotionActionKind.Custom);
        }

        public bool IsActionActive(int actionId)
        {
            return locomotionAnimator != null && locomotionAnimator.IsActionActive
                && locomotionAnimator.CurrentActionId == actionId;
        }

        /// <summary>Places the character instantly (shot setup) and clears all motion state.</summary>
        /// <summary>Slide on the next update when moving fast enough (scripts, Timeline, tests).</summary>
        public void Slide()
        {
            pendingSlide = true;
        }

        /// <summary>Jump on the next update (scripts, Timeline, tests). Ignored while airborne.</summary>
        public void Jump()
        {
            pendingJump = true;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            yaw = rotation.eulerAngles.y;

            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            characterController.enabled = wasEnabled;

            velocity = springAcceleration = actualVelocity = previousVelocity = smoothedAcceleration = Vector3.zero;
            yawVelocity = yawRate = verticalVelocity = 0f;
            previousYaw = yaw;
            isGrounded = false;
            airTime = 0f;
            visualOffset = 0f;
            hasVisualHeight = false;
            ApplyVisualOffset();
            isDrifting = isTurningToStart = false;
            lastDesiredVelocity = Vector3.zero;
            noMoveTime = float.PositiveInfinity;
            ResetActionMotion();
            if (locomotionAnimator != null)
            {
                locomotionAnimator.EndAction(0.0001f);
                locomotionAnimator.ResetLoops();
            }
            Teleported?.Invoke();
        }

        /// <summary>Called by LocomotionAnimator.OnAnimatorMove.</summary>
        public void ApplyAnimatorRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (!IsRootMotionActionActive() || characterController == null) return;

            float dt = Time.deltaTime;
            Vector3 planar = deltaPosition;
            planar.y = 0f;
            characterController.Move(planar);

            yaw += Mathf.DeltaAngle(0f, deltaRotation.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (dt > 0f)
            {
                actualVelocity = planar / dt;
                velocity = actualVelocity;
            }
        }
        #endregion

        #region [Private Methods]
        /// <summary>
        /// Bridges a momentary "no movement" at running speed with the last requested velocity, so it is not
        /// mistaken for a stop. Explicit stops at a target (scripted MoveTo) and facing commands are never bridged.
        /// </summary>
        private LocomotionIntent ApplyStopGrace(LocomotionIntent raw, float dt)
        {
            if (raw.WantsToMove)
            {
                lastDesiredVelocity = raw.DesiredVelocity;
                noMoveTime = 0f;
                return raw;
            }

            noMoveTime += dt;
            bool bridge = noMoveTime <= stopIntentGrace && !raw.HasStopTarget && !raw.HasFacing
                && lastDesiredVelocity.sqrMagnitude > 1e-4f && velocity.magnitude >= pivotMinSpeed;
            if (bridge) raw.DesiredVelocity = lastDesiredVelocity;
            return raw;
        }

        private LocomotionIntent GatherIntent(float dt)
        {
            foreach (LocomotionDriver driver in drivers)
            {
                if (driver == null || !driver.isActiveAndEnabled) continue;
                if (driver.TryGetIntent(this, dt, out LocomotionIntent result))
                {
                    result.DesiredVelocity.y = 0f;
                    return result;
                }
            }
            return LocomotionIntent.Idle;
        }

        private void UpdateTransitions()
        {
            if (locomotionAnimator == null || !locomotionAnimator.IsReady) return;

            LocomotionClipSet clipSet = ClipSet;
            bool wantsMove = intent.WantsToMove;

            if (intent.RequestSlide && isGrounded && clipSet != null && clipSet.slide != null && clipSet.slide.IsValid
                && CurrentSpeed >= clipSet.slide.minEntrySpeed
                && locomotionAnimator.ActionKind != LocomotionActionKind.Slide
                && locomotionAnimator.ActionKind != LocomotionActionKind.Jump
                && locomotionAnimator.ActionKind != LocomotionActionKind.Dash)
            {
                SlideAction slide = clipSet.slide;
                ResetActionMotion();
                actionDirection = Forward();
                actionTravelScale = slide.clipSpeed > 0.1f
                    ? Mathf.Clamp(CurrentSpeed / slide.clipSpeed, slide.travelScaleRange.x, slide.travelScaleRange.y)
                    : 1f;
                Started(locomotionAnimator.PlayAction(slide, LocomotionActionKind.Slide), LocomotionActionKind.Slide);
                return;
            }

            if (intent.RequestJump && canJump && isGrounded && clipSet != null && clipSet.jump != null && clipSet.jump.IsValid
                && locomotionAnimator.ActionKind != LocomotionActionKind.Jump
                && locomotionAnimator.ActionKind != LocomotionActionKind.Dash)
            {
                JumpAction picked = PickJump(clipSet, true, out bool queued, out float travelScale, out float height);
                if (queued)
                {
                    vaultQueuedUntil = Time.time + vaultQueueMax;
                }
                else
                {
                    vaultQueuedUntil = -1f;
                    StartJump(picked, travelScale, height);
                    return;
                }
            }

            // a vault pressed early: take off as soon as the stride matches (or when the wait runs out)
            if (vaultQueuedUntil > 0f)
            {
                bool expired = Time.time >= vaultQueuedUntil;
                if (!isGrounded || !wantsMove || locomotionAnimator.ActionKind == LocomotionActionKind.Jump
                    || locomotionAnimator.ActionKind == LocomotionActionKind.Slide)
                {
                    vaultQueuedUntil = -1f;
                }
                else
                {
                    JumpAction picked = PickJump(clipSet, !expired, out bool queued, out float travelScale, out float height);
                    if (!queued)
                    {
                        // the stride matches, or the obstacle is gone / the wait ran out: jump with what fits now
                        vaultQueuedUntil = -1f;
                        StartJump(picked, travelScale, height);
                        return;
                    }
                }
            }
            Vector3 desiredDir = wantsMove ? intent.DesiredVelocity.normalized : Vector3.zero;
            float speed = velocity.magnitude;

            if (locomotionAnimator.IsActionActive && !HandleRunningAction(clipSet, wantsMove, desiredDir))
                return;

            if (intent.RequestDash && canDash && dashCooldownTimer <= 0f && clipSet.dash != null && clipSet.dash.IsValid)
            {
                StartDash(clipSet, wantsMove ? desiredDir : Forward());
                return;
            }

            if (!wantsMove && speed >= clipSet.MinStopSpeed())
            {
                PhasedAction stop = clipSet.FindStop(locomotionAnimator.Phase, speed);
                if (stop != null)
                {
                    StartStop(stop, speed);
                    return;
                }
            }

            if (wantsMove && speed >= pivotMinSpeed && Vector3.Angle(velocity, desiredDir) >= pivotAngle)
            {
                float signedAngle = Vector3.SignedAngle(Forward(), desiredDir, Vector3.up);
                DirectionalAction pivot = LocomotionClipSet.FindClosest(clipSet.pivots, signedAngle, directionalClipTolerance,
                    intent.DesiredVelocity.magnitude);
                if (pivot != null && !PivotPhaseReady(pivot))
                {
                    intent.DesiredVelocity = velocity;      // keep striding straight until the right foot is down
                    return;
                }
                pivotWaitStart = -1f;
                if (pivot != null)
                {
                    StartDirectional(pivot, LocomotionActionKind.Pivot, signedAngle, desiredDir);
                    return;
                }

                if (brakeOnPivotWithoutClip)
                {
                    PhasedAction stop = clipSet.FindStop(locomotionAnimator.Phase, speed);
                    if (stop != null)
                    {
                        StartStop(stop, speed);
                        return;
                    }
                }
            }
            else
            {
                pivotWaitStart = -1f;
            }

            if (wantsMove && speed < 0.1f)
            {
                float signedAngle = Vector3.SignedAngle(Forward(), desiredDir, Vector3.up);
                DirectionalAction start = LocomotionClipSet.FindClosest(clipSet.starts, signedAngle, directionalClipTolerance,
                    intent.DesiredVelocity.magnitude);
                // pushing into a wall: no start clip (it replayed from standing every half second)
                bool intoWall = Time.time - lastBlockedTime < 0.5f && Vector3.Dot(desiredDir, lastBlockedDirection) > 0.5f;
                if (start != null && !intoWall)
                {
                    StartDirectional(start, LocomotionActionKind.Start, signedAngle, desiredDir);
                    return;
                }
            }

            if (!wantsMove && speed < 0.1f && intent.HasFacing)
            {
                float signedAngle = Vector3.SignedAngle(Forward(), Flat(intent.FacingDirection), Vector3.up);
                if (Mathf.Abs(signedAngle) >= turnInPlaceAngle)
                {
                    DirectionalAction turn = LocomotionClipSet.FindClosest(clipSet.turnsInPlace, signedAngle, directionalClipTolerance);
                    if (turn != null)
                        StartDirectional(turn, LocomotionActionKind.TurnInPlace, signedAngle, Flat(intent.FacingDirection).normalized);
                }
            }
        }

        private bool PivotPhaseReady(DirectionalAction pivot)
        {
            if (pivot.entryPhase < 0f) return true;
            if (pivotWaitStart < 0f) pivotWaitStart = Time.time;
            if (Time.time - pivotWaitStart >= pivotPhaseMaxWait) return true;
            float off = Mathf.Abs(Mathf.DeltaAngle(locomotionAnimator.Phase * 360f, pivot.entryPhase * 360f)) / 360f;
            return off <= pivotPhaseTolerance;
        }

        /// <summary>Returns true when the running action ended and new transitions may be considered.</summary>
        private bool HandleRunningAction(LocomotionClipSet clipSet, bool wantsMove, Vector3 desiredDir)
        {
            LocomotionAnimator anim = locomotionAnimator;
            LocomotionActionKind kind = anim.ActionKind;

            if (kind == LocomotionActionKind.Dash && !anim.IsInFollowUp && !dashResolved)
            {
                anim.GetActionMotionProgress(0f, out float progress, out _);
                if (progress >= 1f)
                {
                    dashResolved = true;
                    // The speed the authored dash curve actually ends at — no jump to an unrelated gait speed.
                    float exitSpeed = DashExitSpeed(anim.CurrentAction);
                    velocity = actionDirection * exitSpeed;
                    springAcceleration = Vector3.zero;

                    if (wantsMove)
                    {
                        anim.EndAction();
                        return false;
                    }

                    PhasedAction stop = brakeAfterDash && exitSpeed >= clipSet.MinStopSpeed()
                        ? clipSet.FindStop(anim.Phase, exitSpeed)
                        : null;
                    if (stop != null) StartStop(stop, exitSpeed);
                    else anim.EndAction();
                    return false;
                }
            }

            if (kind == LocomotionActionKind.Custom) return false;

            bool interruptible = anim.IsInFollowUp
                || kind == LocomotionActionKind.IdleBreak
                || anim.ActionNormalizedTime >= anim.CurrentAction.interruptibleAfter;

            bool wantsChange = wantsMove || intent.RequestDash;
            if (kind == LocomotionActionKind.IdleBreak && intent.HasFacing
                && FacingAngleTo(intent.FacingDirection) > 5f)
                wantsChange = true;

            if (!wantsChange || !interruptible) return false;

            ActionClip leaving = anim.CurrentAction;
            anim.EndAction();
            Launch(leaving);
            return true;
        }

        /// <summary>Push-off after a pivot/start: jump straight to the action's exit speed along the new facing.</summary>
        private void Launch(ActionClip finished)
        {
            if (finished == null || finished.exitSpeed <= 0f || !intent.WantsToMove) return;

            float speed = Mathf.Min(finished.exitSpeed, GetGaitSpeed(intent.Gait));
            if (velocity.magnitude >= speed) return;
            velocity = Forward() * speed;
            springAcceleration = Vector3.zero;
        }

        private void StartStop(PhasedAction stop, float speed)
        {
            actionDirection = speed > 0.01f ? Flat(velocity).normalized : Forward();
            actionEntrySpeed = speed;
            actionTravelScale = 1f;
            actionTurnAmount = 0f;

            if (intent.HasStopTarget)
            {
                Vector3 toTarget = Flat(intent.StopTarget - transform.position);
                float remaining = toTarget.magnitude;
                if (remaining > 0.01f) actionDirection = toTarget / remaining;

                float natural = stop.motion == ActionMotion.Travel
                    ? stop.travelDistance
                    : speed * stop.MotionDuration / (Mathf.Max(0f, stop.decelerationExponent) + 1f);
                if (natural > 1e-4f) actionTravelScale = Mathf.Clamp(remaining / natural, 0.2f, 3f);
            }

            Started(locomotionAnimator.PlayAction(stop, LocomotionActionKind.Stop), LocomotionActionKind.Stop);
        }

        private void StartDash(LocomotionClipSet clipSet, Vector3 fallbackDirection)
        {
            Vector3 direction = Flat(intent.DashDirection);
            direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : fallbackDirection;

            actionDirection = direction;
            actionEntrySpeed = velocity.magnitude;
            actionTravelScale = 1f;
            actionTurnAmount = 0f;
            dashResolved = false;
            dashCooldownTimer = dashCooldown;

            // Anime dash: face the new direction on the first frame (not counted as a turn by lean / twist).
            yaw = YawOf(direction);
            previousYaw = yaw;
            yawVelocity = 0f;

            Started(locomotionAnimator.PlayAction(clipSet.dash, LocomotionActionKind.Dash), LocomotionActionKind.Dash);
        }

        private void StartDirectional(DirectionalAction definition, LocomotionActionKind kind, float signedAngle, Vector3 worldDirection)
        {
            actionDirection = definition.motion == ActionMotion.Decelerate && velocity.sqrMagnitude > 1e-4f
                ? Flat(velocity).normalized
                : worldDirection;
            actionEntrySpeed = velocity.magnitude;
            actionTravelScale = 1f;
            // Warp the authored turn so the character ends exactly on the requested direction, turning the way the
            // clip steps (a left-stepping 180 must not spin right when the request comes out as +179).
            actionTurnAmount = Mathf.Abs(definition.turnAngle) > 0.01f || kind == LocomotionActionKind.Start
                ? definition.turnAngle + Mathf.DeltaAngle(definition.turnAngle, signedAngle)
                : 0f;
            actionTurnWindow = definition.turnWindow;
            if (Mathf.Abs(definition.turnAngle) <= 0.01f && actionTurnWindow.y - actionTurnWindow.x < StartResidualTurnWindow)
                actionTurnWindow = new Vector2(0f, StartResidualTurnWindow);

            Started(locomotionAnimator.PlayAction(definition, kind), kind);
        }

        /// <summary>Obstacle ahead while running: vault over it; sprinting: dive; otherwise the standing jump.</summary>
        private JumpAction PickJump(LocomotionClipSet clipSet, bool allowQueue, out bool queued, out float travelScale, out float height)
        {
            queued = false;
            float speed = CurrentSpeed;
            JumpAction vault = clipSet.obstacleJump;
            float reach = vault != null && vault.plantsHand ? vault.plantTravel + vault.plantForward : vault != null ? vault.apexDistance : 0f;
            if (vault != null && vault.IsValid && speed >= vault.minEntrySpeed && reach > 0.1f
                && FindObstacle(reach * vault.travelScaleRange.y + 0.5f, out float distance, out float top, out Collider obstacle)
                && ObstacleDepth(obstacle, distance) <= vaultMaxDepth)
            {
                float scale;
                if (vault.plantsHand && vault.plantTravel > 0.1f)
                    // the wrist lands just past the obstacle's near edge
                    scale = (distance + vaultHandInset - vault.plantForward) / vault.plantTravel;
                else
                    scale = distance / Mathf.Max(0.1f, vault.apexDistance);
                // a hand plant needs something to put the hand on: low hurdles are cleared with the plain jump
                bool tooLow = vault.plantsHand && top < vault.plantHeight - vaultHandMinDrop;
                if (!tooLow && allowQueue && scale > vaultIdealScale && scale <= vault.travelScaleRange.y)
                {
                    // pressed early: keep running and take off when the stride matches the clip (hand lands, arm in reach)
                    queued = true;
                    travelScale = 1f;
                    height = 0f;
                    return null;
                }
                if (!tooLow && scale >= vault.travelScaleRange.x && scale <= vault.travelScaleRange.y)
                {
                    travelScale = scale;
                    if (vault.plantsHand)
                    {
                        // raise / lower the flight so the planted wrist sits on the top
                        float u = Mathf.Clamp(vault.plantAirPhase, 0.1f, 0.9f);
                        float bump = 4f * u * (1f - u);
                        height = Mathf.Max(0.1f, vault.jumpHeight + (top + vaultWristAboveTop - vault.plantHeight) / bump);
                        PlanHandPlant(vault, scale, top);
                    }
                    else
                    {
                        height = Mathf.Max(vault.jumpHeight, top + obstacleClearance);
                    }
                    IgnoreObstacle(obstacle);
                    return vault;
                }
            }

            JumpAction dive = clipSet.sprintJump;
            if (dive != null && dive.IsValid && speed >= dive.minEntrySpeed)
            {
                travelScale = dive.clipSpeed > 0.1f
                    ? Mathf.Clamp(speed / dive.clipSpeed, dive.travelScaleRange.x, dive.travelScaleRange.y)
                    : 1f;
                height = dive.jumpHeight;
                return dive;
            }

            travelScale = 1f;
            height = clipSet.jump.jumpHeight;
            return clipSet.jump;
        }

        /// <summary>
        /// How deep (along the facing) the obstacle is past its near face. The vault only clears thin things: it lands
        /// right behind them, inside anything deeper (that one is jumped onto instead).
        /// </summary>
        private float ObstacleDepth(Collider obstacle, float distance)
        {
            if (obstacle == null) return float.PositiveInfinity;
            Vector3 forward = Forward();
            float probe = vaultMaxDepth + 0.3f;
            Vector3 near = transform.position + Vector3.up * (obstacleMinHeight + 0.1f) + forward * distance;
            Ray back = new Ray(near + forward * probe, -forward);
            return obstacle.Raycast(back, out RaycastHit hit, probe) ? probe - hit.distance : float.PositiveInfinity;
        }

        /// <summary>Something to vault ahead: distance to its near face and its top above the feet.</summary>
        private bool FindObstacle(float maxDistance, out float distance, out float top, out Collider obstacle)
        {
            distance = top = 0f;
            obstacle = null;
            Vector3 forward = Forward();
            Vector3 origin = transform.position + Vector3.up * (obstacleMinHeight + 0.1f);
            if (!Physics.SphereCast(origin, 0.15f, forward, out RaycastHit hit, maxDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.transform.IsChildOf(transform)) return false;

            Vector3 above = hit.point + forward * 0.15f + Vector3.up * (obstacleMaxHeight + 0.6f);
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit topHit, obstacleMaxHeight + 1f, obstacleLayers, QueryTriggerInteraction.Ignore))
                return false;
            top = topHit.point.y - transform.position.y;
            distance = Vector3.Dot(hit.point - transform.position, forward);
            obstacle = hit.collider;
            return top >= obstacleMinHeight && top <= obstacleMaxHeight;
        }

        private float jumpHeightNow;
        private float vaultQueuedUntil = -1f;
        private bool hasHandPlant;
        private Vector3 handPlantTarget;
        private bool handPlantLeft;
        private JumpAction handPlantJump;
        private Collider ignoredObstacle;

        /// <summary>Where the planned vault puts the wrist: on the obstacle top, beside the travel line.</summary>
        private void PlanHandPlant(JumpAction vault, float scale, float top)
        {
            Vector3 forward = Forward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 point = transform.position + forward * (vault.plantTravel * scale + vault.plantForward)
                + right * vault.plantLateral;
            float y = transform.position.y + top;
            if (Physics.Raycast(new Vector3(point.x, y + 0.5f, point.z), Vector3.down, out RaycastHit hit, 1f, obstacleLayers,
                    QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            handPlantTarget = new Vector3(point.x, y + vaultWristAboveTop, point.z);
            handPlantLeft = vault.plantLeftHand;
            handPlantJump = vault;
            hasHandPlant = true;
        }

        /// <summary>The vault passes over the obstacle the way the clip does; the capsule would snag its edge.</summary>
        private void IgnoreObstacle(Collider obstacle)
        {
            RestoreObstacle();
            if (obstacle == null) return;
            Physics.IgnoreCollision(characterController, obstacle, true);
            ignoredObstacle = obstacle;
        }

        private void RestoreObstacle()
        {
            if (ignoredObstacle != null) Physics.IgnoreCollision(characterController, ignoredObstacle, false);
            ignoredObstacle = null;
        }

        /// <summary>The vault's planted hand: target (world), which hand and blend weight. False when no plant now.</summary>
        public bool TryGetHandPlant(out Vector3 target, out bool left, out float weight)
        {
            target = handPlantTarget;
            left = handPlantLeft;
            weight = 0f;
            if (!hasHandPlant || locomotionAnimator == null || locomotionAnimator.CurrentAction != handPlantJump) return false;
            float t = locomotionAnimator.ActionTime;
            JumpAction j = handPlantJump;
            weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(j.plantStart - handPlantBlend, j.plantStart, t))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(j.plantEnd, j.plantEnd + handPlantBlend, t)));
            return weight > 0f;
        }

        private void StartJump(JumpAction jump, float travelScale, float height)
        {
            if (jump != handPlantJump) hasHandPlant = false;
            if (jump != ClipSet.obstacleJump) RestoreObstacle();
            ResetActionMotion();
            actionDirection = Forward();
            actionTravelScale = travelScale;
            jumpHeightNow = height;
            isJumping = Started(locomotionAnimator.PlayAction(jump, LocomotionActionKind.Jump), LocomotionActionKind.Jump) != 0;
            jumpLaunched = false;
        }

        /// <summary>Touchdown faster than the hard landing's threshold: crouch-absorb instead of the clip's own landing.</summary>
        private void HandleLanding(float impactSpeed)
        {
            LocomotionClipSet clipSet = ClipSet;
            LandingAction land = clipSet != null ? clipSet.hardLanding : null;
            bool falling = locomotionAnimator != null && locomotionAnimator.ActionKind == LocomotionActionKind.Fall;
            if (land == null || !land.IsValid || impactSpeed < land.minImpactSpeed || locomotionAnimator == null)
            {
                // a short fall just goes back to standing / moving
                if (falling) locomotionAnimator.EndAction(0.1f);
                return;
            }
            // running jumps carry their own landing (roll / stride): only plain falls and standing jumps crouch
            if (locomotionAnimator.IsActionActive && locomotionAnimator.ActionKind == LocomotionActionKind.Jump
                && locomotionAnimator.CurrentAction != null && locomotionAnimator.CurrentAction.motion != ActionMotion.None)
                return;

            ResetActionMotion();
            isJumping = false;
            // the crouch absorbs the fall where it lands (the feet lock; sliding on would stretch the legs)
            velocity = Vector3.zero;
            springAcceleration = Vector3.zero;
            if (Started(locomotionAnimator.PlayAction(land, LocomotionActionKind.Land), LocomotionActionKind.Land) != 0)
                locomotionAnimator.SetActionTime(land.touchTime);
        }

        /// <summary>
        /// Takeoff at the clip's takeoff time on a parabola that lands at its landing time; an early landing skips
        /// the clip to the touch-down, a longer fall holds the last airborne pose until the ground is reached.
        /// </summary>
        private void UpdateJump()
        {
            if (!isJumping) return;
            LocomotionAnimator anim = locomotionAnimator;
            if (anim == null || anim.ActionKind != LocomotionActionKind.Jump || !(anim.CurrentAction is JumpAction jump))
            {
                isJumping = false;
                hasHandPlant = false;
                RestoreObstacle();
                if (anim != null) anim.HoldAction(false);
                return;
            }

            float t = anim.ActionTime;
            if (!jumpLaunched)
            {
                if (t < jump.takeoffTime) return;
                jumpLaunched = true;
                float height = Mathf.Max(0.01f, jumpHeightNow);
                float clipAir = Mathf.Max(0.05f, jump.landTime - jump.takeoffTime);
                if (jump.clipFlightTime)
                {
                    // land exactly when the clip does: gravity follows from height and flight time
                    jumpGravity = -8f * height / (clipAir * clipAir);
                    verticalVelocity = 4f * height / clipAir;
                }
                else
                {
                    jumpGravity = Mathf.Min(-1f, jump.jumpGravity);
                    verticalVelocity = Mathf.Sqrt(2f * -jumpGravity * height);
                    // the clip's airborne frames are stretched over the real flight time
                    anim.SetActionSpeedScale(clipAir / (2f * verticalVelocity / -jumpGravity));
                }
                isGrounded = false;
                airTime = 0f;
                return;
            }

            if (isGrounded)
            {
                anim.HoldAction(false);
                anim.SetActionSpeedScale(1f);
                if (t < jump.landTime) anim.SetActionTime(jump.landTime);
                return;
            }
            anim.HoldAction(t >= jump.landTime - jump.fallHoldLead);
        }

        /// <summary>
        /// Free fall (off a ledge, or a jump still falling past its landing frame) plays the looping falling pose;
        /// touchdown ends it (or crouches into the hard landing).
        /// </summary>
        private void UpdateFall()
        {
            LocomotionAnimator anim = locomotionAnimator;
            LocomotionClipSet clipSet = ClipSet;
            if (anim == null || clipSet == null || clipSet.fallLoop == null) return;

            if (anim.ActionKind == LocomotionActionKind.Fall) return;

            if (isGrounded || airTime < clipSet.fallStartTime || verticalVelocity > -1f) return;
            bool free = !anim.IsActionActive;
            // a plain jump that is still falling once its clip has reached the touch-down
            bool overshot = anim.ActionKind == LocomotionActionKind.Jump && anim.CurrentAction is JumpAction j
                && j.motion == ActionMotion.None && anim.ActionTime >= j.landTime - j.fallHoldLead - 0.01f;
            if (!free && !overshot) return;

            isJumping = false;
            anim.HoldAction(false);
            Started(anim.PlayLoopingClip(clipSet.fallLoop, LocomotionActionKind.Fall, 0.15f), LocomotionActionKind.Fall);
        }

        /// <summary>
        /// Short capsule while the slide is low; stands back up only with room above, otherwise the slide holds its
        /// low pose and keeps going until it is out from under.
        /// </summary>
        private void UpdateSlideCapsule()
        {
            if (standingHeight <= 0f)
            {
                standingHeight = characterController.height;
                standingCenter = characterController.center;
            }

            LocomotionAnimator anim = locomotionAnimator;
            bool sliding = anim != null && anim.ActionKind == LocomotionActionKind.Slide && anim.CurrentAction is SlideAction;
            SlideAction slide = sliding ? (SlideAction)anim.CurrentAction : null;
            // (while held under something the clip waits on lowEnd: that is not the authored low part any more)
            bool wantLow = sliding && !slideHeldUnder && slide.lowEnd > slide.lowStart
                && anim.ActionTime >= slide.lowStart && anim.ActionTime <= slide.lowEnd;

            if (wantLow)
            {
                SetCapsuleLow(true);
                return;
            }
            if (!capsuleLow) return;

            if (HasHeadroom())
            {
                HeadroomBlocker = null;
                SetCapsuleLow(false);
                if (slideHeldUnder)
                {
                    slideHeldUnder = false;
                    if (anim != null) anim.HoldAction(false);
                }
                return;
            }

            // still under the bar: stay low (the clip waits on its last low frame) and carry on through
            if (sliding && !slideHeldUnder)
            {
                slideHeldUnder = true;
                anim.SetActionTime(slide.lowEnd);
                anim.HoldAction(true);
            }
            if (!sliding)
            {
                // the slide was cut short under something: keep the short capsule until there is room
                slideHeldUnder = false;
            }
        }

        private void SetCapsuleLow(bool low)
        {
            if (low == capsuleLow) return;
            capsuleLow = low;
            float height = low ? Mathf.Max(slideCapsuleHeight, characterController.radius * 2f + 0.01f) : standingHeight;
            // keep the bottom where it is
            float bottom = standingCenter.y - standingHeight * 0.5f;
            characterController.height = height;
            characterController.center = new Vector3(standingCenter.x, bottom + height * 0.5f, standingCenter.z);
        }

        /// <summary>Debug: what last kept the slide low (no room to stand).</summary>
        public string HeadroomBlocker { get; private set; }

        /// <summary>Room for the standing capsule here (own colliders ignored).</summary>
        private bool HasHeadroom()
        {
            float radius = characterController.radius * 0.95f;
            Vector3 c = transform.TransformPoint(standingCenter);
            float half = Mathf.Max(0f, standingHeight * 0.5f - characterController.radius);
            Vector3 top = c + Vector3.up * half;
            Vector3 bottom = c - Vector3.up * half + Vector3.up * (slideCapsuleHeight - characterController.radius * 2f);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (overlapHits[i] == null || overlapHits[i].transform.IsChildOf(transform)) continue;
                HeadroomBlocker = overlapHits[i].name;
                return false;
            }
            return true;
        }

        private bool IsActionDrivingMotion()
        {
            LocomotionAnimator anim = locomotionAnimator;
            return anim != null && anim.IsActionActive && !anim.IsInFollowUp
                && anim.ActionKind != LocomotionActionKind.IdleBreak
                && !(anim.ActionKind == LocomotionActionKind.Jump && anim.CurrentAction != null
                     && anim.CurrentAction.motion == ActionMotion.None)
                && anim.ActionKind != LocomotionActionKind.Fall
                && anim.CurrentAction != null;
        }

        private bool IsRootMotionActionActive()
        {
            return IsActionDrivingMotion() && locomotionAnimator.CurrentAction.motion == ActionMotion.RootMotion;
        }

        private Vector3 ComputeActionMotion(float dt)
        {
            isDrifting = false;
            ActionClip definition = locomotionAnimator.CurrentAction;
            locomotionAnimator.GetActionMotionProgress(dt, out float from, out float to);
            Vector3 delta = Vector3.zero;

            switch (definition.motion)
            {
                case ActionMotion.Decelerate:
                {
                    float k = Mathf.Max(0f, definition.decelerationExponent);
                    float duration = definition.MotionDuration;
                    float v0 = actionEntrySpeed * actionTravelScale;
                    delta = actionDirection * (v0 * duration * (DecelerationIntegral(to, k) - DecelerationIntegral(from, k)));
                    velocity = actionDirection * (v0 * Mathf.Pow(1f - to, k));
                    break;
                }

                case ActionMotion.Travel when slideHeldUnder:
                {
                    // held low under something: keep sliding out at a steady pace
                    delta = actionDirection * (slideUnderSpeed * dt);
                    velocity = actionDirection * slideUnderSpeed;
                    break;
                }

                case ActionMotion.Travel:
                {
                    float travel = definition.travelDistance * actionTravelScale;
                    delta = actionDirection * (travel * (definition.travelCurve.Evaluate(to) - definition.travelCurve.Evaluate(from)));
                    if (Mathf.Abs(definition.sideDistance) > 1e-4f)
                    {
                        Vector3 right = Vector3.Cross(Vector3.up, actionDirection);
                        delta += right * (definition.sideDistance * actionTravelScale
                            * (definition.sideCurve.Evaluate(to) - definition.sideCurve.Evaluate(from)));
                    }
                    velocity = delta / dt;
                    break;
                }

                case ActionMotion.RootMotion:
                    break;

                default:
                    delta = Spring.CharacterVelocity(ref velocity, ref springAcceleration, Vector3.zero, decelerationHalfLife, dt);
                    break;
            }

            if (definition.motion != ActionMotion.None) springAcceleration = Vector3.zero;

            if (Mathf.Abs(actionTurnAmount) > 0.01f)
                yaw += actionTurnAmount * (TurnProgress(actionTurnWindow, to) - TurnProgress(actionTurnWindow, from));

            return delta;
        }

        private Vector3 SimulateLocomotion(float dt)
        {
            // Follow-ups (brake recovery) and idle breaks stand still until interrupted.
            // Follow-ups and idle breaks stand still; a jump keeps the player's steering (air control)
            bool standStill = locomotionAnimator != null && locomotionAnimator.IsActionActive
                && locomotionAnimator.ActionKind != LocomotionActionKind.Jump
                && locomotionAnimator.ActionKind != LocomotionActionKind.Fall;
            bool allowFacing = !standStill || locomotionAnimator.IsInFollowUp;
            Vector3 goal = standStill ? Vector3.zero : intent.DesiredVelocity;
            bool turningToStart = UpdateProceduralStart(ref goal, standStill);
            UpdateDrift(goal, standStill);

            float halfLife = goal.sqrMagnitude >= velocity.sqrMagnitude ? accelerationHalfLife : decelerationHalfLife;
            if (isDrifting)
            {
                goal *= driftSpeedRetention;
                halfLife = driftGripHalfLife;
            }
            Vector3 delta = Spring.CharacterVelocity(ref velocity, ref springAcceleration, goal, halfLife, dt);

            float speed = velocity.magnitude;
            float targetYaw = yaw;
            float turnHalfLife = speed > 0.5f ? facingHalfLife : idleFacingHalfLife;
            if (isDrifting) turnHalfLife = driftFacingHalfLife;

            if (turningToStart)
            {
                targetYaw = YawOf(intent.DesiredVelocity);
                turnHalfLife = startTurnHalfLife;
            }
            else if (goal.sqrMagnitude > 1e-4f)
            {
                targetYaw = YawOf(goal);
            }
            else if (intent.HasFacing && allowFacing && Flat(intent.FacingDirection).sqrMagnitude > 1e-4f)
            {
                targetYaw = YawOf(intent.FacingDirection);
                // No turn clip: a quick, decisive turn reads better than a slow foot-sliding spin.
                if (FacingAngleTo(intent.FacingDirection) > 45f) turnHalfLife = turnInPlaceHalfLife;
            }
            else if (speed > 0.3f)
            {
                targetYaw = YawOf(velocity);
            }

            Spring.Angle(ref yaw, ref yawVelocity, targetYaw, turnHalfLife, dt);
            return delta;
        }

        /// <summary>Sharp turn at speed: enter a drift (body snaps round, momentum slides) until grip is back.</summary>
        private void UpdateDrift(Vector3 goal, bool standStill)
        {
            float speed = velocity.magnitude;
            if (standStill || goal.sqrMagnitude < 1e-4f || speed < 0.01f)
            {
                isDrifting = false;
                return;
            }

            float angle = Vector3.Angle(velocity, goal);
            if (!isDrifting)
            {
                if (speed >= driftMinSpeed && angle >= driftAngle && angle < pivotAngle)
                {
                    isDrifting = true;
                    DriftStarted?.Invoke();
                }
            }
            else if (angle <= driftExitAngle || speed < driftMinSpeed * 0.5f)
            {
                isDrifting = false;
            }
        }

        /// <summary>
        /// Start without a start clip: if the new direction is behind, turn on the spot first; then push off
        /// with an instant speed jump instead of creeping up from zero. Returns true while turning first.
        /// </summary>
        private bool UpdateProceduralStart(ref Vector3 goal, bool standStill)
        {
            bool wantsMove = goal.sqrMagnitude > 1e-4f;
            if (standStill || !wantsMove)
            {
                isTurningToStart = false;
                return false;
            }

            bool fromRest = velocity.magnitude < 0.1f;
            if (!fromRest && !isTurningToStart) return false;

            float angle = Vector3.Angle(Forward(), goal);
            if (fromRest && !isTurningToStart && angle > startTurnAngle) isTurningToStart = true;

            if (isTurningToStart)
            {
                if (angle > startTurnFinishAngle)
                {
                    goal = Vector3.zero;
                    return true;
                }
                isTurningToStart = false;
            }

            float launch = Mathf.Min(goal.magnitude * startLaunchFraction, startLaunchMaxSpeed);
            if (launch > velocity.magnitude)
            {
                velocity = goal.normalized * launch;
                springAcceleration = Vector3.zero;
            }
            return false;
        }

        /// <summary>Grounded state, ground snapping (slopes / stairs down) and landing detection.</summary>
        private void UpdateGrounding(float dt)
        {
            bool grounded = characterController.isGrounded;
            if (!grounded && isGrounded && verticalVelocity <= 0f) grounded = SnapToGround();

            if (grounded && !isGrounded && airTime >= minAirTimeForLanding)
            {
                float impact = Mathf.Max(0f, -verticalVelocity);
                Landed?.Invoke(impact);
                HandleLanding(impact);
            }

            airTime = grounded ? 0f : airTime + dt;
            isGrounded = grounded;
        }

        private bool SnapToGround()
        {
            const float probeHeight = 0.3f;
            Vector3 origin = transform.position + Vector3.up * probeHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeHeight + groundSnapDistance,
                    groundSnapLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.transform.IsChildOf(transform)) return false;

            float drop = hit.distance - probeHeight;
            if (drop > 0.001f) characterController.Move(Vector3.down * (drop + characterController.skinWidth));
            return true;
        }

        /// <summary>
        /// The model follows the real ground under the character's centre, not the capsule: the capsule's round
        /// bottom rides up step edges / ramp starts before the feet get there (and pops up whole steps), which
        /// made the body float. Target = keep the root-to-ground clearance measured on flat ground; a spring
        /// smooths the change when the centre crosses an edge.
        /// </summary>
        private void UpdateVisualSmoothing(float dt)
        {
            float rootY = transform.position.y;
            float targetHeight = rootY;   // airborne / no ground found: the model rides with the capsule
            bool followGround = false;

            if (isGrounded && ProbeGroundUnderCentre(out float groundY, out Vector3 normal))
            {
                float rootAboveGround = rootY - groundY;
                if (!hasGroundClearance)
                {
                    groundClearance = rootAboveGround;
                    hasGroundClearance = true;
                }
                else if (normal.y > 0.995f && Mathf.Abs(rootAboveGround - groundClearance) < 0.03f)
                {
                    // Keep calibrating on flat ground only (never learn a step edge as "normal").
                    groundClearance = Mathf.Lerp(groundClearance, rootAboveGround, 0.02f);
                }
                targetHeight = groundY + groundClearance;
                followGround = true;
            }

            if (!hasVisualHeight)
            {
                visualHeight = lastTargetHeight = targetHeight;
                visualHeightVelocity = 0f;
                hasVisualHeight = true;
            }

            // Smooth the model's own height (not its offset from the capsule, which would leak the capsule riding
            // up an edge). Feed the ground's rate of change forward so walking up / down a ramp has no lag; the
            // rate is capped at slope speeds so a step edge (a one-frame jump) is smoothed, not fed forward.
            float speedT = Mathf.InverseLerp(GetGaitSpeed(Gait.Walk), GetGaitSpeed(Gait.Sprint), CurrentSpeed);
            float halfLife = Mathf.Lerp(stepSmoothingHalfLife, stepSmoothingHalfLife * 0.4f, speedT);
            float rate = (targetHeight - lastTargetHeight) / dt;
            if (followGround)
            {
                float rateLimit = CurrentSpeed * 0.7f + 0.1f;
                rate = Mathf.Clamp(rate, -rateLimit, rateLimit);
            }
            lastTargetHeight = targetHeight;
            float lead = rate * 2f / (Spring.HalfLifeToDamping(halfLife) * 0.5f);
            Spring.Float(ref visualHeight, ref visualHeightVelocity, targetHeight + lead, halfLife, dt);

            visualOffset = Mathf.Clamp(visualHeight - rootY, -maxStepSmoothing, maxStepSmoothing);
            visualHeight = rootY + visualOffset;
            ApplyVisualOffset();
        }

        private bool ProbeGroundUnderCentre(out float groundY, out Vector3 normal)
        {
            groundY = 0f;
            normal = Vector3.up;
            const float probeHeight = 0.6f;
            Vector3 origin = transform.position + Vector3.up * probeHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeHeight + maxStepSmoothing + 0.2f,
                    groundSnapLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.transform.IsChildOf(transform)) return false;

            groundY = hit.point.y;
            normal = hit.normal;
            return true;
        }

        private void ApplyVisualOffset()
        {
            if (visualRoot == null || !hasVisualBase) return;
            visualRoot.localPosition = visualBaseLocalPosition + Vector3.up * visualOffset;
        }

        private void UpdateDerived(float dt)
        {
            Vector3 rawAcceleration = (actualVelocity - previousVelocity) / dt;
            previousVelocity = actualVelocity;
            rawAcceleration = Vector3.ClampMagnitude(rawAcceleration, maxReportedAcceleration);

            float blend = 1f - Mathf.Exp(-0.6931472f * dt / Mathf.Max(accelerationSmoothingHalfLife, 1e-4f));
            smoothedAcceleration = Vector3.Lerp(smoothedAcceleration, rawAcceleration, blend);

            yawRate = Mathf.DeltaAngle(previousYaw, yaw) / dt;
            previousYaw = yaw;
        }

        private void UpdateIdleBreaks(float dt)
        {
            LocomotionClipSet clipSet = ClipSet;
            bool canIdle = playIdleBreaks && clipSet != null && clipSet.idleBreaks.Length > 0
                && locomotionAnimator != null && !locomotionAnimator.IsActionActive
                && CurrentSpeed < 0.05f && !intent.WantsToMove;

            if (!canIdle)
            {
                idleTimer = 0f;
                nextIdleBreak = 0f;
                return;
            }

            if (nextIdleBreak <= 0f)
            {
                Vector2 range = clipSet.idleBreakInterval;
                nextIdleBreak = Mathf.Lerp(range.x, range.y, (float)idleRandom.NextDouble());
            }

            idleTimer += dt;
            if (idleTimer < nextIdleBreak) return;

            AnimationClip clip = clipSet.idleBreaks[idleRandom.Next(clipSet.idleBreaks.Length)];
            if (clip != null)
            {
                ResetActionMotion();
                Started(locomotionAnimator.PlayClip(clip, LocomotionActionKind.IdleBreak, -1f, 0f), LocomotionActionKind.IdleBreak);
            }
            idleTimer = 0f;
            nextIdleBreak = 0f;
        }

        /// <summary>Eased 0..1 turn progress inside the action's turn window.</summary>
        private static float TurnProgress(Vector2 window, float motionProgress)
        {
            float end = Mathf.Max(window.y, window.x + 1e-3f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(window.x, end, motionProgress));
        }

        /// <summary>Clears per-action motion state so a new action never inherits the previous one's turn.</summary>
        private void ResetActionMotion()
        {
            actionDirection = Forward();
            actionEntrySpeed = velocity.magnitude;
            actionTravelScale = 1f;
            actionTurnAmount = 0f;
        }

        private int Started(int actionId, LocomotionActionKind kind)
        {
            if (actionId != 0) ActionStarted?.Invoke(kind);
            return actionId;
        }

        private static float DashExitSpeed(ActionClip dash)
        {
            if (dash == null || dash.motion != ActionMotion.Travel) return 0f;
            float duration = Mathf.Max(dash.MotionDuration, 1e-4f);
            float slope = (dash.travelCurve.Evaluate(1f) - dash.travelCurve.Evaluate(0.95f)) / 0.05f;
            return Mathf.Max(0f, slope * dash.travelDistance / duration);
        }

        /// <summary>∫0..u (1 - x)^k dx</summary>
        private static float DecelerationIntegral(float u, float k)
        {
            return (1f - Mathf.Pow(1f - u, k + 1f)) / (k + 1f);
        }

        private Vector3 Forward()
        {
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        private static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
        #endregion
    }
}
