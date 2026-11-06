using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Drives the character from script / Timeline commands, one command at a time.
    /// Every command returns a LocomotionCommand that can be yielded in a coroutine.
    /// A new command cancels the running one.
    /// </summary>
    public class ScriptedLocomotionDriver : LocomotionDriver
    {
        private enum CommandType
        {
            None,
            Path,
            Direction,
            Face,
            Stop,
            Dash,
            Action
        }

        #region [Variables/Fields]
        [Header("Path Following")]
        [Tooltip("Distance at which an intermediate waypoint counts as reached")]
        [SerializeField] private float waypointRadius = 0.6f;
        [Tooltip("Final position tolerance (m)")]
        [SerializeField] private float arriveTolerance = 0.05f;
        [Tooltip("Max speed near the destination = distance x gain. Keeps short moves from overshooting")]
        [SerializeField] private float approachGain = 8f;
        [Tooltip("Facing tolerance before a turn counts as done (degrees)")]
        [SerializeField] private float faceTolerance = 3f;

        [Header("Behaviour")]
        [Tooltip("Keep control (stand still) after the last command, so player input cannot take over")]
        [SerializeField] private bool holdControlWhenIdle;

        private LocomotionCommand current;
        private CommandType type;
        private readonly List<Vector3> path = new List<Vector3>();
        private int pathIndex;
        private Gait gait = Gait.Sprint;
        private Vector3 direction;
        private float timer;
        private bool hasFinalFacing;
        private Vector3 finalFacing;
        private const float DashRequestTimeout = 1f;

        private bool isStopping;
        private bool dashStarted;
        private int actionId;
        private AnimationClip actionClip;
        private float actionHalfLife;
        #endregion

        #region [Properties]
        protected override int DefaultPriority => 10;
        public bool IsBusy => current != null && !current.IsDone;
        public LocomotionCommand CurrentCommand => current;

        public bool HoldControlWhenIdle
        {
            get => holdControlWhenIdle;
            set => holdControlWhenIdle = value;
        }
        #endregion

        #region [Public Methods]
        public LocomotionCommand MoveTo(Vector3 destination, Gait moveGait = Gait.Sprint)
        {
            return FollowPath(new[] { destination }, moveGait, null);
        }

        /// <summary>Moves to the transform; optionally ends facing the same way as it.</summary>
        public LocomotionCommand MoveTo(Transform destination, Gait moveGait = Gait.Sprint, bool matchRotation = false)
        {
            if (destination == null) return Completed("MoveTo");
            Vector3? facing = matchRotation ? destination.forward : (Vector3?)null;
            return FollowPath(new[] { destination.position }, moveGait, facing);
        }

        public LocomotionCommand FollowPath(IList<Vector3> points, Gait moveGait = Gait.Sprint, Vector3? endFacing = null)
        {
            LocomotionCommand command = Begin("FollowPath", CommandType.Path);
            path.Clear();
            if (points != null) path.AddRange(points);
            if (path.Count == 0) return Completed(command);

            pathIndex = 0;
            gait = moveGait;
            isStopping = false;
            hasFinalFacing = endFacing.HasValue && Flat(endFacing.Value).sqrMagnitude > 0.0001f;
            if (hasFinalFacing) finalFacing = Flat(endFacing.Value).normalized;
            return command;
        }

        /// <summary>Follows the children of <paramref name="pathRoot"/> in hierarchy order.</summary>
        public LocomotionCommand FollowPath(Transform pathRoot, Gait moveGait = Gait.Sprint, bool matchLastRotation = false)
        {
            if (pathRoot == null || pathRoot.childCount == 0) return Completed("FollowPath");

            List<Vector3> points = new List<Vector3>(pathRoot.childCount);
            foreach (Transform child in pathRoot)
                points.Add(child.position);

            Transform last = pathRoot.GetChild(pathRoot.childCount - 1);
            return FollowPath(points, moveGait, matchLastRotation ? last.forward : (Vector3?)null);
        }

        public LocomotionCommand MoveInDirection(Vector3 worldDirection, Gait moveGait, float duration)
        {
            LocomotionCommand command = Begin("MoveInDirection", CommandType.Direction);
            direction = Flat(worldDirection).normalized;
            gait = moveGait;
            timer = duration;
            return direction.sqrMagnitude > 0f ? command : Completed(command);
        }

        public LocomotionCommand FaceTowards(Vector3 worldPoint)
        {
            return FaceDirection(worldPoint - transform.position);
        }

        public LocomotionCommand FaceDirection(Vector3 worldDirection)
        {
            LocomotionCommand command = Begin("Face", CommandType.Face);
            finalFacing = Flat(worldDirection).normalized;
            return finalFacing.sqrMagnitude > 0f ? command : Completed(command);
        }

        public LocomotionCommand Stop()
        {
            return Begin("Stop", CommandType.Stop);
        }

        /// <summary>Dash along <paramref name="worldDirection"/> (zero = current facing).</summary>
        public LocomotionCommand Dash(Vector3 worldDirection)
        {
            LocomotionCommand command = Begin("Dash", CommandType.Dash);
            direction = Flat(worldDirection).normalized;
            dashStarted = false;
            timer = 0f;
            return command;
        }

        /// <summary>Plays any clip in place (inertialized in and out). Completes when the clip ends.</summary>
        public LocomotionCommand PlayAction(AnimationClip clip, float blendHalfLife = -1f)
        {
            LocomotionCommand command = Begin("PlayAction", CommandType.Action);
            if (clip == null) return Completed(command);
            actionClip = clip;
            actionHalfLife = blendHalfLife;
            actionId = 0;
            return command;
        }

        public void Cancel()
        {
            if (current != null) current.Finish(true);
            current = null;
            type = CommandType.None;
        }

        public override bool TryGetIntent(LocomotionMotor motor, float deltaTime, out LocomotionIntent intent)
        {
            intent = LocomotionIntent.Idle;
            intent.Gait = gait;

            if (current == null || current.IsDone)
                return holdControlWhenIdle;

            switch (type)
            {
                case CommandType.Path:
                    UpdatePath(motor, ref intent);
                    break;

                case CommandType.Direction:
                    // Keep moving on the completing frame too: a chained next command starts later this frame,
                    // and a one-frame "no input" gap would read as a stop and trigger a brake.
                    intent.DesiredVelocity = direction * motor.GetGaitSpeed(gait);
                    timer -= deltaTime;
                    if (timer <= 0f) Complete();
                    break;

                case CommandType.Face:
                    intent.HasFacing = true;
                    intent.FacingDirection = finalFacing;
                    if (motor.IsStopped && motor.FacingAngleTo(finalFacing) <= faceTolerance) Complete();
                    break;

                case CommandType.Stop:
                    if (motor.IsStopped) Complete();
                    break;

                case CommandType.Dash:
                    // Keep asking until the motor actually dashes (cooldown / an uninterruptible clip can delay
                    // it); give up after a second and report the command as cancelled instead of "done".
                    if (!dashStarted)
                    {
                        if (motor.CurrentActionKind == LocomotionActionKind.Dash)
                        {
                            dashStarted = true;
                        }
                        else if ((timer += deltaTime) > DashRequestTimeout)
                        {
                            Fail();
                        }
                        else
                        {
                            intent.RequestDash = true;
                            intent.DashDirection = direction;
                        }
                    }
                    else if (motor.CurrentActionKind != LocomotionActionKind.Dash)
                    {
                        Complete();
                    }
                    break;

                case CommandType.Action:
                    if (actionId == 0)
                    {
                        actionId = motor.PlayCustomAction(actionClip, actionHalfLife);
                        if (actionId == 0) Complete();
                    }
                    else if (!motor.IsActionActive(actionId))
                    {
                        Complete();
                    }
                    break;
            }

            return true;
        }
        #endregion

        #region [Private Methods]
        private void UpdatePath(LocomotionMotor motor, ref LocomotionIntent intent)
        {
            Vector3 position = motor.transform.position;
            Vector3 toTarget = Flat(path[pathIndex] - position);
            float distance = toTarget.magnitude;
            bool isLast = pathIndex >= path.Count - 1;
            float gaitSpeed = motor.GetGaitSpeed(gait);

            if (!isLast)
            {
                if (distance <= waypointRadius)
                {
                    pathIndex++;
                    UpdatePath(motor, ref intent);
                    return;
                }

                intent.DesiredVelocity = toTarget / distance * gaitSpeed;
                return;
            }

            if (hasFinalFacing)
            {
                intent.HasFacing = true;
                intent.FacingDirection = finalFacing;
            }

            bool isMoving = motor.CurrentSpeed > 0.3f;
            if (!isStopping && isMoving && distance <= motor.PredictStopDistance() + arriveTolerance)
                isStopping = true;

            if (isStopping || distance <= arriveTolerance)
            {
                intent.HasStopTarget = true;
                intent.StopTarget = path[pathIndex];

                if (!motor.IsStopped) return;

                // Landed too far away (blocked, or no stop clip): approach again.
                if (distance > Mathf.Max(arriveTolerance * 4f, 0.2f))
                {
                    isStopping = false;
                    return;
                }

                if (!hasFinalFacing || motor.FacingAngleTo(finalFacing) <= faceTolerance)
                    Complete();
                return;
            }

            float desiredSpeed = Mathf.Min(gaitSpeed, distance * approachGain);
            intent.DesiredVelocity = toTarget / distance * desiredSpeed;
        }

        private LocomotionCommand Begin(string name, CommandType commandType)
        {
            Cancel();
            current = new LocomotionCommand(name);
            type = commandType;
            return current;
        }

        private void Complete()
        {
            if (current != null) current.Finish(false);
            type = CommandType.None;
        }

        /// <summary>Ends the command reporting it did not happen (WasCancelled = true).</summary>
        private void Fail()
        {
            if (current != null) current.Finish(true);
            type = CommandType.None;
        }

        private LocomotionCommand Completed(LocomotionCommand command)
        {
            command.Finish(false);
            if (command == current) type = CommandType.None;
            return command;
        }

        private LocomotionCommand Completed(string name)
        {
            Cancel();
            LocomotionCommand command = new LocomotionCommand(name);
            command.Finish(false);
            return command;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
        #endregion
    }
}
