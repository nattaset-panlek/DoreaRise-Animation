using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Hand-made fixes on top of animation clips, kept apart from the clips (they survive re-exporting the FBX):
    /// per clip, per bone, keyed local-rotation offsets over the clip's normalized time. Made with
    /// Tools > DoreaRise > Animation > Pose Fix, applied at runtime by PoseCorrector.
    /// </summary>
    [CreateAssetMenu(menuName = "DoreaRise/Pose Correction Set", fileName = "PoseCorrections")]
    public class PoseCorrectionSet : ScriptableObject
    {
        [Serializable]
        public class Key
        {
            [Tooltip("Normalized clip time 0..1")]
            public float time;
            [Tooltip("Local rotation applied after the clip's own (identity = no change)")]
            public Quaternion rotation = Quaternion.identity;
            [Tooltip("Local position added to the clip's own (the pelvis; zero for other bones)")]
            public Vector3 position;
        }

        [Serializable]
        public class BoneTrack
        {
            public string bone;
            public List<Key> keys = new List<Key>();

            /// <summary>Offset at normalized time t: eased between keys, held before the first / after the last.</summary>
            public Quaternion Evaluate(float t)
            {
                if (keys.Count == 0) return Quaternion.identity;
                if (t <= keys[0].time) return keys[0].rotation;
                for (int i = 1; i < keys.Count; i++)
                {
                    if (t > keys[i].time) continue;
                    Key a = keys[i - 1], b = keys[i];
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.time, b.time, t));
                    return Quaternion.Slerp(a.rotation, b.rotation, u);
                }
                return keys[keys.Count - 1].rotation;
            }

            /// <summary>Position offset at normalized time t, eased like the rotation.</summary>
            public Vector3 EvaluatePosition(float t)
            {
                if (keys.Count == 0) return Vector3.zero;
                if (t <= keys[0].time) return keys[0].position;
                for (int i = 1; i < keys.Count; i++)
                {
                    if (t > keys[i].time) continue;
                    Key a = keys[i - 1], b = keys[i];
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.time, b.time, t));
                    return Vector3.Lerp(a.position, b.position, u);
                }
                return keys[keys.Count - 1].position;
            }

            public void SetKey(float time, Quaternion rotation, float sameTime)
            {
                SetKey(time, rotation, Vector3.zero, sameTime);
            }

            public void SetKey(float time, Quaternion rotation, Vector3 position, float sameTime)
            {
                Key existing = keys.Find(k => Mathf.Abs(k.time - time) <= sameTime);
                if (existing != null)
                {
                    existing.rotation = rotation;
                    existing.position = position;
                }
                else keys.Add(new Key { time = time, rotation = rotation, position = position });
                keys.Sort((x, y) => x.time.CompareTo(y.time));
            }

            public bool RemoveKey(float time, float sameTime)
            {
                return keys.RemoveAll(k => Mathf.Abs(k.time - time) <= sameTime) > 0;
            }
        }

        [Serializable]
        public class ClipFix
        {
            public AnimationClip clip;
            public List<BoneTrack> tracks = new List<BoneTrack>();

            public BoneTrack Track(string bone, bool create)
            {
                BoneTrack t = tracks.Find(x => x.bone == bone);
                if (t == null && create)
                {
                    t = new BoneTrack { bone = bone };
                    tracks.Add(t);
                }
                return t;
            }
        }

        public List<ClipFix> clips = new List<ClipFix>();

        public ClipFix Find(AnimationClip clip, bool create)
        {
            if (clip == null) return null;
            ClipFix fix = clips.Find(c => c.clip == clip);
            if (fix == null && create)
            {
                fix = new ClipFix { clip = clip };
                clips.Add(fix);
            }
            return fix;
        }
    }
}
