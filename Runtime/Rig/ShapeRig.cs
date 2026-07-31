using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Scene-side skeleton root: owns the bone Transform hierarchy generated from a
    /// RigAsset (the "Skeleton2D sibling" of the Godot plugin, as plain Transforms).
    /// The asset is authoring truth for REST; the Transforms are the live pose.
    /// Setup mode (editor) writes moved bones back into the asset's rests.
    ///
    /// Bones are ordinary, scene-saved GameObjects (unlike the generated meshes elsewhere in
    /// this package): the user selects and rotates them, and a saved scene must remember the
    /// pose. Only <see cref="SyncBones"/> creates or destroys them, and it is only ever called
    /// explicitly — never from OnValidate, where structural edits are illegal.
    ///
    /// Editor callers own Undo: this assembly cannot reference UnityEditor, so a bone created
    /// or destroyed here is not undo-tracked unless the tool registers it (see the report /
    /// RigShapeTool). Nothing here runs on its own — no [ExecuteAlways], no Update.
    /// </summary>
    public sealed class ShapeRig : MonoBehaviour
    {
        public RigAsset rig;

        /// <summary>Names this component created on its last sync. Serialized because pruning
        /// has to survive a domain reload: without the record, a bone dropped from the asset
        /// while the scene was closed is indistinguishable from a hand-added child.</summary>
        [SerializeField, HideInInspector] private List<string> m_ManagedBones = new List<string>();

        /// <summary>Create/refresh child bone Transforms from the asset's rest data —
        /// existing bones with matching names keep their current POSE, new bones spawn
        /// at rest, bones no longer in the asset are destroyed. Returns the name→
        /// Transform map. Editor and runtime callable.</summary>
        public Dictionary<string, Transform> SyncBones()
        {
            var map = new Dictionary<string, Transform>();
            List<RigAsset.RigBone> bones = rig != null ? rig.bones : null;

            // pass 1: find or create. Adoption is by NAME, the same invariant that lets a
            // shape survive a redraw — a bone the user renamed in the hierarchy is treated as
            // missing and respawned at rest.
            if (bones != null)
            {
                for (int i = 0; i < bones.Count; i++)
                {
                    string boneName = bones[i].name;
                    if (string.IsNullOrEmpty(boneName) || map.ContainsKey(boneName))
                        continue;           // duplicate names: first entry wins
                    Transform bone = FindBone(boneName);
                    if (bone == null)
                    {
                        var created = new GameObject(boneName);
                        created.layer = gameObject.layer;
                        bone = created.transform;
                        bone.SetParent(transform, false);
                        ApplyRest(bone, bones[i]);
                    }
                    map[boneName] = bone;
                }

                // pass 2: parenting, after every bone exists (the asset's order is not
                // required to be topological, and a re-rig can reparent an existing bone)
                for (int i = 0; i < bones.Count; i++)
                {
                    RigAsset.RigBone bone = bones[i];
                    if (string.IsNullOrEmpty(bone.name)
                        || !map.TryGetValue(bone.name, out Transform child))
                        continue;
                    Transform parent = transform;
                    if (bone.parentIndex >= 0 && bone.parentIndex < bones.Count)
                    {
                        string parentName = bones[bone.parentIndex].name;
                        if (!string.IsNullOrEmpty(parentName)
                            && map.TryGetValue(parentName, out Transform candidate)
                            && candidate != child && !IsAncestorOf(child, candidate))
                            parent = candidate;
                    }
                    if (child.parent != parent)
                        child.SetParent(parent, false);   // keep the LOCAL pose
                }
            }

            Prune(map);

            m_ManagedBones.Clear();
            if (bones != null)
            {
                for (int i = 0; i < bones.Count; i++)
                {
                    string boneName = bones[i].name;
                    if (!string.IsNullOrEmpty(boneName) && map.ContainsKey(boneName)
                        && !m_ManagedBones.Contains(boneName))
                        m_ManagedBones.Add(boneName);
                }
            }
            return map;
        }

        /// <summary>Find the scene Transform for a bone name; null when absent.</summary>
        public Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return null;
            return FindDescendant(transform, boneName);
        }

        /// <summary>Reset every bone Transform to its asset rest pose.</summary>
        public void ResetToRest()
        {
            if (rig == null || rig.bones == null)
                return;
            for (int i = 0; i < rig.bones.Count; i++)
            {
                RigAsset.RigBone bone = rig.bones[i];
                Transform t = FindBone(bone.name);
                if (t != null)
                    ApplyRest(t, bone);
            }
        }

        // --- internals -------------------------------------------------------------------

        private static void ApplyRest(Transform bone, RigAsset.RigBone rest)
        {
            bone.localPosition = new Vector3(rest.restPosition.x, rest.restPosition.y, 0f);
            bone.localRotation = Quaternion.Euler(0f, 0f, rest.restRotationDegrees);
            bone.localScale = Vector3.one;
        }

        private static Transform FindDescendant(Transform root, string boneName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == boneName)
                    return child;
                Transform found = FindDescendant(child, boneName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static bool IsAncestorOf(Transform ancestor, Transform node)
        {
            Transform walk = node;
            int guard = 0;
            while (walk != null && guard++ < 256)
            {
                if (walk == ancestor)
                    return true;
                walk = walk.parent;
            }
            return false;
        }

        /// <summary>Destroy the bones this component made that the asset no longer lists.
        /// Runs after the parenting pass, so a surviving bone has already moved out from under
        /// a doomed parent.</summary>
        private void Prune(Dictionary<string, Transform> map)
        {
            for (int i = 0; i < m_ManagedBones.Count; i++)
            {
                string boneName = m_ManagedBones[i];
                if (string.IsNullOrEmpty(boneName) || map.ContainsKey(boneName))
                    continue;
                Transform stale = FindBone(boneName);
                if (stale == null || stale == transform)
                    continue;
                if (Application.isPlaying)
                    Destroy(stale.gameObject);
                else
                    DestroyImmediate(stale.gameObject);
            }
        }
    }
}
