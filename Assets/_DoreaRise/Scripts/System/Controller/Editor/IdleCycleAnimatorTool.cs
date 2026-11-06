using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace DoreaRise.System.Controller.Editor
{
    public class IdleCycleAnimatorTool : EditorWindow
    {
        #region [Variables/Fields]
        private AnimationClip idleIn;
        private AnimationClip idleStand;
        private AnimationClip idleOut;
        private AnimationClip walk;
        private AnimationClip dodge;

        private string savePath = "Assets/_DoreaRise/Animations/CharacterAnimator.controller";
        private Vector2 scrollPos;
        #endregion

        #region [Unity Methods]
        [MenuItem("DoreaRise/Idle Cycle Animator Builder")]
        public static void ShowWindow()
        {
            var window = GetWindow<IdleCycleAnimatorTool>("Idle Cycle Builder");
            window.minSize = new Vector2(400, 520);
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            DrawHeader();
            DrawClipSlots();
            DrawSavePath();
            DrawPreview();
            DrawBuildButton();

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region [Private Methods]
        private void DrawHeader()
        {
            EditorGUILayout.Space(10);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("🎬 Idle Cycle Animator Builder", headerStyle);

            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "ลาก Animation Clip ใส่ช่องด้านล่าง แล้วกด Build\n" +
                "ระบบจะสร้าง Animator Controller พร้อม Idle Cycle ให้อัตโนมัติ",
                MessageType.Info);

            EditorGUILayout.Space(10);
        }

        private void DrawClipSlots()
        {
            EditorGUILayout.LabelField("Idle Cycle Clips (จำเป็น)", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            idleIn = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("Idle In", "แอนิเมชันเข้า idle"),
                idleIn, typeof(AnimationClip), false);

            idleStand = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("Idle Stand (Loop)", "แอนิเมชันยืน idle วนลูป"),
                idleStand, typeof(AnimationClip), false);

            idleOut = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("Idle Out", "แอนิเมชันออกจาก idle"),
                idleOut, typeof(AnimationClip), false);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Movement Clips (ไม่ใส่ก็ได้)", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            walk = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("Walk / Run", "แอนิเมชันเดิน"),
                walk, typeof(AnimationClip), false);

            dodge = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("Dodge", "แอนิเมชัน dodge"),
                dodge, typeof(AnimationClip), false);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);
        }

        private void DrawSavePath()
        {
            EditorGUILayout.LabelField("Save Path", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            savePath = EditorGUILayout.TextField(savePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save Animator Controller",
                    "CharacterAnimator",
                    "controller",
                    "เลือกที่บันทึก Animator Controller");

                if (!string.IsNullOrEmpty(path))
                    savePath = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview — สิ่งที่จะสร้าง", EditorStyles.boldLabel);

            var boxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
            EditorGUILayout.BeginVertical(boxStyle);

            DrawPreviewItem("Parameters", "Speed (Float), IdleCycleExit (Bool), Dodge (Trigger)");
            EditorGUILayout.Space(4);
            DrawPreviewItem("Idle Cycle", "Idle_In → Idle_Stand (loop 2-4 รอบ) → Idle_Out → วนกลับ");
            EditorGUILayout.Space(4);

            if (walk != null)
                DrawPreviewItem("Walk", "Speed > 0.1 → Walk  |  Speed < 0.1 → IdleCycle");
            else
                DrawPreviewItem("Walk", "⚠ ไม่ได้ใส่ — จะข้าม");

            EditorGUILayout.Space(4);

            if (dodge != null)
                DrawPreviewItem("Dodge", "Any State → Dodge (trigger) → กลับ idle/walk");
            else
                DrawPreviewItem("Dodge", "⚠ ไม่ได้ใส่ — จะข้าม");

            EditorGUILayout.Space(4);
            DrawPreviewItem("Behaviour", "IdleCycleBehaviour จะถูก Add ที่ Idle_Stand อัตโนมัติ");

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
        }

        private void DrawPreviewItem(string label, string detail)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"▸ {label}", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBuildButton()
        {
            bool canBuild = idleIn != null && idleStand != null && idleOut != null;

            if (!canBuild)
            {
                EditorGUILayout.HelpBox("ใส่ Idle In / Stand / Out ให้ครบก่อนกด Build", MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(!canBuild);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 40
            };

            if (GUILayout.Button("🔨  Build Animator Controller", buttonStyle))
            {
                BuildAnimatorController();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void BuildAnimatorController()
        {
            string directory = global::System.IO.Path.GetDirectoryName(savePath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                string[] folders = directory.Replace("\\", "/").Split('/');
                string current = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string next = current + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, folders[i]);
                    current = next;
                }
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(savePath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IdleCycleExit", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);

            var rootStateMachine = controller.layers[0].stateMachine;

            var idleCycleSM = rootStateMachine.AddStateMachine("IdleCycle", new Vector3(250, 0, 0));

            var stateIn = idleCycleSM.AddState("Idle_In", new Vector3(0, 0, 0));
            stateIn.motion = idleIn;

            var stateStand = idleCycleSM.AddState("Idle_Stand", new Vector3(250, 0, 0));
            stateStand.motion = idleStand;

            var behaviour = stateStand.AddStateMachineBehaviour<IdleCycleBehaviour>();

            var stateOut = idleCycleSM.AddState("Idle_Out", new Vector3(500, 0, 0));
            stateOut.motion = idleOut;

            idleCycleSM.defaultState = stateIn;

            var t1 = stateIn.AddTransition(stateStand);
            t1.hasExitTime = true;
            t1.exitTime = 1f;
            t1.duration = 0.1f;
            t1.hasFixedDuration = true;

            var t2 = stateStand.AddTransition(stateOut);
            t2.hasExitTime = false;
            t2.duration = 0.15f;
            t2.hasFixedDuration = true;
            t2.AddCondition(AnimatorConditionMode.If, 0, "IdleCycleExit");

            var t3 = stateOut.AddTransition(stateIn);
            t3.hasExitTime = true;
            t3.exitTime = 1f;
            t3.duration = 0.1f;
            t3.hasFixedDuration = true;

            if (walk != null)
            {
                var walkState = rootStateMachine.AddState("Walk", new Vector3(250, 100, 0));
                walkState.motion = walk;

                var toWalk = rootStateMachine.AddAnyStateTransition(walkState);
                toWalk.hasExitTime = false;
                toWalk.duration = 0.15f;
                toWalk.hasFixedDuration = true;
                toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                toWalk.canTransitionToSelf = false;

                var exitFromWalk = walkState.AddTransition(idleCycleSM);
                exitFromWalk.hasExitTime = false;
                exitFromWalk.duration = 0.2f;
                exitFromWalk.hasFixedDuration = true;
                exitFromWalk.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

                var exitIdleToWalk = rootStateMachine.AddAnyStateTransition(walkState);
                exitIdleToWalk.hasExitTime = false;
                exitIdleToWalk.duration = 0.15f;
                exitIdleToWalk.hasFixedDuration = true;
                exitIdleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                exitIdleToWalk.canTransitionToSelf = false;
            }

            if (dodge != null)
            {
                var dodgeState = rootStateMachine.AddState("Dodge", new Vector3(500, 50, 0));
                dodgeState.motion = dodge;

                var toDodge = rootStateMachine.AddAnyStateTransition(dodgeState);
                toDodge.hasExitTime = false;
                toDodge.duration = 0.05f;
                toDodge.hasFixedDuration = true;
                toDodge.AddCondition(AnimatorConditionMode.If, 0, "Dodge");
                toDodge.canTransitionToSelf = false;

                var dodgeToIdle = dodgeState.AddTransition(idleCycleSM);
                dodgeToIdle.hasExitTime = true;
                dodgeToIdle.exitTime = 0.9f;
                dodgeToIdle.duration = 0.15f;
                dodgeToIdle.hasFixedDuration = true;
                dodgeToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

                if (walk != null)
                {
                    var walkState = FindState(rootStateMachine, "Walk");
                    if (walkState != null)
                    {
                        var dodgeToWalk = dodgeState.AddTransition(walkState);
                        dodgeToWalk.hasExitTime = true;
                        dodgeToWalk.exitTime = 0.9f;
                        dodgeToWalk.duration = 0.15f;
                        dodgeToWalk.hasFixedDuration = true;
                        dodgeToWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "✅ สร้างเสร็จ!",
                $"Animator Controller ถูกสร้างที่:\n{savePath}\n\n" +
                "ลาก Controller ไปใส่ Animator component บนตัวละครได้เลย",
                "OK");

            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        private AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var child in sm.states)
            {
                if (child.state.name == name)
                    return child.state;
            }
            return null;
        }
        #endregion
    }
}
