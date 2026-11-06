using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DoreaRise.System.Controller;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// One-click setup of the locomotion core on a character (undoable). Safe to run again after a rig re-export:
    /// it only adds what is missing, re-finds bones by name and refreshes the clip set's animated paths.
    /// </summary>
    public static class LocomotionSetupTool
    {
        private static readonly string[] LeanBoneNames = { "Spine.001", "Spine.002", "Spine.003" };
        private static readonly float[] LeanBoneWeights = { 0.3f, 0.35f, 0.35f };

        [MenuItem("Tools/DoreaRise/Locomotion/Setup Selected Character")]
        public static void SetupSelected()
        {
            GameObject selected = Selection.activeGameObject;
            CharacterController controller = selected != null ? selected.GetComponentInParent<CharacterController>() : null;
            GameObject root = controller != null ? controller.gameObject : selected;
            Animator animator = root != null ? root.GetComponentInChildren<Animator>(true) : null;

            if (root == null || animator == null)
            {
                EditorUtility.DisplayDialog("Locomotion Setup",
                    "เลือก GameObject ตัวละคร (ตัวที่มี CharacterController และมี Animator อยู่ข้างใน) ก่อน", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Setup Dorea Locomotion");
            int undoGroup = Undo.GetCurrentGroup();
            List<string> report = new List<string>();

            if (root.GetComponent<CharacterController>() == null)
                Undo.AddComponent<CharacterController>(root);

            LocomotionMotor motor = GetOrAdd<LocomotionMotor>(root, report);
            GetOrAdd<PlayerInputDriver>(root, report);
            GetOrAdd<ScriptedLocomotionDriver>(root, report);
            // Lives on the root so a prefab-instance model (FBX) does not need added components.
            LocomotionAnimator locomotionAnimator = root.GetComponentInChildren<LocomotionAnimator>(true);
            if (locomotionAnimator == null) locomotionAnimator = GetOrAdd<LocomotionAnimator>(root, report);

            LocomotionClipSet clipSet = FindOrCreateClipSet(locomotionAnimator, animator, report);
            clipSet.RefreshAnimatedPaths();
            SetReference(locomotionAnimator, "clipSet", clipSet);
            SetReference(locomotionAnimator, "animator", animator);
            SetReference(motor, "locomotionAnimator", locomotionAnimator);

            foreach (CharacterControl legacy in root.GetComponentsInChildren<CharacterControl>(true))
            {
                if (!legacy.enabled) continue;
                Undo.RecordObject(legacy, "Disable CharacterControl");
                legacy.enabled = false;
                report.Add("ปิด CharacterControl (ระบบเก่า) — เปิดกลับได้เพื่อเทียบ");
            }

            // The legacy lean rotated the whole model; the new lean works on the spine.
            if (animator.transform != root.transform && animator.transform.localRotation != Quaternion.identity)
            {
                Undo.RecordObject(animator.transform, "Reset model rotation");
                animator.transform.localRotation = Quaternion.identity;
            }

            int repointed = 0;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && RepointLocomotionSource(behaviour, motor)) repointed++;

            foreach (CameraControl cameraControl in Object.FindObjectsByType<CameraControl>(FindObjectsInactive.Include))
            {
                if (cameraControl.Target == null || cameraControl.Target.root == root.transform.root)
                    if (RepointLocomotionSource(cameraControl, motor)) repointed++;
            }
            report.Add($"ชี้ locomotionSource ของ {repointed} component ไปที่ LocomotionMotor");

            SetupLean(root, animator, report);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = root;

            Debug.Log("[Locomotion Setup] " + root.name + "\n- " + string.Join("\n- ", report), root);
        }

        [MenuItem("Tools/DoreaRise/Locomotion/Setup Selected Character", true)]
        private static bool ValidateSetupSelected()
        {
            return Selection.activeGameObject != null;
        }

        private static T GetOrAdd<T>(GameObject target, List<string> report) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null) return component;

            component = Undo.AddComponent<T>(target);
            report.Add($"เพิ่ม {typeof(T).Name} บน {target.name}");
            return component;
        }

        private static void SetReference(Object owner, string propertyName, Object value)
        {
            SerializedObject serialized = new SerializedObject(owner);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null) return;
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static bool RepointLocomotionSource(Object component, LocomotionMotor motor)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty("locomotionSource");
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return false;
            if (property.objectReferenceValue == motor) return false;

            property.objectReferenceValue = motor;
            serialized.ApplyModifiedProperties();
            return true;
        }

        private static LocomotionClipSet FindOrCreateClipSet(LocomotionAnimator existing, Animator animator, List<string> report)
        {
            if (existing != null && existing.ClipSet != null) return existing.ClipSet;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LocomotionClipSet));
            if (guids.Length > 0)
            {
                LocomotionClipSet found = AssetDatabase.LoadAssetAtPath<LocomotionClipSet>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (found != null)
                {
                    report.Add("ใช้ Clip Set: " + AssetDatabase.GetAssetPath(found));
                    return found;
                }
            }

            string folder = "Assets";
            if (animator.runtimeAnimatorController != null)
                folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController))?.Replace('\\', '/') ?? folder;

            LocomotionClipSet created = ScriptableObject.CreateInstance<LocomotionClipSet>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{animator.name}_LocomotionClipSet.asset");
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            report.Add("สร้าง Clip Set ว่างที่ " + path + " — ใส่คลิปและ idealSpeed ก่อนกด Play");
            return created;
        }

        private static void SetupLean(GameObject root, Animator animator, List<string> report)
        {
            List<Transform> chain = new List<Transform>();
            List<float> weights = new List<float>();
            for (int i = 0; i < LeanBoneNames.Length; i++)
            {
                Transform bone = FindDeep(animator.transform, LeanBoneNames[i]);
                if (bone == null) continue;
                chain.Add(bone);
                weights.Add(LeanBoneWeights[i]);
            }

            LocomotionLean lean = GetOrAdd<LocomotionLean>(root, report);
            if (chain.Count == 0)
            {
                report.Add("ไม่พบกระดูก Spine.001-003 — ใส่ bones ของ LocomotionLean เอง");
                return;
            }

            Undo.RecordObject(lean, "Setup lean bones");
            lean.SetBones(chain.ToArray(), weights.ToArray());
            EditorUtility.SetDirty(lean);
            report.Add($"LocomotionLean ใช้ {chain.Count} กระดูก: {string.Join(", ", LeanBoneNames)}");
        }

        private static Transform FindDeep(Transform parent, string boneName)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == boneName) return child;
            return null;
        }
    }
}
