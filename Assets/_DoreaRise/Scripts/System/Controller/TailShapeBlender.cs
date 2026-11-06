using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using DoreaRise.System.Controller;

namespace Dorea.CharacterTailComponent
{
    [AddComponentMenu("Dorea/Tail Shape Blender")]
    [RequireComponent(typeof(CharacterTail))]
    public class TailShapeBlender : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterTail targetTail;
        [FormerlySerializedAs("characterControl")]
        [Tooltip("CharacterControl or LocomotionMotor (auto-found on parents when empty)")]
        [SerializeField] private MonoBehaviour locomotionSource;

        [Header("Blend Settings")]
        [SerializeField] private float blendSpeed = 8f;

        [Header("Shape Data")]
        [SerializeField] private List<Vector3> idleWalkShape = new List<Vector3>();
        [SerializeField] private List<Vector3> sprintShape = new List<Vector3>();
        [SerializeField] private List<Vector3> brakeShape = new List<Vector3>();

        private List<Vector3> currentShape = new List<Vector3>();
        private ILocomotionState locomotion;

        private void Start()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();

            if (idleWalkShape.Count > 0)
            {
                currentShape = new List<Vector3>(idleWalkShape);
                targetTail.ControlPoints = new List<Vector3>(currentShape);
            }
        }

        private void LateUpdate()
        {
            ILocomotionState state = LocomotionStateResolver.ResolveCached(ref locomotion, locomotionSource, this);
            if (targetTail == null || state == null) return;

            List<Vector3> targetShape = idleWalkShape;

            if (state.IsBraking)
            {
                targetShape = brakeShape;
            }
            else if (state.IsSprinting)
            {
                targetShape = sprintShape;
            }

            if (targetShape.Count == 0) return;

            if (currentShape.Count != targetShape.Count)
            {
                currentShape = new List<Vector3>(targetShape);
            }

            bool needsUpdate = false;
            for (int i = 0; i < currentShape.Count; i++)
            {
                Vector3 newPos = Vector3.Lerp(currentShape[i], targetShape[i], Time.deltaTime * blendSpeed);
                if (Vector3.SqrMagnitude(currentShape[i] - newPos) > 0.0001f)
                {
                    currentShape[i] = newPos;
                    needsUpdate = true;
                }
            }

            if (needsUpdate)
            {
                targetTail.ControlPoints = new List<Vector3>(currentShape);
            }
        }

        [ContextMenu("Save Current as Idle/Walk Shape")]
        public void SaveIdleWalkShape()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();
            idleWalkShape = new List<Vector3>(targetTail.ControlPoints);
        }

        [ContextMenu("Save Current as Sprint Shape")]
        public void SaveSprintShape()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();
            sprintShape = new List<Vector3>(targetTail.ControlPoints);
        }

        [ContextMenu("Save Current as Brake Shape")]
        public void SaveBrakeShape()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();
            brakeShape = new List<Vector3>(targetTail.ControlPoints);
        }

        [ContextMenu("Load Idle/Walk Shape to Tail")]
        public void LoadIdleWalkShape()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();
            if (idleWalkShape != null && idleWalkShape.Count > 0)
                targetTail.ControlPoints = new List<Vector3>(idleWalkShape);
        }

        [ContextMenu("Load Sprint Shape to Tail")]
        public void LoadSprintShape()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();
            if (sprintShape != null && sprintShape.Count > 0)
                targetTail.ControlPoints = new List<Vector3>(sprintShape);
        }

        [ContextMenu("Load Brake Shape to Tail")]
        public void LoadBrakeShape()
        {
            if (targetTail == null) targetTail = GetComponent<CharacterTail>();
            if (brakeShape != null && brakeShape.Count > 0)
                targetTail.ControlPoints = new List<Vector3>(brakeShape);
        }
    }
}