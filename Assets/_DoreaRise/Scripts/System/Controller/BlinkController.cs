using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.System.Controller
{
    public class BlinkController : MonoBehaviour
    {
        [Serializable]
        public class EyeTarget
        {
            public SkinnedMeshRenderer renderer;
            public int blendShapeIndex;
            [Range(0f, 100f)] public float openValue = 0f;
            [Range(0f, 100f)] public float closeValue = 100f;
        }

        #region [Variables/Fields]
        [SerializeField] private List<EyeTarget> eyes = new List<EyeTarget>();
        [SerializeField] private float blinkInterval = 4f;
        [SerializeField] private float blinkDuration = 0.25f;

        private float timer;
        private float blinkTimer;
        private bool isBlinking;
        #endregion

        #region [Properties]
        public List<EyeTarget> Eyes => eyes;
        public float BlinkInterval { get => blinkInterval; set => blinkInterval = value; }
        public float BlinkDuration { get => blinkDuration; set => blinkDuration = value; }
        #endregion

        #region [Unity Methods]
        private void Start()
        {
            timer = blinkInterval;
        }

        private void Update()
        {
            if (!isBlinking)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    isBlinking = true;
                    blinkTimer = 0f;
                }
            }
            else
            {
                blinkTimer += Time.deltaTime;
                float half = blinkDuration * 0.5f;
                float weight = blinkTimer <= half
                    ? Mathf.Lerp(0f, 1f, blinkTimer / half)
                    : Mathf.Lerp(1f, 0f, (blinkTimer - half) / half);

                for (int i = 0; i < eyes.Count; i++)
                {
                    EyeTarget eye = eyes[i];
                    if (eye.renderer == null) continue;
                    eye.renderer.SetBlendShapeWeight(eye.blendShapeIndex, Mathf.Lerp(eye.openValue, eye.closeValue, weight));
                }

                if (blinkTimer >= blinkDuration)
                {
                    isBlinking = false;
                    timer = blinkInterval;
                    for (int i = 0; i < eyes.Count; i++)
                    {
                        EyeTarget eye = eyes[i];
                        if (eye.renderer == null) continue;
                        eye.renderer.SetBlendShapeWeight(eye.blendShapeIndex, eye.openValue);
                    }
                }
            }
        }
        #endregion

        #region [Public Methods]
        public void Blink()
        {
            isBlinking = true;
            blinkTimer = 0f;
        }
        #endregion
    }
}
