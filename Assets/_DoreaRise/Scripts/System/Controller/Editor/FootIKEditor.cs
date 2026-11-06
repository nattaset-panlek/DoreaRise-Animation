using UnityEngine;
using UnityEditor;

namespace DoreaRise.System.Controller.Editor
{
    [CustomEditor(typeof(FootIK))]
    public class FootIKEditor : UnityEditor.Editor
    {
        #region [Variables/Fields]
        private bool showActivation = true;
        private bool showIKMode = true;
        private bool showBones = true;
        private bool showRaycast = true;
        private bool showSmoothing = false;
        private bool showKnee = false;
        private bool showStep = true;
        private bool showBody = true;
        private bool showDebug = true;
        #endregion

        #region [Unity Methods]
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Foot IK - Two-Bone Solver", headerStyle);
            EditorGUILayout.Space(5);

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("PLAY MODE - \u0e1b\u0e23\u0e31\u0e1a\u0e04\u0e48\u0e32\u0e44\u0e14\u0e49 realtime!", MessageType.Info);
                EditorGUILayout.Space(3);
            }

            showActivation = DrawSection("Activation", showActivation, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableFootIK"), new GUIContent("Enable Foot IK"));
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Active Movement States", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("activeOnWalk"), new GUIContent("Walk"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("activeOnSprint"), new GUIContent("Sprint"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("activeOnSprintFast"), new GUIContent("Sprint Fast"));
                EditorGUI.indentLevel--;
            });

            showIKMode = DrawSection("IK Mode", showIKMode, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("overrideLegAnimation"),
                    new GUIContent("Override Leg Animation", "\u0e15\u0e31\u0e14 animation \u0e02\u0e32\u0e17\u0e34\u0e49\u0e07\u0e41\u0e25\u0e30\u0e43\u0e0a\u0e49 IK \u0e41\u0e17\u0e19 100%"));

                if (!serializedObject.FindProperty("overrideLegAnimation").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("blendWeight"),
                        new GUIContent("Blend Weight", "IK weight \u0e2a\u0e39\u0e07\u0e2a\u0e38\u0e14\u0e15\u0e2d\u0e19\u0e17\u0e35\u0e48\u0e44\u0e21\u0e48 override"));
                    EditorGUI.indentLevel--;
                }

                if (Application.isPlaying)
                {
                    var ik = (FootIK)target;
                    EditorGUILayout.Space(3);
                    var style = new GUIStyle(EditorStyles.helpBox) { richText = true };
                    EditorGUILayout.LabelField(
                        $"<b>IK Weight:</b> {ik.CurrentIKWeight:F2}  |  " +
                        $"<b>Left Hit:</b> {(ik.LeftHasHit ? "Yes" : "No")}  |  " +
                        $"<b>Right Hit:</b> {(ik.RightHasHit ? "Yes" : "No")}", style);
                }
            });

            showBones = DrawSection("Bone References", showBones, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("locomotionSource"),
                    new GUIContent("Locomotion Source"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bodyBone"),
                    new GUIContent("Body Bone (optional)", "Root/Pelvis bone for body height adjustment"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Left Leg", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hipL"), new GUIContent("Hip.L (upper leg)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("legL"), new GUIContent("Leg.L (knee)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("legEndL"), new GUIContent("Leg.end.L (ankle)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("footL"), new GUIContent("Foot.L (foot)"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Right Leg", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hipR"), new GUIContent("Hip.R (upper leg)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("legR"), new GUIContent("Leg.R (knee)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("legEndR"), new GUIContent("Leg.end.R (ankle)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("footR"), new GUIContent("Foot.R (foot)"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                ValidateBoneRefs();

                if (Application.isPlaying)
                {
                    var ik = (FootIK)target;
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField(
                        $"Left  | Upper: {ik.UpperLengthL:F3}  Lower: {ik.LowerLengthL:F3}",
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(
                        $"Right | Upper: {ik.UpperLengthR:F3}  Lower: {ik.LowerLengthR:F3}",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    "Bone Hierarchy (Non-Standard):\n" +
                    "  Root\n" +
                    "  +-- Hip.L/R  -> upper leg (pivot at hip)\n" +
                    "  |   +-- Leg.L/R  -> lower leg (pivot at knee)\n" +
                    "  |       +-- Leg.end.L/R  -> ankle marker (no weight)\n" +
                    "  +-- Foot.L/R  -> disconnected foot bone\n\n" +
                    "Foot.L/R \u0e2d\u0e22\u0e39\u0e48\u0e19\u0e2d\u0e01 Hip hierarchy (\u0e16\u0e39\u0e01 disconnect \u0e15\u0e2d\u0e19 export)\n" +
                    "-> Script \u0e08\u0e30 snap Foot \u0e44\u0e1b\u0e17\u0e35\u0e48 Leg.end \u0e2d\u0e31\u0e15\u0e42\u0e19\u0e21\u0e31\u0e15\u0e34\n\n" +
                    "Knee Hint: \u0e04\u0e33\u0e19\u0e27\u0e13\u0e2d\u0e31\u0e15\u0e42\u0e19\u0e21\u0e31\u0e15\u0e34\u0e08\u0e32\u0e01 CharacterControl.forward\n" +
                    "-> \u0e44\u0e21\u0e48\u0e15\u0e49\u0e2d\u0e07\u0e25\u0e32\u0e01 Transform \u0e40\u0e1e\u0e34\u0e48\u0e21",
                    MessageType.None);
            });

            showRaycast = DrawSection("Raycast", showRaycast, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("groundLayer"), new GUIContent("Ground Layer"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("raycastDistance"), new GUIContent("Raycast Distance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("raycastOriginHeight"), new GUIContent("Ray Origin Height"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("footOffset"), new GUIContent("Foot Y Offset"));
            });

            showSmoothing = DrawSection("Smoothing", showSmoothing, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ikSmoothSpeed"), new GUIContent("IK Smooth Speed"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ikBlendSpeed"), new GUIContent("Blend In/Out Speed"));
            });

            showKnee = DrawSection("Knee Hint", showKnee, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("kneeHintOffset"),
                    new GUIContent("Forward Offset", "\u0e23\u0e30\u0e22\u0e30\u0e14\u0e49\u0e32\u0e19\u0e2b\u0e19\u0e49\u0e32\u0e08\u0e32\u0e01\u0e2b\u0e31\u0e27\u0e40\u0e02\u0e48\u0e32\u0e2a\u0e33\u0e2b\u0e23\u0e31\u0e1a pole target"));
                EditorGUILayout.HelpBox(
                    "Pole target \u0e04\u0e33\u0e19\u0e27\u0e13\u0e2d\u0e31\u0e15\u0e42\u0e19\u0e21\u0e31\u0e15\u0e34:\n" +
                    "kneeHint = Leg.position + CharacterControl.forward x offset\n\n" +
                    "\u0e04\u0e48\u0e32\u0e40\u0e22\u0e2d\u0e30 -> \u0e40\u0e02\u0e48\u0e32\u0e0a\u0e35\u0e49\u0e44\u0e1b\u0e02\u0e49\u0e32\u0e07\u0e2b\u0e19\u0e49\u0e32 (\u0e40\u0e14\u0e34\u0e19\u0e1b\u0e01\u0e15\u0e34)\n" +
                    "\u0e04\u0e48\u0e32\u0e15\u0e34\u0e14\u0e25\u0e1a -> \u0e40\u0e02\u0e48\u0e32\u0e0a\u0e35\u0e49\u0e44\u0e1b\u0e02\u0e49\u0e32\u0e07\u0e2b\u0e25\u0e31\u0e07 (hyper-extension)",
                    MessageType.None);
            });

            DrawSection("Foot Rotation Offset", true, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("footRotationOffset"),
                    new GUIContent("Offset (Euler)", "\u0e1b\u0e23\u0e31\u0e1a\u0e2d\u0e07\u0e28\u0e32\u0e40\u0e17\u0e49\u0e32\u0e41\u0e01\u0e49 bone \u0e17\u0e35\u0e48\u0e08\u0e34\u0e49\u0e21\u0e25\u0e07\u0e1e\u0e37\u0e49\u0e19"));
                EditorGUILayout.HelpBox(
                    "\u0e16\u0e49\u0e32\u0e40\u0e17\u0e49\u0e32\u0e08\u0e34\u0e49\u0e21\u0e25\u0e07\u0e1e\u0e37\u0e49\u0e19 -> \u0e1b\u0e23\u0e31\u0e1a X \u0e40\u0e1b\u0e47\u0e19\u0e1a\u0e27\u0e01 (\u0e40\u0e0a\u0e48\u0e19 X = 20~40)\n" +
                    "\u0e16\u0e49\u0e32\u0e40\u0e17\u0e49\u0e32\u0e1a\u0e34\u0e14\u0e2d\u0e2d\u0e01 -> \u0e1b\u0e23\u0e31\u0e1a X \u0e40\u0e1b\u0e47\u0e19\u0e25\u0e1a\n" +
                    "\u0e1b\u0e23\u0e31\u0e1a Y/Z \u0e2a\u0e33\u0e2b\u0e23\u0e31\u0e1a\u0e2b\u0e21\u0e38\u0e19\u0e40\u0e17\u0e49\u0e32\u0e0b\u0e49\u0e32\u0e22-\u0e02\u0e27\u0e32",
                    MessageType.None);
            });

            showStep = DrawSection("Step Height", showStep, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("stepHeight"),
                    new GUIContent("Step Height", "\u0e04\u0e27\u0e32\u0e21\u0e2a\u0e39\u0e07\u0e17\u0e35\u0e48\u0e22\u0e01\u0e40\u0e17\u0e49\u0e32\u0e40\u0e1e\u0e34\u0e48\u0e21\u0e15\u0e2d\u0e19 swing phase"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("stepSwingThreshold"),
                    new GUIContent("Swing Threshold", "\u0e04\u0e27\u0e32\u0e21\u0e2a\u0e39\u0e07\u0e02\u0e2d\u0e07\u0e02\u0e49\u0e2d\u0e40\u0e17\u0e49\u0e32\u0e40\u0e2b\u0e19\u0e37\u0e2d\u0e1e\u0e37\u0e49\u0e19\u0e17\u0e35\u0e48\u0e16\u0e37\u0e2d\u0e27\u0e48\u0e32\u0e40\u0e23\u0e34\u0e48\u0e21 swing"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("stepPeakHeight"),
                    new GUIContent("Peak Height", "\u0e04\u0e27\u0e32\u0e21\u0e2a\u0e39\u0e07\u0e02\u0e2d\u0e07\u0e02\u0e49\u0e2d\u0e40\u0e17\u0e49\u0e32\u0e17\u0e35\u0e48 lift \u0e2a\u0e39\u0e07\u0e2a\u0e38\u0e14"));

                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    "Step Height \u0e22\u0e01\u0e40\u0e17\u0e49\u0e32\u0e40\u0e1e\u0e34\u0e48\u0e21\u0e15\u0e2d\u0e19 swing phase (\u0e40\u0e17\u0e49\u0e32\u0e25\u0e2d\u0e22)\n" +
                    "\u0e1b\u0e49\u0e2d\u0e07\u0e01\u0e31\u0e19\u0e40\u0e17\u0e49\u0e32\u0e25\u0e32\u0e01\u0e1e\u0e37\u0e49\u0e19\u0e15\u0e2d\u0e19\u0e01\u0e49\u0e32\u0e27\u0e02\u0e32\n\n" +
                    "Swing \u0e15\u0e23\u0e27\u0e08\u0e08\u0e31\u0e1a\u0e08\u0e32\u0e01: animated ankle Y - ground Y\n" +
                    "Lift curve: sin(t * PI) -> \u0e42\u0e04\u0e49\u0e07\u0e1e\u0e32\u0e23\u0e32\u0e42\u0e1a\u0e25\u0e32",
                    MessageType.None);
            });

            showBody = DrawSection("Body Adjustment", showBody, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableBodyAdjust"), new GUIContent("Enable"));

                if (serializedObject.FindProperty("enableBodyAdjust").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bodyOffsetSmooth"), new GUIContent("Smooth Speed"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("maxBodyOffset"), new GUIContent("Max Offset"));
                    EditorGUI.indentLevel--;

                    if (serializedObject.FindProperty("bodyBone").objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Body Bone \u0e22\u0e31\u0e07\u0e44\u0e21\u0e48\u0e44\u0e14\u0e49 assign!\n\u0e25\u0e32\u0e01 Root/Pelvis bone \u0e17\u0e35\u0e48\u0e2d\u0e22\u0e39\u0e48\u0e40\u0e2b\u0e19\u0e37\u0e2d Hip.L/R \u0e43\u0e19 hierarchy",
                            MessageType.Warning);
                    }
                }
            });

            showDebug = DrawSection("Debug", showDebug, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showGizmos"), new GUIContent("Show Gizmos"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showFootTrail"), new GUIContent("Show Foot Trail"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showVelocityArrow"), new GUIContent("Show Velocity Arrow"));

                if (serializedObject.FindProperty("showGizmos").boolValue)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox(
                        "Gizmos Legend:\n" +
                        "Yellow lines = Bone chain (Hip -> Leg -> Leg.end)\n" +
                        "Green joints = Left leg bones\n" +
                        "Blue joints = Right leg bones\n" +
                        "Magenta = Foot <-> Leg.end snap\n" +
                        "Red sphere = Knee hint (pole target)\n" +
                        "White line = Raycast\n" +
                        "Yellow ray = Surface normal\n" +
                        "Green/Blue trail = Foot trajectory arc\n" +
                        "Cyan arrow = Foot velocity direction\n" +
                        "Red target = No ground hit",
                        MessageType.None);
                }
            });

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }
        #endregion

        #region [Private Methods]
        private void ValidateBoneRefs()
        {
            string[] requiredBones = { "hipL", "legL", "legEndL", "footL", "hipR", "legR", "legEndR", "footR" };
            bool anyMissing = false;

            foreach (var name in requiredBones)
            {
                if (serializedObject.FindProperty(name).objectReferenceValue == null)
                {
                    anyMissing = true;
                    break;
                }
            }

            if (anyMissing)
            {
                EditorGUILayout.HelpBox(
                    "\u0e22\u0e31\u0e07 assign bone \u0e44\u0e21\u0e48\u0e04\u0e23\u0e1a! IK \u0e08\u0e30\u0e44\u0e21\u0e48\u0e17\u0e33\u0e07\u0e32\u0e19\u0e08\u0e19\u0e01\u0e27\u0e48\u0e32\u0e08\u0e30\u0e43\u0e2a\u0e48\u0e04\u0e23\u0e1a 8 bones",
                    MessageType.Warning);
            }

            if (serializedObject.FindProperty("locomotionSource").objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "CharacterControl \u0e44\u0e21\u0e48\u0e44\u0e14\u0e49 assign \u2014 \u0e08\u0e30 auto-find \u0e08\u0e32\u0e01 parent \u0e15\u0e2d\u0e19 runtime",
                    MessageType.Warning);
            }

            ValidateParentChild("hipL", "legL", "Leg.L", "Hip.L");
            ValidateParentChild("legL", "legEndL", "Leg.end.L", "Leg.L");
            ValidateParentChild("hipR", "legR", "Leg.R", "Hip.R");
            ValidateParentChild("legR", "legEndR", "Leg.end.R", "Leg.R");
        }

        private void ValidateParentChild(string parentProp, string childProp, string childName, string parentName)
        {
            var parent = serializedObject.FindProperty(parentProp).objectReferenceValue as Transform;
            var child = serializedObject.FindProperty(childProp).objectReferenceValue as Transform;

            if (parent != null && child != null && child.parent != parent)
            {
                EditorGUILayout.HelpBox(
                    $"{childName} \u0e44\u0e21\u0e48\u0e44\u0e14\u0e49\u0e40\u0e1b\u0e47\u0e19 child \u0e02\u0e2d\u0e07 {parentName} \u2014 \u0e15\u0e23\u0e27\u0e08\u0e2a\u0e2d\u0e1a hierarchy \u0e2d\u0e35\u0e01\u0e04\u0e23\u0e31\u0e49\u0e07",
                    MessageType.Warning);
            }
        }

        private bool DrawSection(string title, bool isOpen, global::System.Action drawContent)
        {
            EditorGUILayout.Space(3);
            isOpen = EditorGUILayout.BeginFoldoutHeaderGroup(isOpen, title);
            if (isOpen)
            {
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            return isOpen;
        }
        #endregion
    }
}
