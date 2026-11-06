using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Puts a hand on the obstacle during the vault's hand plant (LocomotionMotor.TryGetHandPlant): two-bone IK
    /// on the upper arm and forearm, the wrist (Hand.IK) pinned to the planted point while the body goes over.
    /// Lives on the root "Player Character", next to FootPlanting.
    /// </summary>
    public class HandPlanting : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private LocomotionMotor motor;
        [Range(0f, 1f)] [SerializeField] private float weight = 1f;

        private class Arm
        {
            public Transform upper, lower, wrist;
            public bool Valid => upper != null && lower != null && wrist != null;
        }

        private Arm left, right;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<LocomotionMotor>();
            Animator animator = GetComponentInChildren<Animator>(true);
            Transform root = animator != null ? animator.transform : transform;
            left = FindArm(root, "L");
            right = FindArm(root, "R");
        }

        private void LateUpdate()
        {
            if (motor == null || weight <= 0f) return;
            if (!motor.TryGetHandPlant(out Vector3 target, out bool isLeft, out float plant)) return;
            Arm arm = isLeft ? left : right;
            if (arm == null || !arm.Valid) return;

            Vector3 animated = arm.wrist.position;
            Solve(arm, Vector3.Lerp(animated, target, plant * weight));
        }
        #endregion

        #region [Private Methods]
        private static Arm FindArm(Transform root, string side)
        {
            Arm arm = new Arm();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Upper_arm." + side) arm.upper = t;
                else if (t.name == "Forearm." + side) arm.lower = t;
                else if (t.name == "Hand.IK." + side) arm.wrist = t;
            }
            return arm;
        }

        /// <summary>Two-bone IK keeping the animated elbow's bend side; the hand (under Hand.IK) follows the wrist.</summary>
        private static void Solve(Arm arm, Vector3 target)
        {
            Vector3 shoulder = arm.upper.position;
            Vector3 elbow = arm.lower.position;
            Vector3 wrist = arm.wrist.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float lowerLength = Vector3.Distance(elbow, wrist);
            if (upperLength < 1e-5f || lowerLength < 1e-5f) return;

            Vector3 toTarget = target - shoulder;
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 1e-4f,
                upperLength + lowerLength - 1e-4f);
            if (toTarget.sqrMagnitude < 1e-10f) return;
            Vector3 direction = toTarget.normalized;

            Vector3 bend = Vector3.ProjectOnPlane(elbow - shoulder, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, direction);
            if (bend.sqrMagnitude < 1e-8f) return;
            bend.Normalize();

            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            float across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 newElbow = shoulder + direction * along + bend * across;
            Vector3 newWrist = shoulder + direction * distance;

            // Hand.IK is not a child of the forearm: carry the forearm's direction through the upper-arm turn by hand
            Quaternion upperTurn = Quaternion.FromToRotation(elbow - shoulder, newElbow - shoulder);
            arm.upper.rotation = upperTurn * arm.upper.rotation;
            Vector3 lowerNow = upperTurn * (wrist - elbow);
            arm.lower.rotation = Quaternion.FromToRotation(lowerNow, newWrist - arm.lower.position) * arm.lower.rotation;
            arm.wrist.position = newWrist;
        }
        #endregion
    }
}
