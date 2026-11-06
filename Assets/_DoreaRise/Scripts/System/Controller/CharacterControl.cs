using UnityEngine;

namespace DoreaRise.System.Controller
{
    public enum AnimatorSpeedType
    {
        Normalized_0_1, // 0 = Idle, 0.33 = Walk, 0.66 = Sprint, 1.0 = SprintFast+
        Normalized_0_3, // 0 = Idle, 1.0 = Walk, 2.0 = Sprint, 3.0 = SprintFast
        RawSpeed        // Actual speed in m/s
    }

    public enum MovementState
    {
        Idle,
        Walk,
        Sprint,
        SprintFast
    }

    [RequireComponent(typeof(CharacterController))]
    public class CharacterControl : MonoBehaviour, ILocomotionState
    {
        #region [Variables/Fields]
        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelTransform;

        [Header("Movement Speeds")]
        [SerializeField] private bool sprintOnly = false;
        [SerializeField] private bool maintainSprintOnReleaseShift = true;
        [SerializeField] private float walkSpeed = 3.5f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float sprintFastSpeed = 10.5f;
        [SerializeField] private float acceleration = 10f;
        [SerializeField] private float deceleration = 8f;
        [SerializeField] private float idleTurnSmoothTime = 0.35f;
        [SerializeField] private float moveTurnSmoothTime = 0.25f;
        [SerializeField] private float moveDirSmoothSpeed = 4f;
        [SerializeField] private float gravity = -20f;

        [Header("Input Keys")]
        [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode sprintFastKey = KeyCode.LeftControl;

        [Header("Brake Mechanics & Lock")]
        [SerializeField] private float brakeMinSpeed = 5.5f;
        [SerializeField] private float brakeDeceleration = 16f;
        [SerializeField] private float brakeThresholdAngle = 135f;
        [SerializeField] private float brakeLockDuration = 0.6f;
        [SerializeField] private string brakeStateTag = "Brake";

        [Header("Animator Settings")]
        [SerializeField] private string speedParamName = "Speed";
        [SerializeField] private string isBrakingParamName = "IsBraking";
        [SerializeField] private string isDodgingParamName = "IsDodging";
        [SerializeField] private string brakeTriggerName = "Brake";
        [SerializeField] private string dodgeTriggerName = "Dodge";
        [SerializeField] private float animDampTime = 0.1f;
        [SerializeField] private AnimatorSpeedType speedMode = AnimatorSpeedType.Normalized_0_1;

        [Header("Footstep Stride Speed Sync")]
        [SerializeField] private bool syncAnimatorSpeedToStride = true;
        [SerializeField] private float idealWalkSpeed = 3.5f;
        [SerializeField] private float idealSprintSpeed = 7f;
        [SerializeField] private float idealSprintFastSpeed = 10.5f;
        [SerializeField] private float minAnimPlaybackSpeed = 0.6f;
        [SerializeField] private float maxAnimPlaybackSpeed = 1.4f;

        [Header("Procedural Turn Leaning")]
        [SerializeField] private bool enableProceduralLean = false;
        [SerializeField] private float maxLeanAngle = 15f;
        [SerializeField] private float leanSmoothSpeed = 8f;


        [Header("Dodge")]
        [SerializeField] private bool canDash = true;
        [SerializeField] private float dodgeDistance = 5f;
        [SerializeField] private float dodgeDuration = 0.3f;
        [SerializeField] private float dodgeCooldown = 0.6f;
        [SerializeField] private bool brakeAfterDodge = true;

        [Header("State Effects")]
        [SerializeField] private GameObject[] idleEffects;
        [SerializeField] private GameObject[] walkEffects;
        [SerializeField] private GameObject[] sprintEffects;

        private CharacterController characterController;
        private CameraControl cameraControl;
        private float verticalVelocity;

        private MovementState currentState = MovementState.Idle;
        private bool isBraking;
        private bool isDodging;
        private bool isSprintLocked;
        private float brakeTimer;
        private float dodgeTimer;
        private float dodgeCooldownTimer;
        private Vector3 dodgeDirection;
        private Vector3 lastMoveDirection;

        private float currentSpeed = 0f;
        private Vector3 currentMoveDir = Vector3.forward;
        private float currentLeanZ = 0f;
        private float previousYaw = 0f;
        private float turnSmoothVelocity = 0f;
        #endregion

        #region [Properties]
        public float CurrentSpeed => currentSpeed;
        public bool IsGrounded { get; private set; }
        public bool IsSprinting => currentState == MovementState.Sprint || currentState == MovementState.SprintFast;
        public bool IsSprintFast => currentState == MovementState.SprintFast;
        public bool IsBraking => isBraking;
        public bool IsDodging => isDodging;
        public bool IsSprintLocked => isSprintLocked;
        public bool SprintOnly
        {
            get => sprintOnly;
            set => sprintOnly = value;
        }
        public MovementState CurrentState => currentState;
        public Animator Animator => animator;
        public bool CanDash
        {
            get => canDash;
            set => canDash = value;
        }
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null)
                animator = GetComponentInParent<Animator>();
            if (modelTransform == null && transform.childCount > 0)
                modelTransform = transform.GetChild(0);
        }

        private void Start()
        {
            lastMoveDirection = transform.forward;
            currentMoveDir = transform.forward;
            previousYaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (characterController == null) return;

            IsGrounded = characterController.isGrounded;
            if (IsGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            HandleDodge();

            if (!isDodging)
                HandleMovement();

            HandleProceduralLean();
            UpdateStateEffects();
            UpdateAnimator();

            verticalVelocity += gravity * Time.deltaTime;
        }
        #endregion

        #region [Public Methods]
        public void SetCameraControl(CameraControl cam)
        {
            cameraControl = cam;
        }
        #endregion

        #region [Private Methods]
        private void UpdateStateEffects()
        {
            // Use actual speed to determine effects, so they don't pop up instantly before the character accelerates
            bool isIdle = (currentSpeed <= 0.1f) && !isDodging;
            bool isWalk = (currentSpeed > 0.1f && currentSpeed <= walkSpeed + 0.5f) && !isDodging;
            bool isSprint = (currentSpeed > walkSpeed + 0.5f) && !isDodging;

            ToggleEffects(idleEffects, isIdle);
            ToggleEffects(walkEffects, isWalk);
            ToggleEffects(sprintEffects, isSprint);
        }

        private void ToggleEffects(GameObject[] effects, bool isActive)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                if (effect != null && effect.activeSelf != isActive)
                {
                    effect.SetActive(isActive);
                }
            }
        }

        private void HandleMovement()
        {
            float cameraYaw = GetCameraYaw();

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 input = new Vector3(h, 0f, v).normalized;
            bool hasInput = input.sqrMagnitude > 0.01f;

            if (isBraking)
            {
                isSprintLocked = false;
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, brakeDeceleration * Time.deltaTime);
                brakeTimer -= Time.deltaTime;

                bool isAnimStillBraking = false;
                if (animator != null && !string.IsNullOrEmpty(brakeStateTag))
                {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                    if (stateInfo.IsTag(brakeStateTag) && stateInfo.normalizedTime < 0.95f)
                    {
                        isAnimStillBraking = true;
                    }
                }

                if (currentSpeed <= 0.01f && brakeTimer <= 0f && !isAnimStillBraking)
                {
                    currentSpeed = 0f;
                    isBraking = false;
                    if (animator != null && !string.IsNullOrEmpty(isBrakingParamName))
                        animator.SetBool(isBrakingParamName, false);
                }

                Vector3 brakeMove = currentMoveDir * currentSpeed + Vector3.up * verticalVelocity;
                characterController.Move(brakeMove * Time.deltaTime);
                return;
            }

            if (hasInput)
            {
                bool isShiftPressed = Input.GetKey(sprintKey);
                bool isCtrlPressed = Input.GetKey(sprintFastKey);

                if (isShiftPressed || sprintOnly)
                {
                    if (currentSpeed >= sprintSpeed * 0.85f)
                    {
                        isSprintLocked = true;
                    }
                }

                if (isCtrlPressed || (isShiftPressed && isCtrlPressed))
                {
                    currentState = MovementState.SprintFast;
                }
                else if (isShiftPressed || (maintainSprintOnReleaseShift && isSprintLocked) || sprintOnly)
                {
                    currentState = MovementState.Sprint;
                }
                else
                {
                    currentState = MovementState.Walk;
                }
            }
            else
            {
                currentState = MovementState.Idle;
                isSprintLocked = false;
            }

            float targetSpeed = currentState switch
            {
                MovementState.SprintFast => sprintFastSpeed,
                MovementState.Sprint => sprintSpeed,
                MovementState.Walk => walkSpeed,
                _ => 0f
            };

            float targetAngle = hasInput ? Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg + cameraYaw : transform.eulerAngles.y;
            Vector3 targetMoveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            if (currentSpeed >= brakeMinSpeed)
            {
                bool suddenlyStopped = !hasInput;
                bool sharpDirectionChange = hasInput && Vector3.Angle(currentMoveDir, targetMoveDir) >= brakeThresholdAngle;

                if (suddenlyStopped || sharpDirectionChange)
                {
                    isBraking = true;
                    isSprintLocked = false;
                    brakeTimer = brakeLockDuration;

                    if (animator != null)
                    {
                        if (!string.IsNullOrEmpty(isBrakingParamName))
                            animator.SetBool(isBrakingParamName, true);
                        if (!string.IsNullOrEmpty(brakeTriggerName))
                            animator.SetTrigger(brakeTriggerName);
                    }
                    return;
                }
            }

            float accelRate = hasInput ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelRate * Time.deltaTime);

            if (hasInput)
            {
                float speedRatio = Mathf.Clamp01(currentSpeed / walkSpeed);
                float dynamicSmoothTime = Mathf.Lerp(idleTurnSmoothTime, moveTurnSmoothTime, speedRatio);

                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, dynamicSmoothTime);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);

                float turnCurveFactor = Mathf.Lerp(1.5f, moveDirSmoothSpeed, speedRatio);
                currentMoveDir = Vector3.Slerp(currentMoveDir, targetMoveDir, Time.deltaTime * turnCurveFactor).normalized;
                lastMoveDirection = currentMoveDir;
            }

            if (currentSpeed > 0.001f)
            {
                Vector3 finalMove = currentMoveDir * currentSpeed + Vector3.up * verticalVelocity;
                characterController.Move(finalMove * Time.deltaTime);
            }
            else
            {
                currentSpeed = 0f;
                characterController.Move(Vector3.up * verticalVelocity * Time.deltaTime);
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;

            float targetAnimSpeed = CalculateAnimatorSpeed();
            if (!string.IsNullOrEmpty(speedParamName))
            {
                if (animDampTime > 0f)
                    animator.SetFloat(speedParamName, targetAnimSpeed, animDampTime, Time.deltaTime);
                else
                    animator.SetFloat(speedParamName, targetAnimSpeed);
            }

            if (syncAnimatorSpeedToStride && currentSpeed > 0.1f && !isBraking)
            {
                float currentIdealSpeed = GetCurrentIdealStrideSpeed();
                if (currentIdealSpeed > 0.01f)
                {
                    float speedRatio = currentSpeed / currentIdealSpeed;
                    animator.speed = Mathf.Clamp(speedRatio, minAnimPlaybackSpeed, maxAnimPlaybackSpeed);
                }
            }
            else
            {
                animator.speed = 1f;
            }
        }

        private float GetCurrentIdealStrideSpeed()
        {
            if (currentSpeed <= walkSpeed)
            {
                return idealWalkSpeed;
            }
            else if (currentSpeed <= sprintSpeed)
            {
                float t = (sprintSpeed > walkSpeed) ? (currentSpeed - walkSpeed) / (sprintSpeed - walkSpeed) : 0f;
                return Mathf.Lerp(idealWalkSpeed, idealSprintSpeed, t);
            }
            else
            {
                float t = (sprintFastSpeed > sprintSpeed) ? (currentSpeed - sprintSpeed) / (sprintFastSpeed - sprintSpeed) : 0f;
                return Mathf.Lerp(idealSprintSpeed, idealSprintFastSpeed, t);
            }
        }

        private float CalculateAnimatorSpeed()
        {
            if (currentSpeed <= 0.01f) return 0f;

            return speedMode switch
            {
                AnimatorSpeedType.Normalized_0_1 => currentSpeed <= walkSpeed
                    ? Mathf.Lerp(0f, 0.333f, currentSpeed / walkSpeed)
                    : currentSpeed <= sprintSpeed
                        ? Mathf.Lerp(0.333f, 0.666f, (currentSpeed - walkSpeed) / (sprintSpeed - walkSpeed))
                        : Mathf.Lerp(0.666f, 1.0f, (currentSpeed - sprintSpeed) / (sprintFastSpeed - sprintSpeed)),

                AnimatorSpeedType.Normalized_0_3 => currentSpeed <= walkSpeed
                    ? Mathf.Lerp(0f, 1f, currentSpeed / walkSpeed)
                    : currentSpeed <= sprintSpeed
                        ? Mathf.Lerp(1f, 2f, (currentSpeed - walkSpeed) / (sprintSpeed - walkSpeed))
                        : Mathf.Lerp(2f, 3f, (currentSpeed - sprintSpeed) / (sprintFastSpeed - sprintSpeed)),

                AnimatorSpeedType.RawSpeed => currentSpeed,
                _ => currentSpeed
            };
        }

        private void HandleDodge()
        {
            dodgeCooldownTimer -= Time.deltaTime;

            if (isDodging)
            {
                isSprintLocked = false;
                dodgeTimer -= Time.deltaTime;
                
                // Add currentSpeed to base dodge speed to preserve momentum and scale distance
                float baseDodgeSpeed = dodgeDistance / dodgeDuration;
                float dodgeSpeed = baseDodgeSpeed + (currentSpeed * 0.8f); 
                
                Vector3 move = dodgeDirection * dodgeSpeed + Vector3.up * verticalVelocity;
                characterController.Move(move * Time.deltaTime);

                if (dodgeTimer <= 0f)
                {
                    isDodging = false;
                    if (animator != null && !string.IsNullOrEmpty(isDodgingParamName))
                        animator.SetBool(isDodgingParamName, false);
                    bool wasRunning = currentState == MovementState.Sprint || currentState == MovementState.SprintFast;
                    
                    if (brakeAfterDodge && !wasRunning)
                    {
                        // The dash itself is high momentum, so ensure we have enough speed to skid properly
                        currentSpeed = Mathf.Max(currentSpeed, brakeMinSpeed);
                        currentMoveDir = dodgeDirection;

                        isBraking = true;
                        isSprintLocked = false;
                        brakeTimer = brakeLockDuration;
                        
                        if (animator != null)
                        {
                            if (!string.IsNullOrEmpty(isBrakingParamName))
                                animator.SetBool(isBrakingParamName, true);
                            if (!string.IsNullOrEmpty(brakeTriggerName))
                                animator.SetTrigger(brakeTriggerName);
                        }
                    }
                }
                return;
            }

            // Prevent dash if cooldown is active OR if currently braking OR if canDash is false
            if (canDash && Input.GetKeyDown(KeyCode.Space) && dodgeCooldownTimer <= 0f && !isBraking)
            {
                isDodging = true;
                isSprintLocked = false;
                dodgeTimer = dodgeDuration;
                dodgeCooldownTimer = dodgeCooldown;

                float cameraYaw = GetCameraYaw();
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                Vector3 input = new Vector3(h, 0f, v).normalized;

                if (input.sqrMagnitude > 0.01f)
                {
                    float angle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg + cameraYaw;
                    dodgeDirection = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                }
                else
                {
                    dodgeDirection = lastMoveDirection.sqrMagnitude > 0.01f
                        ? lastMoveDirection.normalized
                        : transform.forward;
                }

                transform.rotation = Quaternion.LookRotation(dodgeDirection);

                if (animator != null)
                {
                    if (!string.IsNullOrEmpty(dodgeTriggerName))
                        animator.SetTrigger(dodgeTriggerName);
                    if (!string.IsNullOrEmpty(isDodgingParamName))
                        animator.SetBool(isDodgingParamName, true);
                }
            }
        }

        private float GetCameraYaw()
        {
            if (cameraControl != null)
                return cameraControl.Yaw;

            if (Camera.main != null)
            {
                cameraControl = Camera.main.GetComponent<CameraControl>();
                if (cameraControl != null)
                    return cameraControl.Yaw;
            }

            return transform.eulerAngles.y;
        }

        private void HandleProceduralLean()
        {
            if (!enableProceduralLean || modelTransform == null) return;

            float currentYaw = transform.eulerAngles.y;
            float turnDelta = Mathf.DeltaAngle(previousYaw, currentYaw) / Time.deltaTime;
            previousYaw = currentYaw;

            float targetLeanZ = -turnDelta * (currentSpeed / sprintFastSpeed) * (maxLeanAngle / 45f);
            targetLeanZ = Mathf.Clamp(targetLeanZ, -maxLeanAngle, maxLeanAngle);

            currentLeanZ = Mathf.Lerp(currentLeanZ, targetLeanZ, Time.deltaTime * leanSmoothSpeed);
            modelTransform.localRotation = Quaternion.Euler(0f, 0f, currentLeanZ);
        }
        #endregion
    }
}
