using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Play-mode half of the direct State Tree editor: finds everything in the scene running
    /// the asset the window is showing — <see cref="StateTreeRunner"/>s AND
    /// <see cref="StateTreeContextHost"/> mounts (M10: a tree mounted on its context is watched
    /// exactly like a tree on a runner) — and reports which state is active, so the tree view
    /// can tint the row the game is actually in.
    ///
    /// MATCHING IS BY nodeId STRING, NEVER BY REFERENCE. <c>StateTreeRunner.StartTree</c> calls
    /// <c>StateTreeAsset.DeepCopy()</c>, so at runtime every node/task/condition the runner
    /// touches is an Instantiate() clone of the authored sub-asset. The authored node objects
    /// the window holds are never the ones being ticked; the only stable identity across the
    /// copy is <c>nodeId</c>. Runners are matched to the window's asset through
    /// <c>runner.data</c>, which stays pointed at the authored asset.
    ///
    /// Both a push and a pull path exist on purpose: <c>activeNodeChanged</c> is exact but only
    /// fires while we are subscribed, and the first transition of a tree happens inside
    /// StartTree — typically before the window has found the runner. The poll (driven by the
    /// window's EditorApplication.update tick) closes that window and also covers a runner that
    /// is created, restarted or destroyed mid-session.
    /// </summary>
    internal sealed class StateTreeRunnerProbe
    {
        /// <summary>Rescan interval while playing. Cheap enough at authoring scale, and it is a
        /// scene scan, not a per-frame allocation in a build.</summary>
        private const double k_RescanInterval = 0.5d;

        private readonly List<Component> m_Runners = new List<Component>();

        private StateTreeAsset m_Tree;
        private Component m_Runner;
        private string m_ActiveNodeId = string.Empty;
        private string m_PreviousNodeId = string.Empty;
        private double m_NextRescan;

        /// <summary>Raised whenever the active state, the runner, or the runner list changes —
        /// the window repaints rows from this and nothing else.</summary>
        internal event Action changed;

        internal IReadOnlyList<Component> runners => m_Runners;

        internal Component runner => m_Runner;

        internal string activeNodeId => m_ActiveNodeId;

        internal string previousNodeId => m_PreviousNodeId;

        internal bool isTracking => EditorApplication.isPlaying && m_Runner != null;

        internal void SetTree(StateTreeAsset tree)
        {
            if (m_Tree == tree)
                return;

            m_Tree = tree;
            Clear();
            m_NextRescan = 0d;
        }

        /// <summary>Drop every subscription and highlight. Called on tree switch, on play-mode
        /// exit and on window disable — an editor object holding a live event handler on a
        /// scene component is how domain reloads start logging null references.</summary>
        internal void Clear()
        {
            Unsubscribe();
            m_Runner = null;
            m_Runners.Clear();
            m_ActiveNodeId = string.Empty;
            m_PreviousNodeId = string.Empty;
            changed?.Invoke();
        }

        /// <summary>Called from the window's editor-update tick.</summary>
        internal void Poll()
        {
            if (!EditorApplication.isPlaying)
            {
                if (m_Runner != null || m_Runners.Count > 0 || m_ActiveNodeId.Length > 0)
                    Clear();
                return;
            }

            if (m_Tree == null)
                return;

            // Rescan on a timer even while no runner is bound: a per-tick scene scan looking for
            // a runner that does not exist is the one way this probe could cost anything.
            var now = EditorApplication.timeSinceStartup;
            if (now >= m_NextRescan)
            {
                m_NextRescan = now + k_RescanInterval;
                Rescan();
            }

            // Pull path: catches the entry transition that fired inside StartTree, before this
            // probe had anything to subscribe to.
            if (m_Runner != null && NodeOf(m_Runner) != m_ActiveNodeId)
                SetActiveNode(m_ActiveNodeId, NodeOf(m_Runner));
        }

        internal void SelectRunner(Component selected)
        {
            if (selected == m_Runner)
                return;

            Unsubscribe();
            m_Runner = selected;
            Subscribe();
            m_PreviousNodeId = string.Empty;
            m_ActiveNodeId = NodeOf(m_Runner);
            changed?.Invoke();
        }

        private void Rescan()
        {
            var previousCount = m_Runners.Count;
            m_Runners.Clear();

            // runner.data / host.tree is the AUTHORED asset (the deep copy lives in a private
            // field), so this is the one place a reference compare is correct.
            var foundRunners = UnityEngine.Object.FindObjectsByType<StateTreeRunner>(FindObjectsInactive.Include);
            for (var i = 0; i < foundRunners.Length; ++i)
            {
                if (foundRunners[i] != null && foundRunners[i].data == m_Tree)
                    m_Runners.Add(foundRunners[i]);
            }
            var foundHosts = UnityEngine.Object.FindObjectsByType<StateTreeContextHost>(FindObjectsInactive.Include);
            for (var i = 0; i < foundHosts.Length; ++i)
            {
                if (foundHosts[i] != null && foundHosts[i].tree == m_Tree)
                    m_Runners.Add(foundHosts[i]);
            }

            m_Runners.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            if (m_Runner == null || !m_Runners.Contains(m_Runner))
            {
                Unsubscribe();
                m_Runner = m_Runners.Count > 0 ? m_Runners[0] : null;
                Subscribe();
                m_PreviousNodeId = string.Empty;
                m_ActiveNodeId = NodeOf(m_Runner);
                changed?.Invoke();
            }
            else if (previousCount != m_Runners.Count)
            {
                changed?.Invoke();
            }
        }

        private void Subscribe()
        {
            if (m_Runner is StateTreeRunner runner)
                runner.activeNodeChanged += OnActiveNodeChanged;
            else if (m_Runner is StateTreeContextHost host)
                host.activeNodeChanged += OnActiveNodeChanged;
        }

        private void Unsubscribe()
        {
            if (m_Runner is StateTreeRunner runner)
                runner.activeNodeChanged -= OnActiveNodeChanged;
            else if (m_Runner is StateTreeContextHost host)
                host.activeNodeChanged -= OnActiveNodeChanged;
        }

        /// <summary>The active node of either mount flavor, empty for none — the one seam where
        /// the probe cares which kind it is watching.</summary>
        private static string NodeOf(Component mount)
        {
            if (mount is StateTreeRunner runner)
                return runner.activeNodeId ?? string.Empty;
            if (mount is StateTreeContextHost host)
                return host.activeNodeId ?? string.Empty;
            return string.Empty;
        }

        private void OnActiveNodeChanged(string previousNodeId, string activeNodeId)
        {
            SetActiveNode(previousNodeId, activeNodeId);
        }

        private void SetActiveNode(string previous, string active)
        {
            m_PreviousNodeId = previous ?? string.Empty;
            m_ActiveNodeId = active ?? string.Empty;
            changed?.Invoke();
        }
    }
}
