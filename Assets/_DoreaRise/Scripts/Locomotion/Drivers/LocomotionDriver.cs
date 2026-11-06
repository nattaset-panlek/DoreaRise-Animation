using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Source of locomotion intent (player input, script, Timeline...). The motor asks enabled drivers
    /// from highest to lowest priority and uses the first one that answers.
    /// </summary>
    public abstract class LocomotionDriver : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Driver")]
        [Tooltip("Higher priority drivers override lower ones while they are active. -1 = driver default")]
        [SerializeField] private int priority = -1;
        #endregion

        #region [Properties]
        public int Priority => priority >= 0 ? priority : DefaultPriority;
        protected virtual int DefaultPriority => 0;
        #endregion

        #region [Public Methods]
        /// <summary>Return false when this driver has nothing to say this frame.</summary>
        public abstract bool TryGetIntent(LocomotionMotor motor, float deltaTime, out LocomotionIntent intent);
        #endregion
    }
}
