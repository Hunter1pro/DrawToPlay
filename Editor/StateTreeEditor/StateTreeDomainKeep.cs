using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// What a build script that RE-AUTHORS a tree must leave alone: the domain nodes a designer made
    /// in the tree's schema mode, with every task and condition they own. A builder that clears its
    /// tree asks <see cref="Detach"/> first, skips what <see cref="Keeps"/> names while it destroys
    /// sub-assets, and calls <see cref="Reattach"/> on the new root — so Verify rebuilds the
    /// programmer's states and the mission stays the designer's.
    ///
    /// A kept node is a direct child of the root carrying a <see cref="StateTreeNodeAsset.roleKind"/>
    /// (the schema's kinds; a builder that types its own states' roles keeps them by not asking).
    /// They come back as the root's last children, in their order — a schema's top nodes are groups,
    /// never entered by sibling order.
    /// </summary>
    public sealed class StateTreeDomainKeep
    {
        private readonly List<StateTreeNodeAsset> m_Roots = new List<StateTreeNodeAsset>();
        private readonly HashSet<Object> m_Kept = new HashSet<Object>();

        public IReadOnlyList<StateTreeNodeAsset> roots => m_Roots;

        public int Count => m_Roots.Count;

        public static StateTreeDomainKeep Detach(StateTreeAsset tree)
        {
            var keep = new StateTreeDomainKeep();
            if (tree == null || tree.root == null)
                return keep;
            for (int i = 0; i < tree.root.children.Count; i++)
            {
                StateTreeNodeAsset child = tree.root.children[i];
                if (child != null && !string.IsNullOrEmpty(child.roleKind))
                {
                    keep.m_Roots.Add(child);
                    keep.Own(child, 0);
                }
            }
            return keep;
        }

        /// <summary>Whether a sub-asset belongs to a kept node and must survive the rebuild.</summary>
        public bool Keeps(Object subAsset)
        {
            return subAsset != null && m_Kept.Contains(subAsset);
        }

        /// <summary>Hang the kept nodes back under the rebuilt root, after its own children.</summary>
        public void Reattach(StateTreeNodeAsset root)
        {
            if (root == null)
                return;
            for (int i = 0; i < m_Roots.Count; i++)
            {
                if (m_Roots[i] != null && !root.children.Contains(m_Roots[i]))
                    root.children.Add(m_Roots[i]);
            }
        }

        private void Own(StateTreeNodeAsset node, int depth)
        {
            if (node == null || depth > StateTreeEditorOps.DepthGuard || !m_Kept.Add(node))
                return;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                if (node.tasks[i] != null)
                    m_Kept.Add(node.tasks[i]);
            }
            for (int i = 0; i < node.transitions.Count; i++)
                OwnCondition(node.transitions[i]?.condition, 0);
            for (int i = 0; i < node.children.Count; i++)
                Own(node.children[i], depth + 1);
        }

        /// <summary>A condition and whatever it composes — the same walk the deep copy makes.</summary>
        private void OwnCondition(StateTreeConditionAsset condition, int depth)
        {
            if (condition == null || depth > 16 || !m_Kept.Add(condition))
                return;
            foreach (FieldInfo field in condition.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (typeof(StateTreeConditionAsset).IsAssignableFrom(field.FieldType))
                    OwnCondition(field.GetValue(condition) as StateTreeConditionAsset, depth + 1);
                else if (field.GetValue(condition) is List<StateTreeConditionAsset> held)
                {
                    for (int i = 0; i < held.Count; i++)
                        OwnCondition(held[i], depth + 1);
                }
            }
        }
    }
}
