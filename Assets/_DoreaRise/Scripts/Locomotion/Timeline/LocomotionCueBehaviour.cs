using UnityEngine;
using UnityEngine.Playables;
using DoreaRise.System.Controller;

namespace DoreaRise.Locomotion
{
    public class LocomotionCueBehaviour : PlayableBehaviour
    {
        #region [Variables/Fields]
        public LocomotionCueType cue;
        public Transform target;
        public Gait gait;
        public bool matchTargetRotation;
        public float moveDuration;
        public AnimationClip actionClip;
        public float blendHalfLife;

        private bool fired;
        #endregion

        #region [Unity Methods]
        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            fired = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // Locomotion is simulated: commands only make sense while the game runs.
            if (fired || !Application.isPlaying) return;

            ScriptedLocomotionDriver driver = playerData as ScriptedLocomotionDriver;
            if (driver == null) return;

            fired = true;
            Fire(driver, (float)playable.GetDuration());
        }
        #endregion

        #region [Private Methods]
        private void Fire(ScriptedLocomotionDriver driver, float clipLength)
        {
            switch (cue)
            {
                case LocomotionCueType.MoveTo:
                    if (target != null) driver.MoveTo(target, gait, matchTargetRotation);
                    break;

                case LocomotionCueType.FollowPath:
                    if (target != null) driver.FollowPath(target, gait, matchTargetRotation);
                    break;

                case LocomotionCueType.MoveInDirection:
                    Vector3 direction = target != null ? target.forward : driver.transform.forward;
                    driver.MoveInDirection(direction, gait, moveDuration > 0f ? moveDuration : clipLength);
                    break;

                case LocomotionCueType.Stop:
                    driver.Stop();
                    break;

                case LocomotionCueType.FaceTowards:
                    if (target != null) driver.FaceTowards(target.position);
                    break;

                case LocomotionCueType.Dash:
                    driver.Dash(target != null ? target.position - driver.transform.position : Vector3.zero);
                    break;

                case LocomotionCueType.PlayAction:
                    driver.PlayAction(actionClip, blendHalfLife);
                    break;

                case LocomotionCueType.LookAt:
                case LocomotionCueType.ClearLookAt:
                    SpineToHeadLookAt lookAt = driver.GetComponentInChildren<SpineToHeadLookAt>();
                    if (lookAt != null) lookAt.LookTarget = cue == LocomotionCueType.LookAt ? target : null;
                    break;

                case LocomotionCueType.Teleport:
                    LocomotionMotor motor = driver.GetComponent<LocomotionMotor>();
                    if (motor != null && target != null)
                    {
                        driver.Cancel();
                        motor.Teleport(target.position, target.rotation);
                    }
                    break;
            }
        }
        #endregion
    }
}
