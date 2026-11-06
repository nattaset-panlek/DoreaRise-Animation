using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DoreaRise.Rendering
{
    /// <summary>
    /// Bakes averaged ("smooth") normals into UV channel 7 of the character meshes on import. They are stored in
    /// tangent space, so they follow skinning; the toon outline pass pushes its hull along them, which keeps hard
    /// edges and UV seams from tearing the outline open.
    /// </summary>
    public class ToonOutlineNormalBaker : AssetPostprocessor
    {
        private const int Channel = 7;
        private const string CharacterFolder = "Assets/_DoreaRise/Characters/";

        // Bump to re-bake every character model.
        public override uint GetVersion() => 1;

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(CharacterFolder)) return;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                Bake(filter.sharedMesh);
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Bake(skin.sharedMesh);
        }

        public static bool Bake(Mesh mesh)
        {
            if (mesh == null) return false;

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            if (vertices.Length == 0 || normals.Length != vertices.Length || tangents.Length != vertices.Length)
                return false;

            // Sum the normals of every vertex at the same position (split by hard edges or UV seams).
            float quantum = Mathf.Max(mesh.bounds.size.magnitude * 1e-5f, 1e-8f);
            var sums = new Dictionary<Vector3Int, Vector3>(vertices.Length);
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i] / quantum;
                Vector3Int key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                keys[i] = key;
                sums.TryGetValue(key, out Vector3 sum);
                sums[key] = sum + normals[i];
            }

            var smooth = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals[i];
                Vector3 average = sums[keys[i]];
                average = average.sqrMagnitude > 1e-12f ? average.normalized : normal;

                Vector3 tangent = tangents[i];
                tangent = (tangent - normal * Vector3.Dot(normal, tangent)).normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent) * tangents[i].w;
                smooth.Add(new Vector3(Vector3.Dot(average, tangent), Vector3.Dot(average, bitangent), Vector3.Dot(average, normal)));
            }

            mesh.SetUVs(Channel, smooth);
            return true;
        }

        [MenuItem("Tools/DoreaRise/Rendering/Rebake Outline Normals (reimport character models)")]
        private static void ReimportCharacterModels()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { CharacterFolder.TrimEnd('/') });
            foreach (string guid in guids)
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log($"[ToonOutlineNormalBaker] Reimported {guids.Length} character model(s).");
        }
    }
}
