using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Drag-to-pose controls for Pose Fix: hands / feet reach with two-bone IK, elbows / knees swing about the limb,
    /// the pelvis moves with the feet kept planted, chest and head aim. Every bone it turns is reported so it can be
    /// keyed. In this rig hands and feet hang under their own IK bones (Hand.IK / Foot.IK), not under the forearm /
    /// shin, so those are moved along explicitly.
    /// </summary>
    public class PoseFixRig
    {
        public enum Part { HandL, HandR, ElbowL, ElbowR, FootL, FootR, KneeL, KneeR, Pelvis, Chest, Head }

        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly Transform character;

        public PoseFixRig(Transform modelRoot, Transform characterRoot)
        {
            character = characterRoot != null ? characterRoot : modelRoot;
            foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
        }

        public Transform Bone(string name) => bones.TryGetValue(name, out Transform t) ? t : null;

        public bool Valid => Bone("Spine") != null && Bone("Upper_arm.L") != null && Bone("Hip.L") != null;

        /// <summary>Where the handle for a part sits now.</summary>
        public Vector3 HandlePosition(Part part)
        {
            switch (part)
            {
                case Part.HandL: return Bone("Hand.IK.L").position;
                case Part.HandR: return Bone("Hand.IK.R").position;
                case Part.ElbowL: return Bone("Forearm.L").position;
                case Part.ElbowR: return Bone("Forearm.R").position;
                case Part.FootL: return Bone("Foot.IK.L").position;
                case Part.FootR: return Bone("Foot.IK.R").position;
                case Part.KneeL: return Bone("Leg.L").position;
                case Part.KneeR: return Bone("Leg.R").position;
                case Part.Pelvis: return Bone("Spine").position;
                case Part.Chest: return Bone("Spine.004").position;
                default: return HeadTop();
            }
        }

        /// <summary>Moves a part toward the target; adds every bone it changed to `edited`.</summary>
        public void Drag(Part part, Vector3 target, HashSet<Transform> edited)
        {
            switch (part)
            {
                case Part.HandL: Reach("Upper_arm.L", "Forearm.L", "Hand.IK.L", target, edited); break;
                case Part.HandR: Reach("Upper_arm.R", "Forearm.R", "Hand.IK.R", target, edited); break;
                case Part.ElbowL: Swing("Upper_arm.L", "Forearm.L", "Hand.IK.L", target, edited); break;
                case Part.ElbowR: Swing("Upper_arm.R", "Forearm.R", "Hand.IK.R", target, edited); break;
                case Part.FootL: Reach("Hip.L", "Leg.L", "Foot.IK.L", target, edited); break;
                case Part.FootR: Reach("Hip.R", "Leg.R", "Foot.IK.R", target, edited); break;
                case Part.KneeL: Swing("Hip.L", "Leg.L", "Foot.IK.L", target, edited); break;
                case Part.KneeR: Swing("Hip.R", "Leg.R", "Foot.IK.R", target, edited); break;
                case Part.Pelvis: MovePelvis(target, edited); break;
                case Part.Chest: Aim(new[] { "Spine.001", "Spine.002", "Spine.003" }, Bone("Spine.001").position, HandlePosition(Part.Chest), target, edited); break;
                case Part.Head: Aim(new[] { "Spine.004", "Head" }, Bone("Spine.004").position, HeadTop(), target, edited); break;
            }
        }

        /// <summary>The bone a part's rotate gizmo turns (its orientation is used for the Local gizmo).</summary>
        public Quaternion HandleRotation(Part part)
        {
            Transform t = RotateBone(part);
            return t != null ? t.rotation : Quaternion.identity;
        }

        private Transform RotateBone(Part part)
        {
            switch (part)
            {
                case Part.HandL: return Bone("Hand.IK.L");
                case Part.HandR: return Bone("Hand.IK.R");
                case Part.ElbowL: return Bone("Forearm.L");
                case Part.ElbowR: return Bone("Forearm.R");
                case Part.FootL: return Bone("Foot.IK.L");
                case Part.FootR: return Bone("Foot.IK.R");
                case Part.KneeL: return Bone("Leg.L");
                case Part.KneeR: return Bone("Leg.R");
                case Part.Pelvis: return Bone("Spine");
                case Part.Chest: return Bone("Spine.003");
                default: return Bone("Head");
            }
        }

        /// <summary>
        /// Turns a part by a world-space rotation: hands / feet turn the wrist / ankle, elbows / knees turn the
        /// forearm / shin (the hand / foot rides along), the pelvis turns the body with the feet kept planted, chest
        /// and head spread the turn over their chain.
        /// </summary>
        public void Rotate(Part part, Quaternion delta, HashSet<Transform> edited)
        {
            switch (part)
            {
                case Part.HandL: case Part.HandR: case Part.FootL: case Part.FootR:
                {
                    Transform t = RotateBone(part);
                    t.rotation = delta * t.rotation;
                    edited.Add(t);
                    break;
                }
                case Part.ElbowL: case Part.ElbowR: case Part.KneeL: case Part.KneeR:
                {
                    Transform t = RotateBone(part);
                    string side = part == Part.ElbowL || part == Part.KneeL ? "L" : "R";
                    string holder = part == Part.ElbowL || part == Part.ElbowR ? "Hand.IK." + side : "Foot.IK." + side;
                    var carry = KeepOnLimb(new[] { t.name }, new[] { holder });
                    t.rotation = delta * t.rotation;
                    carry();
                    edited.Add(t);
                    break;
                }
                case Part.Pelvis:
                {
                    Transform pelvis = Bone("Spine");
                    Vector3 footL = Bone("Foot.IK.L").position, footR = Bone("Foot.IK.R").position;
                    var hands = KeepHandsOnArms();
                    var feet = KeepOnLimb(new[] { "Leg.L", "Leg.R" }, new[] { "Foot.IK.L", "Foot.IK.R" });
                    pelvis.rotation = delta * pelvis.rotation;
                    edited.Add(pelvis);
                    feet();
                    Reach("Hip.L", "Leg.L", "Foot.IK.L", footL, edited);
                    Reach("Hip.R", "Leg.R", "Foot.IK.R", footR, edited);
                    hands();
                    break;
                }
                case Part.Chest: Spread(new[] { "Spine.001", "Spine.002", "Spine.003" }, delta, edited); break;
                case Part.Head: Spread(new[] { "Spine.004", "Head" }, delta, edited); break;
            }
        }

        // --- single joints -----------------------------------------------------------------------------
        private static readonly string[] BodyJointNames =
        {
            "Shoulder.L", "Shoulder.R", "Upper_arm.L", "Upper_arm.R", "Hip.L", "Hip.R",
            "Spine.001", "Spine.002", "Spine.004", "Toe.L", "Toe.R"
        };

        /// <summary>Parents a joint's Move must not turn (the body core and the rig roots); those joints only rotate.</summary>
        private static readonly string[] FixedParents =
        {
            "Spine", "Spine.001", "Spine.002", "Spine.003", "Root", "ModelBase", "Armature",
            "Hand.IK.L", "Hand.IK.R", "Foot.IK.L", "Foot.IK.R"
        };

        /// <summary>The single-joint points to show: body joints, finger bones. (The tail is not keyed by the clips,
        /// CharacterTail moves it at runtime, so it has no points.)</summary>
        public List<Transform> Joints(bool body, bool fingers)
        {
            var list = new List<Transform>();
            if (body)
                foreach (string n in BodyJointNames)
                    if (Bone(n) != null) list.Add(Bone(n));
            if (fingers)
                foreach (string side in new[] { "L", "R" })
                    foreach (string f in new[] { "f_index", "f_middle", "f_ring", "f_pinky", "thumb" })
                        for (int i = 1; i <= 3; i++)
                        {
                            Transform t = Bone(f + ".0" + i + "." + side);
                            if (t != null) list.Add(t);
                        }
            return list;
        }

        /// <summary>Whether Move works on this joint (it turns the parent so the joint goes to the target).</summary>
        public bool CanMoveJoint(Transform joint)
        {
            return joint != null && joint.parent != null && global::System.Array.IndexOf(FixedParents, joint.parent.name) < 0;
        }

        /// <summary>Moves a joint by turning its parent bone toward the target (bone lengths stay).</summary>
        public void MoveJoint(Transform joint, Vector3 target, HashSet<Transform> edited)
        {
            if (!CanMoveJoint(joint)) return;
            Transform parent = joint.parent;
            Vector3 a = joint.position - parent.position, b = target - parent.position;
            if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) return;
            TurnBone(parent, Quaternion.FromToRotation(a, b), edited);
        }

        /// <summary>Turns a joint by a world-space rotation; hands / feet on limbs below it ride along.</summary>
        public void RotateJoint(Transform joint, Quaternion delta, HashSet<Transform> edited)
        {
            if (joint != null) TurnBone(joint, delta, edited);
        }

        public Transform[] MoveJoint(Transform joint, Vector3 target)
        {
            var changed = new HashSet<Transform>();
            MoveJoint(joint, target, changed);
            return new List<Transform>(changed).ToArray();
        }

        public Transform[] RotateJoint(Transform joint, Quaternion delta)
        {
            var changed = new HashSet<Transform>();
            RotateJoint(joint, delta, changed);
            return new List<Transform>(changed).ToArray();
        }

        private void TurnBone(Transform bone, Quaternion delta, HashSet<Transform> edited)
        {
            var ends = new List<string>();
            var holders = new List<string>();
            foreach (string holder in new[] { "Hand.IK.L", "Hand.IK.R", "Foot.IK.L", "Foot.IK.R" })
            {
                string endName = PoseCorrector.HolderLimbEnd(holder);
                Transform end = Bone(endName);
                if (end != null && end.IsChildOf(bone)) { ends.Add(endName); holders.Add(holder); }
            }
            var carry = KeepOnLimb(ends.ToArray(), holders.ToArray());
            bone.rotation = delta * bone.rotation;
            carry();
            edited.Add(bone);
        }

        /// <summary>Rotate and return the bones it changed (for scripts / tests).</summary>
        public Transform[] Rotate(Part part, Quaternion delta)
        {
            var changed = new HashSet<Transform>();
            Rotate(part, delta, changed);
            return new List<Transform>(changed).ToArray();
        }

        /// <summary>Drag and return the bones it changed (for scripts / tests).</summary>
        public Transform[] Drag(Part part, Vector3 target)
        {
            var changed = new HashSet<Transform>();
            Drag(part, target, changed);
            var list = new List<Transform>(changed);
            return list.ToArray();
        }

        private Vector3 HeadTop()
        {
            Transform head = Bone("Head");
            Transform neck = Bone("Spine.004");
            Vector3 up = head.position - neck.position;
            return head.position + (up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up) * 0.22f;
        }

        /// <summary>Two-bone IK: the end reaches the target, the bend stays on its current side.</summary>
        private void Reach(string upperName, string lowerName, string endName, Vector3 target, HashSet<Transform> edited)
        {
            Transform upper = Bone(upperName), lower = Bone(lowerName), end = Bone(endName);
            Vector3 root = upper.position, mid = lower.position, tip = end.position;
            float a = Vector3.Distance(root, mid), b = Vector3.Distance(mid, tip);
            if (a < 1e-5f || b < 1e-5f) return;
            Vector3 to = target - root;
            if (to.sqrMagnitude < 1e-10f) return;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            Vector3 dir = to.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(mid - root, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(character.forward, dir);
            bend.Normalize();
            float along = (a * a + d * d - b * b) / (2f * d);
            float across = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 newMid = root + dir * along + bend * across;
            Vector3 newTip = root + dir * d;

            Quaternion upperTurn = Quaternion.FromToRotation(mid - root, newMid - root);
            upper.rotation = upperTurn * upper.rotation;
            Vector3 lowerNow = upperTurn * (tip - mid);
            Quaternion lowerTurn = Quaternion.FromToRotation(lowerNow, newTip - lower.position);
            lower.rotation = lowerTurn * lower.rotation;
            end.position = newTip;
            end.rotation = lowerTurn * upperTurn * end.rotation;
            edited.Add(upper);
            edited.Add(lower);
        }

        /// <summary>Turns the limb about its root-to-end line so the middle joint points at the target; the end stays.</summary>
        private void Swing(string upperName, string lowerName, string endName, Vector3 target, HashSet<Transform> edited)
        {
            Transform upper = Bone(upperName), lower = Bone(lowerName), end = Bone(endName);
            Vector3 root = upper.position, tip = end.position;
            Vector3 axis = tip - root;
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            Vector3 now = Vector3.ProjectOnPlane(lower.position - root, axis);
            Vector3 want = Vector3.ProjectOnPlane(target - root, axis);
            if (now.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(now, want, axis), axis);
            Quaternion endRot = end.rotation;
            upper.rotation = turn * upper.rotation;
            end.rotation = turn * endRot;           // the hand / foot turns with its limb, and stays where it was
            edited.Add(upper);
        }

        /// <summary>Moves the pelvis; the legs re-reach so the feet stay put, the hands ride along with the arms.</summary>
        private void MovePelvis(Vector3 target, HashSet<Transform> edited)
        {
            Transform pelvis = Bone("Spine");
            Vector3 footL = Bone("Foot.IK.L").position, footR = Bone("Foot.IK.R").position;
            var hands = KeepHandsOnArms();
            var feet = KeepOnLimb(new[] { "Leg.L", "Leg.R" }, new[] { "Foot.IK.L", "Foot.IK.R" });
            pelvis.position = target;
            edited.Add(pelvis);
            feet();                                 // feet back on the shins first, so the legs reach from a true tip
            Reach("Hip.L", "Leg.L", "Foot.IK.L", footL, edited);
            Reach("Hip.R", "Leg.R", "Foot.IK.R", footR, edited);
            hands();
        }

        /// <summary>Turns a chain so `from` (seen from `pivot`) points at the target, spread evenly over the bones.</summary>
        private void Aim(string[] chain, Vector3 pivot, Vector3 from, Vector3 target, HashSet<Transform> edited)
        {
            Vector3 a = from - pivot, b = target - pivot;
            if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return;
            Spread(chain, Quaternion.FromToRotation(a, b), edited);
        }

        /// <summary>Applies a world rotation spread evenly over a chain (parent first), hands kept on the arms.</summary>
        private void Spread(string[] chain, Quaternion full, HashSet<Transform> edited)
        {
            Quaternion part = Quaternion.Slerp(Quaternion.identity, full, 1f / chain.Length);
            var hands = KeepHandsOnArms();
            foreach (string name in chain)
            {
                Transform t = Bone(name);
                if (t == null) continue;
                t.rotation = part * t.rotation;
                edited.Add(t);
            }
            hands();
        }

        /// <summary>Records each hand's place on its forearm; the returned action puts it back there.</summary>
        private global::System.Action KeepHandsOnArms()
        {
            return KeepOnLimb(new[] { "Forearm.L", "Forearm.R" }, new[] { "Hand.IK.L", "Hand.IK.R" });
        }

        /// <summary>Records each holder's place on its limb end; the returned action puts it back there.</summary>
        private global::System.Action KeepOnLimb(string[] ends, string[] holders)
        {
            var restore = new List<global::System.Action>();
            for (int i = 0; i < ends.Length; i++)
            {
                Transform end = Bone(ends[i]), holder = Bone(holders[i]);
                if (end == null || holder == null) continue;
                Vector3 p = end.InverseTransformPoint(holder.position);
                Quaternion r = Quaternion.Inverse(end.rotation) * holder.rotation;
                restore.Add(() => holder.SetPositionAndRotation(end.TransformPoint(p), end.rotation * r));
            }
            return () => { foreach (var a in restore) a(); };
        }
    }
}
