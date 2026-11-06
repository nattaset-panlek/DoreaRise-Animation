using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Applies a PoseCorrectionSet on top of whatever clips LocomotionAnimator is playing (each weighted like the
    /// clip), before the IK layers (FootPlanting, HandPlanting) run. Hands and feet hang under their own IK bones in
    /// this rig, not under the forearm / shin, so they are carried along when an arm or leg is corrected.
    /// Lives on the root "Player Character".
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PoseCorrector : MonoBehaviour
    {
        #region [Variables/Fields]
        public PoseCorrectionSet corrections;
        [SerializeField] private LocomotionMotor motor;
        [Range(0f, 1f)] public float weight = 1f;

        /// <summary>(limb end, IK bone that holds the hand / foot): the holder follows the limb end.</summary>
        private static readonly string[][] Holders =
        {
            new[] { "Forearm.L", "Hand.IK.L" }, new[] { "Forearm.R", "Hand.IK.R" },
            new[] { "Leg.L", "Foot.IK.L" }, new[] { "Leg.R", "Foot.IK.R" }
        };

        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly List<LocomotionAnimator.PlayingClip> playing = new List<LocomotionAnimator.PlayingClip>();
        private readonly Vector3[] holderPos = new Vector3[4];
        private readonly Quaternion[] holderRot = new Quaternion[4];
        #endregion

        #region [Unity Methods]
        private void LateUpdate()
        {
            if (corrections == null || weight <= 0f) return;
            LocomotionMotor m = motor != null ? motor : motor = GetComponentInParent<LocomotionMotor>();
            if (m == null || m.LocomotionAnimator == null) return;
            m.LocomotionAnimator.GetPlayingClips(playing);
            foreach (LocomotionAnimator.PlayingClip p in playing)
                Apply(p.clip, p.normalizedTime, p.weight * weight);
        }
        #endregion

        #region [Public Methods]
        /// <summary>Applies this clip's fixes at normalized time t, weighted (also used by the Pose Fix window).</summary>
        public bool Apply(AnimationClip clip, float t, float w)
        {
            if (corrections == null || w <= 0f) return false;
            PoseCorrectionSet.ClipFix fix = corrections.Find(clip, false);
            if (fix == null || fix.tracks.Count == 0) return false;
            CacheBones();

            // remember where each hand / foot sits on its limb before anything moves
            for (int i = 0; i < Holders.Length; i++)
            {
                if (!Get(Holders[i][0], out Transform end) || !Get(Holders[i][1], out Transform holder)) continue;
                holderPos[i] = end.InverseTransformPoint(holder.position);
                holderRot[i] = Quaternion.Inverse(end.rotation) * holder.rotation;
            }

            foreach (PoseCorrectionSet.BoneTrack track in fix.tracks)
            {
                if (IsHolder(track.bone) || !Get(track.bone, out Transform bone)) continue;
                bone.localRotation = bone.localRotation * Quaternion.Slerp(Quaternion.identity, track.Evaluate(t), w);
                Vector3 offset = track.EvaluatePosition(t);
                if (offset.sqrMagnitude > 1e-10f) bone.localPosition += offset * w;
            }

            for (int i = 0; i < Holders.Length; i++)
            {
                if (!Get(Holders[i][0], out Transform end) || !Get(Holders[i][1], out Transform holder)) continue;
                holder.SetPositionAndRotation(end.TransformPoint(holderPos[i]), end.rotation * holderRot[i]);
            }

            foreach (PoseCorrectionSet.BoneTrack track in fix.tracks)
            {
                if (!IsHolder(track.bone) || !Get(track.bone, out Transform bone)) continue;
                bone.localRotation = bone.localRotation * Quaternion.Slerp(Quaternion.identity, track.Evaluate(t), w);
            }
            return true;
        }

        /// <summary>The limb end that carries this hand / foot IK bone, or null if the bone is not one.</summary>
        public static string HolderLimbEnd(string bone)
        {
            foreach (string[] pair in Holders)
                if (pair[1] == bone) return pair[0];
            return null;
        }

        public Transform FindBone(string name)
        {
            CacheBones();
            return Get(name, out Transform t) ? t : null;
        }

        public void ClearCache()
        {
            bones.Clear();
        }
        #endregion

        #region [Private Methods]
        private void CacheBones()
        {
            if (bones.Count > 0) return;
            Animator animator = GetComponentInChildren<Animator>(true);
            Transform root = animator != null ? animator.transform : transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones.Add(t.name, t);
        }

        private bool Get(string name, out Transform t)
        {
            return bones.TryGetValue(name, out t) && t != null;
        }

        private static bool IsHolder(string bone)
        {
            foreach (string[] pair in Holders)
                if (pair[1] == bone) return true;
            return false;
        }
        #endregion
    }
}
