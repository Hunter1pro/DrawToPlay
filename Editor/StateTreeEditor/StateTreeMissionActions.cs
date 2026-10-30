using System;
using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>What a mission action's button says on a node, and whether it may be pressed now.</summary>
    public readonly struct StateTreeMissionOffer
    {
        public StateTreeMissionOffer(string label, string tooltip, string blocked = null)
        {
            this.label = label;
            this.tooltip = tooltip;
            this.blocked = blocked;
        }

        public string label { get; }

        public string tooltip { get; }

        /// <summary>Why it cannot run now (the button is shown greyed), or null.</summary>
        public string blocked { get; }
    }

    /// <summary>
    /// A game's button on a mission node — open the level, play from a step. The package knows no level and no save:
    /// it hands the action the node and the way to it from the root, and draws the button where the action offers one.
    /// </summary>
    public interface IStateTreeMissionAction
    {
        /// <summary>False: no button on this node.</summary>
        bool Offers(StateTreeMissionPath at, out StateTreeMissionOffer offer);

        void Run(StateTreeMissionPath at);
    }

    /// <summary>The actions a game registered, from an <c>[InitializeOnLoad]</c> class of its own (a domain reload
    /// clears them, and the game's class registers again).</summary>
    public static class StateTreeMissionActions
    {
        private static readonly List<IStateTreeMissionAction> s_Actions = new List<IStateTreeMissionAction>();

        public static void Register(IStateTreeMissionAction action)
        {
            if (action != null && !s_Actions.Contains(action))
                s_Actions.Add(action);
        }

        public static void Unregister(IStateTreeMissionAction action) => s_Actions.Remove(action);

        /// <summary>The actions offered on <paramref name="at"/>, in the order they were registered.</summary>
        public static List<(IStateTreeMissionAction action, StateTreeMissionOffer offer)> Offered(StateTreeMissionPath at)
        {
            var offered = new List<(IStateTreeMissionAction, StateTreeMissionOffer)>();
            if (at == null)
                return offered;
            foreach (IStateTreeMissionAction action in s_Actions)
            {
                if (action.Offers(at, out StateTreeMissionOffer offer))
                    offered.Add((action, offer));
            }
            return offered;
        }
    }

    /// <summary>A step the run has gone through before the pressed node, with the answer it went by.</summary>
    public readonly struct StateTreeMissionStep
    {
        public StateTreeMissionStep(StateTreeNodeAsset node, StateTreeSchemaKind kind, int branch, StateTreeNodeAsset restsIn)
        {
            this.node = node;
            this.kind = kind;
            this.branch = branch;
            this.restsIn = restsIn;
        }

        public StateTreeNodeAsset node { get; }

        public StateTreeSchemaKind kind { get; }

        /// <summary>A routed node's answer — the index of the branch it went to — or -1.</summary>
        public int branch { get; }

        /// <summary>The nearest group around it whose run rests at its end (a level's part), or null.</summary>
        public StateTreeNodeAsset restsIn { get; }
    }

    /// <summary>
    /// A domain node and the way to it from the tree's root: the nodes in between, and so the branch taken at every
    /// question on the way. <see cref="Before"/> walks the run from its start to the node as the executor would — a
    /// group runs its children in order, a question goes to one branch.
    /// </summary>
    public sealed class StateTreeMissionPath
    {
        private readonly List<StateTreeNodeAsset> m_Nodes;

        private StateTreeMissionPath(StateTreeAsset tree, StateTreeSchema schema, List<StateTreeNodeAsset> nodes)
        {
            this.tree = tree;
            this.schema = schema;
            m_Nodes = nodes;
        }

        public StateTreeAsset tree { get; }

        public StateTreeSchema schema { get; }

        /// <summary>The tree's root first, the pressed node last.</summary>
        public IReadOnlyList<StateTreeNodeAsset> nodes => m_Nodes;

        public StateTreeNodeAsset node => m_Nodes[m_Nodes.Count - 1];

        public StateTreeSchemaKind kind => StateTreeSchemaRules.KindOf(schema, node);

        /// <summary>Null when <paramref name="node"/> is not in <paramref name="tree"/>.</summary>
        public static StateTreeMissionPath Of(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node)
        {
            if (tree == null || tree.root == null || node == null)
                return null;
            var nodes = new List<StateTreeNodeAsset>();
            return Find(tree.root, node, nodes, 0) ? new StateTreeMissionPath(tree, schema, nodes) : null;
        }

        private static bool Find(StateTreeNodeAsset at, StateTreeNodeAsset target, List<StateTreeNodeAsset> into, int depth)
        {
            if (at == null || depth > StateTreeEditorOps.DepthGuard)
                return false;
            into.Add(at);
            if (at == target)
                return true;
            foreach (StateTreeNodeAsset child in at.children)
            {
                if (Find(child, target, into, depth + 1))
                    return true;
            }
            into.RemoveAt(into.Count - 1);
            return false;
        }

        /// <summary>The nearest node on the way (the pressed one included) of <paramref name="kind"/>, or null.</summary>
        public StateTreeNodeAsset Nearest(string kind)
        {
            for (int i = m_Nodes.Count - 1; i >= 0; i--)
            {
                if (m_Nodes[i] != null && m_Nodes[i].roleKind == kind && StateTreeSchemaRules.IsDomain(schema, m_Nodes[i]))
                    return m_Nodes[i];
            }
            return null;
        }

        /// <summary>The branch the way takes under <paramref name="routed"/>: its index among the node's domain
        /// children, or -1 when the way does not go through one of them.</summary>
        public int BranchTaken(StateTreeNodeAsset routed)
        {
            int at = m_Nodes.IndexOf(routed);
            if (at < 0 || at == m_Nodes.Count - 1)
                return -1;
            return StateTreeSchemaRules.DomainChildren(schema, routed).IndexOf(m_Nodes[at + 1]);
        }

        /// <summary>
        /// Every step the run goes through before the pressed node, in order, each routed one with its answer. A
        /// question on the way goes to the branch the way takes; one before it, to the branch an answer on the way
        /// names (the same route key: a Recall of a Talk), else to the answer it was already given, else its first.
        /// </summary>
        public List<StateTreeMissionStep> Before()
        {
            var steps = new List<StateTreeMissionStep>();
            var answers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < m_Nodes.Count - 1; i++)
            {
                StateTreeSchemaKind kind = StateTreeSchemaRules.KindOf(schema, m_Nodes[i]);
                string key = StateTreeSchemaRules.RouteKey(kind, m_Nodes[i]);
                int taken = BranchTaken(m_Nodes[i]);
                if (!string.IsNullOrEmpty(key) && taken >= 0)
                    answers[key] = taken;
            }
            for (int i = 1; i < m_Nodes.Count; i++)
            {
                StateTreeNodeAsset parent = m_Nodes[i - 1];
                StateTreeSchemaKind parentKind = StateTreeSchemaRules.KindOf(schema, parent);
                if (parentKind == null)
                    continue;
                if (!string.IsNullOrEmpty(parentKind.routeBy))
                {
                    steps.Add(new StateTreeMissionStep(parent, parentKind, BranchTaken(parent), RestsIn(i - 1)));
                    continue;
                }
                foreach (StateTreeNodeAsset child in StateTreeSchemaRules.DomainChildren(schema, parent))
                {
                    if (child == m_Nodes[i])
                        break;
                    Run(child, RestsIn(i), answers, steps, 0);
                }
            }
            return steps;
        }

        /// <summary>The nearest resting group above the node at <paramref name="index"/> on the way.</summary>
        private StateTreeNodeAsset RestsIn(int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                StateTreeSchemaKind kind = StateTreeSchemaRules.KindOf(schema, m_Nodes[i]);
                if (kind != null && kind.rests)
                    return m_Nodes[i];
            }
            return null;
        }

        private void Run(StateTreeNodeAsset node, StateTreeNodeAsset restsIn, Dictionary<string, int> answers,
            List<StateTreeMissionStep> steps, int depth)
        {
            StateTreeSchemaKind kind = StateTreeSchemaRules.KindOf(schema, node);
            if (kind == null || depth > StateTreeEditorOps.DepthGuard)
                return;
            List<StateTreeNodeAsset> children = StateTreeSchemaRules.DomainChildren(schema, node);
            StateTreeNodeAsset rests = kind.rests ? node : restsIn;
            if (!string.IsNullOrEmpty(kind.routeBy))
            {
                int branch = -1;
                if (children.Count > 0)
                {
                    string key = StateTreeSchemaRules.RouteKey(kind, node);
                    branch = !string.IsNullOrEmpty(key) && answers.TryGetValue(key, out int given) && given < children.Count
                        ? given
                        : 0;
                    if (!string.IsNullOrEmpty(key))
                        answers[key] = branch;
                }
                steps.Add(new StateTreeMissionStep(node, kind, branch, rests));
                if (branch >= 0)
                    Run(children[branch], rests, answers, steps, depth + 1);
                return;
            }
            if (kind.isTemplate)
            {
                steps.Add(new StateTreeMissionStep(node, kind, -1, rests));
                return;
            }
            foreach (StateTreeNodeAsset child in children)
                Run(child, rests, answers, steps, depth + 1);
        }
    }
}
