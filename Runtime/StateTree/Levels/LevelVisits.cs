using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A Level node that may be a VISIT: several Level nodes of an episode may name the same level, each its own part
    /// of the story there. <see cref="visitManifest"/> is what this visit adds and hides
    /// (<see cref="LevelObjectRegistry.visitOf"/>); none, or the level's own manifest, is the plain level.
    /// </summary>
    public interface IStateTreeLevelVisit : IStateTreeLevel
    {
        LevelObjectRegistry visitManifest { get; set; }
    }

    /// <summary>The visits of an episode tree: which Level nodes name a level, which one holds a node, and what each
    /// stands in the level.</summary>
    public static class LevelVisits
    {
        /// <summary>The Level nodes naming <paramref name="level"/>, in tree order — its visits.</summary>
        public static List<string> NodesOf(StateTreeAsset episode, string level)
        {
            var into = new List<string>();
            if (episode != null && !string.IsNullOrEmpty(level))
                Collect(episode.root, level, into, 0);
            return into;
        }

        private static void Collect(StateTreeNodeAsset node, string level, List<string> into, int depth)
        {
            if (node == null || depth > 64)
                return;
            if (LevelNameOf(node) == level)
                into.Add(node.nodeId);
            for (int i = 0; i < node.children.Count; i++)
                Collect(node.children[i], level, into, depth + 1);
        }

        /// <summary>The level a node is, by its <see cref="IStateTreeLevel"/> context; null when it is no Level.</summary>
        public static string LevelNameOf(StateTreeNodeAsset node)
        {
            for (int i = 0; node != null && i < node.tasks.Count; i++)
            {
                if (node.tasks[i] is IStateTreeLevel context)
                    return context.levelName;
            }
            return null;
        }

        /// <summary>The visit's own manifest — what it adds and hides — or null for the plain level.</summary>
        public static LevelObjectRegistry ManifestOf(StateTreeNodeAsset node)
        {
            for (int i = 0; node != null && i < node.tasks.Count; i++)
            {
                if (node.tasks[i] is IStateTreeLevelVisit visit && visit.visitManifest != null && visit.visitManifest.isVisit)
                    return visit.visitManifest;
            }
            return null;
        }

        /// <summary>The own manifest of the visit <paramref name="visitNode"/> (a Level node's id), or null for the plain
        /// level.</summary>
        public static LevelObjectRegistry ManifestOf(StateTreeAsset episode, string visitNode)
        {
            return episode != null && !string.IsNullOrEmpty(visitNode) ? ManifestOf(Find(episode.root, visitNode, 0)) : null;
        }

        private static StateTreeNodeAsset Find(StateTreeNodeAsset node, string id, int depth)
        {
            if (node == null || depth > 64)
                return null;
            if (node.nodeId == id)
                return node;
            for (int i = 0; i < node.children.Count; i++)
            {
                StateTreeNodeAsset found = Find(node.children[i], id, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>The Level node of <paramref name="episode"/> that holds <paramref name="nodeId"/> (or is it); empty
        /// when none does.</summary>
        public static string VisitHolding(StateTreeAsset episode, string nodeId)
        {
            return episode != null && !string.IsNullOrEmpty(nodeId) ? Holding(episode.root, nodeId, "", 0) : "";
        }

        private static string Holding(StateTreeNodeAsset node, string nodeId, string visit, int depth)
        {
            if (node == null || depth > 64)
                return "";
            if (LevelNameOf(node) != null)
                visit = node.nodeId;
            if (node.nodeId == nodeId)
                return visit;
            for (int i = 0; i < node.children.Count; i++)
            {
                string found = Holding(node.children[i], nodeId, visit, depth + 1);
                if (!string.IsNullOrEmpty(found))
                    return found;
            }
            return "";
        }

        /// <summary>
        /// Whether a visit's part is over at <paramref name="cursor"/>: done there, and the run, going on, would find
        /// nothing more to run inside the visit — no later sibling on the way up, no edge out of it, or a state that
        /// rests (a Level rests where its run ends). The completion flow the executor follows, read without running it.
        /// </summary>
        public static bool IsOver(StateTreeAsset episode, string visit, StateTreeEpisodeCursor cursor)
        {
            if (episode == null || cursor == null || !cursor.complete || string.IsNullOrEmpty(visit))
                return false;
            if (cursor.node == visit)
                return true;
            var path = new List<StateTreeNodeAsset>();
            if (!PathTo(episode.root, cursor.node, path, 0))
                return false;
            StateTreeNodeAsset node = path[path.Count - 1];
            if (HasEdge(node))
                return false;
            if (node.completionFlow == StateTreeCompletionFlow.Hold)
                return true;
            for (int i = path.Count - 2; i >= 0; i--)
            {
                StateTreeNodeAsset parent = path[i];
                if (node.completionFlow != StateTreeCompletionFlow.EndsParent && LaterSibling(parent, node))
                    return false;
                if (parent.nodeId == visit)
                    return true;
                if (node.completionFlow != StateTreeCompletionFlow.EndsParent && HasEdge(parent))
                    return false;
                if (parent.completionFlow == StateTreeCompletionFlow.Hold)
                    return true;
                node = parent;
            }
            return true;
        }

        private static bool HasEdge(StateTreeNodeAsset node)
        {
            foreach (StateTreeTransition transition in node.transitions)
            {
                if (transition != null && !transition.checkWhileRunning)
                    return true;
            }
            return false;
        }

        /// <summary>A sibling after <paramref name="node"/> the run would go into — one switched off is passed through.</summary>
        private static bool LaterSibling(StateTreeNodeAsset parent, StateTreeNodeAsset node)
        {
            int slot = parent.children.IndexOf(node);
            for (int i = slot + 1; slot >= 0 && i < parent.children.Count; i++)
            {
                if (parent.children[i] != null && !parent.children[i].disabled)
                    return true;
            }
            return false;
        }

        private static bool PathTo(StateTreeNodeAsset node, string id, List<StateTreeNodeAsset> path, int depth)
        {
            if (node == null || depth > 64)
                return false;
            path.Add(node);
            if (node.nodeId == id)
                return true;
            for (int i = 0; i < node.children.Count; i++)
            {
                if (PathTo(node.children[i], id, path, depth + 1))
                    return true;
            }
            path.RemoveAt(path.Count - 1);
            return false;
        }

        /// <summary>The level whose Level nodes build on <paramref name="manifest"/> — carry it, or a visit of it —
        /// or null. How a level's scene finds its name in the episode from the manifest it spawns.</summary>
        public static string LevelOfManifest(StateTreeAsset episode, LevelObjectRegistry manifest)
        {
            return episode != null && manifest != null ? Carrying(episode.root, manifest, 0) : null;
        }

        private static string Carrying(StateTreeNodeAsset node, LevelObjectRegistry manifest, int depth)
        {
            if (node == null || depth > 64)
                return null;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                if (node.tasks[i] is IStateTreeLevelVisit visit && visit.visitManifest != null
                    && (visit.visitManifest == manifest || visit.visitManifest.visitOf == manifest))
                    return visit.levelName;
            }
            for (int i = 0; i < node.children.Count; i++)
            {
                string found = Carrying(node.children[i], manifest, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>What stands in a level on a visit: <paramref name="level"/>'s rows less what
        /// <paramref name="visit"/> hides, then the visit's own. No visit (or not one): the level's rows.</summary>
        public static void Rows(LevelObjectRegistry level, LevelObjectRegistry visit, List<LevelObjectDef> into)
        {
            if (into == null)
                return;
            if (visit != null && visit.isVisit)
            {
                visit.CollectRows(into);
                return;
            }
            if (level != null)
                level.CollectRows(into);
        }
    }
}
