using System;
using System.Collections;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Minimal scripted shot: walk/run through targets, optionally play a clip at each, then wait.
    /// Shows the coroutine API: <c>yield return driver.MoveTo(target, Gait.Sprint);</c>
    /// </summary>
    public class LocomotionSequenceSample : MonoBehaviour
    {
        [Serializable]
        public class Step
        {
            public Transform target;
            public Gait gait = Gait.Sprint;
            [Tooltip("End facing the target's forward")]
            public bool matchRotation;
            [Tooltip("Optional clip played in place on arrival")]
            public AnimationClip playOnArrival;
            public float waitAfter = 0.5f;
        }

        #region [Variables/Fields]
        [SerializeField] private ScriptedLocomotionDriver driver;
        [SerializeField] private Step[] steps = new Step[0];
        [SerializeField] private KeyCode playKey = KeyCode.P;
        [SerializeField] private bool playOnStart;
        [SerializeField] private bool loop;

        private Coroutine running;
        #endregion

        #region [Unity Methods]
        private void Start()
        {
            if (driver == null) driver = FindAnyObjectByType<ScriptedLocomotionDriver>();
            if (playOnStart) Play();
        }

        private void Update()
        {
            if (Input.GetKeyDown(playKey)) Play();
        }
        #endregion

        #region [Public Methods]
        public void Play()
        {
            if (driver == null) return;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run());
        }
        #endregion

        #region [Private Methods]
        private IEnumerator Run()
        {
            do
            {
                foreach (Step step in steps)
                {
                    if (step.target != null)
                        yield return driver.MoveTo(step.target, step.gait, step.matchRotation);

                    if (step.playOnArrival != null)
                        yield return driver.PlayAction(step.playOnArrival);

                    if (step.waitAfter > 0f)
                        yield return new WaitForSeconds(step.waitAfter);
                }
            }
            while (loop);

            running = null;
        }
        #endregion
    }
}
