using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Dorea.CharacterTailComponent;

namespace Dorea.CharacterTailComponent.Editor
{
    [CustomEditor(typeof(CharacterTail))]
    public class CharacterTailEditor : UnityEditor.Editor
    {
        #region [Variables/Fields]
        private CharacterTail tail;
        private int selectedPointIndex = -1;
        private static readonly float handleSize = 0.06f;

        private enum EditMode
        {
            Spline,
            Weight
        }
        private EditMode currentEditMode = EditMode.Spline;
        private int selectedBoneIndex = -1;
        private float setAllWeightValue = 1f;

        private static bool showBoneChain = true;
        private static bool showShape = true;
        private static bool showJiggle = true;
        private static bool showWagging = true;
        private static bool showGizmosSection = false;
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            tail = (CharacterTail)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. Bone Chain Section
            showBoneChain = EditorGUILayout.Foldout(showBoneChain, "Bone Chain", true, EditorStyles.foldoutHeader);
            if (showBoneChain)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rootBone"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);

            // 2. Shape Section
            showShape = EditorGUILayout.Foldout(showShape, "Shape (Spline Control Points)", true, EditorStyles.foldoutHeader);
            if (showShape)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controlPoints"), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);

            // 3. Jiggle Dynamics Section
            showJiggle = EditorGUILayout.Foldout(showJiggle, "Jiggle Dynamics", true, EditorStyles.foldoutHeader);
            if (showJiggle)
            {
                EditorGUI.indentLevel++;
                SerializedProperty isJiggleProp = serializedObject.FindProperty("isJiggle");
                EditorGUILayout.PropertyField(isJiggleProp);
                if (isJiggleProp.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("jiggleStiffness"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("jiggleDamping"));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);

            // 4. Wagging / Swishing Section
            showWagging = EditorGUILayout.Foldout(showWagging, "Wagging / Swishing", true, EditorStyles.foldoutHeader);
            if (showWagging)
            {
                EditorGUI.indentLevel++;
                SerializedProperty isWaggingProp = serializedObject.FindProperty("isWagging");
                EditorGUILayout.PropertyField(isWaggingProp);
                if (isWaggingProp.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("waggingAxis"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("waggingSpeed"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("waggingAmplitude"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("waggingFrequency"));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);

            // 5. Gizmos Section
            showGizmosSection = EditorGUILayout.Foldout(showGizmosSection, "Gizmos", true, EditorStyles.foldoutHeader);
            if (showGizmosSection)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showGizmos"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("splineColor"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("boneColor"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controlPointColor"));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);
            currentEditMode = (EditMode)GUILayout.Toolbar((int)currentEditMode, new string[] { "Edit Spline", "Edit Weight" });
            EditorGUILayout.Space(10);

            if (currentEditMode == EditMode.Spline)
            {
                EditorGUILayout.LabelField("Control Point Tools", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Add Point"))
                {
                    Undo.RecordObject(tail, "Add Control Point");
                    tail.AddControlPoint();
                    EditorUtility.SetDirty(tail);
                }

                if (GUILayout.Button("Remove Last"))
                {
                    Undo.RecordObject(tail, "Remove Control Point");
                    tail.RemoveLastControlPoint();
                    EditorUtility.SetDirty(tail);
                }

                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Reset to Default Shape"))
                {
                    Undo.RecordObject(tail, "Reset Control Points");
                    tail.ResetControlPoints();
                    EditorUtility.SetDirty(tail);
                }

                if (GUILayout.Button("Rebuild Bone Chain"))
                {
                    Undo.RecordObject(tail, "Rebuild Bone Chain");
                    tail.RebuildBoneChain();
                    EditorUtility.SetDirty(tail);
                }

                if (GUILayout.Button("Refresh Tail"))
                {
                    Undo.RecordObject(tail, "Refresh Tail");
                    tail.Refresh();
                    EditorUtility.SetDirty(tail);
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Control Points define the rest shape of the tail.\n" +
                    "Drag the handles in Scene View to sculpt the tail curve.\n" +
                    "The physics simulation will try to follow this shape.",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.LabelField("Tail Bone Weight Tools", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                setAllWeightValue = EditorGUILayout.Slider("Weight Value", setAllWeightValue, 0f, 1f);
                if (GUILayout.Button("Set All", GUILayout.Width(70)))
                {
                    Undo.RecordObject(tail, "Set All Bone Weights");
                    foreach (var bone in tail.Bones)
                    {
                        bone.weight = setAllWeightValue;
                    }
                    EditorUtility.SetDirty(tail);
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Root -> Tip Gradient"))
                {
                    Undo.RecordObject(tail, "Root -> Tip Weight Gradient");
                    int count = tail.Bones.Count;
                    for (int i = 0; i < count; i++)
                    {
                        tail.Bones[i].weight = (float)i / Mathf.Max(1, count - 1);
                    }
                    EditorUtility.SetDirty(tail);
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Tip -> Root Gradient"))
                {
                    Undo.RecordObject(tail, "Tip -> Root Weight Gradient");
                    int count = tail.Bones.Count;
                    for (int i = 0; i < count; i++)
                    {
                        tail.Bones[i].weight = 1f - ((float)i / Mathf.Max(1, count - 1));
                    }
                    EditorUtility.SetDirty(tail);
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Bones List (Weight 0 = Static, 1 = Physics)", EditorStyles.boldLabel);

                if (tail.Bones == null || tail.Bones.Count == 0)
                {
                    EditorGUILayout.HelpBox("No bones found. Please Rebuild Bone Chain.", MessageType.Warning);
                }
                else
                {
                    for (int i = 0; i < tail.Bones.Count; i++)
                    {
                        var bone = tail.Bones[i];
                        string boneName = bone.transform != null ? bone.transform.name : "Tip (Virtual)";

                        GUIStyle rowStyle = new GUIStyle(GUI.skin.box);
                        if (i == selectedBoneIndex)
                        {
                            GUI.backgroundColor = new Color(0.7f, 0.9f, 1f);
                        }

                        EditorGUILayout.BeginVertical(rowStyle);
                        GUI.backgroundColor = Color.white;

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"[{i}] {boneName}", EditorStyles.miniLabel, GUILayout.Width(150));

                        EditorGUI.BeginChangeCheck();
                        float newWeight = EditorGUILayout.Slider(bone.weight, 0f, 1f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(tail, $"Modify Bone {i} Weight");
                            bone.weight = newWeight;
                            EditorUtility.SetDirty(tail);
                            SceneView.RepaintAll();
                        }

                        if (GUILayout.Button("Select", GUILayout.Width(50)))
                        {
                            selectedBoneIndex = i;
                            SceneView.RepaintAll();
                        }
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                    }
                }
            }
        }

        private void OnSceneGUI()
        {
            if (tail == null) return;

            if (currentEditMode == EditMode.Spline)
            {
                List<Vector3> points = tail.ControlPoints;
                if (points == null || points.Count == 0) return;

                Undo.RecordObject(tail, "Move Tail Control Point");
                bool changed = false;

                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 worldPos = tail.transform.TransformPoint(points[i]);
                    float size = HandleUtility.GetHandleSize(worldPos) * handleSize;

                    Handles.color = (i == selectedPointIndex) ? Color.white : tail.ControlPointColor;
                    if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                    {
                        selectedPointIndex = i;
                        Repaint();
                    }

                    EditorGUI.BeginChangeCheck();

                    Vector3 newWorldPos;
                    if (i == 0 || i == points.Count - 1)
                    {
                        newWorldPos = Handles.PositionHandle(worldPos, tail.transform.rotation);
                    }
                    else
                    {
                        newWorldPos = Handles.FreeMoveHandle(
                            worldPos,
                            size * 2f,
                            Vector3.one * 0.01f,
                            Handles.SphereHandleCap
                        );
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        points[i] = tail.transform.InverseTransformPoint(newWorldPos);
                        changed = true;
                    }

                    Handles.color = Color.white;
                    string label = i == 0 ? "Root" : i == points.Count - 1 ? "Tip" : "P" + i;
                    Handles.Label(worldPos + Vector3.up * (size * 3f), label, EditorStyles.boldLabel);
                }

                Handles.color = tail.SplineColor;
                Vector3 prev = CatmullRomSpline.EvaluateWorld(points, tail.transform, 0f);
                int segments = 60;
                for (int s = 1; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    Vector3 curr = CatmullRomSpline.EvaluateWorld(points, tail.transform, t);
                    Handles.DrawLine(prev, curr, 2f);
                    prev = curr;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(tail);
                }
            }
            else
            {
                List<TailBone> bones = tail.Bones;
                if (bones == null || bones.Count == 0) return;

                bool changed = false;

                // 1. Draw connection lines
                Handles.color = Color.gray;
                for (int i = 1; i < bones.Count; i++)
                {
                    Handles.DrawLine(bones[i - 1].simulatedPosition, bones[i].simulatedPosition, 2f);
                }

                // 2. Draw handles for each bone
                for (int i = 0; i < bones.Count; i++)
                {
                    TailBone bone = bones[i];
                    Vector3 worldPos = bone.simulatedPosition;
                    float size = HandleUtility.GetHandleSize(worldPos) * handleSize * 1.5f;

                    // Color based on weight: Red (0) to Green (1)
                    Color color = Color.Lerp(Color.red, Color.green, bone.weight);
                    if (i == selectedBoneIndex)
                    {
                        color = Color.white;
                    }
                    Handles.color = color;

                    if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                    {
                        selectedBoneIndex = i;
                        Repaint();
                    }

                    string label = $"[{i}] W: {bone.weight:F2}";
                    Handles.color = Color.white;
                    Handles.Label(worldPos + Vector3.up * (size * 1.8f), label, EditorStyles.boldLabel);
                }

                // 3. Draw Scene View Overlay Window
                if (selectedBoneIndex >= 0 && selectedBoneIndex < bones.Count)
                {
                    TailBone selBone = bones[selectedBoneIndex];
                    string boneName = selBone.transform != null ? selBone.transform.name : "Tip (Virtual)";

                    Handles.BeginGUI();
                    GUILayout.BeginArea(new Rect(10, 10, 260, 100), "Tail Weight Editor", GUI.skin.window);

                    GUILayout.Label($"Selected: [{selectedBoneIndex}] {boneName}", EditorStyles.boldLabel);

                    EditorGUI.BeginChangeCheck();
                    float newW = GUILayout.HorizontalSlider(selBone.weight, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(tail, $"Modify Bone {selectedBoneIndex} Weight");
                        selBone.weight = newW;
                        changed = true;
                        Repaint();
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Weight: {selBone.weight:F2}");
                    if (GUILayout.Button("0.0 (Static)", GUILayout.Width(75)))
                    {
                        Undo.RecordObject(tail, $"Modify Bone {selectedBoneIndex} Weight");
                        selBone.weight = 0f;
                        changed = true;
                        Repaint();
                    }
                    if (GUILayout.Button("1.0 (Phys)", GUILayout.Width(75)))
                    {
                        Undo.RecordObject(tail, $"Modify Bone {selectedBoneIndex} Weight");
                        selBone.weight = 1f;
                        changed = true;
                        Repaint();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.EndArea();
                    Handles.EndGUI();
                }

                if (changed)
                {
                    EditorUtility.SetDirty(tail);
                    SceneView.RepaintAll();
                }
            }
        }
        #endregion
    }
}
