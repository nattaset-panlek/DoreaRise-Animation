using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Added at runtime on the Animator's GameObject when LocomotionAnimator lives elsewhere
    /// (OnAnimatorMove is only sent to scripts next to the Animator). Forwards root motion to the motor.
    /// </summary>
    [AddComponentMenu("")]
    public class LocomotionRootMotionRelay : MonoBehaviour
    {
        #region [Variables/Fields]
        private Animator animator;
        private LocomotionMotor motor;
        #endregion

        #region [Unity Methods]
        private void OnAnimatorMove()
        {
            if (motor != null && animator != null)
                motor.ApplyAnimatorRootMotion(animator.deltaPosition, animator.deltaRotation);
        }
        #endregion

        #region [Public Methods]
        public void Bind(Animator targetAnimator, LocomotionMotor targetMotor)
        {
            animator = targetAnimator;
            motor = targetMotor;
        }
        #endregion
    }
}
