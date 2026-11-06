using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Import settings for the clips made by the Blender generator (Animation/Generated): generic rig, no avatar,
    /// explicit prefab root (the FBX holds only the armature, so ModelBase must not become the root), loops for
    /// "_Loop" clips.
    /// </summary>
    public class GeneratedClipImportSettings : AssetPostprocessor
    {
        public const string Folder = "/Animation/Generated/";

        public static bool IsGenerated(string path)
        {
            return path.Replace('\\', '/').Contains(Folder) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        public static void ApplyModelSettings(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.motionNodeName = string.Empty;
        }

        public static bool NeedsSettings(ModelImporter importer)
        {
            string expected = Path.GetFileNameWithoutExtension(importer.assetPath);
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            return !importer.preserveHierarchy || importer.animationType != ModelImporterAnimationType.Generic
                || clips.Length == 0 || clips[0].name != expected;
        }

        private void OnPreprocessModel()
        {
            if (IsGenerated(assetPath)) ApplyModelSettings((ModelImporter)assetImporter);
        }

        private void OnPreprocessAnimation()
        {
            if (!IsGenerated(assetPath)) return;
            ModelImporter importer = (ModelImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = name;
                clip.loopTime = name.EndsWith("_Loop", StringComparison.Ordinal);
                clip.loopPose = false;
                clip.lockRootRotation = false;
                clip.lockRootHeightY = false;
                clip.lockRootPositionXZ = false;
            }
            if (clips.Length > 1) clips = clips.Take(1).ToArray();
            importer.clipAnimations = clips;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p.Replace('\\', '/').Contains(Folder)))
                GeneratedClipInstaller.ScheduleAutoInstall();
        }
    }

    /// <summary>
    /// Puts the generated clips into Dorea's LocomotionClipSet from the generator's manifest: the Run loop,
    /// the run starts (0 / 90 / 180) and the run + walk stops. Runs by itself once per new manifest; also
    /// Tools > DoreaRise > Locomotion > Install Generated Clips.
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratedClipInstaller
    {
        [Serializable]
        private class Manifest
        {
            public float runSpeed;
            public ClipEntry[] clips;
        }

        [Serializable]
        private class ClipEntry
        {
            public string name;
            public string kind;
            public string fbx;
            public float idealSpeed;
            public float phaseOffset;
            public float angle;
            public float turnAngle;
            public float[] turnWindow;
            public float exitSpeed;
            public float exitPhase = -1f;
            public float interruptibleAfter = 0.6f;
            public float blendInHalfLife = -1f;
            public float minIntentSpeed;
            public float maxIntentSpeed;
            public bool lockFeet;
            public float travelDistance;
            public float[] travelCurveT;
            public float[] travelCurveV;
            public float[] travelCurveSlope;
            public float[] phaseRange;
            public float minSpeed;
            public float decelerationExponent = 2f;
            public float motionDuration = -1f;
        }

        private const string ManifestPath = "Assets/_DoreaRise/Characters/Dorea/Animation/Generated/Dorea_Locomotion_Batch1.json";
        private const string ClipSetPath = "Assets/_DoreaRise/Characters/Dorea/Dorea_LocomotionClipSet.asset";
        private const string GeneratedPrefix = "Dorea_";
        /// <summary>The user's sprint brake keeps sprint-speed stops; slower stops use the authored run / walk stops.</summary>
        private const float SprintBrakeMinSpeed = 6f;

        private static bool scheduled;

        static GeneratedClipInstaller()
        {
            ScheduleAutoInstall();
        }

        public static void ScheduleAutoInstall()
        {
            if (scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                AutoInstall();
            };
        }

        [MenuItem("Tools/DoreaRise/Locomotion/Install Generated Clips")]
        private static void InstallFromMenu()
        {
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifest == null)
            {
                EditorUtility.DisplayDialog("Install Generated Clips", "No manifest at " + ManifestPath, "OK");
                return;
            }
            if (Install(manifest, out string report)) EditorPrefs.SetString(PrefKey, Hash(manifest));
            Debug.Log("[GeneratedClips] " + report);
        }

        private static string PrefKey => "DoreaRise.GeneratedClips." + Application.dataPath.GetHashCode();

        private static string Hash(TextAsset manifest) => Hash128.Compute(manifest.text).ToString();

        private static void AutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.isUpdating || EditorApplication.isCompiling) ScheduleAutoInstall();
                return;
            }
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifest == null) return;
            string hash = Hash(manifest);
            if (EditorPrefs.GetString(PrefKey) == hash) return;
            if (Install(manifest, out string report))
            {
                EditorPrefs.SetString(PrefKey, hash);
                Debug.Log("[GeneratedClips] " + report);
            }
            else
            {
                Debug.LogWarning("[GeneratedClips] not installed yet: " + report);
            }
        }

        private static bool Install(TextAsset manifestAsset, out string report)
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(manifestAsset.text);
            LocomotionClipSet set = AssetDatabase.LoadAssetAtPath<LocomotionClipSet>(ClipSetPath);
            if (manifest?.clips == null || set == null)
            {
                report = set == null ? "clip set not found at " + ClipSetPath : "manifest unreadable";
                return false;
            }

            string folder = Path.GetDirectoryName(ManifestPath).Replace('\\', '/');
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            List<string> missing = new List<string>();
            foreach (ClipEntry entry in manifest.clips)
            {
                string path = folder + "/" + entry.fbx;
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && GeneratedClipImportSettings.NeedsSettings(importer))
                {
                    GeneratedClipImportSettings.ApplyModelSettings(importer);
                    importer.SaveAndReimport();
                }
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                if (clip == null) missing.Add(entry.fbx);
                else clips[entry.name] = clip;
            }
            if (missing.Count > 0)
            {
                report = "missing / not imported: " + string.Join(", ", missing);
                return false;
            }

            Undo.RecordObject(set, "Install generated locomotion clips");
            List<string> done = new List<string>();

            ClipEntry loop = manifest.clips.FirstOrDefault(e => e.kind == "loop");
            if (loop != null)
            {
                set.run = new LoopClip { clip = clips[loop.name], idealSpeed = loop.idealSpeed, speedScale = 1f, phaseOffset = loop.phaseOffset };
                done.Add($"run loop {loop.idealSpeed:0.0} m/s");
            }

            List<DirectionalAction> starts = set.starts.Where(a => a != null && !IsGenerated(a.clip)).ToList();
            foreach (ClipEntry e in manifest.clips.Where(e => e.kind == "start"))
            {
                DirectionalAction a = new DirectionalAction { clip = clips[e.name], angle = e.angle };
                FillCommon(a, e);
                a.motion = ActionMotion.Travel;
                a.travelDistance = e.travelDistance;
                a.travelCurve = BuildCurve(e);
                a.turnAngle = e.turnAngle;
                a.turnWindow = e.turnWindow != null && e.turnWindow.Length == 2 ? new Vector2(e.turnWindow[0], e.turnWindow[1]) : new Vector2(0f, 1f);
                a.exitSpeed = e.exitSpeed;
                a.minIntentSpeed = e.minIntentSpeed;
                a.maxIntentSpeed = e.maxIntentSpeed;
                starts.Add(a);
            }
            set.starts = starts.ToArray();
            done.Add($"{starts.Count(a => IsGenerated(a.clip))} starts");

            List<PhasedAction> stops = set.stops.Where(a => a != null && !IsGenerated(a.clip)).ToList();
            foreach (PhasedAction brake in stops)
                brake.minSpeed = Mathf.Max(brake.minSpeed, SprintBrakeMinSpeed);
            // FindStop takes the first match: faster stops first.
            foreach (ClipEntry e in manifest.clips.Where(e => e.kind == "stop").OrderByDescending(e => e.minSpeed))
            {
                PhasedAction a = new PhasedAction { clip = clips[e.name] };
                FillCommon(a, e);
                a.motion = ActionMotion.Decelerate;
                a.decelerationExponent = e.decelerationExponent;
                a.motionDuration = e.motionDuration;
                a.minSpeed = e.minSpeed;
                a.phaseRange = e.phaseRange != null && e.phaseRange.Length == 2 ? new Vector2(e.phaseRange[0], e.phaseRange[1]) : new Vector2(0f, 1f);
                stops.Add(a);
            }
            set.stops = stops.ToArray();
            done.Add($"{stops.Count(a => IsGenerated(a.clip))} stops (sprint brake now from {SprintBrakeMinSpeed:0} m/s)");

            set.RefreshAnimatedPaths();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            report = "installed into " + set.name + ": " + string.Join(", ", done);
            return true;
        }

        private static void FillCommon(ActionClip a, ClipEntry e)
        {
            a.playbackSpeed = 1f;
            a.blendInHalfLife = e.blendInHalfLife;
            a.motionDuration = e.motionDuration;
            a.interruptibleAfter = e.interruptibleAfter;
            a.exitPhase = e.exitPhase;
            a.lockFeet = e.lockFeet;
            a.followUp = null;
        }

        private static bool IsGenerated(AnimationClip clip)
        {
            return clip != null && clip.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal);
        }

        private static AnimationCurve BuildCurve(ClipEntry e)
        {
            if (e.travelCurveT == null || e.travelCurveT.Length < 2)
                return new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));
            Keyframe[] keys = new Keyframe[e.travelCurveT.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                float slope = e.travelCurveSlope != null && i < e.travelCurveSlope.Length ? e.travelCurveSlope[i] : 0f;
                keys[i] = new Keyframe(e.travelCurveT[i], e.travelCurveV[i], slope, slope);
            }
            return new AnimationCurve(keys);
        }
    }
}
