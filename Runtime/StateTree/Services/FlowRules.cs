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
            if (def == null)
                return;

            if (def.flows != null && !string.IsNullOrEmpty(def.treeKind)
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
                if (string.IsNullOrEmpty(row.key))
                {
                    Debug.LogError("[Flows] '" + def.serviceName + "': request row " + i
                        + " is missing its key.", context);
                    continue;
                }
                for (int j = 0; j < i; j++)
                {
                    if (def.requests[j] != null && def.requests[j].key == row.key)
                        Debug.LogError("[Flows] '" + def.serviceName + "': request key '"
                            + row.key + "' is declared twice — which row serves it is "
                            + "undefined.", context);
                }
                // §4g: a row without a state is def-served — it must SAY something
                // (a domain action, reactions, or both), or asking it does nothing.
                if (string.IsNullOrEmpty(row.stateId))
                {
                    if (string.IsNullOrEmpty(row.action)
                        && (row.reactions == null || row.reactions.Count == 0))
                        Debug.LogError("[Flows] '" + def.serviceName + "': request '"
                            + row.key + "' has no state, no action, and no reactions — "
                            + "serving it does nothing.", context);
                    else if (!string.IsNullOrEmpty(row.action)
                        && !DeclaresAction(def.serviceTypeName, row.action))
                        Debug.LogError("[Flows] '" + def.serviceName + "': request '"
                            + row.key + "' asks action '" + row.action + "', which "
                            + def.serviceTypeName + " does not declare "
                            + "([ServiceActionContract]).", context);
                    continue;
                }
                if (def.flows == null)
                {
                    Debug.LogError("[Flows] '" + def.serviceName + "': request '" + row.key
                        + "' names state '" + row.stateId + "' but the def declares no "
                        + "flows tree.", context);
                    continue;
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

        /// <summary>Whether the named service class declares this action — quiet TRUE when
        /// the type cannot be resolved (an unset serviceTypeName is not a finding; a wrong
        /// action against a KNOWN vocabulary is).</summary>
        private static bool DeclaresAction(string serviceTypeName, string action)
        {
            if (string.IsNullOrEmpty(serviceTypeName))
                return true;
            System.Type type = FindServiceType(serviceTypeName);
            if (type == null)
                return true;
            var contracts = (ServiceActionContractAttribute[])type.GetCustomAttributes(
                typeof(ServiceActionContractAttribute), true);
            for (int i = 0; i < contracts.Length; i++)
            {
                if (string.Equals(contracts[i].action, action, System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static System.Type FindServiceType(string typeName)
        {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                System.Type direct = assemblies[i].GetType(typeName);
                if (direct != null)
                    return direct;
                System.Type qualified = assemblies[i].GetType(
                    "PowerOfFire.DrawToPlay." + typeName);
                if (qualified != null)
                    return qualified;
            }
            return null;
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
