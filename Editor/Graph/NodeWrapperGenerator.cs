using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WRITES THE MISSING WRAPPERS (M38.3) — one file per missing pair, into a Generated folder
    /// beside the hand-written ones, in the graph assembly that can see the library type.
    ///
    /// A COMMAND, never a build step: the files are written, reviewed and committed like
    /// anything else here. A wrapper that already exists — hand-written or generated — is left
    /// alone, which is what lets a hand-tuned one (a custom category, a nicer name) stay.
    /// </summary>
    public static class NodeWrapperGenerator
    {
        private const string k_CoreFolder = "Assets/DrawToPlay/GraphEditor/Generated";
        private const string k_ExamplesFolder = "Assets/DrawToPlayExamples/GraphEditor/Generated";

        public sealed class Result
        {
            public readonly List<string> written = new List<string>();
            public readonly List<string> skipped = new List<string>();
        }

        [MenuItem("Tools/Draw To Play/Graph/Generate Node Wrappers")]
        private static void GenerateFromMenu()
        {
            Result result = Generate();
            Debug.Log("[Wrappers] wrote " + result.written.Count + " file(s)"
                + (result.skipped.Count > 0 ? "; skipped " + result.skipped.Count : "")
                + (result.written.Count > 0 ? ":\n" + string.Join("\n", result.written) : "."));
            if (result.written.Count > 0)
                AssetDatabase.Refresh();
        }

        public static Result Generate()
        {
            var result = new Result();
            List<NodeWrapperDrift.Finding> findings = NodeWrapperDrift.Check();
            for (int i = 0; i < findings.Count; i++)
            {
                NodeWrapperDrift.Finding finding = findings[i];
                if (finding.duplicate)
                {
                    result.skipped.Add(finding.ToString());
                    continue;
                }
                string folder = FolderFor(finding.library);
                if (folder == null)
                {
                    result.skipped.Add(finding.library.Name + ": not in a library assembly this writes for");
                    continue;
                }
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, WrapperName(finding.library, finding.missing) + ".cs").Replace('\\', '/');
                if (File.Exists(path))
                {
                    result.skipped.Add(path + " exists");
                    continue;
                }
                File.WriteAllText(path, Source(finding.library, finding.missing));
                result.written.Add(path);
            }
            return result;
        }

        private static string FolderFor(Type library)
        {
            switch (library.Assembly.GetName().Name)
            {
                case "PowerOfFire.DrawToPlay": return k_CoreFolder;
                case "PowerOfFire.DrawToPlay.Examples": return k_ExamplesFolder;
                default: return null;
            }
        }

        public static string WrapperName(Type library, NodeWrapperDrift.Wrapper kind)
        {
            string stem = library.Name;
            switch (kind)
            {
                case NodeWrapperDrift.Wrapper.Block: return stem + "Node";
                case NodeWrapperDrift.Wrapper.Call: return stem + "CallNode";
                case NodeWrapperDrift.Wrapper.ConditionNode: return stem + "Node";
                default: return stem + "ValueNode";
            }
        }

        /// <summary>"SayLineTask" → "Say Line"; "HasTagCondition" → "Has Tag".</summary>
        public static string DisplayName(Type library)
        {
            string stem = library.Name;
            foreach (string suffix in new[] { "Task", "Condition" })
            {
                if (stem.EndsWith(suffix, StringComparison.Ordinal) && stem.Length > suffix.Length)
                    stem = stem.Substring(0, stem.Length - suffix.Length);
            }
            return ObjectNames.NicifyVariableName(stem);
        }

        public static string Category(Type library)
        {
            var marked = (StateTreeCategoryAttribute)Attribute.GetCustomAttribute(library,
                typeof(StateTreeCategoryAttribute), false);
            string path = marked != null ? marked.path : null;
            return string.IsNullOrWhiteSpace(path) ? "Library" : path.Trim().Trim('/');
        }

        public static string Source(Type library, NodeWrapperDrift.Wrapper kind)
        {
            var marked = (StateTreeCategoryAttribute)Attribute.GetCustomAttribute(library,
                typeof(StateTreeCategoryAttribute), false);
            string description = marked != null ? marked.description : "";
            bool isTask = kind == NodeWrapperDrift.Wrapper.Block || kind == NodeWrapperDrift.Wrapper.Call;
            bool onStateCanvas = kind == NodeWrapperDrift.Wrapper.Block || kind == NodeWrapperDrift.Wrapper.ConditionNode;
            string baseClass;
            switch (kind)
            {
                case NodeWrapperDrift.Wrapper.Block: baseClass = "StateTaskBlockNode"; break;
                case NodeWrapperDrift.Wrapper.Call: baseClass = "TaskCallNode"; break;
                case NodeWrapperDrift.Wrapper.ConditionNode: baseClass = "StateTreeConditionNode"; break;
                default: baseClass = "ConditionValueNode"; break;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Written by Tools/Draw To Play/Graph/Generate Node Wrappers from " + library.FullName + ".");
            sb.AppendLine("// Edit the library type, or replace this file with a hand-written wrapper; do not edit here.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("using System;");
            sb.AppendLine("using Unity.GraphToolkit.Editor;");
            sb.AppendLine();
            sb.AppendLine("namespace PowerOfFire.DrawToPlay.GraphEditor");
            sb.AppendLine("{");
            if (!string.IsNullOrEmpty(description))
                sb.AppendLine("    /// <summary>" + description.Replace("\n", " ") + "</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    [UseWithGraph(typeof(" + (onStateCanvas ? "StateTreeGraph" : "TaskGraph") + "))]");
            if (kind == NodeWrapperDrift.Wrapper.Block)
                sb.AppendLine("    [UseWithContext(typeof(StateNode))]");
            sb.AppendLine("    [Node(" + Quote(Category(library)) + ", null, " + Quote(DisplayName(library)) + ")]");
            sb.AppendLine("    public class " + WrapperName(library, kind) + " : " + baseClass);
            sb.AppendLine("    {");
            sb.AppendLine("        public override Type " + (isTask ? "taskType" : "conditionType")
                + " => typeof(" + library.FullName + ");");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Quote(string text)
        {
            return "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
