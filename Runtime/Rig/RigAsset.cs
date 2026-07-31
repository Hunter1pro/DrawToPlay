using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The rig as data (draw-tool-port-brief.md §3): a flat bone tree with local REST
    /// poses and lengths. Scene bones are plain Transforms created from this asset by
    /// ShapeRig; binding and weights always evaluate against REST (never the current
    /// pose), and animation/binding reference bones by NAME — the two invariants that
    /// make redraw and re-pose safe. No Skeleton2D equivalent is needed.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Rig", fileName = "Rig")]
    public sealed class RigAsset : ScriptableObject
    {
        [Serializable]
        public struct RigBone
        {
            public string name;
            /// <summary>Index into bones; -1 = child of the rig root.</summary>
            public int parentIndex;
            /// <summary>Rest pose local to the parent bone (or rig root).</summary>
            public Vector2 restPosition;
            public float restRotationDegrees;
            public float length;
        }

        public List<RigBone> bones = new List<RigBone>();

        public int IndexOf(string boneName)
        {
            for (int i = 0; i < bones.Count; i++)
                if (bones[i].name == boneName)
                    return i;
            return -1;
        }

        /// <summary>Rest pose of a bone accumulated to rig-root space — the port of
        /// _bone_rest_global's chain walk (curve_shape_2d.gd lines 649-655), minus the
        /// scene transform which callers apply themselves.</summary>
        public Matrix4x4 RestRootMatrix(int index)
        {
            Matrix4x4 m = Matrix4x4.identity;
            int guard = 0;
            while (index >= 0 && guard++ < 256)
            {
                var b = bones[index];
                m = Matrix4x4.TRS(b.restPosition, Quaternion.Euler(0f, 0f, b.restRotationDegrees), Vector3.one) * m;
                index = b.parentIndex;
            }
            return m;
        }
    }
}
