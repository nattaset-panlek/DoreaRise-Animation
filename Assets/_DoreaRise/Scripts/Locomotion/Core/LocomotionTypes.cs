using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>Run is appended (not inserted) so values already serialized in scenes / Timeline keep their meaning.</summary>
    public enum Gait
    {
        Walk,
        Sprint,
        SprintFast,
        Run
    }

    public enum LocomotionActionKind
    {
        None,
        Stop,
        Dash,
        Start,
        Pivot,
        TurnInPlace,
        IdleBreak,
        Custom,
        Jump,
        Land,
        Fall,
        Slide
    }

    /// <summary>
    /// What a character wants to do this frame. Produced by a LocomotionDriver, consumed by LocomotionMotor.
    /// </summary>
    public struct LocomotionIntent
    {
        /// <summary>Planar world velocity the character should reach (m/s).</summary>
        public Vector3 DesiredVelocity;
        public Gait Gait;

        /// <summary>When set, the character turns to face this planar direction while standing.</summary>
        public bool HasFacing;
        public Vector3 FacingDirection;

        /// <summary>When set, a stop should land exactly on this point (distance matching).</summary>
        public bool HasStopTarget;
        public Vector3 StopTarget;

        public bool RequestDash;
        public bool RequestJump;
        public bool RequestSlide;
        /// <summary>Planar dash direction. Zero = desired move direction, or facing when standing.</summary>
        public Vector3 DashDirection;

        public bool WantsToMove => DesiredVelocity.sqrMagnitude > 0.0001f;

        public static LocomotionIntent Idle => new LocomotionIntent { Gait = Gait.Walk };
    }
}
