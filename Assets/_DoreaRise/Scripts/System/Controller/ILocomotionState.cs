using UnityEngine;

namespace DoreaRise.System.Controller
{
    /// <summary>
    /// Read-only locomotion state shared by CharacterControl (legacy) and LocomotionMotor,
    /// so rig/camera/tail scripts work with either.
    /// </summary>
    public interface ILocomotionState
    {
        float CurrentSpeed { get; }
        MovementState CurrentState { get; }
        bool IsSprinting { get; }
        bool IsBraking { get; }
        Transform transform { get; }
    }

    public static class LocomotionStateResolver
    {
        /// <summary>
        /// Returns the assigned source when it is an enabled ILocomotionState, otherwise the first
        /// enabled ILocomotionState found on the parents of <paramref name="from"/>.
        /// </summary>
        public static ILocomotionState Resolve(MonoBehaviour assigned, Component from)
        {
            if (assigned is ILocomotionState state && assigned.isActiveAndEnabled)
                return state;

            if (from != null)
            {
                foreach (MonoBehaviour behaviour in from.GetComponentsInParent<MonoBehaviour>())
                {
                    if (behaviour is ILocomotionState found && behaviour.isActiveAndEnabled)
                        return found;
                }
            }

            return assigned as ILocomotionState;
        }

        /// <summary>Keeps <paramref name="cached"/> while it is still enabled, otherwise resolves again.</summary>
        public static ILocomotionState ResolveCached(ref ILocomotionState cached, MonoBehaviour assigned, Component from)
        {
            if (cached is Behaviour behaviour && behaviour != null && behaviour.isActiveAndEnabled)
                return cached;

            cached = Resolve(assigned, from);
            return cached;
        }
    }
}
