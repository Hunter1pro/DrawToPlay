using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A PREFAB AS DRAWABLE PARTS — the geometry the manifest overlay ghosts, including the part
    /// that cannot simply be read off the asset.
    ///
    /// STATIC MESHES ARE EASY and were always drawn: a MeshFilter's sharedMesh is the thing, and
    /// its transform relative to the prefab root places it.
    ///
    /// A RIGGED CHARACTER IS NOT. A skinned mesh's vertices live in bind space — M21's mannequin
    /// measures 2cm and is flat in one axis — and the pose is built by 67 bones the asset does not
    /// apply for you. Drawn straight it rasterises as a vertical sliver, which is how this started:
    /// a "hologram" that looked like a rendering fault.
    ///
    /// SO IT IS POSED, ONCE, IN A PREVIEW SCENE. EditorSceneManager.NewPreviewScene is the sanctioned
    /// place to instantiate something the user must never see — it is not the open scene, nothing is
    /// saved, nothing is selectable, and it is closed in the same call. Inside it
    /// SkinnedMeshRenderer.BakeMesh snapshots the rest pose, and the result is cached, so a level
    /// with forty guards bakes one mannequin.
    ///
    /// THE MATRIX IS ROTATION AND POSITION ONLY, measured rather than reasoned: BakeMesh(mesh) —
    /// the overload WITHOUT useScale — already returns the mesh at world size, while the renderer's
    /// own transform on this asset carries a ×100 import scale. Applying that scale gives a
    /// 180-unit giant; dropping it gives 1.80 units standing on the origin, which matches the
    /// posed renderer's own bounds and the rig's 1.66-unit bone spread.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelGhostMeshes
    {
        static LevelGhostMeshes()
        {
            // A baked mesh is a real object marked HideAndDontSave, which means it survives a
            // domain reload with nothing left holding it — the cache that knew about it is gone.
            // Dropped deliberately on the way out, so recompiling twenty times in a session does
            // not leave twenty mannequins in memory.
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
        }

        /// <summary>One drawable piece of a prefab: a mesh and where it sits relative to the
        /// prefab's root.</summary>
        public readonly struct Part
        {
            public readonly Mesh mesh;
            public readonly Matrix4x4 local;

            public Part(Mesh mesh, Matrix4x4 local)
            {
                this.mesh = mesh;
                this.local = local;
            }
        }

        /// <summary>
        /// The prefab's drawable parts, built on first ask and kept.
        /// </summary>
        /// <param name="prefab">The prefab a kind names, or null.</param>
        /// <returns>Never null; empty when the prefab has no geometry at all (a look that is all
        /// particles, sprites or code), which is the caller's cue to draw a stand-in.</returns>
        public static IReadOnlyList<Part> Of(GameObject prefab)
        {
            if (prefab == null)
                return s_None;
            if (s_Cache.TryGetValue(prefab, out List<Part> cached))
                return cached;

            var parts = new List<Part>();
            Collect(prefab, parts);
            s_Cache[prefab] = parts;
            return parts;
        }

        /// <summary>Forget everything, so a prefab edited while the overlay is open shows its new
        /// shape. Cheap to call — the next draw rebuilds only what it asks for.</summary>
        public static void Clear()
        {
            for (int i = 0; i < s_Baked.Count; i++)
            {
                if (s_Baked[i] != null)
                    Object.DestroyImmediate(s_Baked[i]);
            }
            s_Baked.Clear();
            s_Cache.Clear();
        }

        private static void Collect(GameObject prefab, List<Part> into)
        {
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null)
                    continue;
                into.Add(new Part(filters[i].sharedMesh,
                    rootInverse * filters[i].transform.localToWorldMatrix));
            }

            SkinnedMeshRenderer[] skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var rigged = false;
            for (int i = 0; i < skins.Length && !rigged; i++)
                rigged = skins[i].sharedMesh != null;
            if (rigged)
                CollectPosed(prefab, into);
        }

        /// <summary>
        /// The rigged half — see the class remarks. One instantiate, one bake per renderer, one
        /// scene closed.
        /// </summary>
        /// <param name="prefab">The prefab to pose.</param>
        /// <param name="into">Accumulator.</param>
        private static void CollectPosed(GameObject prefab, List<Part> into)
        {
            Scene preview = default;
            GameObject instance = null;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                instance = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(instance, preview);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                Matrix4x4 rootInverse = instance.transform.worldToLocalMatrix;
                SkinnedMeshRenderer[] skins =
                    instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < skins.Length; i++)
                {
                    if (skins[i].sharedMesh == null)
                        continue;

                    var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    skins[i].BakeMesh(baked);
                    s_Baked.Add(baked);

                    Matrix4x4 place = rootInverse * skins[i].transform.localToWorldMatrix;
                    into.Add(new Part(baked, Matrix4x4.TRS(place.GetColumn(3), place.rotation,
                        Vector3.one)));
                }
            }
            catch (System.Exception error)
            {
                // A prefab this cannot pose is not worth an exception in the middle of a repaint;
                // the caller falls back to the stand-in, which is the honest picture anyway.
                Debug.LogWarning("[LevelGhostMeshes] could not pose '" + prefab.name + "': "
                    + error.Message);
            }
            finally
            {
                if (instance != null)
                    Object.DestroyImmediate(instance);
                if (preview.IsValid())
                    EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static readonly Dictionary<GameObject, List<Part>> s_Cache =
            new Dictionary<GameObject, List<Part>>();

        /// <summary>Meshes this made, so they can be destroyed rather than leaked — a baked mesh
        /// is a real object and nothing else owns it.</summary>
        private static readonly List<Mesh> s_Baked = new List<Mesh>();

        private static readonly List<Part> s_None = new List<Part>();
    }
}
