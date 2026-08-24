using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// AUTHOR A TASK GRAPH BY PICKING — the small vocabulary a generator speaks to write a real
    /// .taskgraph file: a node, a constant on a port, an exec wire, and save-and-bake. Every
    /// problem is collected and reported as an ERROR at save: a graph with an unwritten port
    /// does not fail, it takes a different branch, silently.
    /// </summary>
    public static class GraphAuthoring
    {
        /// <summary>The baked program of an existing file, or null — generators KEEP an edited
        /// graph and only author the stock one when the file is missing.</summary>
        public static GraphTaskAsset Existing(string path)
        {
            return AssetDatabase.LoadMainAssetAtPath(path) as GraphTaskAsset;
        }

        public static TaskGraph NewGraph(string path, List<string> problems)
        {
            TaskGraph graph = TaskGraphAuthoring.CreateGraphAsset(path, out _, out string error);
            if (graph == null)
                problems.Add(error);
            return graph;
        }

        public static Node Add(TaskGraph graph, Type type, float x, float y, List<string> problems)
        {
            return TaskGraphAuthoring.AddNode(graph, type, new Vector2(x, y), problems);
        }

        /// <summary>Connect an exec pin by name — the chain a program runs along.</summary>
        public static void Exec(TaskGraph graph, Node from, string outPort, Node to, List<string> problems)
        {
            TaskGraphAuthoring.Connect(graph, from, outPort, to, TaskGraphPorts.ExecInPortName, problems);
        }

        /// <summary>The overwhelmingly common wiring: this call finished, do that next.</summary>
        public static void Success(TaskGraph graph, Node from, Node to, List<string> problems)
        {
            Exec(graph, from, TaskGraphPorts.SuccessExecPortName, to, problems);
        }

        public static void Connect(TaskGraph graph, Node from, string outPort, Node to, string inPort,
            List<string> problems)
        {
            TaskGraphAuthoring.Connect(graph, from, outPort, to, inPort, problems);
        }

        /// <summary>A declared-API node whose shape follows its dropdowns is redefined by the
        /// editor on every change; authored in code it has no editor, so settle the pins by hand.</summary>
        public static void Settle(Node node)
        {
            if (!(node is IDeclaredApiNode api))
                return;
            api.AdoptChoiceSources();
            node.DefineNode();
        }

        /// <summary>Write a constant into a node's input port — what an author types on the canvas.</summary>
        public static void Write(Node node, string portName, object value, List<string> problems)
        {
            if (node == null)
                return;
            IPort port = node.GetInputPortByName(portName);
            if (port == null)
            {
                problems.Add($"{node.GetType().Name} has no '{portName}' port.");
                return;
            }
            if (!LibraryParameterPorts.TryWriteValue(port, value.GetType(), value))
                problems.Add($"'{portName}' on {node.GetType().Name} refused {value}.");
        }

        public static GraphTaskAsset Save(TaskGraph graph, string path, string label, List<string> problems)
        {
            TaskGraphAuthoring.SaveAndImport(graph, path);
            GraphTaskAsset baked = TaskGraphAuthoring.FindProgramAt(path);
            if (problems.Count > 0)
            {
                Debug.LogError($"[GraphAuthoring] '{label}' was authored with problems — the graph will "
                    + "behave wrongly, not fail:\n  " + string.Join("\n  ", problems));
            }
            if (baked == null)
                Debug.LogError($"[GraphAuthoring] '{label}': {path} baked no program — the console above carries the reason.");
            return baked;
        }

        public static GraphTaskAsset Fail(string label, string path, List<string> problems)
        {
            Debug.LogError($"[GraphAuthoring] '{label}': could not create {path} — " + string.Join("; ", problems));
            return null;
        }
    }
}
