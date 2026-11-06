using System.Collections.Generic;
using UnityEngine;

namespace DoreaRise.Rendering
{
    /// <summary>
    /// Feeds the head frame (centre, forward, up) to the "DoreaRise/Anime Toon" materials on this character, for
    /// the texture-free face shadow and the hair angel ring. Runs after animation and every rig layer, and in edit
    /// mode, so the face shades the same in the Scene view as in Play mode.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(1150)]
    public class ToonCharacterLighting : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Head")]
        [Tooltip("Head bone. Found by name under this object when empty")]
        [SerializeField] private Transform head;
        [SerializeField] private string headBoneName = "Head";
        [Tooltip("Head centre from the head bone, in metres along the head's up axis")]
        [SerializeField] private float centerUp = 0.09f;
        [Tooltip("Head centre from the head bone, in metres along the head's forward axis")]
        [SerializeField] private float centerForward = 0.02f;

        [Header("Renderers")]
        [Tooltip("Renderers to feed. Empty = every renderer under this object that uses the toon shader")]
        [SerializeField] private Renderer[] renderers = new Renderer[0];

        private const string ToonShaderName = "DoreaRise/Anime Toon";
        private static readonly int HeadCenterId = Shader.PropertyToID("_ToonHeadCenter");
        private static readonly int HeadForwardId = Shader.PropertyToID("_ToonHeadForward");
        private static readonly int HeadUpId = Shader.PropertyToID("_ToonHeadUp");
        private static readonly int FaceMaskInnerId = Shader.PropertyToID("_FaceMaskInner");
        private static readonly int FaceMaskOuterId = Shader.PropertyToID("_FaceMaskOuter");

        private readonly List<Renderer> targets = new List<Renderer>();
        private MaterialPropertyBlock block;
        private Vector3 headLocalForward;
        private Vector3 headLocalUp;
        private bool hasAxes;
        #endregion

        #region [Properties]
        public Transform Head => head;
        public Vector3 HeadCenter { get; private set; }
        public Vector3 HeadForward { get; private set; }
        public Vector3 HeadUp { get; private set; }
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            hasAxes = false;
            CollectTargets();
        }

        private void OnDisable()
        {
            // w = 0 tells the shader there is no head data: face and hair fall back to plain shading.
            Apply(Vector4.zero, Vector4.zero, Vector4.zero);
        }

        private void OnValidate()
        {
            hasAxes = false;
            CollectTargets();
        }

        private void LateUpdate()
        {
            if (head == null)
            {
                head = FindDeep(transform, headBoneName);
                if (head == null) return;
                hasAxes = false;
            }

            if (!hasAxes) ResolveHeadAxes();

            HeadUp = head.rotation * headLocalUp;
            HeadForward = head.rotation * headLocalForward;
            HeadCenter = head.position + HeadUp * centerUp + HeadForward * centerForward;

            Vector3 center = HeadCenter;
            Apply(new Vector4(center.x, center.y, center.z, 1f), HeadForward, HeadUp);
        }

        private void OnDrawGizmosSelected()
        {
            if (head == null) return;

            float inner = 0.13f;
            float outer = 0.17f;
            Material face = FindFaceMaterial();
            if (face != null)
            {
                inner = face.GetFloat(FaceMaskInnerId);
                outer = face.GetFloat(FaceMaskOuterId);
            }

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(HeadCenter, inner);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(HeadCenter, outer);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(HeadCenter, HeadCenter + HeadForward * 0.25f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(HeadCenter, HeadCenter + HeadUp * 0.2f);
        }
        #endregion

        #region [Public Methods]
        /// <summary>Re-reads the renderer list and the head's rest axes (call after swapping the model).</summary>
        public void Refresh()
        {
            hasAxes = false;
            CollectTargets();
        }
        #endregion

        #region [Private Methods]
        private void CollectTargets()
        {
            targets.Clear();
            Renderer[] source = renderers != null && renderers.Length > 0
                ? renderers
                : GetComponentsInChildren<Renderer>(true);

            foreach (Renderer candidate in source)
            {
                if (candidate != null && UsesToonShader(candidate))
                    targets.Add(candidate);
            }
        }

        private static bool UsesToonShader(Renderer target)
        {
            foreach (Material material in target.sharedMaterials)
            {
                if (material != null && material.shader != null && material.shader.name == ToonShaderName)
                    return true;
            }
            return false;
        }

        private Material FindFaceMaterial()
        {
            foreach (Renderer target in targets)
            {
                if (target == null) continue;
                foreach (Material material in target.sharedMaterials)
                {
                    if (material != null && material.IsKeywordEnabled("_FACE_SHADING"))
                        return material;
                }
            }
            return null;
        }

        /// <summary>
        /// The head's forward/up relative to its own rotation, measured from the skinned mesh bind pose, where the
        /// head is not animated and faces the character's forward. Falls back to the current pose.
        /// </summary>
        private void ResolveHeadAxes()
        {
            Quaternion restRotation = head.rotation;
            if (TryGetBindRotation(head, out Quaternion bindRotation))
                restRotation = bindRotation;

            Quaternion toHead = Quaternion.Inverse(restRotation);
            headLocalForward = toHead * transform.forward;
            headLocalUp = toHead * transform.up;
            hasAxes = true;
        }

        private bool TryGetBindRotation(Transform bone, out Quaternion rotation)
        {
            foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skin.sharedMesh;
                if (mesh == null) continue;

                Transform[] bones = skin.bones;
                Matrix4x4[] bindposes = mesh.bindposes;
                for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
                {
                    if (bones[i] != bone) continue;
                    Matrix4x4 boneAtBind = skin.transform.localToWorldMatrix * bindposes[i].inverse;
                    rotation = boneAtBind.rotation;
                    return true;
                }
            }

            rotation = Quaternion.identity;
            return false;
        }

        private void Apply(Vector4 center, Vector4 forward, Vector4 up)
        {
            if (block == null) block = new MaterialPropertyBlock();

            foreach (Renderer target in targets)
            {
                if (target == null) continue;
                target.GetPropertyBlock(block);
                block.SetVector(HeadCenterId, center);
                block.SetVector(HeadForwardId, forward);
                block.SetVector(HeadUpId, up);
                target.SetPropertyBlock(block);
            }
        }

        private static Transform FindDeep(Transform parent, string boneName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == boneName) return child;
                Transform found = FindDeep(child, boneName);
                if (found != null) return found;
            }
            return null;
        }
        #endregion
    }
}
