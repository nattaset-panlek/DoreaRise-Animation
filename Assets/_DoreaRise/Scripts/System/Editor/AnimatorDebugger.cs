using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DoreaRise.System.EditorTools
{
    public class AnimatorDebugger : EditorWindow
    {
        private string jsonOutput = "";
        private Vector2 scrollPosition;

        [MenuItem("Tools/Dorea/Export Selected Animator to JSON")]
        public static void ShowWindow()
        {
            AnimatorController controller = null;

            // Try to get it from the selected asset
            if (Selection.activeObject is AnimatorController selectedAsset)
            {
                controller = selectedAsset;
            }
            // Try to get it from the selected GameObject's Animator component
            else if (Selection.activeGameObject != null)
            {
                var animator = Selection.activeGameObject.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    controller = animator.runtimeAnimatorController as AnimatorController;
                }
            }

            if (controller == null)
            {
                Debug.LogWarning("Please select an Animator Controller asset or a GameObject with an Animator component.");
                return;
            }

            var exportData = new AnimatorExportData
            {
                name = controller.name,
                parameters = new List<string>(),
                layers = new List<LayerData>()
            };

            // Export parameters
            foreach (var param in controller.parameters)
            {
                exportData.parameters.Add($"{param.name} ({param.type})");
            }

            // Export layers and states
            foreach (var layer in controller.layers)
            {
                var layerData = new LayerData
                {
                    name = layer.name,
                    states = new List<StateData>(),
                    anyStateTransitions = new List<TransitionData>()
                };

                // Export AnyState transitions
                foreach (var trans in layer.stateMachine.anyStateTransitions)
                {
                    layerData.anyStateTransitions.Add(GetTransitionData(trans, "AnyState", trans.destinationState?.name ?? "Exit"));
                }

                // Export States
                foreach (var childState in layer.stateMachine.states)
                {
                    var state = childState.state;
                    var stateData = new StateData
                    {
                        name = state.name,
                        isDefault = (layer.stateMachine.defaultState == state),
                        transitions = new List<TransitionData>()
                    };

                    foreach (var trans in state.transitions)
                    {
                        string destName = trans.isExit ? "Exit" : trans.destinationState?.name;
                        stateData.transitions.Add(GetTransitionData(trans, state.name, destName));
                    }

                    layerData.states.Add(stateData);
                }

                exportData.layers.Add(layerData);
            }

            string json = JsonUtility.ToJson(exportData, true);

            var window = GetWindow<AnimatorDebugger>("Animator JSON");
            window.jsonOutput = json;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animator Data (JSON)", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Copy to Clipboard"))
            {
                GUIUtility.systemCopyBuffer = jsonOutput;
                Debug.Log("JSON copied to clipboard!");
            }

            EditorGUILayout.Space();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.TextArea(jsonOutput, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static TransitionData GetTransitionData(AnimatorStateTransition trans, string source, string dest)
        {
            var data = new TransitionData
            {
                source = source,
                destination = dest,
                hasExitTime = trans.hasExitTime,
                exitTime = trans.exitTime,
                duration = trans.duration,
                conditions = new List<string>()
            };

            foreach (var cond in trans.conditions)
            {
                string modeStr = cond.mode.ToString();
                data.conditions.Add($"{cond.parameter} {modeStr} {(cond.mode == AnimatorConditionMode.If ? "true" : cond.mode == AnimatorConditionMode.IfNot ? "false" : cond.threshold.ToString())}");
            }

            return data;
        }

        [global::System.Serializable]
        public class AnimatorExportData
        {
            public string name;
            public List<string> parameters;
            public List<LayerData> layers;
        }

        [global::System.Serializable]
        public class LayerData
        {
            public string name;
            public List<TransitionData> anyStateTransitions;
            public List<StateData> states;
        }

        [global::System.Serializable]
        public class StateData
        {
            public string name;
            public bool isDefault;
            public List<TransitionData> transitions;
        }

        [global::System.Serializable]
        public class TransitionData
        {
            public string source;
            public string destination;
            public bool hasExitTime;
            public float exitTime;
            public float duration;
            public List<string> conditions;
        }
    }
}
