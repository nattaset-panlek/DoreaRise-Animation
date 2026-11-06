using UnityEngine;
using UnityEngine.Serialization;

namespace DoreaRise.System.Controller
{
    public class CameraControl : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Target")]
        [SerializeField] private Transform target;
        [FormerlySerializedAs("characterControl")]
        [Tooltip("CharacterControl or LocomotionMotor (auto-found on the target when empty)")]
        [SerializeField] private MonoBehaviour locomotionSource;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.4f, 0f);
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.5f, 0f, 0f);

        [Header("Camera")]
        [SerializeField] private float cameraDistance = 3.5f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 8f;
        [SerializeField] private float zoomSensitivity = 3f;
        [SerializeField] private float zoomSmoothSpeed = 8f;
        [SerializeField] private float followSmoothTime = 0.08f;
        [SerializeField] private float rotationSmoothTime = 0.06f;
        [SerializeField] private float xSensitivity = 2.5f;
        [SerializeField] private float ySensitivity = 2.0f;
        [SerializeField] private float yMinAngle = -30f;
        [SerializeField] private float yMaxAngle = 70f;

        [Header("Camera Shake")]
        [Tooltip("Constant shake while sprinting. Off when CameraImpulse/LocomotionFeedback provide event-based impacts")]
        [SerializeField] private bool sprintShake = true;
        [SerializeField] private float shakeFrequency = 25f;
        [SerializeField] private float minShakeAmplitude = 0.02f;
        [SerializeField] private float maxShakeAmplitude = 0.12f;
        [SerializeField] private float shakeStartSpeed = 7f;
        [SerializeField] private float shakeMaxSpeed = 10.5f;

        private float shakeTimer;
        private ILocomotionState locomotion;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionLayers;
        [SerializeField] private float collisionRadius = 0.2f;
        [SerializeField] private float collisionRecoverSpeed = 10f;

        [Header("Sprint FOV")]
        [SerializeField] private float normalFov = 55f;
        [SerializeField] private float sprintFov = 62f;
        [SerializeField] private float fovSmoothSpeed = 6f;

        private float yaw;
        private float pitch;
        private float currentDistance;
        private float desiredDistance;
        private Vector3 followVelocity;
        private Vector3 currentPivot;
        private float rotSmoothVelX;
        private float rotSmoothVelY;
        private float currentYaw;
        private float currentPitch;
        private Camera cam;
        #endregion

        #region [Properties]
        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public float Yaw => yaw;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
        }

        private void Start()
        {
            SetCursorLock(true);
            currentDistance = cameraDistance;
            desiredDistance = cameraDistance;

            if (target != null)
            {
                Vector3 dir = target.forward;
                yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                pitch = 10f;
                currentPivot = target.position + targetOffset;
            }
            else
            {
                Vector3 angles = transform.eulerAngles;
                yaw = angles.y;
                pitch = angles.x;
                currentPivot = transform.position;
            }

            currentYaw = yaw;
            currentPitch = pitch;
            if (cam != null) cam.fieldOfView = normalFov;
        }

        private void LateUpdate()
        {
            if (!target) return;

            HandleCameraRotation();
            HandleZoom();
            HandleCameraPosition();
            HandleFov();
            HandleCameraShake();
        }
        #endregion

        #region [Public Methods]
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null)
            {
                currentPivot = target.position + targetOffset;
                locomotion = LocomotionStateResolver.Resolve(null, target);
                locomotionSource = locomotion as MonoBehaviour;
            }
        }

        public void ResetCamera()
        {
            if (target != null)
            {
                Vector3 dir = target.forward;
                yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                pitch = 10f;
            }
        }

        public void SetCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
        #endregion

        #region [Private Methods]
        private void HandleCameraRotation()
        {
            float mouseX = Input.GetAxis("Mouse X") * xSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * ySensitivity;

            yaw += mouseX;
            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, yMinAngle, yMaxAngle);

            currentYaw = Mathf.SmoothDamp(currentYaw, yaw, ref rotSmoothVelX, rotationSmoothTime);
            currentPitch = Mathf.SmoothDamp(currentPitch, pitch, ref rotSmoothVelY, rotationSmoothTime);
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
                desiredDistance -= scroll * zoomSensitivity;

            desiredDistance = Mathf.Clamp(desiredDistance, minDistance, maxDistance);
            cameraDistance = Mathf.Lerp(cameraDistance, desiredDistance, Time.deltaTime * zoomSmoothSpeed);
        }

        private void HandleCameraPosition()
        {
            Vector3 pivotTarget = target.position + targetOffset;
            currentPivot = Vector3.SmoothDamp(currentPivot, pivotTarget, ref followVelocity, followSmoothTime);

            Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            Vector3 localShoulder = rotation * shoulderOffset;

            Vector3 origin = currentPivot + localShoulder;
            Vector3 direction = rotation * Vector3.back;

            float desiredDist = cameraDistance;
            RaycastHit hit;
            if (Physics.SphereCast(origin, collisionRadius, direction, out hit, cameraDistance, collisionLayers))
                desiredDist = Mathf.Max(hit.distance - 0.1f, 0.2f);

            currentDistance = currentDistance > desiredDist
                ? desiredDist
                : Mathf.Lerp(currentDistance, desiredDist, Time.deltaTime * collisionRecoverSpeed);

            transform.rotation = rotation;
            transform.position = origin + direction * currentDistance;
        }

        private void HandleFov()
        {
            if (cam == null) return;
            ILocomotionState state = ResolveLocomotion();
            bool sprinting = state != null && state.IsSprinting;
            float targetFov = sprinting ? sprintFov : normalFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * fovSmoothSpeed);
        }

        private void HandleCameraShake()
        {
            if (!sprintShake) return;
            ILocomotionState state = ResolveLocomotion();
            if (state == null || !state.IsSprinting) return;

            float speed = state.CurrentSpeed;
            if (speed <= shakeStartSpeed) return;

            float t = Mathf.InverseLerp(shakeStartSpeed, shakeMaxSpeed, speed);
            float amplitude = Mathf.Lerp(minShakeAmplitude, maxShakeAmplitude, t);

            shakeTimer += Time.deltaTime * shakeFrequency;
            float offsetX = Mathf.Sin(shakeTimer) * amplitude;
            float offsetY = Mathf.Cos(shakeTimer * 0.8f) * amplitude;

            transform.position += transform.right * offsetX + transform.up * offsetY;
        }

        private ILocomotionState ResolveLocomotion()
        {
            return LocomotionStateResolver.ResolveCached(ref locomotion, locomotionSource, target);
        }
        #endregion
    }
}
