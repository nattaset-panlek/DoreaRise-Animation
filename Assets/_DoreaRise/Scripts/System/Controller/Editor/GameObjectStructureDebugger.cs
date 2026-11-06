#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class GameObjectStructureDebugger : EditorWindow
{
    private GameObject targetRoot;
    private Vector2 scrollPosition;
    private Dictionary<int, bool> foldoutStatus = new Dictionary<int, bool>();

    [System.Serializable]
    public class HierarchyNode
    {
        public string name;
        public List<string> components = new List<string>();
        public List<HierarchyNode> children = new List<HierarchyNode>();
    }

    [MenuItem("Tools/GameObject Structure Debugger")]
    public static void ShowWindow()
    {
        GetWindow<GameObjectStructureDebugger>("Structure Debugger");
    }

    void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("GameObject Structure Analyzer", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Analyze Selected Object", GUILayout.Height(30)))
        {
            if (Selection.activeGameObject != null)
            {
                targetRoot = Selection.activeGameObject;
                foldoutStatus.Clear();
            }
            else
            {
                Debug.LogWarning("Please select a GameObject first!");
            }
        }

        if (GUILayout.Button("Copy JSON", GUILayout.Height(30), GUILayout.Width(100)))
        {
            if (targetRoot != null)
            {
                CopyStructureToJson();
            }
            else
            {
                Debug.LogWarning("No object analyzed to copy.");
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);
        GuiLine(1);
        GUILayout.Space(10);

        if (targetRoot != null)
        {
            GUILayout.Label($"Root: {targetRoot.name}", EditorStyles.largeLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawNode(targetRoot.transform);
            EditorGUILayout.EndScrollView();
        }
        else
        {
            GUILayout.Label("Select a GameObject and click Analyze.", EditorStyles.helpBox);
        }
    }

    void CopyStructureToJson()
    {
        HierarchyNode rootNode = CreateNodeData(targetRoot.transform);
        string json = JsonUtility.ToJson(rootNode, true);
        GUIUtility.systemCopyBuffer = json;
        Debug.Log($"Structure of '{targetRoot.name}' copied to clipboard!");
    }

    HierarchyNode CreateNodeData(Transform currentTransform)
    {
        HierarchyNode node = new HierarchyNode();
        node.name = currentTransform.name;

        Component[] components = currentTransform.GetComponents<Component>();
        foreach (Component c in components)
        {
            if (c != null && !(c is Transform))
            {
                node.components.Add(c.GetType().Name);
            }
        }

        foreach (Transform child in currentTransform)
        {
            node.children.Add(CreateNodeData(child));
        }

        return node;
    }

    void DrawNode(Transform currentTransform)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        int instanceID = currentTransform.gameObject.GetInstanceID();
        if (!foldoutStatus.ContainsKey(instanceID)) foldoutStatus[instanceID] = true;

        EditorGUILayout.BeginHorizontal();

        foldoutStatus[instanceID] = EditorGUILayout.Foldout(foldoutStatus[instanceID], currentTransform.name, true);

        if (GUILayout.Button("Ping", GUILayout.Width(40), GUILayout.Height(15)))
        {
            EditorGUIUtility.PingObject(currentTransform.gameObject);
            Selection.activeGameObject = currentTransform.gameObject;
        }
        EditorGUILayout.EndHorizontal();

        if (foldoutStatus[instanceID])
        {
            EditorGUI.indentLevel++;

            Component[] components = currentTransform.GetComponents<Component>();
            if (components.Length > 0)
            {
                GUI.color = new Color(0.7f, 1f, 0.7f);
                foreach (Component c in components)
                {
                    if (c == null)
                    {
                        GUILayout.Label($" - (Missing Script)", EditorStyles.miniLabel);
                        continue;
                    }

                    if (c is Transform) continue;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.Label($"[C] {c.GetType().Name}", EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
                GUI.color = Color.white;
            }

            if (currentTransform.childCount > 0)
            {
                foreach (Transform child in currentTransform)
                {
                    DrawNode(child);
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    void GuiLine( int i_height = 1 )
    {
       Rect rect = EditorGUILayout.GetControlRect(false, i_height );
       rect.height = i_height;
       EditorGUI.DrawRect(rect, new Color ( 0.5f,0.5f,0.5f, 1 ) );
    }
}
#endif
