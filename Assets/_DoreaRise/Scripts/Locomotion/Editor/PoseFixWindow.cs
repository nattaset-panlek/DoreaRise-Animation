using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Fix a pose in any locomotion clip without touching the clip: pick the clip, scrub to the frame, rotate bones
    /// in the Scene view (or click a pose point and use its Move arrows / Rotate rings), press Key. The offsets go into a PoseCorrectionSet and PoseCorrector applies them in game.
    /// Works in Edit Mode (the character is posed with AnimationMode; nothing in the scene is changed).
    /// </summary>
    public class PoseFixWindow : EditorWindow
    {
        private const string DefaultAssetPath = "Assets/_DoreaRise/Characters/Dorea/Dorea_PoseCorrections.asset";
        private const float Fps = 30f;

        private PoseCorrector corrector;
        private Animator animator;
        private AnimationClip[] clipList = new AnimationClip[0];
        private int clipIndex;
        private float time;
        private bool previewing;
        private readonly Dictionary<Transform, Quaternion> baseline = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> baselinePosition = new Dictionary<Transform, Vector3>();
        private Vector2 scroll;
        private PoseFixRig rig;
        private readonly HashSet<Transform> edited = new HashSet<Transform>();
        private bool showHandles = true;
        private bool autoKey = true;
        private int lastHotControl;
        private int selectedPart = -1;                  // index into HandleParts, -1 = none
        private Transform selectedJoint;                // or a single joint
        private bool rotateMode;                        // false = Move arrows, true = Rotate rings
        private bool showJoints = true, showFingers;
        /// <summary>Every bone's pose when the preview started: put back before each sample and when it stops, so bones
        /// the clip does not key (the tail, CharacterTail's) never keep an edit.</summary>
        private readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> rest =
            new Dictionary<Transform, (Vector3 position, Quaternion rotation)>();
        private readonly HashSet<string> clipBones = new HashSet<string>();
        /// <summary>Hand / foot IK bones: their rotation relative to the limb end in the clip (what PoseCorrector re-attaches to).</summary>
        private readonly Dictionary<Transform, Quaternion> holderRelation = new Dictionary<Transform, Quaternion>();

        private static readonly (PoseFixRig.Part part, Color color, float size)[] HandleParts =
        {
            (PoseFixRig.Part.HandL, new Color(0.3f, 0.75f, 1f), 0.07f), (PoseFixRig.Part.HandR, new Color(1f, 0.45f, 0.4f), 0.07f),
            (PoseFixRig.Part.ElbowL, new Color(0.3f, 0.75f, 1f, 0.8f), 0.05f), (PoseFixRig.Part.ElbowR, new Color(1f, 0.45f, 0.4f, 0.8f), 0.05f),
            (PoseFixRig.Part.FootL, new Color(0.3f, 0.75f, 1f), 0.08f), (PoseFixRig.Part.FootR, new Color(1f, 0.45f, 0.4f), 0.08f),
            (PoseFixRig.Part.KneeL, new Color(0.3f, 0.75f, 1f, 0.8f), 0.05f), (PoseFixRig.Part.KneeR, new Color(1f, 0.45f, 0.4f, 0.8f), 0.05f),
            (PoseFixRig.Part.Pelvis, new Color(1f, 0.85f, 0.2f), 0.1f), (PoseFixRig.Part.Chest, new Color(1f, 0.85f, 0.2f), 0.08f),
            (PoseFixRig.Part.Head, new Color(0.6f, 1f, 0.5f), 0.08f)
        };

        [MenuItem("Tools/DoreaRise/Animation/Pose Fix")]
        private static void Open()
        {
            GetWindow<PoseFixWindow>("Pose Fix").Show();
        }

        private AnimationClip Clip => clipIndex >= 0 && clipIndex < clipList.Length ? clipList[clipIndex] : null;
        private float SameKeyTime => Clip != null ? 0.5f / (Clip.length * Fps) : 0.01f;

        private void OnEnable()
        {
            Find();
            EditorApplication.playModeStateChanged += OnPlayMode;
            Undo.undoRedoPerformed += OnUndo;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            StopPreview();
            Tools.hidden = false;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Undo.undoRedoPerformed -= OnUndo;
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) StopPreview();
        }

        private void OnUndo()
        {
            if (previewing) Sample();
            Repaint();
        }

        private void Find()
        {
            corrector = FindAnyObjectByType<PoseCorrector>();
            LocomotionMotor motor = corrector != null ? corrector.GetComponentInParent<LocomotionMotor>() : FindAnyObjectByType<LocomotionMotor>();
            animator = motor != null ? motor.GetComponentInChildren<Animator>(true) : null;
            LocomotionClipSet set = motor != null ? motor.ClipSet : null;
            if (set == null)
            {
                string guid = AssetDatabase.FindAssets("t:LocomotionClipSet").FirstOrDefault();
                if (guid != null) set = AssetDatabase.LoadAssetAtPath<LocomotionClipSet>(AssetDatabase.GUIDToAssetPath(guid));
            }
            clipList = set != null
                ? set.AllClips().Where(c => c != null).Distinct().OrderBy(c => c.name).ToArray()
                : new AnimationClip[0];
            clipIndex = Mathf.Clamp(clipIndex, 0, Mathf.Max(0, clipList.Length - 1));
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Pose Fix works in Edit Mode. In Play Mode the fixes already play on the character.", MessageType.Info);
                return;
            }

            if (corrector == null || animator == null)
            {
                EditorGUILayout.HelpBox("Needs a PoseCorrector on the character (Player Character).", MessageType.Warning);
                if (GUILayout.Button("Add PoseCorrector to the character")) AddCorrector();
                if (GUILayout.Button("Find again")) Find();
                return;
            }

            if (corrector.corrections == null)
            {
                EditorGUILayout.HelpBox("The PoseCorrector has no correction set.", MessageType.Warning);
                if (GUILayout.Button("Create " + DefaultAssetPath)) CreateAsset();
                return;
            }

            // --- clip and time -------------------------------------------------------------------------
            EditorGUI.BeginChangeCheck();
            string[] names = clipList.Select(c => c.name + (corrector.corrections.Find(c, false)?.tracks.Count > 0 ? "  *" : "")).ToArray();
            clipIndex = EditorGUILayout.Popup("Clip", clipIndex, names);
            AnimationClip clip = Clip;
            if (clip == null) return;
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * Fps));
            int frame = Mathf.RoundToInt(time * frames);
            frame = EditorGUILayout.IntSlider("Frame", frame, 0, frames);
            time = frame / (float)frames;
            bool changed = EditorGUI.EndChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ Key")) { time = PrevKeyTime(); changed = true; }
                if (GUILayout.Button("◀")) { time = Mathf.Max(0f, (frame - 1) / (float)frames); changed = true; }
                if (GUILayout.Button("▶")) { time = Mathf.Min(1f, (frame + 1) / (float)frames); changed = true; }
                if (GUILayout.Button("Key ▶")) { time = NextKeyTime(); changed = true; }
            }

            bool want = GUILayout.Toggle(previewing, previewing ? "Previewing (click to stop)" : "Preview on the character", "Button", GUILayout.Height(26));
            if (want != previewing)
            {
                if (want) StartPreview();
                else StopPreview();
            }
            if (previewing && changed) Sample();

            if (!previewing)
            {
                EditorGUILayout.HelpBox("Start the preview, scrub to the frame, select bones in the Scene view or Hierarchy " +
                                        "and rotate them. Then press 'Key selected'.", MessageType.None);
                DrawKeyList(clip);
                return;
            }

            // --- drag handles ----------------------------------------------------------------------------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pose points in the Scene view", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                showHandles = GUILayout.Toggle(showHandles, "Show points", "Button");
                autoKey = GUILayout.Toggle(autoKey, "Auto-key on release", "Button");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                showJoints = GUILayout.Toggle(showJoints, "Joints", "ButtonLeft");
                showFingers = GUILayout.Toggle(showFingers, "Fingers", "ButtonRight");
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(!rotateMode, "Move (W)", "ButtonLeft") && rotateMode) { rotateMode = false; SceneView.RepaintAll(); }
                if (GUILayout.Toggle(rotateMode, "Rotate (E)", "ButtonRight") && !rotateMode) { rotateMode = true; SceneView.RepaintAll(); }
                string pivot = Tools.pivotRotation == PivotRotation.Local ? "Local" : "Global";
                if (GUILayout.Button(pivot, GUILayout.Width(60)))
                {
                    Tools.pivotRotation = Tools.pivotRotation == PivotRotation.Local ? PivotRotation.Global : PivotRotation.Local;
                    SceneView.RepaintAll();
                }
            }
            string pointName = selectedPart >= 0 ? HandleParts[selectedPart].part.ToString()
                : selectedJoint != null ? selectedJoint.name + (rig != null && !rig.CanMoveJoint(selectedJoint) ? "  (rotate only)" : "")
                : "(click a point)";
            EditorGUILayout.LabelField("Point", pointName + (selectedPart >= 0 || selectedJoint != null ? "   Esc = deselect" : ""));
            EditorGUILayout.HelpBox("Click a point, then use the arrows (Move, W) or rings (Rotate, E).\n" +
                                    "Big points - Move: hands / feet reach, elbows / knees turn the bend, pelvis moves the body " +
                                    "with the feet planted, chest / head lean. Rotate: wrist / ankle, forearm / shin, body (feet " +
                                    "stay), along the spine.\n" +
                                    "Small points (joints, fingers) - Rotate turns that joint; Move swings the bone above " +
                                    "it so the joint goes there (bone lengths stay).\n" +
                                    "Blue = left, red = right, yellow = pelvis / chest, green = head.", MessageType.None);
            using (new EditorGUI.DisabledScope(edited.Count == 0))
            {
                if (GUILayout.Button("Key edited (" + edited.Count + " bones)", GUILayout.Height(26)))
                    KeyBones(edited.ToArray(), false);
            }

            // --- keying ----------------------------------------------------------------------------------
            EditorGUILayout.Space();
            Transform[] selected = SelectedBones();
            EditorGUILayout.LabelField("Selected bones", selected.Length > 0 ? string.Join(", ", selected.Select(t => t.name)) : "(select bones to key)");
            using (new EditorGUI.DisabledScope(selected.Length == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Key selected", GUILayout.Height(28))) KeyBones(selected, false);
                if (GUILayout.Button("Neutral key", GUILayout.Height(28))) KeyBones(selected, true);
                if (GUILayout.Button("Delete key here", GUILayout.Height(28))) DeleteKeys(selected);
            }
            EditorGUILayout.HelpBox("Key selected = keep the rotation you gave the bone at this frame. Neutral key = no change " +
                                    "at this frame (use it where the fix should fade out). Between keys the fix eases; before " +
                                    "the first and after the last key it holds.", MessageType.None);
            if (GUILayout.Button("Discard unkeyed edits (re-sample)")) Sample();

            DrawKeyList(clip);
        }

        private void DrawKeyList(AnimationClip clip)
        {
            PoseCorrectionSet.ClipFix fix = corrector.corrections.Find(clip, false);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fixes in this clip", EditorStyles.boldLabel);
            if (fix == null || fix.tracks.Count == 0)
            {
                EditorGUILayout.LabelField("(none)");
                return;
            }
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * Fps));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (PoseCorrectionSet.BoneTrack track in fix.tracks.ToArray())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(track.bone, EditorStyles.label, GUILayout.Width(140)))
                    {
                        Transform b = corrector.FindBone(track.bone);
                        if (b != null) Selection.activeTransform = b;
                    }
                    foreach (PoseCorrectionSet.Key k in track.keys)
                    {
                        bool neutral = Quaternion.Angle(k.rotation, Quaternion.identity) < 0.01f;
                        if (GUILayout.Button((neutral ? "○" : "●") + Mathf.RoundToInt(k.time * frames), GUILayout.Width(44)))
                        {
                            time = k.time;
                            if (previewing) Sample();
                        }
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Remove bone", GUILayout.Width(90)))
                    {
                        Undo.RecordObject(corrector.corrections, "Remove pose fix");
                        fix.tracks.Remove(track);
                        EditorUtility.SetDirty(corrector.corrections);
                        if (previewing) Sample();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private Transform[] SelectedBones()
        {
            return Selection.transforms.Where(t => t != animator.transform && t.IsChildOf(animator.transform)).ToArray();
        }

        private void KeyBones(Transform[] keyBones, bool neutral)
        {
            PoseCorrectionSet set = corrector.corrections;
            Undo.RecordObject(set, neutral ? "Neutral pose key" : "Pose key");
            PoseCorrectionSet.ClipFix fix = set.Find(Clip, true);
            foreach (Transform bone in keyBones)
            {
                if (bone == null || !baseline.TryGetValue(bone, out Quaternion clipLocal)) continue;
                if (!clipBones.Contains(bone.name))
                {
                    // nothing resets a bone the clip does not key, so an offset on it would pile up every frame
                    Debug.LogWarning("Pose Fix: " + bone.name + " is not animated by " + Clip.name + ", not keyed.");
                    continue;
                }
                // a hand / foot is re-attached to its forearm / shin first, so its offset is from that carried rotation
                bool holder = TryCarriedLocal(bone, out Quaternion carried);
                Quaternion offset = neutral ? Quaternion.identity : Quaternion.Inverse(holder ? carried : clipLocal) * bone.localRotation;
                Vector3 move = Vector3.zero;
                if (!neutral && !holder && baselinePosition.TryGetValue(bone, out Vector3 clipPos))
                {
                    move = bone.localPosition - clipPos;
                    if (move.sqrMagnitude < 1e-10f) move = Vector3.zero;
                }
                fix.Track(bone.name, true).SetKey(time, offset, move, SameKeyTime);
            }
            EditorUtility.SetDirty(set);
            Sample();
        }

        /// <summary>The local rotation PoseCorrector gives a hand / foot IK bone by carrying it on its limb.</summary>
        private bool TryCarriedLocal(Transform bone, out Quaternion carried)
        {
            carried = Quaternion.identity;
            string endName = PoseCorrector.HolderLimbEnd(bone.name);
            Transform end = endName != null && rig != null ? rig.Bone(endName) : null;
            if (end == null || !holderRelation.TryGetValue(bone, out Quaternion relation)) return false;
            Quaternion world = end.rotation * relation;
            carried = bone.parent != null ? Quaternion.Inverse(bone.parent.rotation) * world : world;
            return true;
        }

        private static Color JointColor(Transform joint)
        {
            if (joint.name.EndsWith(".L")) return new Color(0.3f, 0.75f, 1f, 0.75f);
            if (joint.name.EndsWith(".R")) return new Color(1f, 0.45f, 0.4f, 0.75f);
            return new Color(1f, 0.85f, 0.2f, 0.75f);
        }

        private void OnSceneGUI(SceneView view)
        {
            if (!previewing || !showHandles || rig == null || !rig.Valid || EditorApplication.isPlaying) return;

            // skeleton lines so the points read as a body
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            foreach (var chain in new[]
            {
                new[] { "Spine", "Spine.001", "Spine.002", "Spine.003", "Spine.004", "Head" },
                new[] { "Spine.003", "Upper_arm.L", "Forearm.L", "Hand.IK.L" }, new[] { "Spine.003", "Upper_arm.R", "Forearm.R", "Hand.IK.R" },
                new[] { "Spine", "Hip.L", "Leg.L", "Foot.IK.L" }, new[] { "Spine", "Hip.R", "Leg.R", "Foot.IK.R" }
            })
            {
                for (int i = 1; i < chain.Length; i++)
                {
                    Transform a = rig.Bone(chain[i - 1]), b = rig.Bone(chain[i]);
                    if (a != null && b != null) Handles.DrawLine(a.position, b.position, 2f);
                }
            }

            Event e = Event.current;
            bool anySelected = selectedPart >= 0 || selectedJoint != null;
            if (e.type == EventType.KeyDown && anySelected && !e.alt && !e.control && !e.shift && !e.command)
            {
                if (e.keyCode == KeyCode.W) { rotateMode = false; e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.E) { rotateMode = true; e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.Escape) { selectedPart = -1; selectedJoint = null; e.Use(); Repaint(); }
            }
            Tools.hidden = selectedPart >= 0 || selectedJoint != null;   // the point's gizmo replaces Unity's own

            // small joint points first, so the big anatomy points sit on top
            foreach (Transform joint in rig.Joints(showJoints, showFingers))
            {
                Vector3 pos = joint.position;
                float size = HandleUtility.GetHandleSize(pos) * (joint.name.StartsWith("f_") || joint.name.StartsWith("thumb") ? 0.025f : 0.04f);
                Handles.color = joint == selectedJoint ? Color.white : JointColor(joint);
                if (joint == selectedJoint)
                {
                    if (e.type == EventType.Repaint) Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
                    continue;
                }
                if (Handles.Button(pos, Quaternion.identity, size, size, Handles.SphereHandleCap))
                {
                    selectedJoint = joint;
                    selectedPart = -1;
                    Repaint();
                }
            }

            for (int i = 0; i < HandleParts.Length; i++)
            {
                var h = HandleParts[i];
                Vector3 pos = rig.HandlePosition(h.part);
                float size = HandleUtility.GetHandleSize(pos) * h.size;
                Handles.color = i == selectedPart ? Color.white : h.color;
                if (i == selectedPart)
                {
                    if (e.type == EventType.Repaint) Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
                    continue;
                }
                if (Handles.Button(pos, Quaternion.identity, size, size, Handles.SphereHandleCap))
                {
                    selectedPart = i;
                    selectedJoint = null;
                    Repaint();
                }
            }

            bool local = Tools.pivotRotation == PivotRotation.Local;
            if (selectedPart >= 0)
            {
                PoseFixRig.Part part = HandleParts[selectedPart].part;
                Vector3 pos = rig.HandlePosition(part);
                Quaternion frame = local ? rig.HandleRotation(part) : Quaternion.identity;
                EditorGUI.BeginChangeCheck();
                if (rotateMode)
                {
                    Quaternion turned = Handles.RotationHandle(frame, pos);
                    if (EditorGUI.EndChangeCheck())
                    {
                        rig.Rotate(part, turned * Quaternion.Inverse(frame), edited);
                        Repaint();
                    }
                }
                else
                {
                    Vector3 moved = Handles.PositionHandle(pos, frame);
                    if (EditorGUI.EndChangeCheck())
                    {
                        rig.Drag(part, moved, edited);
                        Repaint();
                    }
                }
            }
            else if (selectedJoint != null)
            {
                Vector3 pos = selectedJoint.position;
                Quaternion frame = local ? selectedJoint.rotation : Quaternion.identity;
                EditorGUI.BeginChangeCheck();
                if (rotateMode || !rig.CanMoveJoint(selectedJoint))
                {
                    Quaternion turned = Handles.RotationHandle(frame, pos);
                    if (EditorGUI.EndChangeCheck())
                    {
                        rig.RotateJoint(selectedJoint, turned * Quaternion.Inverse(frame), edited);
                        Repaint();
                    }
                }
                else
                {
                    Vector3 moved = Handles.PositionHandle(pos, frame);
                    if (EditorGUI.EndChangeCheck())
                    {
                        rig.MoveJoint(selectedJoint, moved, edited);
                        Repaint();
                    }
                }
            }

            // a drag just ended (the handle releases the mouse; its MouseUp event is already used by then)
            int hot = GUIUtility.hotControl;
            if (autoKey && lastHotControl != 0 && hot == 0 && edited.Count > 0)
                KeyBones(edited.ToArray(), false);
            lastHotControl = hot;
        }

        private void DeleteKeys(Transform[] selected)
        {
            PoseCorrectionSet set = corrector.corrections;
            PoseCorrectionSet.ClipFix fix = set.Find(Clip, false);
            if (fix == null) return;
            Undo.RecordObject(set, "Delete pose key");
            foreach (Transform bone in selected)
            {
                PoseCorrectionSet.BoneTrack track = fix.Track(bone.name, false);
                if (track == null) continue;
                track.RemoveKey(time, SameKeyTime);
                if (track.keys.Count == 0) fix.tracks.Remove(track);
            }
            EditorUtility.SetDirty(set);
            Sample();
        }

        private float PrevKeyTime()
        {
            PoseCorrectionSet.ClipFix fix = corrector != null && corrector.corrections != null ? corrector.corrections.Find(Clip, false) : null;
            if (fix == null) return time;
            float best = -1f;
            foreach (var k in fix.tracks.SelectMany(t => t.keys))
                if (k.time < time - SameKeyTime && k.time > best) best = k.time;
            return best >= 0f ? best : time;
        }

        private float NextKeyTime()
        {
            PoseCorrectionSet.ClipFix fix = corrector != null && corrector.corrections != null ? corrector.corrections.Find(Clip, false) : null;
            if (fix == null) return time;
            float best = 2f;
            foreach (var k in fix.tracks.SelectMany(t => t.keys))
                if (k.time > time + SameKeyTime && k.time < best) best = k.time;
            return best <= 1f ? best : time;
        }

        private void StartPreview()
        {
            if (animator == null) return;
            rest.Clear();
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
                rest[t] = (t.localPosition, t.localRotation);
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            previewing = true;
            Sample();
        }

        private void StopPreview()
        {
            if (previewing && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (previewing) RestoreRest();
            previewing = false;
            baseline.Clear();
            baselinePosition.Clear();
            holderRelation.Clear();
            edited.Clear();
            rig = null;
            selectedPart = -1;
            selectedJoint = null;
            Tools.hidden = false;
            SceneView.RepaintAll();
        }

        /// <summary>Poses the character at the clip time; remembers the clip's own rotations, then shows the fixes.</summary>
        private void Sample()
        {
            AnimationClip clip = Clip;
            if (!previewing || clip == null || animator == null) return;
            RestoreRest();
            clipBones.Clear();
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                clipBones.Add(b.path.Substring(b.path.LastIndexOf('/') + 1));
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, time * clip.length);
            AnimationMode.EndSampling();
            baseline.Clear();
            baselinePosition.Clear();
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                baseline[t] = t.localRotation;
                baselinePosition[t] = t.localPosition;
            }
            rig = new PoseFixRig(animator.transform, corrector.transform);
            holderRelation.Clear();
            foreach (string holder in new[] { "Hand.IK.L", "Hand.IK.R", "Foot.IK.L", "Foot.IK.R" })
            {
                Transform h = rig.Bone(holder), end = rig.Bone(PoseCorrector.HolderLimbEnd(holder));
                if (h != null && end != null) holderRelation[h] = Quaternion.Inverse(end.rotation) * h.rotation;
            }
            corrector.ClearCache();
            corrector.Apply(clip, time, 1f);
            edited.Clear();
            SceneView.RepaintAll();
        }

        private void RestoreRest()
        {
            foreach (var kv in rest)
                if (kv.Key != null) kv.Key.SetLocalPositionAndRotation(kv.Value.position, kv.Value.rotation);
        }

        private void AddCorrector()
        {
            LocomotionMotor motor = FindAnyObjectByType<LocomotionMotor>();
            if (motor == null) return;
            corrector = Undo.AddComponent<PoseCorrector>(motor.gameObject);
            CreateAsset();
            EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
            Find();
        }

        private void CreateAsset()
        {
            PoseCorrectionSet set = AssetDatabase.LoadAssetAtPath<PoseCorrectionSet>(DefaultAssetPath);
            if (set == null)
            {
                set = CreateInstance<PoseCorrectionSet>();
                AssetDatabase.CreateAsset(set, DefaultAssetPath);
                AssetDatabase.SaveAssets();
            }
            Undo.RecordObject(corrector, "Assign pose corrections");
            corrector.corrections = set;
            EditorUtility.SetDirty(corrector);
            EditorSceneManager.MarkSceneDirty(corrector.gameObject.scene);
        }
    }
}
