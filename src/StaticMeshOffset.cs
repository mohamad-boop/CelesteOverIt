using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CelesteOverIt
{
    /// <summary>
    /// Getting Over It's scenery is statically batched: its meshes are baked, in world space, into big combined
    /// meshes when the game is built, so moving an object's transform moves its collider but not its visuals.
    /// This hides a statically-batched renderer and redraws its slice of the combined mesh with an offset.
    /// </summary>
    public class StaticMeshOffset : MonoBehaviour
    {
        struct Part
        {
            public Mesh Mesh;
            public int SubMesh;
            public Material Material;
            public int Layer;
            public ShadowCastingMode Shadows;
            public bool ReceiveShadows;
            public Transform ProbeAnchor;
        }

        readonly List<Part> parts = new List<Part>();
        Matrix4x4 matrix = Matrix4x4.identity;

        /// <summary>Take over every statically-batched renderer under root; returns how many were taken over.</summary>
        public int Capture(Transform root, Vector3 offset)
        {
            matrix = Matrix4x4.Translate(offset);
            int taken = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.isPartOfStaticBatch) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    int sub = r.subMeshStartIndex + i;
                    if (mats[i] == null || sub >= mf.sharedMesh.subMeshCount) continue;
                    var p = new Part();
                    p.Mesh = mf.sharedMesh;           // the combined mesh: vertices are already in world space
                    p.SubMesh = sub;
                    p.Material = mats[i];
                    p.Layer = r.gameObject.layer;
                    p.Shadows = r.shadowCastingMode;
                    p.ReceiveShadows = r.receiveShadows;
                    p.ProbeAnchor = r.transform;
                    parts.Add(p);
                }
                r.enabled = false;
                taken++;
            }
            return taken;
        }

        void LateUpdate()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                Graphics.DrawMesh(p.Mesh, matrix, p.Material, p.Layer, null, p.SubMesh, null, p.Shadows, p.ReceiveShadows, p.ProbeAnchor, true);
            }
        }
    }
}
