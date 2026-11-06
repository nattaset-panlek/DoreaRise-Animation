using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Turns locomotion events into presentation: camera punches, shakes, hit-stop and a speed dolly.
    /// This is where the "impact" of a dash or a skid comes from — the animation alone does not sell it.
    /// </summary>
    public class LocomotionFeedback : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("References")]
        [SerializeField] private LocomotionMotor motor;
        [Tooltip("Auto-found (or added) on Camera.main when empty")]
        [SerializeField] private CameraImpulse cameraImpulse;

        [Header("Dash")]
        [SerializeField] private float dashFovKick = 9f;
        [SerializeField] private float dashPushBack = 0.35f;
        [SerializeField] private Vector3 dashShake = new Vector3(0.015f, 0.6f, 0.18f);
        [Tooltip("Real-time seconds. 0 = off")]
        [SerializeField] private float dashHitStop = 0.04f;

        [Header("Brake / Stop")]
        [Tooltip("Camera shake on stops and pivots. Push-back and FOV kick still play when off")]
        [SerializeField] private bool shakeOnBrake;
        [Tooltip("Below this entry speed a stop gets no impact")]
        [SerializeField] private float brakeMinSpeed = 2.5f;
        [Tooltip("Negative = the camera keeps travelling forward for a beat (momentum)")]
        [SerializeField] private float brakePushBack = -0.25f;
        [SerializeField] private float brakeFovKick = -2.5f;
        [Tooltip("Shake per m/s of entry speed: x = position (m), y = rotation (deg), z = duration (s)")]
        [SerializeField] private Vector3 brakeShakePerSpeed = new Vector3(0.003f, 0.12f, 0.06f);
        [SerializeField] private float brakeHitStop;

        [Header("Pivot")]
        [SerializeField] private Vector3 pivotShake = new Vector3(0.01f, 0.4f, 0.15f);

        [Header("Drift (sharp high-speed turn)")]
        [SerializeField] private Vector3 driftShake = new Vector3(0.008f, 0.35f, 0.2f);
        [SerializeField] private float driftFovKick = -1.5f;

        [Header("Landing")]
        [Tooltip("Fall speed (m/s) below which a landing gets no impact")]
        [SerializeField] private float landingMinSpeed = 3f;
        [Tooltip("Shake per m/s of fall speed: x = position (m), y = rotation (deg), z = duration (s)")]
        [SerializeField] private Vector3 landingShakePerSpeed = new Vector3(0.004f, 0.15f, 0.035f);
        [SerializeField] private float landingPushPerSpeed = -0.03f;
        [Tooltip("Shake when a jump lands. Off: only falls (off ledges) and hard landings shake")]
        [SerializeField] private bool shakeOnJumpLanding;

        [Header("Speed Dolly")]
        [Tooltip("Camera pull-back (m) at sprint speed — sells speed without changing the shot")]
        [SerializeField] private float dollyAtSprint = 0.3f;
        [Tooltip("Extra pull-back per m/s² of forward acceleration (negative while braking)")]
        [SerializeField] private float dollyPerAcceleration = 0.015f;
        [SerializeField] private Vector2 dollyRange = new Vector2(-0.3f, 0.6f);
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<LocomotionMotor>();
        }

        private void OnEnable()
        {
            if (motor == null) return;
            motor.ActionStarted += OnActionStarted;
            motor.DriftStarted += OnDriftStarted;
            motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.ActionStarted -= OnActionStarted;
                motor.DriftStarted -= OnDriftStarted;
                motor.Landed -= OnLanded;
            }
            if (cameraImpulse != null) cameraImpulse.SetDollyTarget(0f);
        }

        private void LateUpdate()
        {
            CameraImpulse impulse = ResolveCamera();
            if (impulse == null || motor == null) return;

            float sprintSpeed = Mathf.Max(0.01f, motor.GetGaitSpeed(Gait.Sprint));
            float speedPull = Mathf.Clamp01(motor.CurrentSpeed / sprintSpeed) * dollyAtSprint;
            float accelerationPull = motor.LocalAcceleration.z * dollyPerAcceleration;
            impulse.SetDollyTarget(Mathf.Clamp(speedPull + accelerationPull, dollyRange.x, dollyRange.y));
        }
        #endregion

        #region [Private Methods]
        private void OnActionStarted(LocomotionActionKind kind)
        {
            CameraImpulse impulse = ResolveCamera();

            switch (kind)
            {
                case LocomotionActionKind.Dash:
                    if (impulse != null)
                    {
                        impulse.KickFov(dashFovKick);
                        impulse.PushBack(dashPushBack);
                        impulse.Shake(dashShake.x, dashShake.y, dashShake.z);
                    }
                    HitStop.Trigger(dashHitStop);
                    break;

                case LocomotionActionKind.Stop:
                case LocomotionActionKind.Pivot:
                    if (kind == LocomotionActionKind.Pivot && shakeOnBrake && impulse != null)
                        impulse.Shake(pivotShake.x, pivotShake.y, pivotShake.z);
                    float speed = motor.ActionEntrySpeed;
                    if (speed < brakeMinSpeed) break;
                    if (impulse != null)
                    {
                        impulse.PushBack(brakePushBack);
                        impulse.KickFov(brakeFovKick);
                        if (shakeOnBrake)
                            impulse.Shake(brakeShakePerSpeed.x * speed, brakeShakePerSpeed.y * speed, brakeShakePerSpeed.z * speed);
                    }
                    HitStop.Trigger(brakeHitStop);
                    break;
            }
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed < landingMinSpeed) return;
            if (!shakeOnJumpLanding && motor.CurrentActionKind == LocomotionActionKind.Jump)
            {
                LandingAction hard = motor.ClipSet != null ? motor.ClipSet.hardLanding : null;
                if (hard == null || !hard.IsValid || impactSpeed < hard.minImpactSpeed) return;
            }
            CameraImpulse impulse = ResolveCamera();
            if (impulse == null) return;
            impulse.Shake(landingShakePerSpeed.x * impactSpeed, landingShakePerSpeed.y * impactSpeed,
                landingShakePerSpeed.z * impactSpeed);
            impulse.PushBack(landingPushPerSpeed * impactSpeed);
        }

        private void OnDriftStarted()
        {
            CameraImpulse impulse = ResolveCamera();
            if (impulse == null) return;
            impulse.Shake(driftShake.x, driftShake.y, driftShake.z);
            impulse.KickFov(driftFovKick);
        }

        private CameraImpulse ResolveCamera()
        {
            if (cameraImpulse != null) return cameraImpulse;

            Camera main = Camera.main;
            if (main == null) return null;

            cameraImpulse = main.GetComponent<CameraImpulse>();
            if (cameraImpulse == null) cameraImpulse = main.gameObject.AddComponent<CameraImpulse>();
            return cameraImpulse;
        }
        #endregion
    }
}
