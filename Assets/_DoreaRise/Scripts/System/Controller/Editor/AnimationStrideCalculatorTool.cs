using UnityEditor;
using UnityEngine;

namespace DoreaRise.System.Controller.EditorTools
{
    public class AnimationStrideCalculatorTool : EditorWindow
    {
        #region [Variables/Fields]
        [Header("Target Character")]
        [SerializeField] private GameObject characterPrefab;

        [Header("Detection Mode")]
        [SerializeField] private bool useManualFrames = false;

        [Header("Animation Clips & Manual Frames")]
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private int walkLeftFrame = 0;
        [SerializeField] private int walkRightFrame = 15;

        [SerializeField] private AnimationClip sprintClip;
        [SerializeField] private int sprintLeftFrame = 0;
        [SerializeField] private int sprintRightFrame = 12;

        [SerializeField] private AnimationClip sprintFastClip;
        [SerializeField] private int sprintFastLeftFrame = 0;
        [SerializeField] private int sprintFastRightFrame = 10;

        [Header("Bones Setup")]
        [SerializeField] private HumanBodyBones leftFootBone = HumanBodyBones.LeftFoot;
        [SerializeField] private HumanBodyBones rightFootBone = HumanBodyBones.RightFoot;
        [SerializeField] private Transform customLeftFoot;
        [SerializeField] private Transform customRightFoot;

        // Calculated Results
        private float walkIdealSpeed = 0f;
        private float sprintIdealSpeed = 0f;
        private float sprintFastIdealSpeed = 0f;

        private float walkStrideDist = 0f;
        private float sprintStrideDist = 0f;
        private float sprintFastStrideDist = 0f;

        private int detectedWalkLeft = 0, detectedWalkRight = 0;
        private int detectedSprintLeft = 0, detectedSprintRight = 0;
        private int detectedSprintFastLeft = 0, detectedSprintFastRight = 0;

        private bool hasWalkResult = false;
        private bool hasSprintResult = false;
        private bool hasSprintFastResult = false;

        private Vector2 scrollPos;
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/DoreaRise/Animation Stride & Speed Calculator")]
        public static void ShowWindow()
        {
            GetWindow<AnimationStrideCalculatorTool>("Stride Calculator");
        }
        #endregion

        #region [Unity Methods]
        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            GUILayout.Label("Multi-Clip Stride & Footstep Calculator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("คำนวณระยะก้าวและความเร็ว Ideal Speed (m/s) จากอนิเมชัน เพื่อนำไปตั้งค่าความเร็วเดิน/วิ่งให้ตรงกับท่าทาง 100%", MessageType.Info);

            EditorGUILayout.Space();
            characterPrefab = (GameObject)EditorGUILayout.ObjectField("Character Prefab / Scene Object", characterPrefab, typeof(GameObject), true);

            EditorGUILayout.Space();
            useManualFrames = EditorGUILayout.ToggleLeft("กำหนดเลขเฟรมที่เท้าแตะพื้นเอง (Manual Frame Input)", useManualFrames, EditorStyles.boldLabel);

            EditorGUILayout.Space();
            DrawClipInput("Walk Clip", ref walkClip, ref walkLeftFrame, ref walkRightFrame);
            DrawClipInput("Sprint Clip", ref sprintClip, ref sprintLeftFrame, ref sprintRightFrame);
            DrawClipInput("Sprint Fast Clip", ref sprintFastClip, ref sprintFastLeftFrame, ref sprintFastRightFrame);

            EditorGUILayout.Space();
            GUILayout.Label("Bones Configuration", EditorStyles.boldLabel);
            leftFootBone = (HumanBodyBones)EditorGUILayout.EnumPopup("Left Foot (Humanoid)", leftFootBone);
            rightFootBone = (HumanBodyBones)EditorGUILayout.EnumPopup("Right Foot (Humanoid)", rightFootBone);

            if (characterPrefab != null)
            {
                Animator anim = characterPrefab.GetComponentInChildren<Animator>();
                if (anim == null || !anim.isHuman)
                {
                    EditorGUILayout.HelpBox("ไม่พบ Humanoid Avatar! กรุณาใส่ Transform เท้าซ้าย/ขวาในช่อง Custom ด้านล่าง", MessageType.Warning);
                    customLeftFoot = (Transform)EditorGUILayout.ObjectField("Custom Left Foot", customLeftFoot, typeof(Transform), true);
                    customRightFoot = (Transform)EditorGUILayout.ObjectField("Custom Right Foot", customRightFoot, typeof(Transform), true);
                }
            }

            EditorGUILayout.Space();
            GUI.enabled = characterPrefab != null && (walkClip != null || sprintClip != null || sprintFastClip != null);
            if (GUILayout.Button("Analyze All Animation Clips", GUILayout.Height(38)))
            {
                AnalyzeAllClips();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
            DrawAnalysisResults();

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region [Private Methods]
        private void DrawClipInput(string label, ref AnimationClip clip, ref int leftFrame, ref int rightFrame)
        {
            EditorGUILayout.BeginVertical("box");
            clip = (AnimationClip)EditorGUILayout.ObjectField(label, clip, typeof(AnimationClip), false);

            if (clip != null && useManualFrames)
            {
                int maxFrame = Mathf.RoundToInt(clip.length * clip.frameRate);
                EditorGUILayout.HelpBox($"Clip Length: {clip.length:F2}s ({maxFrame} frames @ {clip.frameRate} fps)", MessageType.None);
                leftFrame = EditorGUILayout.IntSlider("Left Foot Contact Frame", leftFrame, 0, maxFrame);
                rightFrame = EditorGUILayout.IntSlider("Right Foot Contact Frame", rightFrame, 0, maxFrame);
            }
            EditorGUILayout.EndVertical();
        }

        private void AnalyzeAllClips()
        {
            Debug.Log("<color=cyan>[StrideCalculator] === Starting Analysis ===</color>");

            if (characterPrefab == null)
            {
                Debug.LogError("[StrideCalculator] Character Prefab is missing!");
                return;
            }

            GameObject tempInstance = null;
            GameObject targetObj = characterPrefab;

            // Handle Prefab Asset sampling by instantiating temporary dummy in scene
            if (PrefabUtility.IsPartOfPrefabAsset(characterPrefab))
            {
                tempInstance = Instantiate(characterPrefab);
                tempInstance.hideFlags = HideFlags.HideAndDontSave;
                targetObj = tempInstance;
                Debug.Log("[StrideCalculator] Created temporary instance in scene for accurate sampling.");
            }

            try
            {
                if (walkClip != null)
                {
                    hasWalkResult = AnalyzeSingleClip(targetObj, walkClip, useManualFrames, walkLeftFrame, walkRightFrame,
                        out walkStrideDist, out walkIdealSpeed, out detectedWalkLeft, out detectedWalkRight);
                }

                if (sprintClip != null)
                {
                    hasSprintResult = AnalyzeSingleClip(targetObj, sprintClip, useManualFrames, sprintLeftFrame, sprintRightFrame,
                        out sprintStrideDist, out sprintIdealSpeed, out detectedSprintLeft, out detectedSprintRight);
                }

                if (sprintFastClip != null)
                {
                    hasSprintFastResult = AnalyzeSingleClip(targetObj, sprintFastClip, useManualFrames, sprintFastLeftFrame, sprintFastRightFrame,
                        out sprintFastStrideDist, out sprintFastIdealSpeed, out detectedSprintFastLeft, out detectedSprintFastRight);
                }
            }
            finally
            {
                if (tempInstance != null)
                {
                    DestroyImmediate(tempInstance);
                    Debug.Log("[StrideCalculator] Temporary sampling instance cleaned up.");
                }
            }

            Debug.Log("<color=green>[StrideCalculator] === Analysis Complete! ===</color>");
        }

        private bool AnalyzeSingleClip(GameObject target, AnimationClip clip, bool isManual, int manualLeft, int manualRight,
            out float strideDist, out float idealSpeed, out int finalLeftFrame, out int finalRightFrame)
        {
            strideDist = 0f;
            idealSpeed = 0f;
            finalLeftFrame = 0;
            finalRightFrame = 0;

            Animator anim = target.GetComponentInChildren<Animator>();
            Transform lFoot = customLeftFoot;
            Transform rFoot = customRightFoot;

            if (anim != null && anim.isHuman)
            {
                if (lFoot == null) lFoot = anim.GetBoneTransform(leftFootBone);
                if (rFoot == null) rFoot = anim.GetBoneTransform(rightFootBone);
            }

            if (lFoot == null || rFoot == null)
            {
                Debug.LogError($"[StrideCalculator] Failed to locate foot bones for clip '{clip.name}'! Check Humanoid avatar or set Custom Feet transforms.");
                return false;
            }

            float duration = clip.length;
            float frameRate = clip.frameRate > 0f ? clip.frameRate : 30f;
            int totalFrames = Mathf.RoundToInt(duration * frameRate);

            if (isManual)
            {
                finalLeftFrame = Mathf.Clamp(manualLeft, 0, totalFrames);
                finalRightFrame = Mathf.Clamp(manualRight, 0, totalFrames);
            }
            else
            {
                // Auto-detect lowest Y points for left & right feet
                float lowestL = float.MaxValue;
                float lowestR = float.MaxValue;

                for (int f = 0; f <= totalFrames; f++)
                {
                    float time = (float)f / totalFrames * duration;
                    clip.SampleAnimation(target, time);

                    if (lFoot.position.y < lowestL)
                    {
                        lowestL = lFoot.position.y;
                        finalLeftFrame = f;
                    }
                    if (rFoot.position.y < lowestR)
                    {
                        lowestR = rFoot.position.y;
                        finalRightFrame = f;
                    }
                }
            }

            // Calculate distance between left foot contact and right foot contact position
            float leftTime = (float)finalLeftFrame / totalFrames * duration;
            clip.SampleAnimation(target, leftTime);
            Vector3 leftContactPos = lFoot.position;

            float rightTime = (float)finalRightFrame / totalFrames * duration;
            clip.SampleAnimation(target, rightTime);
            Vector3 rightContactPos = rFoot.position;

            // Planar distance between foot contacts (X-Z distance)
            Vector3 flatLeft = new Vector3(leftContactPos.x, 0f, leftContactPos.z);
            Vector3 flatRight = new Vector3(rightContactPos.x, 0f, rightContactPos.z);
            float singleStepDist = Vector3.Distance(flatLeft, flatRight);

            // Full Stride (2 steps: Left -> Right -> Left)
            strideDist = singleStepDist * 2f;
            idealSpeed = duration > 0f ? (strideDist / duration) : 0f;

            Debug.Log($"<color=yellow>[Clip Analysis: '{clip.name}']</color>\n" +
                      $"  • Duration: {duration:F2}s ({totalFrames} frames @ {frameRate} FPS)\n" +
                      $"  • Left Contact Frame: {finalLeftFrame} | Right Contact Frame: {finalRightFrame}\n" +
                      $"  • Single Step Distance: {singleStepDist:F3} m\n" +
                      $"  • Full Stride Distance: {strideDist:F3} m\n" +
                      $"  • Recommended Ideal Speed: <b>{idealSpeed:F2} m/s</b> ({(idealSpeed * 3.6f):F1} km/h)");

            return true;
        }

        private void DrawAnalysisResults()
        {
            if (!hasWalkResult && !hasSprintResult && !hasSprintFastResult) return;

            GUILayout.Label("Analysis Results Summary", EditorStyles.boldLabel);

            if (hasWalkResult)
            {
                EditorGUILayout.HelpBox($"[Walk Clip]\n• Contact Frames: Left #{detectedWalkLeft} | Right #{detectedWalkRight}\n• Full Stride: {walkStrideDist:F2} m\n• Ideal Speed: {walkIdealSpeed:F2} m/s ({(walkIdealSpeed * 3.6f):F1} km/h)", MessageType.Info);
            }
            if (hasSprintResult)
            {
                EditorGUILayout.HelpBox($"[Sprint Clip]\n• Contact Frames: Left #{detectedSprintLeft} | Right #{detectedSprintRight}\n• Full Stride: {sprintStrideDist:F2} m\n• Ideal Speed: {sprintIdealSpeed:F2} m/s ({(sprintIdealSpeed * 3.6f):F1} km/h)", MessageType.Info);
            }
            if (hasSprintFastResult)
            {
                EditorGUILayout.HelpBox($"[SprintFast Clip]\n• Contact Frames: Left #{detectedSprintFastLeft} | Right #{detectedSprintFastRight}\n• Full Stride: {sprintFastStrideDist:F2} m\n• Ideal Speed: {sprintFastIdealSpeed:F2} m/s ({(sprintFastIdealSpeed * 3.6f):F1} km/h)", MessageType.Info);
            }

            CharacterControl ctrl = characterPrefab != null ? characterPrefab.GetComponentInChildren<CharacterControl>() : null;
            if (ctrl != null)
            {
                if (GUILayout.Button("Apply All Calculated Speeds to CharacterControl", GUILayout.Height(38)))
                {
                    Undo.RecordObject(ctrl, "Apply All Stride Speeds");
                    SerializedObject serializedCtrl = new SerializedObject(ctrl);

                    if (hasWalkResult)
                    {
                        serializedCtrl.FindProperty("walkSpeed").floatValue = walkIdealSpeed;
                        serializedCtrl.FindProperty("idealWalkSpeed").floatValue = walkIdealSpeed;
                    }
                    if (hasSprintResult)
                    {
                        serializedCtrl.FindProperty("sprintSpeed").floatValue = sprintIdealSpeed;
                        serializedCtrl.FindProperty("idealSprintSpeed").floatValue = sprintIdealSpeed;
                    }
                    if (hasSprintFastResult)
                    {
                        serializedCtrl.FindProperty("sprintFastSpeed").floatValue = sprintFastIdealSpeed;
                        serializedCtrl.FindProperty("idealSprintFastSpeed").floatValue = sprintFastIdealSpeed;
                    }

                    serializedCtrl.ApplyModifiedProperties();
                    EditorUtility.SetDirty(ctrl);
                    Debug.Log($"<color=green>[StrideCalculator] Successfully applied calculated speeds to CharacterControl on {characterPrefab.name}!</color>");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("ไม่พบสคริปต์ CharacterControl บน Character Prefab ที่เลือก! กรุณาเพิ่ม Component 'CharacterControl' ให้ตัวละครเพื่อกด Apply ค่าความเร็วเข้าสคริปต์", MessageType.Warning);
            }
        }
        #endregion
    }
}
