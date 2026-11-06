using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Playables-based pose source for LocomotionMotor. Replaces the Animator Controller while enabled.
    /// - Gait loops are phase-synced and advanced by distance travelled, so feet match ground speed.
    /// - One-shot actions (stop, dash, start, pivot, idle breaks, custom clips) switch instantly and
    ///   are smoothed by inertialization instead of crossfades.
    /// Graph: [idle, walk, sprint, sprintFast] -> loopMixer -> rootMixer(loops | action) -> inertializer -> Animator
    /// Can live on the Animator's GameObject or on the character root (Animator auto-found in children).
    /// </summary>
    [DisallowMultipleComponent]
    public class LocomotionAnimator : MonoBehaviour
    {
        private const int IdleSlot = 0;
        private const int LoopSlotCount = 5;
        private const float ActionEndEpsilon = 0.001f;

        #region [Variables/Fields]
        [Header("References")]
        [Tooltip("Auto-found on this object or its children when empty")]
        [SerializeField] private Animator animator;

        [Header("Clips")]
        [SerializeField] private LocomotionClipSet clipSet;

        [Header("Gait Loops")]
        [Tooltip("Smoothing of gait blend weights (s)")]
        [SerializeField] private float gaitWeightHalfLife = 0.05f;
        [Tooltip("Loop playback rate is clamped to this range; outside it the feet slide instead of the legs going frantic")]
        [SerializeField] private Vector2 playbackRateRange = new Vector2(0.6f, 2.4f);
        [Tooltip("Speed (m/s) by which a starting gait cycle has fully replaced idle")]
        [SerializeField] private float startBlendSpeed = 0.8f;
        [Tooltip("Shift up to a faster gait once speed reaches this fraction of its speed")]
        [SerializeField] private float upshiftFraction = 0.3f;
        [Tooltip("Shift down to a slower gait once speed drops below its speed x this")]
        [SerializeField] private float downshiftFactor = 1.35f;
        [Tooltip("Inertialization half-life when switching gait cycles / starting from idle (s)")]
        [SerializeField] private float gaitSwitchHalfLife = 0.15f;
        [SerializeField] private float startHalfLife = 0.1f;

        [Header("Inertialization")]
        [SerializeField] private bool inertialize = true;

        private LocomotionMotor motor;
        private LocomotionRootMotionRelay rootMotionRelay;
        private RuntimeAnimatorController previousController;
        private bool previousApplyRootMotion;
        private bool hasStoredAnimatorState;

        private PlayableGraph graph;
        private AnimationMixerPlayable rootMixer;
        private AnimationMixerPlayable loopMixer;
        private readonly AnimationClipPlayable[] loopPlayables = new AnimationClipPlayable[LoopSlotCount];
        private readonly LoopClip[] loops = new LoopClip[LoopSlotCount];
        private readonly float[] loopWeights = new float[LoopSlotCount];
        private readonly float[] loopWeightVelocities = new float[LoopSlotCount];
        private readonly float[] targetWeights = new float[LoopSlotCount];
        private AnimationClipPlayable actionPlayable;
        private bool hasActionPlayable;
        private AnimationScriptPlayable inertializer;
        private InertializationJob inertialJob;
        private bool hasInertializer;

        private float phase;
        private int activeSlot = IdleSlot;
        private float idleTime;
        private float playbackRate = 1f;

        private ActionClip action;
        private LocomotionActionKind actionKind = LocomotionActionKind.None;
        private AnimationClip activeClip;
        private float activeSpeed = 1f;
        private bool actionHeld;
        private float actionSpeedScale = 1f;
        private bool actionLoops;
        private bool inFollowUp;
        private int actionId;
        private int nextActionId = 1;
        #endregion

        #region [Properties]
        public LocomotionClipSet ClipSet => clipSet;
        public Animator Animator => animator;
        public bool IsReady => graph.IsValid();

        /// <summary>Gait cycle currently playing ("Idle" when none).</summary>
        public string ActiveGaitName
        {
            get
            {
                switch (activeSlot)
                {
                    case 1: return "Walk";
                    case 2: return "Run";
                    case 3: return "Sprint";
                    case 4: return "SprintFast";
                    default: return "Idle";
                }
            }
        }

        /// <summary>Loop phase 0..1 (0 = left foot contact).</summary>
        public float Phase => phase;
        public float PlaybackRate => playbackRate;

        public bool IsActionActive => actionKind != LocomotionActionKind.None;
        public LocomotionActionKind ActionKind => actionKind;
        public ActionClip CurrentAction => action;
        public int CurrentActionId => actionId;
        public bool IsInFollowUp => inFollowUp;

        /// <summary>Seconds since the current action clip (main or follow-up) started, in real time.</summary>
        public float ActionElapsed => hasActionPlayable ? (float)actionPlayable.GetTime() / Mathf.Max(0.01f, activeSpeed) : 0f;
        /// <summary>Clip time (s) of the running action.</summary>
        public float ActionTime => hasActionPlayable ? (float)actionPlayable.GetTime() : 0f;
        public float ActionNormalizedTime => hasActionPlayable && activeClip != null && activeClip.length > 0f
            ? Mathf.Clamp01((float)actionPlayable.GetTime() / activeClip.length)
            : 0f;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            ResolveAnimator();
            motor = GetComponentInParent<LocomotionMotor>();
        }

        private void OnEnable()
        {
            BuildGraph();
        }

        private void OnDisable()
        {
            DestroyGraph();
        }

        private void OnAnimatorMove()
        {
            if (motor != null && animator != null)
                motor.ApplyAnimatorRootMotion(animator.deltaPosition, animator.deltaRotation);
        }
        #endregion

        #region [Public Methods]
        public void SetClipSet(LocomotionClipSet newClipSet)
        {
            clipSet = newClipSet;
            if (isActiveAndEnabled)
            {
                DestroyGraph();
                BuildGraph();
            }
        }

        /// <summary>Movement speed the gait loop is tuned for (ideal speed x speed scale).</summary>
        public float GetLoopSpeed(Gait gait)
        {
            LoopClip loop = GetLoop(gait);
            return loop != null && loop.IsValid ? loop.TargetSpeed : 0f;
        }

        /// <summary>Starts an action clip. Returns its id (0 when it could not be played).</summary>
        public int PlayAction(ActionClip definition, LocomotionActionKind kind)
        {
            if (!graph.IsValid() || definition == null || !definition.IsValid) return 0;

            action = definition;
            actionKind = kind;
            inFollowUp = false;
            actionId = nextActionId++;
            SetActionClip(definition.clip, definition.playbackSpeed, definition.blendInHalfLife);
            return actionId;
        }

        /// <summary>A clip contributing to the current pose: its normalized time and blend weight.</summary>
        public struct PlayingClip
        {
            public AnimationClip clip;
            public float normalizedTime;
            public float weight;
        }

        /// <summary>The clips making up the pose right now (an action covers the loops completely).</summary>
        public void GetPlayingClips(List<PlayingClip> into)
        {
            into.Clear();
            if (IsActionActive && hasActionPlayable && activeClip != null && activeClip.length > 0f)
            {
                float t = (float)actionPlayable.GetTime() / activeClip.length;
                into.Add(new PlayingClip { clip = activeClip, normalizedTime = actionLoops ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t), weight = 1f });
                return;
            }
            if (clipSet == null) return;
            if (loopPlayables[IdleSlot].IsValid() && clipSet.idle != null && loopWeights[IdleSlot] > 0.001f)
                into.Add(new PlayingClip { clip = clipSet.idle, normalizedTime = Mathf.Repeat(idleTime / clipSet.idle.length, 1f), weight = loopWeights[IdleSlot] });
            for (int i = 1; i < LoopSlotCount; i++)
            {
                if (!loopPlayables[i].IsValid() || loops[i] == null || !loops[i].IsValid || loopWeights[i] <= 0.001f) continue;
                into.Add(new PlayingClip { clip = loops[i].clip, normalizedTime = Mathf.Repeat(phase + loops[i].phaseOffset, 1f), weight = loopWeights[i] });
            }
        }

        /// <summary>Plays a looping clip as an action (e.g. a falling pose) until EndAction or another action.</summary>
        public int PlayLoopingClip(AnimationClip clip, LocomotionActionKind kind, float blendHalfLife)
        {
            int id = PlayClip(clip, kind, blendHalfLife, 1f);
            actionLoops = id != 0;
            return id;
        }

        /// <summary>Jumps the running action's clip to this time (s).</summary>
        public void SetActionTime(float time)
        {
            if (!hasActionPlayable) return;
            actionPlayable.SetTime(time);
            actionPlayable.SetTime(time);
        }

        /// <summary>Plays the running action faster / slower (e.g. stretch a jump's airborne frames over a longer flight).</summary>
        public void SetActionSpeedScale(float scale)
        {
            actionSpeedScale = Mathf.Max(0.01f, scale);
        }

        /// <summary>Freezes the running action's clip on its current frame (e.g. waiting for a long fall to land).</summary>
        public void HoldAction(bool hold)
        {
            actionHeld = hold && hasActionPlayable;
        }

        /// <summary>Plays a bare clip in place (idle breaks, custom cinematic moves).</summary>
        public int PlayClip(AnimationClip clip, LocomotionActionKind kind, float blendHalfLife, float interruptibleAfter)
        {
            if (clip == null) return 0;
            ActionClip definition = new ActionClip
            {
                clip = clip,
                blendInHalfLife = blendHalfLife,
                motion = ActionMotion.None,
                interruptibleAfter = interruptibleAfter
            };
            return PlayAction(definition, kind);
        }

        /// <summary>Leaves the current action and returns to the gait loops.</summary>
        public void EndAction(float halfLife = -1f)
        {
            if (!IsActionActive) return;

            if (action != null && action.exitPhase >= 0f)
                phase = Mathf.Repeat(action.exitPhase, 1f);

            action = null;
            actionKind = LocomotionActionKind.None;
            inFollowUp = false;
            actionLoops = false;
            activeClip = null;

            if (!graph.IsValid()) return;

            rootMixer.SetInputWeight(0, 1f);
            rootMixer.SetInputWeight(1, 0f);
            DestroyActionPlayable();
            RequestInertialization(halfLife);
        }

        /// <summary>Back to idle with no gait cycle (after a teleport / cut).</summary>
        public void ResetLoops()
        {
            activeSlot = IdleSlot;
            phase = 0f;
            bool hasIdle = loopPlayables[IdleSlot].IsValid();
            for (int i = 0; i < LoopSlotCount; i++)
            {
                loopWeights[i] = i == IdleSlot && hasIdle ? 1f : 0f;
                loopWeightVelocities[i] = 0f;
                if (graph.IsValid() && loopPlayables[i].IsValid()) loopMixer.SetInputWeight(i, loopWeights[i]);
            }
        }

        /// <summary>Call before movement each frame: advances to follow-ups and finishes ended actions.</summary>
        public void TickActions()
        {
            if (!IsActionActive || !hasActionPlayable || activeClip == null || actionLoops) return;

            if (actionPlayable.GetTime() < activeClip.length - ActionEndEpsilon * 2f) return;

            if (!inFollowUp && action.followUp != null)
            {
                inFollowUp = true;
                SetActionClip(action.followUp, 1f, action.followUpHalfLife);
            }
            else
            {
                EndAction();
            }
        }

        /// <summary>Progress (0..1) of the current action's motion window before and after this frame.</summary>
        public void GetActionMotionProgress(float deltaTime, out float from, out float to)
        {
            from = to = 1f;
            if (!IsActionActive || inFollowUp || action == null) return;

            float duration = Mathf.Max(action.MotionDuration, 1e-4f);
            float elapsed = ActionElapsed;
            from = Mathf.Clamp01(elapsed / duration);
            to = Mathf.Clamp01((elapsed + deltaTime) / duration);
        }

        /// <summary>Call after movement each frame with the planar speed actually travelled.</summary>
        public void Animate(float speed, float deltaTime, Gait gait, bool wantsMove)
        {
            if (!graph.IsValid() || deltaTime <= 0f) return;

            UpdateLoopWeights(speed, deltaTime, gait, wantsMove);
            AdvancePhase(speed, deltaTime);
            ApplyLoopTimes(deltaTime);
            ClampActionSpeed(deltaTime);
        }
        #endregion

        #region [Private Methods]
        private void ResolveAnimator()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
        }

        private void BuildGraph()
        {
            if (graph.IsValid()) return;
            ResolveAnimator();
            if (animator == null)
            {
                Debug.LogWarning($"[LocomotionAnimator] No Animator found on {name} or its children", this);
                return;
            }
            if (clipSet == null)
            {
                Debug.LogWarning($"[LocomotionAnimator] No LocomotionClipSet assigned on {name}", this);
                return;
            }

            previousController = animator.runtimeAnimatorController;
            previousApplyRootMotion = animator.applyRootMotion;
            hasStoredAnimatorState = true;
            animator.runtimeAnimatorController = null;

            // Root motion is only needed by RootMotion actions; otherwise keep the model locked to the root.
            bool usesRootMotion = UsesRootMotion();
            animator.applyRootMotion = usesRootMotion;
            if (usesRootMotion && animator.gameObject != gameObject)
            {
                rootMotionRelay = animator.gameObject.AddComponent<LocomotionRootMotionRelay>();
                rootMotionRelay.hideFlags = HideFlags.DontSave;
                rootMotionRelay.Bind(animator, motor != null ? motor : GetComponentInParent<LocomotionMotor>());
            }

            graph = PlayableGraph.Create($"{name}.Locomotion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Locomotion", animator);

            loops[IdleSlot] = null;
            loops[1] = clipSet.walk;
            loops[2] = clipSet.run;
            loops[3] = clipSet.sprint;
            loops[4] = clipSet.sprintFast;

            loopMixer = AnimationMixerPlayable.Create(graph, LoopSlotCount);
            for (int i = 0; i < LoopSlotCount; i++)
            {
                AnimationClip clip = i == IdleSlot ? clipSet.idle : (loops[i] != null && loops[i].IsValid ? loops[i].clip : null);
                loopWeights[i] = 0f;
                loopWeightVelocities[i] = 0f;
                loopPlayables[i] = default;
                if (clip == null) continue;

                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetSpeed(0d);
                graph.Connect(playable, 0, loopMixer, i);
                loopPlayables[i] = playable;
            }
            loopWeights[IdleSlot] = clipSet.idle != null ? 1f : 0f;
            activeSlot = IdleSlot;

            rootMixer = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(loopMixer, 0, rootMixer, 0);
            rootMixer.SetInputWeight(0, 1f);

            Playable source = rootMixer;
            if (inertialize)
            {
                List<Transform> bones = CollectAnimatedBones();
                if (bones.Count > 0)
                {
                    inertialJob = InertializationJob.Create(animator, bones);
                    inertializer = AnimationScriptPlayable.Create(graph, inertialJob, 1);
                    graph.Connect(rootMixer, 0, inertializer, 0);
                    inertializer.SetInputWeight(0, 1f);
                    source = inertializer;
                    hasInertializer = true;
                }
            }

            output.SetSourcePlayable(source);
            ApplyLoopTimes(0f);
            graph.Play();
        }

        private void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            if (hasInertializer) inertialJob.Dispose();
            hasInertializer = false;
            hasActionPlayable = false;
            action = null;
            actionKind = LocomotionActionKind.None;
            inFollowUp = false;

            if (animator != null && hasStoredAnimatorState)
            {
                animator.runtimeAnimatorController = previousController;
                animator.applyRootMotion = previousApplyRootMotion;
            }
            previousController = null;
            hasStoredAnimatorState = false;

            if (rootMotionRelay != null) Destroy(rootMotionRelay);
            rootMotionRelay = null;
        }

        private bool UsesRootMotion()
        {
            foreach (ActionClip candidate in clipSet.AllActions())
                if (candidate.IsValid && candidate.motion == ActionMotion.RootMotion) return true;
            return false;
        }

        private List<Transform> CollectAnimatedBones()
        {
            List<Transform> bones = new List<Transform>();

#if UNITY_EDITOR
            if (clipSet.animatedPaths == null || clipSet.animatedPaths.Length == 0)
                clipSet.RefreshAnimatedPaths();
#endif
            if (clipSet.animatedPaths != null && clipSet.animatedPaths.Length > 0)
            {
                foreach (string path in clipSet.animatedPaths)
                {
                    Transform bone = animator.transform.Find(path);
                    if (bone != null) bones.Add(bone);
                }
                return bones;
            }

            foreach (Transform child in animator.GetComponentsInChildren<Transform>(true))
            {
                if (child != animator.transform) bones.Add(child);
            }
            return bones;
        }

        private void SetActionClip(AnimationClip clip, float speed, float halfLife)
        {
            DestroyActionPlayable();

            actionPlayable = AnimationClipPlayable.Create(graph, clip);
            actionPlayable.SetApplyFootIK(false);
            actionPlayable.SetTime(0d);
            actionPlayable.SetSpeed(speed);
            graph.Connect(actionPlayable, 0, rootMixer, 1);
            rootMixer.SetInputWeight(0, 0f);
            rootMixer.SetInputWeight(1, 1f);

            activeClip = clip;
            activeSpeed = Mathf.Max(0.01f, speed);
            actionHeld = false;
            actionSpeedScale = 1f;
            actionLoops = false;
            hasActionPlayable = true;
            RequestInertialization(halfLife);
        }

        private void DestroyActionPlayable()
        {
            if (!hasActionPlayable) return;
            if (actionPlayable.IsValid())
            {
                graph.Disconnect(rootMixer, 1);
                actionPlayable.Destroy();
            }
            hasActionPlayable = false;
        }

        private void RequestInertialization(float halfLife)
        {
            if (!hasInertializer || !inertializer.IsValid()) return;

            InertializationJob job = inertializer.GetJobData<InertializationJob>();
            job.requestId++;
            job.halfLife = halfLife > 0f ? halfLife : clipSet.defaultInertialHalfLife;
            inertializer.SetJobData(job);
        }

        private LoopClip GetLoop(Gait gait)
        {
            if (clipSet == null) return null;
            switch (gait)
            {
                case Gait.Walk: return clipSet.walk;
                case Gait.Run: return clipSet.run;
                case Gait.Sprint: return clipSet.sprint;
                default: return clipSet.sprintFast;
            }
        }

        private void UpdateLoopWeights(float speed, float deltaTime, Gait gait, bool wantsMove)
        {
            SelectActiveGait(speed, gait, wantsMove);
            ComputeTargetWeights(speed);

            float sum = 0f;
            for (int i = 0; i < LoopSlotCount; i++)
            {
                Spring.Float(ref loopWeights[i], ref loopWeightVelocities[i], targetWeights[i], gaitWeightHalfLife, deltaTime);
                loopWeights[i] = Mathf.Max(0f, loopWeights[i]);
                sum += loopWeights[i];
            }

            for (int i = 0; i < LoopSlotCount; i++)
            {
                float weight = sum > 1e-5f ? loopWeights[i] / sum : (i == IdleSlot ? 1f : 0f);
                if (loopPlayables[i].IsValid()) loopMixer.SetInputWeight(i, weight);
            }
        }

        /// <summary>
        /// One gait cycle at a time, chosen from the intended gait like a gearbox (no walk cycle flashing
        /// through a sprint start). Switching cycles is a discrete, inertialized change; phase carries over.
        /// </summary>
        private void SelectActiveGait(float speed, Gait gait, bool wantsMove)
        {
            int intended = ValidSlot(SlotOf(gait));
            if (intended < 0) return;

            if (activeSlot == IdleSlot)
            {
                if (!wantsMove && speed < 0.2f) return;

                // Start from rest: straight into the intended cycle, left foot first.
                activeSlot = wantsMove ? intended : SlotForSpeed(speed);
                phase = 0f;
                if (!IsActionActive) RequestInertialization(startHalfLife);
                return;
            }

            if (!wantsMove)
            {
                // Coming to rest: hand the cycle back to idle once its weight has faded.
                if (speed < 0.05f && loopWeights[activeSlot] < 0.02f) activeSlot = IdleSlot;
                return;
            }

            if (intended == activeSlot) return;

            bool shift = intended > activeSlot
                ? speed >= loops[intended].TargetSpeed * upshiftFraction
                : speed <= loops[intended].TargetSpeed * downshiftFactor;
            if (!shift) return;

            // Carry the cycle weight over to the new gait and let inertialization hide the switch.
            loopWeights[intended] = loopWeights[activeSlot];
            loopWeightVelocities[intended] = 0f;
            loopWeights[activeSlot] = 0f;
            loopWeightVelocities[activeSlot] = 0f;
            activeSlot = intended;
            if (!IsActionActive) RequestInertialization(gaitSwitchHalfLife);
        }

        /// <summary>Idle vs the active gait cycle by speed: the cycle is fully in by startBlendSpeed.</summary>
        private void ComputeTargetWeights(float speed)
        {
            for (int i = 0; i < LoopSlotCount; i++) targetWeights[i] = 0f;

            bool hasIdle = loopPlayables[IdleSlot].IsValid();
            if (activeSlot == IdleSlot || !IsSlotValid(activeSlot))
            {
                if (hasIdle) targetWeights[IdleSlot] = 1f;
                return;
            }

            float blendSpeed = Mathf.Min(startBlendSpeed, loops[activeSlot].TargetSpeed * 0.5f);
            float moving = hasIdle ? Mathf.SmoothStep(0f, 1f, speed / Mathf.Max(blendSpeed, 1e-3f)) : 1f;
            targetWeights[activeSlot] = moving;
            if (hasIdle) targetWeights[IdleSlot] = 1f - moving;
        }

        private static int SlotOf(Gait gait)
        {
            switch (gait)
            {
                case Gait.Walk: return 1;
                case Gait.Run: return 2;
                case Gait.Sprint: return 3;
                default: return 4;
            }
        }

        private bool IsSlotValid(int slot)
        {
            return slot > IdleSlot && slot < LoopSlotCount && loops[slot] != null && loops[slot].IsValid
                && loopPlayables[slot].IsValid();
        }

        /// <summary>The slot itself, or the nearest valid gait (prefers slower) when that clip is missing.</summary>
        private int ValidSlot(int slot)
        {
            for (int offset = 0; offset < LoopSlotCount; offset++)
            {
                if (IsSlotValid(slot - offset)) return slot - offset;
                if (IsSlotValid(slot + offset)) return slot + offset;
            }
            return -1;
        }

        /// <summary>Slowest gait that can cover this speed without the legs going frantic.</summary>
        private int SlotForSpeed(float speed)
        {
            int fastest = -1;
            for (int i = 1; i < LoopSlotCount; i++)
            {
                if (!IsSlotValid(i)) continue;
                if (speed <= loops[i].TargetSpeed * downshiftFactor) return i;
                fastest = i;
            }
            return fastest;
        }

        /// <summary>Phase advances by distance / cycle distance, so stride matches ground speed.</summary>
        private void AdvancePhase(float speed, float deltaTime)
        {
            float movingWeight = 0f, cycleDistance = 0f, cycleLength = 0f;
            for (int i = 1; i < LoopSlotCount; i++)
            {
                if (loops[i] == null || !loops[i].IsValid) continue;
                float w = loopWeights[i];
                movingWeight += w;
                cycleDistance += w * loops[i].CycleDistance;
                cycleLength += w * loops[i].clip.length;
            }

            if (movingWeight < 1e-3f)
            {
                playbackRate = 1f;
                return;
            }

            cycleDistance /= movingWeight;
            cycleLength /= movingWeight;

            playbackRate = speed / Mathf.Max(cycleDistance, 1e-4f) * cycleLength;
            playbackRate = Mathf.Min(playbackRate, playbackRateRange.y);
            if (speed > 0.05f) playbackRate = Mathf.Max(playbackRate, playbackRateRange.x);

            phase = Mathf.Repeat(phase + playbackRate / Mathf.Max(cycleLength, 1e-4f) * deltaTime, 1f);
        }

        private void ApplyLoopTimes(float deltaTime)
        {
            if (loopPlayables[IdleSlot].IsValid() && clipSet.idle != null)
            {
                idleTime += deltaTime;
                loopPlayables[IdleSlot].SetTime(Mathf.Repeat(idleTime, clipSet.idle.length));
            }

            for (int i = 1; i < LoopSlotCount; i++)
            {
                if (!loopPlayables[i].IsValid() || loops[i] == null || !loops[i].IsValid) continue;
                float normalized = Mathf.Repeat(phase + loops[i].phaseOffset, 1f);
                loopPlayables[i].SetTime(normalized * loops[i].clip.length);
            }
        }

        /// <summary>
        /// Lands the last evaluated frame just short of the clip end: evaluating a looping clip exactly at its
        /// length wraps to frame 0 and would flash the first pose.
        /// </summary>
        private void ClampActionSpeed(float deltaTime)
        {
            if (!hasActionPlayable || activeClip == null) return;
            if (actionHeld)
            {
                actionPlayable.SetSpeed(0f);
                return;
            }
            if (actionLoops)
            {
                actionPlayable.SetSpeed(activeSpeed * actionSpeedScale);
                return;
            }

            float speed = activeSpeed * actionSpeedScale;
            float remaining = activeClip.length - ActionEndEpsilon - (float)actionPlayable.GetTime();
            if (remaining < speed * deltaTime)
                actionPlayable.SetSpeed(Mathf.Max(0f, remaining / deltaTime));
            else
                actionPlayable.SetSpeed(speed);
        }
        #endregion
    }
}
