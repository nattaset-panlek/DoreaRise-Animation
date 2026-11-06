using System.Collections.Generic;
using DoreaRise.Locomotion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DoreaRise.Rendering
{
    /// <summary>
    /// Keeps the depth of field focused on the character's head while the camera orbits and zooms.
    /// A fixed focus distance blurs the character at every distance except one. Play mode only: it edits the
    /// volumes' runtime profile copies, never the profile assets.
    /// </summary>
    [DefaultExecutionOrder(1250)]
    [RequireComponent(typeof(Camera))]
    public class CameraAutoFocus : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Focus Target")]
        [Tooltip("Focus on this character's head centre (takes priority over Target)")]
        [SerializeField] private ToonCharacterLighting character;
        [Tooltip("Used when Character is empty or has no head")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.4f, 0f);

        [Header("Response")]
        [Tooltip("Focus pull half-life (s). Small = the focus never lags behind zooms")]
        [SerializeField] private float halfLife = 0.06f;
        [SerializeField] private float minFocusDistance = 0.3f;
        [Tooltip("Gaussian mode only: blur starts this far behind the focus point")]
        [SerializeField] private float gaussianStartBehind = 2f;

        private readonly List<DepthOfField> depthOfFields = new List<DepthOfField>();
        private readonly List<float> gaussianRanges = new List<float>();
        private float focus;
        private float focusVelocity;
        private bool hasFocus;
        #endregion

        #region [Properties]
        public float FocusDistance => focus;
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            hasFocus = false;
            CollectDepthOfField();
        }

        private void LateUpdate()
        {
            if (depthOfFields.Count == 0 || !TryGetFocusPoint(out Vector3 point)) return;

            // DoF measures along the view axis, not straight-line distance.
            float distance = Mathf.Max(Vector3.Dot(point - transform.position, transform.forward), minFocusDistance);
            if (!hasFocus)
            {
                focus = distance;
                focusVelocity = 0f;
                hasFocus = true;
            }
            else
            {
                Spring.Float(ref focus, ref focusVelocity, distance, halfLife, Time.unscaledDeltaTime);
                focus = Mathf.Max(focus, minFocusDistance);
            }

            for (int i = 0; i < depthOfFields.Count; i++)
            {
                DepthOfField dof = depthOfFields[i];
                dof.focusDistance.Override(focus);
                if (dof.mode.value == DepthOfFieldMode.Gaussian)
                {
                    float start = focus + gaussianStartBehind;
                    dof.gaussianStart.Override(start);
                    dof.gaussianEnd.Override(start + gaussianRanges[i]);
                }
            }
        }
        #endregion

        #region [Private Methods]
        private void CollectDepthOfField()
        {
            depthOfFields.Clear();
            gaussianRanges.Clear();
            if (!Application.isPlaying) return;

            foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (volume.sharedProfile == null || !volume.sharedProfile.Has<DepthOfField>()) continue;
                if (volume.profile.TryGet(out DepthOfField dof))
                {
                    depthOfFields.Add(dof);
                    gaussianRanges.Add(Mathf.Max(dof.gaussianEnd.value - dof.gaussianStart.value, 1f));
                }
            }
        }

        private bool TryGetFocusPoint(out Vector3 point)
        {
            if (character != null && character.Head != null)
            {
                point = character.HeadCenter;
                return true;
            }

            if (target != null)
            {
                point = target.position + target.rotation * targetOffset;
                return true;
            }

            point = default;
            return false;
        }
        #endregion
    }
}
