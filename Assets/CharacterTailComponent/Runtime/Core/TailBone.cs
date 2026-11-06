using System;
using UnityEngine;

namespace Dorea.CharacterTailComponent
{
    [Serializable]
    public class TailBone
    {
        #region [Variables/Fields]
        public Transform transform;
        public Vector3 localOffset;
        public Quaternion localRotation;
        public float length;
        [Range(0f, 1f)] public float weight = 1f;

        [HideInInspector] public Vector3 simulatedPosition;
        [HideInInspector] public Vector3 velocity;
        [HideInInspector] public Vector3 animatedPosition;
        [HideInInspector] public Quaternion animatedRotation;
        #endregion
    }
}
