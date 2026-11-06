using UnityEngine;

namespace DoreaRise.System.Controller
{
    public class IdleCycleBehaviour : StateMachineBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private int minLoops = 2;
        [SerializeField] private int maxLoops = 4;

        private int targetLoops;
        private int currentLoop;
        private float lastNormalizedTime;
        #endregion

        #region [Unity Methods]
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            currentLoop = 0;
            lastNormalizedTime = 0f;
            targetLoops = Random.Range(minLoops, maxLoops + 1);
            animator.SetBool("IdleCycleExit", false);
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            float current = stateInfo.normalizedTime % 1f;

            if (current < lastNormalizedTime)
            {
                currentLoop++;

                if (currentLoop >= targetLoops)
                    animator.SetBool("IdleCycleExit", true);
            }

            lastNormalizedTime = current;
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IdleCycleExit", false);
        }
        #endregion
    }
}
