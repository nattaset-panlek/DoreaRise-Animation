using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif

namespace DoreaRise.Locomotion
{
    public enum ActionMotion
    {
        /// <summary>Character stands still.</summary>
        None,
        /// <summary>Movement comes from the clip's root motion.</summary>
        RootMotion,
        /// <summary>Entry velocity decays to zero: v(t) = v0 * (1 - t/T)^k. Used for stops / brakes.</summary>
        Decelerate,
        /// <summary>Moves a fixed distance along the action direction following travelCurve. Used for dashes / starts.</summary>
        Travel
    }

    /// <summary>A looping gait cycle. Phase 0 must be the left foot contact (use phaseOffset to align).</summary>
    [Serializable]
    public class LoopClip
    {
        public AnimationClip clip;
        [Tooltip("Ground speed of a planted foot at playback 1x (m/s), measured during stance — not stride / cycle " +
                 "time, which misses the flight phase of a run. This is what stops foot sliding")]
        public float idealSpeed = 1f;
        [Tooltip("Gait speed = idealSpeed x speedScale; the clip plays that much faster and stays foot-synced " +
                 "(e.g. 1.3 turns a run into a faster anime sprint)")]
        public float speedScale = 1f;
        [Tooltip("Normalized clip time of the left foot contact")]
        [Range(0f, 1f)] public float phaseOffset;

        public bool IsValid => clip != null && idealSpeed > 0.01f;
        public float TargetSpeed => idealSpeed * Mathf.Max(0.01f, speedScale);
        public float CycleDistance => IsValid ? idealSpeed * clip.length : 0f;
    }

    [Serializable]
    public class ActionClip
    {
        public AnimationClip clip;
        public float playbackSpeed = 1f;
        [Tooltip("Inertialization half-life when entering this clip (s). <= 0 uses the clip set default")]
        public float blendInHalfLife = -1f;

        [Header("Motion")]
        public ActionMotion motion = ActionMotion.None;
        [Tooltip("Decelerate: exponent k in v(t) = v0 (1 - t/T)^k. Larger = harder brake at the start")]
        public float decelerationExponent = 3f;
        [Tooltip("Travel: metres moved along the action direction")]
        public float travelDistance;
        [Tooltip("Travel: normalized displacement over normalized motion time")]
        public AnimationCurve travelCurve = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("Travel: metres moved to the right of the action direction (e.g. a turning start swinging round its " +
                 "inside foot). 0 = none")]
        public float sideDistance;
        [Tooltip("Travel: normalized sideways displacement over normalized motion time")]
        public AnimationCurve sideCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        [Tooltip("Seconds over which the motion is applied. <= 0 = whole clip")]
        public float motionDuration = -1f;
        [Tooltip("Degrees the character turns during the action (in-place clips without root rotation)")]
        public float turnAngle;
        [Tooltip("Part of the motion window (0..1) in which the turn happens, e.g. 0.45-0.95 = skid first, spin at the end")]
        public Vector2 turnWindow = new Vector2(0f, 1f);
        [Tooltip("Leaving this action while still moving: launch at this speed along the new facing (m/s). 0 = no launch")]
        public float exitSpeed;
        [Tooltip("Planted feet may lock during this action (authored steps). Off for skids, which must slide")]
        public bool lockFeet;

        [Header("Flow")]
        [Tooltip("Normalized time after which new movement intent may interrupt this clip")]
        [Range(0f, 1f)] public float interruptibleAfter = 0.6f;
        [Tooltip("Loop phase to resume at when leaving this action. < 0 keeps the current phase")]
        public float exitPhase = -1f;
        [Tooltip("Optional clip played right after (e.g. Brake -> BrakeToIdle). Always interruptible")]
        public AnimationClip followUp;
        public float followUpHalfLife = -1f;

        public bool IsValid => clip != null;
        public float Duration => clip != null ? clip.length / Mathf.Max(0.01f, playbackSpeed) : 0f;
        public float MotionDuration => motionDuration > 0f ? Mathf.Min(motionDuration, Duration) : Duration;
    }

    /// <summary>
    /// Jump: the clip plays from the button press; at takeoffTime the capsule leaves the ground on a parabola that
    /// lands at landTime (on flat ground) and peaks at jumpHeight. The clip itself carries no rise, so a longer
    /// fall holds the last airborne pose and an early landing skips to the touch-down.
    /// </summary>
    [Serializable]
    public class JumpAction : ActionClip
    {
        [Tooltip("Clip time (s) the feet leave the ground")]
        public float takeoffTime = 0.2f;
        [Tooltip("Clip time (s) the feet touch down on flat ground")]
        public float landTime = 0.7f;
        [Tooltip("Apex height of the jump (m). The clip's airborne frames stretch over however long the flight lasts")]
        public float jumpHeight = 1.2f;
        [Tooltip("Gravity while jumping (m/s², negative). Stronger = snappier, shorter flight for the same height")]
        public float jumpGravity = -24f;
        [Tooltip("Seconds before landTime at which the clip waits while still falling (longer drops)")]
        public float fallHoldLead = 0.03f;

        [Header("Running Jumps (Travel motion)")]
        [Tooltip("Flight time comes from the clip (takeoff to landing); gravity is derived from it and the height. " +
                 "Off = jumpGravity sets the flight and the clip's airborne frames stretch to it")]
        public bool clipFlightTime;
        [Tooltip("Only chosen when moving at least this fast (m/s)")]
        public float minEntrySpeed;
        [Tooltip("Ground speed the clip was authored at, at takeoff (m/s)")]
        public float clipSpeed;
        [Tooltip("Travel from the clip's start to the top of the jump (m), at travel scale 1")]
        public float apexDistance;
        [Tooltip("Allowed travel scale (entry speed / clip speed, or obstacle distance / apex distance)")]
        public Vector2 travelScaleRange = new Vector2(0.7f, 1.6f);

        [Header("Hand Plant (vault)")]
        [Tooltip("A hand pushes off the obstacle: the jump is placed so it lands on the top, then IK holds it there")]
        public bool plantsHand;
        public bool plantLeftHand = true;
        [Tooltip("Clip time (s) the hand is on the obstacle")]
        public float plantStart;
        public float plantEnd;
        [Tooltip("Root travel from the clip start to the plant, at travel scale 1 (m)")]
        public float plantTravel;
        [Tooltip("The wrist's offset from the root at the plant: forward / to the right (m)")]
        public float plantForward;
        public float plantLateral;
        [Tooltip("Wrist height above the ground at the plant in the clip (m)")]
        public float plantHeight;
        [Tooltip("Where in the flight (0 takeoff .. 1 landing) the plant is")]
        public float plantAirPhase = 0.4f;
    }

    /// <summary>Sprint slide: travels like the clip, stretched to the entry speed, then runs on (or stops).</summary>
    [Serializable]
    public class SlideAction : ActionClip
    {
        [Tooltip("Only when moving at least this fast (m/s)")]
        public float minEntrySpeed = 5f;
        [Tooltip("Speed the clip enters the slide at (m/s)")]
        public float clipSpeed = 6.5f;
        [Tooltip("Allowed travel scale (entry speed / clip speed)")]
        public Vector2 travelScaleRange = new Vector2(0.8f, 1.6f);
        [Tooltip("Clip time (s) the body is low enough for the short capsule (head top under lowHeadTop)")]
        public float lowStart;
        public float lowEnd;
        [Tooltip("Highest the head top gets inside the low part (m)")]
        public float lowHeadTop = 0.85f;
    }

    /// <summary>Played on touchdown after a fall faster than minImpactSpeed, from the clip's touchdown frame.</summary>
    [Serializable]
    public class LandingAction : ActionClip
    {
        [Tooltip("Clip time (s) the feet touch the ground; playback starts here")]
        public float touchTime;
        [Tooltip("Vertical speed at touchdown (m/s) that calls for this landing. A 1.2 m jump lands at about 7.6")]
        public float minImpactSpeed = 10f;
    }

    /// <summary>Action chosen by the angle between facing and desired direction (starts, pivots, turns).</summary>
    [Serializable]
    public class DirectionalAction : ActionClip
    {
        [Tooltip("Signed angle (degrees) this clip is authored for. +90 = turn right")]
        public float angle;
        [Tooltip("Only used when the requested move speed is at least this (m/s), e.g. a run start is not a walk start")]
        public float minIntentSpeed;
        [Tooltip("Only used when the requested move speed is at most this (m/s). 0 = no limit")]
        public float maxIntentSpeed;
        [Tooltip("Pivots: loop phase the clip's first frame matches (which foot is planted). The pivot waits briefly " +
                 "for it so the planted foot is not dragged. < 0 = start at any phase")]
        public float entryPhase = -1f;

        public bool AcceptsIntentSpeed(float speed)
        {
            return speed >= minIntentSpeed && (maxIntentSpeed <= 0f || speed <= maxIntentSpeed);
        }
    }

    /// <summary>Action chosen by the current loop phase (e.g. stop on left foot vs right foot).</summary>
    [Serializable]
    public class PhasedAction : ActionClip
    {
        [Tooltip("Loop phase range in which this clip is valid (wraps when x > y)")]
        public Vector2 phaseRange = new Vector2(0f, 1f);
        [Tooltip("Only used when moving at least this fast (m/s)")]
        public float minSpeed = 2.5f;

        public bool ContainsPhase(float phase)
        {
            if (phaseRange.x <= phaseRange.y) return phase >= phaseRange.x && phase <= phaseRange.y;
            return phase >= phaseRange.x || phase <= phaseRange.y;
        }
    }

    [CreateAssetMenu(menuName = "DoreaRise/Locomotion Clip Set", fileName = "LocomotionClipSet")]
    public class LocomotionClipSet : ScriptableObject
    {
        #region [Variables/Fields]
        [Header("Idle")]
        public AnimationClip idle;
        [Tooltip("Personality idles played now and then while standing")]
        public AnimationClip[] idleBreaks = new AnimationClip[0];
        [Tooltip("Seconds of standing still between idle breaks (random in range)")]
        public Vector2 idleBreakInterval = new Vector2(8f, 14f);

        [Header("Gait Loops (phase 0 = left foot contact)")]
        public LoopClip walk = new LoopClip();
        public LoopClip run = new LoopClip();
        public LoopClip sprint = new LoopClip();
        public LoopClip sprintFast = new LoopClip();

        [Header("Transitions")]
        [Tooltip("Stops, chosen by loop phase")]
        public PhasedAction[] stops = new PhasedAction[0];
        [Tooltip("Starts from idle, chosen by angle between facing and move direction")]
        public DirectionalAction[] starts = new DirectionalAction[0];
        [Tooltip("Sharp direction changes while moving, chosen by angle")]
        public DirectionalAction[] pivots = new DirectionalAction[0];
        [Tooltip("Turns while standing, chosen by angle")]
        public DirectionalAction[] turnsInPlace = new DirectionalAction[0];
        public ActionClip dash = new ActionClip();
        public JumpAction jump = new JumpAction();
        [Tooltip("Jump pressed while running with an obstacle ahead: the top of the jump is placed over it")]
        public JumpAction obstacleJump = new JumpAction();
        [Tooltip("Jump pressed while sprinting: a long dive, roll and back up running")]
        public JumpAction sprintJump = new JumpAction();
        public LandingAction hardLanding = new LandingAction();
        [Tooltip("Slide key while sprinting")]
        public SlideAction slide = new SlideAction();
        [Tooltip("Looping in-air pose for falls (off ledges, or a jump that keeps falling past its landing)")]
        public AnimationClip fallLoop;
        [Tooltip("Seconds of free fall before the falling loop takes over")]
        public float fallStartTime = 0.3f;

        [Header("Blending")]
        [Tooltip("Default inertialization half-life (s). Small = snappy anime transitions")]
        public float defaultInertialHalfLife = 0.1f;

        [HideInInspector] public string[] animatedPaths = new string[0];
        #endregion

        #region [Public Methods]
        public IEnumerable<AnimationClip> AllClips()
        {
            if (idle) yield return idle;
            foreach (AnimationClip clip in idleBreaks) if (clip) yield return clip;
            foreach (LoopClip loop in new[] { walk, run, sprint, sprintFast }) if (loop != null && loop.clip) yield return loop.clip;
            foreach (ActionClip action in AllActions())
            {
                if (action.clip) yield return action.clip;
                if (action.followUp) yield return action.followUp;
            }
        }

        public IEnumerable<ActionClip> AllActions()
        {
            foreach (PhasedAction a in stops) if (a != null) yield return a;
            foreach (DirectionalAction a in starts) if (a != null) yield return a;
            foreach (DirectionalAction a in pivots) if (a != null) yield return a;
            foreach (DirectionalAction a in turnsInPlace) if (a != null) yield return a;
            if (dash != null) yield return dash;
            if (fallLoop != null) yield return new ActionClip { clip = fallLoop };
            if (jump != null) yield return jump;
            if (obstacleJump != null) yield return obstacleJump;
            if (sprintJump != null) yield return sprintJump;
            if (hardLanding != null) yield return hardLanding;
            if (slide != null) yield return slide;
        }

        public PhasedAction FindStop(float phase, float speed)
        {
            PhasedAction fallback = null;
            foreach (PhasedAction stop in stops)
            {
                if (stop == null || !stop.IsValid || speed < stop.minSpeed) continue;
                if (stop.ContainsPhase(phase)) return stop;
                if (fallback == null) fallback = stop;
            }
            return fallback;
        }

        /// <summary>Lowest minSpeed among valid stops, or +inf when there is no stop clip.</summary>
        public float MinStopSpeed()
        {
            float min = float.PositiveInfinity;
            foreach (PhasedAction stop in stops)
                if (stop != null && stop.IsValid) min = Mathf.Min(min, stop.minSpeed);
            return min;
        }

        public static DirectionalAction FindClosest(DirectionalAction[] actions, float signedAngle, float maxError,
            float intentSpeed = -1f)
        {
            DirectionalAction best = null;
            float bestError = maxError;
            foreach (DirectionalAction action in actions)
            {
                if (action == null || !action.IsValid) continue;
                if (intentSpeed >= 0f && !action.AcceptsIntentSpeed(intentSpeed)) continue;
                float error = Mathf.Abs(Mathf.DeltaAngle(action.angle, signedAngle));
                // +180 and -180 are the same angle: prefer the clip that turns the requested way
                if (Mathf.Abs(signedAngle) > 1f && Mathf.Abs(action.angle) > 1f && Mathf.Sign(action.angle) != Mathf.Sign(signedAngle))
                    error += 0.5f;
                if (error <= bestError)
                {
                    bestError = error;
                    best = action;
                }
            }
            return best;
        }

#if UNITY_EDITOR
        /// <summary>Collects every transform path animated by the clips (used to bind the inertializer).</summary>
        [ContextMenu("Refresh Animated Paths")]
        public void RefreshAnimatedPaths()
        {
            HashSet<string> paths = new HashSet<string>();
            foreach (AnimationClip clip in AllClips())
            {
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(Transform) && !string.IsNullOrEmpty(binding.path))
                        paths.Add(binding.path);
                }
            }

            List<string> sorted = new List<string>(paths);
            sorted.Sort(string.CompareOrdinal);
            if (animatedPaths != null && animatedPaths.SequenceEqual(sorted)) return;

            animatedPaths = sorted.ToArray();
            EditorUtility.SetDirty(this);
        }

        private void OnValidate()
        {
            // Defer: AnimationUtility is not safe to call during deserialization.
            EditorApplication.delayCall += () =>
            {
                if (this != null) RefreshAnimatedPaths();
            };
        }
#endif
        #endregion
    }
}
