using System;
using System.Collections.Generic;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// FINDINGS ON THE NODE (M38.4) — a declared-API node whose picks no longer name what the
    /// defs declare says so on the canvas, in the graph's own change pass, instead of at bake or
    /// at play: a subsystem that was renamed, a request its def dropped, a value that is not a
    /// row of the catalog, a verb the row's skins stopped declaring, a field the contract lost.
    ///
    /// Also the generic Request node's key: a key no def serves is a request nobody will ever
    /// answer, and the ⛓ rule elsewhere in the project says that out loud.
    /// </summary>
    public static class DeclaredApiValidator
    {
        /// <summary>One thing a node's picks get wrong, as data — the canvas shows it via the
        /// <see cref="GraphLogger"/>, a probe reads it directly.</summary>
        public readonly struct Finding
        {
            public readonly Node node;
            public readonly string message;
            public readonly bool isError;
            /// <summary>A note: nothing wrong, something worth reading on the node.</summary>
            public readonly bool isNote;

            public Finding(Node node, string message, bool isError, bool isNote = false)
            {
                this.node = node;
                this.message = message;
                this.isError = isError;
                this.isNote = isNote;
            }

            public override string ToString()
                => (isNote ? "note: " : isError ? "error: " : "warning: ") + message;
        }

        /// <summary>Collects a finding per wrong pick, to be shown by <see cref="Validate"/>.</summary>
        private sealed class Sink
        {
            public readonly List<Finding> findings = new List<Finding>();
            public void LogError(string message, Node node) => findings.Add(new Finding(node, message, true));
            public void LogWarning(string message, Node node) => findings.Add(new Finding(node, message, false));
            public void Log(string message, Node node) => findings.Add(new Finding(node, message, false, true));
        }

        /// <summary>The canvas pass: every finding lands on its node as a Graph Toolkit marker.</summary>
        public static void Validate(IReadOnlyList<INode> nodes, GraphLogger graphLogger)
        {
            List<Finding> findings = Findings(nodes);
            for (int i = 0; i < findings.Count; i++)
            {
                Finding finding = findings[i];
                if (finding.isNote)
                    graphLogger.Log(finding.message, finding.node);
                else if (finding.isError)
                    graphLogger.LogError(finding.message, finding.node);
                else
                    graphLogger.LogWarning(finding.message, finding.node);
            }
        }

        /// <summary>The checks alone — what a probe or a tool asks without a canvas.</summary>
        public static List<Finding> Findings(IReadOnlyList<INode> nodes)
        {
            var graphLogger = new Sink();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!(nodes[i] is Node node))
                    continue;
                try
                {
                    switch (node)
                    {
                        case AskSubsystemNode _:
                        case AskSubsystemBlockNode _:
                            ValidateAsk(node, graphLogger);
                            break;
                        case WhenAnnouncedNode _:
                        case WhenAnnouncedConditionNode _:
                        case AnnouncedPayloadNode _:
                            ValidateAnnouncement(node, graphLogger);
                            break;
                        case AskedResultNode _:
                            ValidateAskedResult(node, graphLogger);
                            break;
                        case SayToUiNode _:
                        case SayToUiBlockNode _:
                            ValidateSay(node, graphLogger);
                            break;
                        case ShowUiNode _:
                        case ShowUiBlockNode _:
                            ValidateRow(node, Pin(node, "ui"), graphLogger);
                            break;
                        case RequestTaskNode _:
                            ValidateGenericRequest(node, graphLogger);
                            break;
                    }
                }
                catch (Exception)
                {
                    // A half-built node answers by throwing; it is asked again on the next change.
                }
            }
            return graphLogger.findings;
        }

        private static void ValidateAsk(Node node, Sink log)
        {
            string subsystem = Pin(node, "subsystem");
            string key = Pin(node, "key");
            if (!Subsystem(node, subsystem, log))
                return;
            if (string.IsNullOrEmpty(key))
            {
                log.LogWarning("Ask: pick a request — the node writes nothing until it has one.", node);
                return;
            }
            ServiceRequest row = DeclaredApi.Request(subsystem, key);
            if (row == null)
            {
                log.LogError("Ask: '" + subsystem + "' no longer declares a request '" + key
                    + "' — it would be refused at the door.", node);
                return;
            }
            string value = Pin(node, "value");
            if (row.namesRowOf != null && !string.IsNullOrEmpty(value)
                && row.namesRowOf.FindByName(value) == null)
            {
                log.LogError("Ask: '" + key + "' takes a row of '" + row.namesRowOf.name + "', and '"
                    + value + "' names none of them.", node);
            }
            // A ROW WITH NO CLASS VERB (M41.3) is served by its graph — said on the node, and
            // said louder when there is no graph to serve it either.
            if (string.IsNullOrEmpty(row.action))
            {
                if (row.reactionGraph == null)
                    log.LogWarning("Ask: '" + key + "' has no class verb and no graph — nothing "
                        + "serves it. Pick a graph on the def's row.", node);
                else
                    log.Log("Ask: '" + key + "' is served by the graph '"
                        + row.reactionGraph.name + "' — no class behind it.", node);
            }
        }

        private static void ValidateAnnouncement(Node node, Sink log)
        {
            string subsystem = Pin(node, "subsystem");
            string key = Pin(node, "key");
            if (!Subsystem(node, subsystem, log))
                return;
            if (string.IsNullOrEmpty(key))
            {
                log.LogWarning("Pick an announcement — nothing is heard until then.", node);
                return;
            }
            if (!DeclaredApi.AnnouncementKeys(subsystem).Contains(key))
            {
                log.LogError("'" + subsystem + "' no longer announces '" + key + "' — this would never fire.", node);
                return;
            }
            string field = Pin(node, "field");
            if (!string.IsNullOrEmpty(field)
                && !DeclaredApi.FieldChoices(DeclaredApi.PayloadOf(subsystem, key)).Contains(field))
            {
                log.LogError("'" + key + "' carries no field '" + field + "' any more.", node);
            }
        }

        private static void ValidateAskedResult(Node node, Sink log)
        {
            string subsystem = Pin(node, "subsystem");
            string request = Pin(node, AskedResultNode.RequestPortName);
            if (!Subsystem(node, subsystem, log))
                return;
            if (string.IsNullOrEmpty(request))
            {
                log.LogWarning("Asked Result: pick a request that answers with a contract.", node);
                return;
            }
            Type answer = DeclaredApi.AnswerOf(subsystem, request);
            if (answer == null)
            {
                log.LogError("Asked Result: '" + request + "' on '" + subsystem
                    + "' answers with nothing — its action declares no contract.", node);
                return;
            }
            string field = Pin(node, AskedResultNode.FieldPortName);
            if (!string.IsNullOrEmpty(field) && !DeclaredApi.FieldChoices(answer).Contains(field))
                log.LogError("Asked Result: " + answer.Name + " has no field '" + field + "'.", node);
        }

        private static void ValidateSay(Node node, Sink log)
        {
            string row = Pin(node, "ui");
            if (!ValidateRow(node, row, log))
                return;
            string verb = Pin(node, "verb");
            if (string.IsNullOrEmpty(verb))
            {
                log.LogWarning("Say To Screen: pick a verb the row's skins declare.", node);
                return;
            }
            if (!DeclaredApi.Verbs(row).Contains(verb))
            {
                log.LogError("Say To Screen: no skin on '" + row + "' declares '" + verb
                    + "' — nothing would answer, and the runtime would say so every time.", node);
            }
        }

        private static bool ValidateRow(Node node, string row, Sink log)
        {
            if (string.IsNullOrEmpty(row))
            {
                log.LogWarning("Pick a UI row.", node);
                return false;
            }
            if (DeclaredApi.UiRow(row) == null)
            {
                log.LogError("No UI registry has a row named '" + row + "' any more.", node);
                return false;
            }
            return true;
        }

        private static void ValidateGenericRequest(Node node, Sink log)
        {
            string key = Pin(node, "key");
            if (string.IsNullOrEmpty(key))
                return;
            foreach (string defName in DeclaredApi.Subsystems())
            {
                if (!string.IsNullOrEmpty(defName) && DeclaredApi.Request(defName, key) != null)
                    return;
            }
            log.LogWarning("Request Subsystem: no def serves '" + key
                + "' — the key is written and nobody answers. Prefer Ask, which picks one.", node);
        }

        private static bool Subsystem(Node node, string subsystem, Sink log)
        {
            if (string.IsNullOrEmpty(subsystem))
            {
                log.LogWarning("Pick a subsystem.", node);
                return false;
            }
            if (DeclaredApi.Subsystem(subsystem) == null)
            {
                log.LogError("No subsystem def named '" + subsystem + "' exists any more.", node);
                return false;
            }
            return true;
        }

        private static string Pin(Node node, string name)
        {
            IPort port = node.GetInputPortByName(name);
            return port != null
                && LibraryParameterPorts.TryReadValue(port, typeof(string), out object value)
                && value is string text ? text : "";
        }
    }
}
