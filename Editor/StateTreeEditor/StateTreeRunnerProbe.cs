using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Play-mode half of the direct State Tree editor: finds the <see cref="StateTreeRunner"/>s
    /// running the asset the window is showing and reports which state is active, so the tree
    /// view can tint the row the game is actually in.
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

        private readonly List<StateTreeRunner> m_Runners = new List<StateTreeRunner>();

        private StateTreeAsset m_Tree;
        private StateTreeRunner m_Runner;
        private string m_ActiveNodeId = string.Empty;
        private string m_PreviousNodeId = string.Empty;
        private double m_NextRescan;

        /// <summary>Raised whenever the active state, the runner, or the runner list changes —
        /// the window repaints rows from this and nothing else.</summary>
        internal event Action changed;

        internal IReadOnlyList<StateTreeRunner> runners => m_Runners;

        internal StateTreeRunner runner => m_Runner;

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
            if (m_Runner != null && m_Runner.activeNodeId != m_ActiveNodeId)
                SetActiveNode(m_ActiveNodeId, m_Runner.activeNodeId);
        }

        internal void SelectRunner(StateTreeRunner selected)
        {
            if (selected == m_Runner)
                return;

            Unsubscribe();
            m_Runner = selected;
            Subscribe();
            m_PreviousNodeId = string.Empty;
            m_ActiveNodeId = m_Runner != null ? m_Runner.activeNodeId : string.Empty;
            changed?.Invoke();
        }

        private void Rescan()
        {
            var previousCount = m_Runners.Count;
            m_Runners.Clear();

            var found = UnityEngine.Object.FindObjectsByType<StateTreeRunner>(FindObjectsInactive.Include);
            for (var i = 0; i < found.Length; ++i)
            {
                // runner.data is the AUTHORED asset (the deep copy lives in a private field), so
                // this is the one place a reference compare is correct.
                if (found[i] != null && found[i].data == m_Tree)
                    m_Runners.Add(found[i]);
            }

            m_Runners.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            if (m_Runner == null || !m_Runners.Contains(m_Runner))
            {
                Unsubscribe();
                m_Runner = m_Runners.Count > 0 ? m_Runners[0] : null;
                Subscribe();
                m_PreviousNodeId = string.Empty;
                m_ActiveNodeId = m_Runner != null ? m_Runner.activeNodeId : string.Empty;
                changed?.Invoke();
            }
            else if (previousCount != m_Runners.Count)
            {
                changed?.Invoke();
            }
        }

        private void Subscribe()
        {
            if (m_Runner != null)
                m_Runner.activeNodeChanged += OnActiveNodeChanged;
        }

        private void Unsubscribe()
        {
            if (m_Runner != null)
                m_Runner.activeNodeChanged -= OnActiveNodeChanged;
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
