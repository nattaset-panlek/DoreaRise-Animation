using System.IO;
using UnityEditor;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Tools > DoreaRise > Locomotion > Run Self Test: enters Play Mode, runs LocomotionSelfTest on the character,
    /// writes Logs/LocomotionSelfTest.md and leaves Play Mode again.
    /// </summary>
    [InitializeOnLoad]
    public static class LocomotionSelfTestMenu
    {
        private const string PendingKey = "DoreaRise.LocomotionSelfTest.Pending";

        static LocomotionSelfTestMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            LocomotionSelfTest.Completed += OnCompleted;
        }

        [MenuItem("Tools/DoreaRise/Locomotion/Run Self Test (Play Mode)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                StartTest();
                return;
            }

            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private const string ProbePendingKey = "DoreaRise.LocomotionClipProbe.Pending";

        [MenuItem("Tools/DoreaRise/Locomotion/Run Jump Probe (Play Mode)")]
        public static void RunJumpProbe()
        {
            SessionState.SetBool(JumpsOnlyKey, true);
            SessionState.SetBool(PropsOnlyKey, false);
            RunProbeInternal();
        }

        private const string PropsOnlyKey = "DoreaRise.LocomotionClipProbe.PropsOnly";

        [MenuItem("Tools/DoreaRise/Locomotion/Run Jump Test Props Probe (Play Mode)")]
        public static void RunPropsProbe()
        {
            SessionState.SetBool(JumpsOnlyKey, false);
            SessionState.SetBool(PropsOnlyKey, true);
            RunProbeInternal();
        }

        private const string JumpsOnlyKey = "DoreaRise.LocomotionClipProbe.JumpsOnly";

        [MenuItem("Tools/DoreaRise/Locomotion/Run Clip Probe (Play Mode)")]
        public static void RunProbe()
        {
            SessionState.SetBool(JumpsOnlyKey, false);
            SessionState.SetBool(PropsOnlyKey, false);
            RunProbeInternal();
        }

        private static void RunProbeInternal()
        {
            if (EditorApplication.isPlaying)
            {
                StartProbe();
                return;
            }

            SessionState.SetBool(ProbePendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void StartProbe()
        {
            LocomotionMotor motor = Object.FindAnyObjectByType<LocomotionMotor>();
            if (motor == null)
            {
                Debug.LogError("[ClipProbe] No LocomotionMotor in the open scene.");
                return;
            }
            LocomotionClipProbe.JumpsOnly = SessionState.GetBool(JumpsOnlyKey, false);
            LocomotionClipProbe.PropsOnly = SessionState.GetBool(PropsOnlyKey, false);
            if (motor.GetComponent<LocomotionClipProbe>() == null)
                motor.gameObject.AddComponent<LocomotionClipProbe>();
        }

        /// <summary>Clears the "unexpected guid mismatch" left by a .meta that was rewritten after import.</summary>
        [MenuItem("Tools/DoreaRise/Locomotion/Reimport Locomotion Scripts")]
        public static void ReimportScripts()
        {
            AssetDatabase.ImportAsset("Assets/_DoreaRise/Scripts/Locomotion",
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[Locomotion] Reimported Assets/_DoreaRise/Scripts/Locomotion");
        }

        [MenuItem("Tools/DoreaRise/Locomotion/Open Last Self Test Report")]
        public static void OpenReport()
        {
            if (File.Exists(LocomotionSelfTest.ReportPath))
                EditorUtility.OpenWithDefaultApp(LocomotionSelfTest.ReportPath);
            else
                Debug.LogWarning("[LocomotionSelfTest] No report yet: " + LocomotionSelfTest.ReportPath);
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ProbePendingKey, false))
            {
                SessionState.SetBool(ProbePendingKey, false);
                StartProbe();
                return;
            }
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            StartTest();
        }

        private static void StartTest()
        {
            LocomotionMotor motor = Object.FindAnyObjectByType<LocomotionMotor>();
            if (motor == null)
            {
                Debug.LogError("[LocomotionSelfTest] No LocomotionMotor in the open scene.");
                return;
            }

            if (motor.GetComponent<LocomotionSelfTest>() == null)
                motor.gameObject.AddComponent<LocomotionSelfTest>();
            Debug.Log("[LocomotionSelfTest] Running (about 2 minutes, hands off the keyboard)...");
        }

        private static void OnCompleted(string reportPath, string summary)
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                EditorUtility.DisplayDialog("Locomotion Self Test", summary, "OK");
            };
        }
    }
}
