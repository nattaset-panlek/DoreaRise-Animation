using UnityEngine;

namespace DoreaRise.System.Controller
{
    [DefaultExecutionOrder(1000)]
    public class HandIKSolver : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Left Arm Connection")]
        [SerializeField] private Transform leftForearmEnd;
        [SerializeField] private Transform leftHand;

        [Header("Right Arm Connection")]
        [SerializeField] private Transform rightForearmEnd;
        [SerializeField] private Transform rightHand;

        [Header("Lock Settings")]
        [SerializeField] private bool lockHandToForearm = true;
        [SerializeField] private bool matchRotation = false;
        #endregion

        #region [Properties]
        public bool LockHandToForearm
        {
            get => lockHandToForearm;
            set => lockHandToForearm = value;
        }
        #endregion

        private void Start()
        {
            SnapHand();
        }

        #region [Unity Methods]
        private void LateUpdate()
        {
            SnapHand();
        }

        private void SnapHand()
        {
            if (!lockHandToForearm) return;

            SnapHandToForearm(leftForearmEnd, leftHand);
            SnapHandToForearm(rightForearmEnd, rightHand);
        }

        #endregion

        #region [Private Methods]
        private void SnapHandToForearm(Transform forearmEnd, Transform hand)
        {
            if (forearmEnd == null || hand == null) return;

            hand.position = forearmEnd.position;
            if (matchRotation)
            {
                hand.rotation = forearmEnd.rotation;
            }
        }
        #endregion
    }
}
