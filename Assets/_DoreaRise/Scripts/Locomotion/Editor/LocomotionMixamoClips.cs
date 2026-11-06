using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Puts the Mixamo clips retargeted to Dorea (Animation/Generated/Mixamo, prefix "DoreaMx_") into the clip set
    /// slots that had no clip: walk starts, walk 180 pivots, turns in place, an idle break and the jump. Loops, the
    /// walk stop and strafes are imported but not installed (the clip set already has those or has no slot).
    /// Runs by itself once per new manifest; also Tools > DoreaRise > Locomotion > Install Mixamo Clips.
    /// </summary>
    [InitializeOnLoad]
    public static class MixamoClipInstaller
    {
        [Serializable]
        private class Manifest
        {
            public ClipEntry[] clips;
        }

        [Serializable]
        private class ClipEntry
        {
            public string name;
            public string kind;
            public string fbx;
            public float angle;
            public float turnAngle;
            public float[] turnWindow;
            public float exitSpeed;
            public float exitPhase = -1f;
            public float interruptibleAfter = 0.6f;
            public float blendInHalfLife = -1f;
            public float minIntentSpeed;
            public float maxIntentSpeed;
            public float entryPhase = -1f;
            public bool lockFeet;
            public float travelDistance;
            public float[] travelCurveT;
            public float[] travelCurveV;
            public float[] travelCurveSlope;
            public float takeoffTime;
            public float landTime;
            public float jumpHeight;
            public float clipSpeed;
            public float apexDistance;
            public float touchTime;
            public float lowStart;
            public float lowEnd;
            public float lowHeadTop;
            public string plantSide;
            public float plantStart;
            public float plantEnd;
            public float plantTravel;
            public float plantForward;
            public float plantLateral;
            public float plantHeight;
            public float plantAirPhase;
            public float sideDistance;
            public float[] sideCurveT;
            public float[] sideCurveV;
            public float[] sideCurveSlope;
        }

        public const string Folder = "/Animation/Generated/Mixamo/";
        private const string ManifestPath = "Assets/_DoreaRise/Characters/Dorea/Animation/Generated/Mixamo/Dorea_Mixamo.json";
        private const string ClipSetPath = "Assets/_DoreaRise/Characters/Dorea/Dorea_LocomotionClipSet.asset";
        private const string Prefix = "DoreaMx_";
        /// <summary>Turn-in-place variants installed (the "_B" takes stay in the folder as alternatives).</summary>
        private static readonly string[] InstalledTurns = { "DoreaMx_Turn_L_A", "DoreaMx_Turn_R_A" };
        /// <summary>The authored run pivot (Break) only above this intent speed; the walk turn takes slower ones.</summary>
        private const float RunPivotMinIntentSpeed = 2f;

        private static bool scheduled;

        static MixamoClipInstaller()
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

        [MenuItem("Tools/DoreaRise/Locomotion/Install Mixamo Clips")]
        private static void InstallFromMenu()
        {
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifest == null)
            {
                EditorUtility.DisplayDialog("Install Mixamo Clips", "No manifest at " + ManifestPath, "OK");
                return;
            }
            if (Install(manifest, out string report)) EditorPrefs.SetString(PrefKey, Hash(manifest));
            Debug.Log("[MixamoClips] " + report);
        }

        private static string PrefKey => "DoreaRise.MixamoClips." + Application.dataPath.GetHashCode();

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
                Debug.Log("[MixamoClips] " + report);
            }
            else
            {
                Debug.LogWarning("[MixamoClips] not installed yet: " + report);
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

            Undo.RecordObject(set, "Install Mixamo locomotion clips");
            List<string> done = new List<string>();

            List<DirectionalAction> starts = set.starts.Where(a => a != null && !IsMixamo(a.clip)).ToList();
            foreach (ClipEntry e in manifest.clips.Where(e => e.kind == "start"))
                starts.Add(Directional(e, clips[e.name], ActionMotion.Travel));
            set.starts = starts.ToArray();
            done.Add($"{starts.Count(a => IsMixamo(a.clip))} walk start");

            List<DirectionalAction> pivots = set.pivots.Where(a => a != null && !IsMixamo(a.clip)).ToList();
            foreach (DirectionalAction runPivot in pivots)
                runPivot.minIntentSpeed = Mathf.Max(runPivot.minIntentSpeed, RunPivotMinIntentSpeed);
            foreach (ClipEntry e in manifest.clips.Where(e => e.kind == "pivot"))
                pivots.Add(Directional(e, clips[e.name], ActionMotion.Travel));
            set.pivots = pivots.ToArray();
            done.Add($"{pivots.Count(a => IsMixamo(a.clip))} walk pivot (run pivot from {RunPivotMinIntentSpeed:0} m/s intent)");

            List<DirectionalAction> turns = set.turnsInPlace.Where(a => a != null && !IsMixamo(a.clip)).ToList();
            foreach (ClipEntry e in manifest.clips.Where(e => e.kind == "turn" && InstalledTurns.Contains(e.name)))
                turns.Add(Directional(e, clips[e.name], ActionMotion.None));
            set.turnsInPlace = turns.ToArray();
            done.Add($"{turns.Count(a => IsMixamo(a.clip))} turns in place");

            ClipEntry jumpEntry = manifest.clips.FirstOrDefault(e => e.kind == "jump");
            if (jumpEntry != null)
            {
                // keep a jump height / gravity tuned in the inspector; the clip's own 0.27 m is far too low for Dorea
                float height = set.jump != null && set.jump.clip != null ? set.jump.jumpHeight : 1.2f;
                float gravity = set.jump != null && set.jump.clip != null ? set.jump.jumpGravity : -24f;
                if (height < 0.5f) height = 1.2f;
                set.jump = new JumpAction
                {
                    clip = clips[jumpEntry.name],
                    playbackSpeed = 1f,
                    blendInHalfLife = jumpEntry.blendInHalfLife,
                    motion = ActionMotion.None,
                    interruptibleAfter = jumpEntry.interruptibleAfter,
                    takeoffTime = jumpEntry.takeoffTime,
                    landTime = jumpEntry.landTime,
                    jumpHeight = height,
                    jumpGravity = gravity,
                    fallHoldLead = 0.03f
                };
                done.Add($"jump ({height:0.00} m)");
            }

            foreach (ClipEntry e in manifest.clips.Where(e => e.kind == "runjump" || e.kind == "sprintjump"))
            {
                bool vault = e.kind == "runjump";
                JumpAction old = vault ? set.obstacleJump : set.sprintJump;
                JumpAction a = new JumpAction
                {
                    clip = clips[e.name],
                    playbackSpeed = 1f,
                    blendInHalfLife = e.blendInHalfLife,
                    motion = ActionMotion.Travel,
                    travelDistance = e.travelDistance,
                    travelCurve = BuildCurve(e.travelCurveT, e.travelCurveV, e.travelCurveSlope),
                    interruptibleAfter = e.interruptibleAfter,
                    exitSpeed = e.exitSpeed,
                    exitPhase = e.exitPhase,
                    takeoffTime = e.takeoffTime,
                    landTime = e.landTime,
                    jumpHeight = e.jumpHeight,
                    clipFlightTime = true,
                    clipSpeed = e.clipSpeed,
                    apexDistance = e.apexDistance,
                    // keep thresholds tuned in the inspector
                    minEntrySpeed = old != null && old.clip != null ? old.minEntrySpeed : (vault ? 2.5f : 6f),
                    travelScaleRange = old != null && old.clip != null ? old.travelScaleRange
                        : (vault ? new Vector2(0.6f, 1.6f) : new Vector2(1f, 2.5f)),
                    plantsHand = !string.IsNullOrEmpty(e.plantSide),
                    plantLeftHand = e.plantSide == "L",
                    plantStart = e.plantStart,
                    plantEnd = e.plantEnd,
                    plantTravel = e.plantTravel,
                    plantForward = e.plantForward,
                    plantLateral = e.plantLateral,
                    plantHeight = e.plantHeight,
                    plantAirPhase = e.plantAirPhase
                };
                if (vault) set.obstacleJump = a;
                else set.sprintJump = a;
                done.Add(vault ? "obstacle jump" : "sprint jump");
            }

            ClipEntry landEntry = manifest.clips.FirstOrDefault(e => e.kind == "land");
            if (landEntry != null)
            {
                float minImpact = set.hardLanding != null && set.hardLanding.clip != null ? set.hardLanding.minImpactSpeed : 10f;
                set.hardLanding = new LandingAction
                {
                    clip = clips[landEntry.name],
                    playbackSpeed = 1f,
                    blendInHalfLife = landEntry.blendInHalfLife,
                    motion = ActionMotion.None,
                    interruptibleAfter = landEntry.interruptibleAfter,
                    lockFeet = landEntry.lockFeet,
                    touchTime = landEntry.touchTime,
                    minImpactSpeed = minImpact
                };
                done.Add($"hard landing (from {minImpact:0} m/s)");
            }

            ClipEntry slideEntry = manifest.clips.FirstOrDefault(e => e.kind == "slide");
            if (slideEntry != null)
            {
                SlideAction old = set.slide;
                bool tuned = old != null && old.clip != null;
                set.slide = new SlideAction
                {
                    clip = clips[slideEntry.name],
                    playbackSpeed = 1f,
                    blendInHalfLife = slideEntry.blendInHalfLife,
                    motion = ActionMotion.Travel,
                    travelDistance = slideEntry.travelDistance,
                    travelCurve = BuildCurve(slideEntry.travelCurveT, slideEntry.travelCurveV, slideEntry.travelCurveSlope),
                    interruptibleAfter = slideEntry.interruptibleAfter,
                    exitSpeed = slideEntry.exitSpeed,
                    exitPhase = slideEntry.exitPhase,
                    clipSpeed = slideEntry.clipSpeed,
                    lowStart = slideEntry.lowStart,
                    lowEnd = slideEntry.lowEnd,
                    lowHeadTop = slideEntry.lowHeadTop,
                    minEntrySpeed = tuned ? old.minEntrySpeed : 5f,
                    travelScaleRange = tuned ? old.travelScaleRange : new Vector2(0.8f, 1.6f)
                };
                done.Add("slide");
            }

            ClipEntry fallEntry = manifest.clips.FirstOrDefault(e => e.kind == "fall");
            if (fallEntry != null)
            {
                set.fallLoop = clips[fallEntry.name];
                done.Add("falling loop");
            }

            List<AnimationClip> idles = set.idleBreaks.Where(c => c != null && !IsMixamo(c)).ToList();
            idles.AddRange(manifest.clips.Where(e => e.kind == "idle").Select(e => clips[e.name]));
            set.idleBreaks = idles.ToArray();
            done.Add($"{idles.Count(IsMixamo)} idle break");

            set.RefreshAnimatedPaths();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            report = "installed into " + set.name + ": " + string.Join(", ", done);
            return true;
        }

        private static DirectionalAction Directional(ClipEntry e, AnimationClip clip, ActionMotion motion)
        {
            DirectionalAction a = new DirectionalAction
            {
                clip = clip,
                angle = e.angle,
                playbackSpeed = 1f,
                blendInHalfLife = e.blendInHalfLife,
                motion = motion,
                motionDuration = -1f,
                turnAngle = e.turnAngle,
                turnWindow = e.turnWindow != null && e.turnWindow.Length == 2 ? new Vector2(e.turnWindow[0], e.turnWindow[1]) : new Vector2(0f, 1f),
                exitSpeed = e.exitSpeed,
                exitPhase = e.exitPhase,
                interruptibleAfter = e.interruptibleAfter,
                lockFeet = e.lockFeet,
                minIntentSpeed = e.minIntentSpeed,
                maxIntentSpeed = e.maxIntentSpeed,
                entryPhase = e.entryPhase,
                followUp = null
            };
            if (motion == ActionMotion.Travel)
            {
                a.travelDistance = e.travelDistance;
                a.travelCurve = BuildCurve(e.travelCurveT, e.travelCurveV, e.travelCurveSlope);
                a.sideDistance = e.sideDistance;
                if (Mathf.Abs(e.sideDistance) > 1e-4f) a.sideCurve = BuildCurve(e.sideCurveT, e.sideCurveV, e.sideCurveSlope);
            }
            return a;
        }

        private static bool IsMixamo(AnimationClip clip)
        {
            return clip != null && clip.name.StartsWith(Prefix, StringComparison.Ordinal);
        }

        private static AnimationCurve BuildCurve(float[] t, float[] v, float[] slopes)
        {
            if (t == null || t.Length < 2)
                return new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));
            Keyframe[] keys = new Keyframe[t.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                float slope = slopes != null && i < slopes.Length ? slopes[i] : 0f;
                keys[i] = new Keyframe(t[i], v[i], slope, slope);
            }
            return new AnimationCurve(keys);
        }

        private class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Any(p => p.Replace('\\', '/').Contains(Folder)))
                    ScheduleAutoInstall();
            }
        }
    }
}
