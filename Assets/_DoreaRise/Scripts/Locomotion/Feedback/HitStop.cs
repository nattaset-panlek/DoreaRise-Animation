using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Brief global slow-down on impacts (the "freeze frame" of anime action). Call HitStop.Trigger(...).
    /// Overlapping calls extend the stop instead of stacking. Restores the previous Time.timeScale.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        #region [Variables/Fields]
        private static HitStop instance;

        private float remaining;
        private float restoreTimeScale = 1f;
        private bool isActive;
        #endregion

        #region [Properties]
        public static bool IsActive => instance != null && instance.isActive;
        /// <summary>Set false to ignore all hit-stops (e.g. while recording a take that must keep real time).</summary>
        public static bool Enabled { get; set; } = true;
        #endregion

        #region [Unity Methods]
        private void Update()
        {
            if (!isActive) return;

            remaining -= Time.unscaledDeltaTime;
            if (remaining > 0f) return;

            Time.timeScale = restoreTimeScale;
            isActive = false;
        }

        private void OnDisable()
        {
            if (isActive) Time.timeScale = restoreTimeScale;
            isActive = false;
        }
        #endregion

        #region [Public Methods]
        /// <param name="duration">Real-time seconds (0.03–0.08 reads as an impact).</param>
        /// <param name="timeScale">Time scale during the stop (0 = full freeze).</param>
        public static void Trigger(float duration, float timeScale = 0.05f)
        {
            if (!Enabled || duration <= 0f || !Application.isPlaying) return;

            if (instance == null)
            {
                // DontDestroyOnLoad objects are cleaned up when Play Mode ends.
                GameObject host = new GameObject("[HitStop]") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(host);
                instance = host.AddComponent<HitStop>();
            }

            instance.Begin(duration, timeScale);
        }
        #endregion

        #region [Private Methods]
        private void Begin(float duration, float timeScale)
        {
            if (!isActive) restoreTimeScale = Time.timeScale;
            Time.timeScale = Mathf.Min(Mathf.Clamp01(timeScale), restoreTimeScale);
            remaining = Mathf.Max(remaining, duration);
            isActive = true;
        }
        #endregion
    }
}
