using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The flows grammar, validated (§4c, the AbilityRules shape): a def that declares
    /// requests must declare them TRUE — every row's state must exist in the flows tree
    /// and be request-kind, keys must be unique, and the tree must carry the def's claimed
    /// kind. Findings are loud once, at flow start, naming the row — a silent mismatch
    /// here is a button that does nothing forever.
    /// </summary>
    public static class FlowRules
    {
        public static void Validate(ServiceDef def, Object context)
        {
            if (def == null || def.flows == null)
                return;

            if (!string.IsNullOrEmpty(def.treeKind)
                && !string.Equals(def.flows.treeKind, def.treeKind, System.StringComparison.Ordinal))
            {
                Debug.LogError("[Flows] '" + def.serviceName + "': its flows tree '"
                    + def.flows.name + "' is kind '" + def.flows.treeKind + "', not the "
                    + "declared '" + def.treeKind + "'.", context);
            }

            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row == null)
                    continue;
                if (string.IsNullOrEmpty(row.key) || string.IsNullOrEmpty(row.stateId))
                {
                    Debug.LogError("[Flows] '" + def.serviceName + "': request row " + i
                        + " is missing its key or its state.", context);
                    continue;
                }
                for (int j = 0; j < i; j++)
                {
                    if (def.requests[j] != null && def.requests[j].key == row.key)
                        Debug.LogError("[Flows] '" + def.serviceName + "': request key '"
                            + row.key + "' is declared twice — which state serves it is "
                            + "undefined.", context);
                }
                // §4d: when both the request row and the flows tree's declaration type the
                // same key, they must type it the SAME — two authorities disagreeing about
                // what a value names is a picker offering rows a validator then refuses.
                if (row.namesRowOf != null)
                {
                    for (int k = 0; k < def.flows.keys.Count; k++)
                    {
                        StateTreeKeyDeclaration declared = def.flows.keys[k];
                        if (declared == null || declared.name != row.key
                            || declared.namesRowOf == null)
                            continue;
                        if (declared.namesRowOf != row.namesRowOf)
                            Debug.LogError("[Flows] '" + def.serviceName + "': request '"
                                + row.key + "' names rows of '" + row.namesRowOf.name
                                + "' but the flow tree's declaration says '"
                                + declared.namesRowOf.name + "'.", context);
                    }
                }

                StateTreeNodeAsset state = FindNode(def.flows.root, row.stateId);
                if (state == null)
                {
                    Debug.LogError("[Flows] '" + def.serviceName + "': request '" + row.key
                        + "' names state '" + row.stateId + "', which the flows tree does "
                        + "not have.", context);
                }
                else if (!string.Equals(state.roleKind, "request",
                    System.StringComparison.Ordinal))
                {
                    Debug.LogError("[Flows] '" + def.serviceName + "': request '" + row.key
                        + "' serves state '" + row.stateId + "', which is not request-kind "
                        + "('" + state.roleKind + "') — type it, or the grammar cannot see "
                        + "it.", context);
                }
            }
        }

        private static StateTreeNodeAsset FindNode(StateTreeNodeAsset node, string nodeId)
        {
            if (node == null)
                return null;
            if (string.Equals(node.nodeId, nodeId, System.StringComparison.Ordinal))
                return node;
            for (int i = 0; i < node.children.Count; i++)
            {
                StateTreeNodeAsset found = FindNode(node.children[i], nodeId);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
