using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace DoreaRise.EditorTools
{
    /// <summary>
    /// After a fresh clone, Unity opens an empty "Untitled" scene because the last-opened
    /// scene is stored in Library/ (not in Git). This opens the main scene instead.
    /// </summary>
    [InitializeOnLoad]
    static class OpenMainSceneOnFirstLoad
    {
        const string MainScene = "Assets/Scenes/SampleScene.unity";
        const string SessionKey = "DoreaRise.OpenMainSceneChecked";

        static OpenMainSceneOnFirstLoad()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += TryOpen;
        }

        static void TryOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.sceneCount != 1) return;

            Scene active = SceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(active.path) && !active.isDirty;
            if (!untitled) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScene) == null) return;

            EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
        }
    }
}
