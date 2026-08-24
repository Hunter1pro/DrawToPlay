using System.Collections.Generic;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHERE THE TOOLS WRITE. The package is read-only in spirit (and literally when it comes
    /// from git), so everything an author creates — drawn shapes, trees, tasks, graphs, flows,
    /// stamps, generated code — lands in the HOST PROJECT under <see cref="project"/>. A host
    /// sets that (and registers the assemblies whose tasks get node wrappers) from an
    /// <c>[InitializeOnLoadMethod]</c>; the defaults suit a project that has nothing to say.
    /// </summary>
    public static class DrawToPlayFolders
    {
        public const string Package = "Packages/com.powerofire.drawtoplay";
        public const string PackageGenerated = Package + "/GraphEditor/Generated";
        public const string Icons = Package + "/Editor/Icons";

        /// <summary>The host project's Draw-to-Play folder — every output folder hangs off it.</summary>
        public static string project = "Assets/DrawToPlay";

        /// <summary>Where the host's generated node wrappers go — inside an assembly whose
        /// name contains "GraphEditor", or the drift check will not see them.</summary>
        public static string projectGenerated = "Assets/DrawToPlay/GraphEditor/Generated";

        public static string Drawn => project + "/Drawn";
        public static string Trees => project + "/Trees";
        public static string Tasks => project + "/Tasks";
        public static string Graphs => project + "/Graphs";
        public static string Flows => project + "/Flows";
        public static string Stamps => project + "/Stamps";
        public static string Subsystems => project + "/Subsystems";
        public static string Tests => project + "/Tests/Editor";

        private static readonly List<string> s_TaskAssemblies = new List<string> { "PowerOfFire.DrawToPlay" };

        /// <summary>An assembly whose tasks and conditions get node wrappers (and are checked
        /// for drift). The package's own is always in; a host adds its game's.</summary>
        public static void RegisterTaskAssembly(string assemblyName)
        {
            if (!string.IsNullOrEmpty(assemblyName) && !s_TaskAssemblies.Contains(assemblyName))
                s_TaskAssemblies.Add(assemblyName);
        }

        public static string[] TaskAssemblies() => s_TaskAssemblies.ToArray();

        public static bool IsTaskAssembly(string assemblyName) => s_TaskAssemblies.Contains(assemblyName);

        /// <summary>Create every missing folder along a project path.</summary>
        public static void Ensure(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
