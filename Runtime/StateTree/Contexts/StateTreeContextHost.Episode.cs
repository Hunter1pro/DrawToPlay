using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Where an episode's run stood in one level: the state, and whether it had finished
    /// there. Plain data, so a game's save carries it with the rest of that level.</summary>
    [Serializable]
    public sealed class StateTreeEpisodeCursor
    {
        public string node = "";
        public bool complete;

        public bool isEmpty => string.IsNullOrEmpty(node);

        public StateTreeEpisodeCursor Copy() => new StateTreeEpisodeCursor { node = node, complete = complete };
    }

    /// <summary>
    /// THE EPISODE SCOPE: a host of kind <see cref="StateTreeContextKind.Episode"/> lives with the
    /// root and holds a tree whose <see cref="IStateTreeLevel"/> nodes are the levels of the story.
    /// It never runs that tree on its own board. A level, once put back, joins it
    /// (<see cref="JoinEpisode"/>): the level's node runs on the LEVEL's board and spine — the
    /// facts its steps write are the level's, saved with it — and may not leave that node. When the
    /// level's host goes, the episode is told and keeps where the run stood, which the game's save carries and puts
    /// back (<see cref="RestoreCursor"/>) before the next join.
    ///
    /// A level may be VISITED more than once: several Level nodes may name it (<see cref="LevelVisits"/>). Where the
    /// run stood is kept per visit. A level joins its first visit not finished, in tree order — a visit is finished
    /// once its part is over, or once a later visit of the level has begun. The save still carries one cursor per
    /// level: the latest visit's (<see cref="CursorOf"/>), whose node says which visit it is, so a save from before
    /// visits is the first visit's.
    /// </summary>
    public sealed partial class StateTreeContextHost
    {
        /// <summary>Level-board key naming the kind of the episode's step running in that level (its
        /// roleKind, else its id) — absent while none runs. What a level's own tree steps aside for.</summary>
        public const string EpisodeStepKey = "episode:step";

        /// <summary>Where the run stood, per visit (its Level node's id).</summary>
        private readonly Dictionary<string, StateTreeEpisodeCursor> m_Cursors =
            new Dictionary<string, StateTreeEpisodeCursor>();

        /// <summary>Cursors restored for a level this tree has no node for, by the level's name.</summary>
        private readonly Dictionary<string, StateTreeEpisodeCursor> m_Unplaced =
            new Dictionary<string, StateTreeEpisodeCursor>();

        private StateTreeContextHost m_EpisodeLevel;
        private string m_EpisodeLevelName = "";
        private string m_EpisodeLevelNode = "";
        private string m_PublishedStep;

        /// <summary>The level whose part of the episode is running, or empty.</summary>
        public string episodeLevel => m_EpisodeLevelName;

        /// <summary>The visit running — its Level node's id — or empty.</summary>
        public string episodeVisit => m_EpisodeLevelNode;

        /// <summary>
        /// The level <paramref name="level"/> is up and put back: its node runs on
        /// <paramref name="levelHost"/>'s board, from where it last stood. False when this tree has
        /// no node for that level — nothing runs.
        /// </summary>
        public bool JoinEpisode(string level, StateTreeContextHost levelHost)
        {
            if (kind != StateTreeContextKind.Episode || tree == null || levelHost == null)
                return false;
            if (!ReferenceEquals(m_EpisodeLevel, null))
                LeaveEpisode(m_EpisodeLevel);

            string levelNode = VisitToJoin(level);
            if (string.IsNullOrEmpty(levelNode))
                return false;

            StateTreeEpisodeCursor cursor = m_Cursors.TryGetValue(levelNode, out StateTreeEpisodeCursor kept)
                ? kept.Copy()
                : new StateTreeEpisodeCursor();
            string entry = levelNode;
            bool complete = false;
            if (!cursor.isEmpty && IsUnder(tree, cursor.node, levelNode))
            {
                entry = cursor.node;
                complete = cursor.complete;
            }

            m_EpisodeLevel = levelHost;
            m_EpisodeLevelName = level ?? "";
            m_EpisodeLevelNode = levelNode;
            StateTreeExecutor executor = EnsureExecutor();
            executor.data = tree;
            executor.parameterOverrides = parameterOverrides;
            executor.owner = levelHost.gameObject;
            executor.context = new StateTreeContext(levelHost.gameObject, levelHost.Context.blackboard);
            executor.boundaryNodeId = levelNode;
            executor.StartTree(entry, complete);
            AfterEpisodeTick();
            return true;
        }

        /// <summary>The joined level is going: where its run stands is kept, and it stops.</summary>
        public void LeaveEpisode(StateTreeContextHost levelHost)
        {
            // By reference: the level host may already be a destroyed object.
            if (ReferenceEquals(m_EpisodeLevel, null) || !ReferenceEquals(levelHost, m_EpisodeLevel))
                return;
            if (m_Executor != null && m_Executor.isRunning)
                Keep(m_EpisodeLevelNode, m_Executor.activeNodeId, m_Executor.activeNodeComplete);
            Unpublish();
            if (m_Executor != null)
            {
                m_Executor.StopTree();
                m_Executor.boundaryNodeId = "";
            }
            m_EpisodeLevel = null;
            m_EpisodeLevelName = "";
            m_EpisodeLevelNode = "";
        }

        /// <summary>Where the run stands in <paramref name="level"/> — live while it is the joined one, else its
        /// latest visit's. Empty: it has not run there.</summary>
        public StateTreeEpisodeCursor CursorOf(string level)
        {
            if (string.IsNullOrEmpty(level))
                return new StateTreeEpisodeCursor();
            if (level == m_EpisodeLevelName && m_Executor != null && m_Executor.isRunning)
                return new StateTreeEpisodeCursor
                {
                    node = m_Executor.activeNodeId, complete = m_Executor.activeNodeComplete
                };
            List<string> visits = LevelVisits.NodesOf(tree, level);
            for (int i = visits.Count - 1; i >= 0; i--)
            {
                if (m_Cursors.TryGetValue(visits[i], out StateTreeEpisodeCursor kept))
                    return kept.Copy();
            }
            return m_Unplaced.TryGetValue(level, out StateTreeEpisodeCursor unplaced)
                ? unplaced.Copy()
                : new StateTreeEpisodeCursor();
        }

        /// <summary>Where the run stands on one visit — its Level node's id. Empty: it has not run there.</summary>
        public StateTreeEpisodeCursor CursorOfVisit(string visit)
        {
            if (!string.IsNullOrEmpty(visit) && visit == m_EpisodeLevelNode && m_Executor != null && m_Executor.isRunning)
                return new StateTreeEpisodeCursor
                {
                    node = m_Executor.activeNodeId, complete = m_Executor.activeNodeComplete
                };
            return !string.IsNullOrEmpty(visit) && m_Cursors.TryGetValue(visit, out StateTreeEpisodeCursor kept)
                ? kept.Copy()
                : new StateTreeEpisodeCursor();
        }

        /// <summary>The save's word on where <paramref name="level"/>'s run stood: kept on the visit its node is in —
        /// the first visit when it is in none, which is every save from before visits. An empty cursor leaves what
        /// this session already knows.</summary>
        public void RestoreCursor(string level, StateTreeEpisodeCursor cursor)
        {
            if (string.IsNullOrEmpty(level) || cursor == null || cursor.isEmpty)
                return;
            string visit = VisitOfCursor(LevelVisits.NodesOf(tree, level), cursor);
            if (string.IsNullOrEmpty(visit))
                m_Unplaced[level] = cursor.Copy();
            else
                m_Cursors[visit] = cursor.Copy();
        }

        /// <summary>
        /// The visit <paramref name="level"/> joins next: its first Level node not finished, in tree order — finished
        /// when its part is over (<see cref="LevelVisits.IsOver"/>: it left, or rests at its end), or when a later visit
        /// has begun. All finished: the last, resting. With
        /// <paramref name="restoring"/>, as if that cursor were restored first — what a level's birth asks before its
        /// restore has run. Empty when the tree has no node for the level.
        /// </summary>
        public string VisitToJoin(string level, StateTreeEpisodeCursor restoring = null)
        {
            List<string> visits = LevelVisits.NodesOf(tree, level);
            if (visits.Count == 0)
                return "";
            string restored = restoring != null && !restoring.isEmpty ? VisitOfCursor(visits, restoring) : "";
            for (int i = visits.Count - 1; i >= 0; i--)
            {
                StateTreeEpisodeCursor at = visits[i] == restored ? restoring
                    : m_Cursors.TryGetValue(visits[i], out StateTreeEpisodeCursor kept) ? kept : null;
                if (at == null)
                    continue;
                return LevelVisits.IsOver(tree, visits[i], at) && i + 1 < visits.Count ? visits[i + 1] : visits[i];
            }
            return visits[0];
        }

        /// <summary>What the visit <paramref name="level"/> joins next adds and hides (<see cref="VisitToJoin"/>), or null
        /// for the plain level — what its scene builds.</summary>
        public LevelObjectRegistry VisitManifest(string level, StateTreeEpisodeCursor restoring = null)
        {
            return LevelVisits.ManifestOf(tree, VisitToJoin(level, restoring));
        }

        /// <summary>The visit a cursor's node is in, the first when it is in none; empty when there is no visit.</summary>
        private string VisitOfCursor(List<string> visits, StateTreeEpisodeCursor cursor)
        {
            if (visits.Count == 0)
                return "";
            string holding = LevelVisits.VisitHolding(tree, cursor.node);
            return visits.Contains(holding) ? holding : visits[0];
        }

        /// <summary>The authored id of the node that is <paramref name="level"/> — its first visit — or empty.</summary>
        public static string LevelNodeOf(StateTreeAsset episode, string level)
        {
            if (episode == null || episode.root == null || string.IsNullOrEmpty(level))
                return "";
            StateTreeNodeAsset found = FindLevelNode(episode.root, level, 0);
            return found != null ? found.nodeId : "";
        }

        private static StateTreeNodeAsset FindLevelNode(StateTreeNodeAsset node, string level, int depth)
        {
            if (node == null || depth > 64)
                return null;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                if (node.tasks[i] is IStateTreeLevel context && context.levelName == level)
                    return node;
            }
            for (int i = 0; i < node.children.Count; i++)
            {
                StateTreeNodeAsset found = FindLevelNode(node.children[i], level, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>Whether <paramref name="nodeId"/> is <paramref name="ancestorId"/> or lies under it.</summary>
        public static bool IsUnder(StateTreeAsset episode, string nodeId, string ancestorId)
        {
            if (episode == null || episode.root == null || string.IsNullOrEmpty(nodeId))
                return false;
            StateTreeNodeAsset ancestor = FindById(episode.root, ancestorId, 0);
            return ancestor != null && FindById(ancestor, nodeId, 0) != null;
        }

        private static StateTreeNodeAsset FindById(StateTreeNodeAsset node, string id, int depth)
        {
            if (node == null || depth > 64)
                return null;
            if (node.nodeId == id)
                return node;
            for (int i = 0; i < node.children.Count; i++)
            {
                StateTreeNodeAsset found = FindById(node.children[i], id, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>After each tick of a joined run: a run that stopped at its level's edge leaves the
        /// level resting and the next level's part waiting; the running step is said on the level's board.</summary>
        private void AfterEpisodeTick()
        {
            if (ReferenceEquals(m_EpisodeLevel, null) || m_Executor == null)
                return;
            if (!m_Executor.isRunning)
            {
                Keep(m_EpisodeLevelNode, m_EpisodeLevelNode, true);
                string next = m_Executor.leftFor;
                if (!string.IsNullOrEmpty(next))
                    Keep(LevelVisits.VisitHolding(tree, next), next, false);
                Unpublish();
                return;
            }
            StateTreeNodeAsset active = m_Executor.activeNode;
            string step = active != null && !m_Executor.activeNodeComplete && StateTreeExecutor.RunsTasks(active)
                ? (string.IsNullOrEmpty(active.roleKind) ? active.nodeId : active.roleKind)
                : null;
            if (step == m_PublishedStep)
                return;
            Dictionary<string, object> board = m_EpisodeLevel.Context.blackboard;
            if (step == null)
                board.Remove(EpisodeStepKey);
            else
                board[EpisodeStepKey] = step;
            m_PublishedStep = step;
        }

        private void Unpublish()
        {
            if (m_PublishedStep != null && !ReferenceEquals(m_EpisodeLevel, null))
                m_EpisodeLevel.Context.blackboard.Remove(EpisodeStepKey);
            m_PublishedStep = null;
        }

        private void Keep(string visit, string node, bool complete)
        {
            if (string.IsNullOrEmpty(visit) || string.IsNullOrEmpty(node))
                return;
            m_Cursors[visit] = new StateTreeEpisodeCursor { node = node, complete = complete };
        }

        /// <summary>A level host going tells every episode it joined.</summary>
        private void TellEpisodesGone()
        {
            for (int i = s_Registered.Count - 1; i >= 0; i--)
            {
                StateTreeContextHost episode = s_Registered[i];
                if (episode != null && episode.kind == StateTreeContextKind.Episode && episode.m_EpisodeLevel == this)
                    episode.LeaveEpisode(this);
            }
        }
    }
}
