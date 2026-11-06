using System.Collections.Generic;
using UnityEngine;

namespace Dorea.CharacterTailComponent
{
    [AddComponentMenu("Dorea/Character Tail")]
    [DisallowMultipleComponent]
    public class CharacterTail : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Bone Chain")]
        [SerializeField] private Transform rootBone;

        [Header("Shape (Spline Control Points)")]
        [SerializeField] private List<Vector3> controlPoints = new List<Vector3>();

        [Header("Jiggle Dynamics")]
        [SerializeField] private bool isJiggle = false;
        [Range(0f, 1f)] [SerializeField] private float jiggleStiffness = 0.4f;
        [Range(0f, 1f)] [SerializeField] private float jiggleDamping = 0.4f;

        [Header("Wagging / Swishing")]
        [SerializeField] private bool isWagging = false;
        [SerializeField] private Vector3 waggingAxis = Vector3.right;
        [SerializeField] private float waggingSpeed = 5f;
        [SerializeField] private float waggingAmplitude = 0.2f;
        [SerializeField] private float waggingFrequency = 2f;

        [Header("Gizmos")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private Color splineColor = Color.green;
        [SerializeField] private Color boneColor = Color.cyan;
        [SerializeField] private Color controlPointColor = Color.yellow;

        [HideInInspector] [SerializeField] private List<TailBone> bones = new List<TailBone>();
        #endregion

        #region [Properties]
        public Transform RootBone
        {
            get => rootBone;
            set
            {
                rootBone = value;
                Initialize();
            }
        }

        public List<Vector3> ControlPoints
        {
            get => controlPoints;
            set => controlPoints = value;
        }

        public bool IsJiggle
        {
            get => isJiggle;
            set => isJiggle = value;
        }

        public float JiggleStiffness
        {
            get => jiggleStiffness;
            set => jiggleStiffness = Mathf.Clamp01(value);
        }

        public float JiggleDamping
        {
            get => jiggleDamping;
            set => jiggleDamping = Mathf.Clamp01(value);
        }

        public bool IsWagging
        {
            get => isWagging;
            set => isWagging = value;
        }

        public Vector3 WaggingAxis
        {
            get => waggingAxis;
            set => waggingAxis = value;
        }

        public float WaggingSpeed
        {
            get => waggingSpeed;
            set => waggingSpeed = value;
        }

        public float WaggingAmplitude
        {
            get => waggingAmplitude;
            set => waggingAmplitude = value;
        }

        public float WaggingFrequency
        {
            get => waggingFrequency;
            set => waggingFrequency = value;
        }

        public List<TailBone> Bones => bones;

        public bool ShowGizmos
        {
            get => showGizmos;
            set => showGizmos = value;
        }

        public Color SplineColor => splineColor;
        public Color BoneColor => boneColor;
        public Color ControlPointColor => controlPointColor;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            Initialize();
        }

        private void LateUpdate()
        {
            ApplyShapeToBones();
        }

        private void OnValidate()
        {
            Initialize();
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos) return;

            DrawControlPointGizmos();
            DrawSplineGizmos();
            DrawBoneGizmos();
        }
        #endregion

        #region [Public Methods]
        [ContextMenu("Refresh Tail")]
        public void Refresh()
        {
            Initialize();
        }

        [ContextMenu("Rebuild Bone Chain")]
        public void RebuildBoneChain()
        {
            Initialize();
        }

        [ContextMenu("Reset Control Points")]
        public void ResetControlPoints()
        {
            controlPoints.Clear();
            GenerateDefaultControlPoints();
        }

        [ContextMenu("Add Control Point")]
        public void AddControlPoint()
        {
            if (controlPoints.Count == 0)
            {
                controlPoints.Add(Vector3.zero);
                return;
            }

            Vector3 lastPoint = controlPoints[controlPoints.Count - 1];
            Vector3 direction = Vector3.back;
            if (controlPoints.Count >= 2)
            {
                direction = (lastPoint - controlPoints[controlPoints.Count - 2]).normalized;
            }
            controlPoints.Add(lastPoint + direction * 0.3f);
        }

        [ContextMenu("Remove Last Control Point")]
        public void RemoveLastControlPoint()
        {
            if (controlPoints.Count > 2)
                controlPoints.RemoveAt(controlPoints.Count - 1);
        }
        #endregion

        #region [Private Methods]
        private void Initialize()
        {
            if (rootBone == null) return;

            BuildBoneChain();

            if (controlPoints.Count < 2)
            {
                controlPoints.Clear();
                GenerateDefaultControlPoints();
            }

            ApplyShapeToBones();
        }

        private void BuildBoneChain()
        {
            // First, count how many bones we should have
            Transform countCurrent = rootBone;
            int count = 0;
            while (countCurrent != null)
            {
                count++;
                countCurrent = countCurrent.childCount > 0 ? countCurrent.GetChild(0) : null;
            }

            // Check if existing chain matches the hierarchy perfectly
            bool matches = (bones != null && bones.Count == count);
            if (matches)
            {
                Transform checkCurrent = rootBone;
                for (int i = 0; i < count; i++)
                {
                    if (bones[i].transform != checkCurrent)
                    {
                        matches = false;
                        break;
                    }
                    checkCurrent = checkCurrent.childCount > 0 ? checkCurrent.GetChild(0) : null;
                }
            }

            // If it matches, just update lengths and offsets, but KEEP the objects (and weights)
            if (matches)
            {
                Transform current = rootBone;
                Transform previous = null;
                for (int i = 0; i < count; i++)
                {
                    bones[i].simulatedPosition = current.position;
                    bones[i].localRotation = current.localRotation;
                    
                    if (previous != null)
                    {
                        bones[i].length = Vector3.Distance(current.position, previous.position);
                        bones[i].localOffset = previous.InverseTransformPoint(current.position);
                    }
                    else
                    {
                        bones[i].length = 0f;
                        bones[i].localOffset = Vector3.zero;
                    }
                    previous = current;
                    current = current.childCount > 0 ? current.GetChild(0) : null;
                }
                return;
            }

            // Otherwise, rebuild from scratch
            bones.Clear();
            Transform buildCurrent = rootBone;
            Transform buildPrevious = null;

            while (buildCurrent != null)
            {
                TailBone bone = new TailBone
                {
                    transform = buildCurrent,
                    simulatedPosition = buildCurrent.position,
                    localRotation = buildCurrent.localRotation
                };

                if (buildPrevious != null)
                {
                    bone.length = Vector3.Distance(buildCurrent.position, buildPrevious.position);
                    bone.localOffset = buildPrevious.InverseTransformPoint(buildCurrent.position);
                }
                else
                {
                    bone.length = 0f;
                    bone.localOffset = Vector3.zero;
                }

                bones.Add(bone);
                buildPrevious = buildCurrent;
                buildCurrent = buildCurrent.childCount > 0 ? buildCurrent.GetChild(0) : null;
            }
        }

        private void GenerateDefaultControlPoints()
        {
            if (rootBone == null) return;

            Transform current = rootBone;
            var list = new List<Transform>();
            while (current != null)
            {
                list.Add(current);
                current = current.childCount > 0 ? current.GetChild(0) : null;
            }

            if (list.Count == 0) return;

            if (list.Count == 1)
            {
                controlPoints.Add(transform.InverseTransformPoint(list[0].position));
                controlPoints.Add(transform.InverseTransformPoint(list[0].position + list[0].forward * 0.3f));
                return;
            }

            int step = Mathf.Max(1, list.Count / 3);
            controlPoints.Add(transform.InverseTransformPoint(list[0].position));

            int mid1 = Mathf.Min(step, list.Count - 1);
            controlPoints.Add(transform.InverseTransformPoint(list[mid1].position));

            int mid2 = Mathf.Min(step * 2, list.Count - 1);
            if (mid2 > mid1)
                controlPoints.Add(transform.InverseTransformPoint(list[mid2].position));

            controlPoints.Add(transform.InverseTransformPoint(list[list.Count - 1].position));
        }

        private void ApplyShapeToBones()
        {
            if (bones == null || bones.Count == 0 || controlPoints == null || controlPoints.Count < 2) return;

            int totalBones = bones.Count;

            // Calculate cumulative lengths along bone chain to handle unequal bone lengths
            float totalLength = 0f;
            for (int i = 1; i < totalBones; i++)
            {
                totalLength += bones[i].length;
            }

            float[] boneT = new float[totalBones];
            float accumulatedLength = 0f;
            for (int i = 0; i < totalBones; i++)
            {
                if (i > 0) accumulatedLength += bones[i].length;
                boneT[i] = totalLength > 0.0001f ? accumulatedLength / totalLength : (float)i / Mathf.Max(1, totalBones - 1);
            }

            // Perform Quaternion Slerp blending between initial/anim rotation and target spline rotation
            for (int i = 1; i < totalBones; i++)
            {
                Transform parentTransform = bones[i - 1].transform;
                if (parentTransform == null) continue;

                Quaternion baseRotation = parentTransform.rotation;
                Vector3 targetSplinePos = CatmullRomSpline.EvaluateWorld(controlPoints, transform, boneT[i]);

                if (isWagging && Application.isPlaying)
                {
                    float wave = Mathf.Sin(Time.time * waggingSpeed - boneT[i] * waggingFrequency * Mathf.PI * 2f) * waggingAmplitude * boneT[i];
                    Vector3 wagDirection = transform.TransformDirection(waggingAxis.normalized);
                    targetSplinePos += wagDirection * wave;
                }

                Vector3 targetPos = targetSplinePos;

                if (isJiggle && Application.isPlaying)
                {
                    float stiffness = Mathf.Lerp(10f, 200f, jiggleStiffness);
                    float damping = Mathf.Lerp(1f, 15f, jiggleDamping);

                    Vector3 force = (targetSplinePos - bones[i].simulatedPosition) * stiffness;
                    bones[i].velocity += force * Time.deltaTime;
                    bones[i].velocity *= Mathf.Max(0f, 1f - damping * Time.deltaTime);
                    bones[i].simulatedPosition += bones[i].velocity * Time.deltaTime;

                    Vector3 anchorPos = bones[i - 1].transform != null ? bones[i - 1].transform.position : parentTransform.position;
                    Vector3 dir = bones[i].simulatedPosition - anchorPos;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        bones[i].simulatedPosition = anchorPos + dir.normalized * bones[i].length;
                    }

                    targetPos = bones[i].simulatedPosition;
                }
                else
                {
                    bones[i].simulatedPosition = targetSplinePos;
                    bones[i].velocity = Vector3.zero;
                }

                Vector3 currentOffsetWorld = parentTransform.TransformVector(bones[i].localOffset);
                Vector3 targetDir = targetPos - parentTransform.position;

                if (currentOffsetWorld.sqrMagnitude > 0.0001f && targetDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion rotDiff = Quaternion.FromToRotation(currentOffsetWorld.normalized, targetDir.normalized);
                    Quaternion targetRotation = rotDiff * baseRotation;

                    float influence = Mathf.Clamp01(bones[i].weight);

                    parentTransform.rotation = Quaternion.Slerp(baseRotation, targetRotation, influence);

                    // --- Robust Twist Correction ---
                    Quaternion parentParentRot = parentTransform.parent != null ? parentTransform.parent.rotation : transform.rotation;
                    Quaternion expectedWorldRot = parentParentRot * bones[i - 1].localRotation;

                    Vector3 localForward = bones[i].localOffset.normalized;
                    Vector3 localRef = Vector3.up;
                    if (Mathf.Abs(Vector3.Dot(localForward, localRef)) > 0.9f)
                        localRef = Vector3.forward;

                    Vector3 expectedRefWorld = expectedWorldRot * localRef;
                    Vector3 currentRefWorld = parentTransform.TransformDirection(localRef);
                    Vector3 boneForwardWorld = parentTransform.TransformDirection(localForward);

                    Vector3 projectedExpected = (expectedRefWorld - Vector3.Dot(expectedRefWorld, boneForwardWorld) * boneForwardWorld).normalized;
                    Vector3 projectedCurrent = (currentRefWorld - Vector3.Dot(currentRefWorld, boneForwardWorld) * boneForwardWorld).normalized;

                    if (projectedExpected.sqrMagnitude > 0.001f && projectedCurrent.sqrMagnitude > 0.001f)
                    {
                        float twistAngle = Vector3.SignedAngle(projectedCurrent, projectedExpected, boneForwardWorld);
                        parentTransform.rotation = Quaternion.AngleAxis(twistAngle * influence, boneForwardWorld) * parentTransform.rotation;
                    }
                }
            }
        }

        private void DrawControlPointGizmos()
        {
            if (controlPoints == null || controlPoints.Count < 2) return;

            Gizmos.color = controlPointColor;
            for (int i = 0; i < controlPoints.Count; i++)
            {
                Gizmos.DrawSphere(transform.TransformPoint(controlPoints[i]), 0.03f);
            }
        }

        private void DrawSplineGizmos()
        {
            if (controlPoints == null || controlPoints.Count < 2) return;

            Gizmos.color = splineColor;
            Vector3 prev = CatmullRomSpline.EvaluateWorld(controlPoints, transform, 0f);
            int segments = 50;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 curr = CatmullRomSpline.EvaluateWorld(controlPoints, transform, t);
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }
        }

        private void DrawBoneGizmos()
        {
            if (bones == null || bones.Count == 0) return;

            Gizmos.color = boneColor;
            for (int i = 0; i < bones.Count; i++)
            {
                Gizmos.DrawWireSphere(bones[i].simulatedPosition, 0.02f);
                if (i > 0)
                {
                    Gizmos.DrawLine(bones[i - 1].simulatedPosition, bones[i].simulatedPosition);
                }
            }
        }
        #endregion
    }
}
