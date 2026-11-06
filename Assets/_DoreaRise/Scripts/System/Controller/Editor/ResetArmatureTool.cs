using UnityEditor;
using UnityEngine;

namespace DoreaRise.System.Controller.EditorTools
{
    public static class ResetArmatureTool
    {
        #region [Public Methods]
        [MenuItem("GameObject/DoreaRise/Reset Bones Transform to FBX Default", false, 10)]
        public static void ResetSelectedBones(MenuCommand menuCommand)
        {
            GameObject selectedObj = menuCommand.context as GameObject;
            if (selectedObj == null) selectedObj = Selection.activeGameObject;

            if (selectedObj == null)
            {
                Debug.LogWarning("[ResetArmatureTool] Please select an Armature or Character root object!");
                return;
            }

            Transform[] allBones = selectedObj.GetComponentsInChildren<Transform>(true);
            int resetCount = 0;

            foreach (Transform bone in allBones)
            {
                // Only revert transform overrides, keeping all components intact
                if (PrefabUtility.IsPartOfPrefabInstance(bone.gameObject))
                {
                    PrefabUtility.RevertObjectOverride(bone, InteractionMode.UserAction);
                    resetCount++;
                }
            }

            Debug.Log($"<color=green>[ResetArmatureTool] Successfully reset {resetCount} bone transforms to FBX default position/rotation!</color>");
        }

        [MenuItem("Tools/DoreaRise/Reset Selected Character Armature Pose")]
        public static void ResetFromMenu()
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("Reset Armature", "กรุณาเลือกตัวละครหรือกระดูก Armature ใน Hierarchy ก่อนกดเมนูนี้ครับ", "OK");
                return;
            }

            ResetSelectedBones(new MenuCommand(Selection.activeGameObject));
        }
        #endregion
    }
}
