using UnityEngine;
using DoreaRise.System.Controller;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Keyboard/gamepad control (legacy Input Manager), camera relative. Useful to rehearse a shot
    /// before scripting it. Scripted/Timeline drivers should use a higher priority.
    /// </summary>
    public class PlayerInputDriver : LocomotionDriver
    {
        #region [Variables/Fields]
        [Header("Input Keys")]
        [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode sprintFastKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode dashKey = KeyCode.E;
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode slideKey = KeyCode.C;
        [Tooltip("Hold to walk when the default gait is faster than walking (or to run when the default is Walk)")]
        [SerializeField] private KeyCode alternateGaitKey = KeyCode.LeftAlt;

        [Header("Gait")]
        [Tooltip("Gait with no modifier key held. Walk = Dorea's walk cycle (the user's choice); Run = action-game style")]
        [SerializeField] private Gait defaultGait = Gait.Walk;
        [SerializeField] private bool sprintOnly;
        [SerializeField] private bool maintainSprintOnReleaseShift = true;

        [Header("Camera")]
        [Tooltip("Movement is relative to this camera's yaw. Auto-found on Camera.main when empty")]
        [SerializeField] private CameraControl cameraControl;

        private bool isSprintLocked;
        #endregion

        #region [Properties]
        public bool SprintOnly
        {
            get => sprintOnly;
            set => sprintOnly = value;
        }
        #endregion

        #region [Public Methods]
        public override bool TryGetIntent(LocomotionMotor motor, float deltaTime, out LocomotionIntent intent)
        {
            intent = LocomotionIntent.Idle;

            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            bool hasInput = input.sqrMagnitude > 0.01f;

            bool isShiftPressed = Input.GetKey(sprintKey);
            bool isCtrlPressed = Input.GetKey(sprintFastKey);

            if (!hasInput)
                isSprintLocked = false;
            else if ((isShiftPressed || sprintOnly) && motor.CurrentSpeed >= motor.GetGaitSpeed(Gait.Sprint) * 0.85f)
                isSprintLocked = true;

            Gait gait = defaultGait;
            if (Input.GetKey(alternateGaitKey))
                gait = defaultGait == Gait.Walk ? Gait.Run : Gait.Walk;
            if (isCtrlPressed)
                gait = Gait.SprintFast;
            else if (isShiftPressed || sprintOnly || (maintainSprintOnReleaseShift && isSprintLocked))
                gait = Gait.Sprint;

            intent.Gait = gait;
            intent.RequestDash = Input.GetKeyDown(dashKey);
            intent.RequestJump = Input.GetKeyDown(jumpKey);
            intent.RequestSlide = Input.GetKeyDown(slideKey);

            if (hasInput)
            {
                Vector3 worldDir = Quaternion.Euler(0f, GetCameraYaw(motor), 0f) * input;
                intent.DesiredVelocity = worldDir * motor.GetGaitSpeed(gait);
            }

            return true;
        }
        #endregion

        #region [Private Methods]
        private float GetCameraYaw(LocomotionMotor motor)
        {
            if (cameraControl == null && Camera.main != null)
                cameraControl = Camera.main.GetComponent<CameraControl>();

            if (cameraControl != null)
                return cameraControl.Yaw;

            if (Camera.main != null)
                return Camera.main.transform.eulerAngles.y;

            return motor.transform.eulerAngles.y;
        }
        #endregion
    }
}
