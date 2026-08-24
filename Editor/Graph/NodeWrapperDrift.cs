using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE LIBRARY AGAINST THE PALETTE (M38.3) — which library tasks and conditions have their
    /// two graph wrappers, and which do not.
    ///
    /// Graph Toolkit wants a concrete attributed type per palette entry, so every library type
    /// has (or should have) a state BLOCK and a graph CALL (tasks), or a condition NODE and a
    /// condition VALUE (conditions) — seventeen lines each, naming the runtime type. A type
    /// added without them is invisible on the canvas, and nothing said so: on the day this was
    /// written, 57 of 77 library tasks had no state block. This reads both sides by reflection
    /// and names every missing pair — and every type wrapped twice.
    ///
    /// Pure: the editor assembly does not reference the graph assembly (Graph Toolkit is
    /// experimental and firewalled), so the wrappers are found by their base class NAMES and
    /// their wrapped type read off an uninitialised instance — a property that returns
    /// <c>typeof(X)</c> needs no constructor.
    /// </summary>
    public static class NodeWrapperDrift
    {
        public enum Wrapper { Block, Call, ConditionNode, ConditionValue }

        public sealed class Finding
        {
            public Type library;
            public Wrapper missing;
            public bool duplicate;
            public string wrapperName;

            public override string ToString()
            {
                return duplicate
                    ? "✕ " + library.Name + " is wrapped twice as a " + missing + " (" + wrapperName + ")"
                    : "⚠ " + library.Name + " has no " + Describe(missing);
            }
        }

        /// <summary>The runtime assemblies whose library types count — test stubs do not.</summary>
        public static string[] LibraryAssemblies => DrawToPlayFolders.TaskAssemblies();

        /// <summary>Types that are pickable as tasks but are not library tasks to wrap: a baked
        /// program is picked as an asset, and the two composite wrappers run what they are
        /// handed rather than doing anything themselves.</summary>
        public static readonly HashSet<string> NotWrapped = new HashSet<string>
        {
            "GraphTaskAsset", "RunSubTreeTask", "RunGraphTask"
        };

        public static List<Finding> Check()
        {
            var findings = new List<Finding>();
            Dictionary<Wrapper, Dictionary<Type, List<string>>> have = ExistingWrappers();

            foreach (Type task in LibraryTasks())
            {
                Expect(findings, have, task, Wrapper.Block);
                Expect(findings, have, task, Wrapper.Call);
            }
            foreach (Type condition in LibraryConditions())
            {
                Expect(findings, have, condition, Wrapper.ConditionNode);
                Expect(findings, have, condition, Wrapper.ConditionValue);
            }
            return findings;
        }

        public static List<Type> LibraryTasks()
        {
            return LibraryTypes(typeof(StateTreeTaskAsset));
        }

        public static List<Type> LibraryConditions()
        {
            return LibraryTypes(typeof(StateTreeConditionAsset));
        }

        private static List<Type> LibraryTypes(Type baseType)
        {
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;
                if (!type.IsDefined(typeof(StateTreeCategoryAttribute), false))
                    continue;
                if (NotWrapped.Contains(type.Name))
                    continue;
                if (Array.IndexOf(LibraryAssemblies, type.Assembly.GetName().Name) < 0)
                    continue;
                types.Add(type);
            }
            types.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return types;
        }

        /// <summary>Every wrapper in the graph assemblies, by kind, by the type it wraps.</summary>
        public static Dictionary<Wrapper, Dictionary<Type, List<string>>> ExistingWrappers()
        {
            var have = new Dictionary<Wrapper, Dictionary<Type, List<string>>>();
            foreach (Wrapper kind in Enum.GetValues(typeof(Wrapper)))
                have[kind] = new Dictionary<Type, List<string>>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("GraphEditor"))
                    continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }
                foreach (Type wrapper in types)
                {
                    if (wrapper.IsAbstract || IsDeclaredApiNode(wrapper))
                        continue;
                    Wrapper? kind = KindOf(wrapper);
                    if (kind == null)
                        continue;
                    Type wrapped = WrappedType(wrapper, kind.Value);
                    if (wrapped == null)
                        continue;
                    if (!have[kind.Value].TryGetValue(wrapped, out List<string> names))
                    {
                        names = new List<string>();
                        have[kind.Value][wrapped] = names;
                    }
                    names.Add(wrapper.Name);
                }
            }
            return have;
        }

        /// <summary>A declared-API node (Ask, Say To Screen…) wraps a library type too, but it is
        /// not that type's wrapper — it is a subsystem's — so the generic pair is still expected
        /// beside it. Known by the interface's name, across the firewall.</summary>
        private static bool IsDeclaredApiNode(Type wrapper)
        {
            foreach (Type face in wrapper.GetInterfaces())
            {
                if (face.Name == "IDeclaredApiNode")
                    return true;
            }
            return false;
        }

        private static Wrapper? KindOf(Type wrapper)
        {
            for (Type walk = wrapper.BaseType; walk != null; walk = walk.BaseType)
            {
                switch (walk.Name)
                {
                    case "StateTaskBlockNode": return Wrapper.Block;
                    case "TaskCallNode": return Wrapper.Call;
                    case "StateTreeConditionNode": return Wrapper.ConditionNode;
                    case "ConditionValueNode": return Wrapper.ConditionValue;
                }
            }
            return null;
        }

        private static Type WrappedType(Type wrapper, Wrapper kind)
        {
            string property = kind == Wrapper.Block || kind == Wrapper.Call ? "taskType" : "conditionType";
            PropertyInfo getter = wrapper.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (getter == null)
                return null;
            try
            {
                object bare = FormatterServices.GetUninitializedObject(wrapper);
                return getter.GetValue(bare) as Type;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Expect(List<Finding> into, Dictionary<Wrapper, Dictionary<Type, List<string>>> have,
            Type library, Wrapper kind)
        {
            if (!have[kind].TryGetValue(library, out List<string> names) || names.Count == 0)
            {
                into.Add(new Finding { library = library, missing = kind });
                return;
            }
            if (names.Count > 1)
            {
                into.Add(new Finding
                {
                    library = library, missing = kind, duplicate = true, wrapperName = string.Join(" and ", names)
                });
            }
        }

        public static string Describe(Wrapper kind)
        {
            switch (kind)
            {
                case Wrapper.Block: return "state block — it cannot be placed in a state on the canvas";
                case Wrapper.Call: return "graph call — a logic graph cannot call it";
                case Wrapper.ConditionNode: return "condition node — a transition on the canvas cannot test it";
                default: return "condition value — a logic graph cannot branch on it";
            }
        }

        [MenuItem("Tools/Draw To Play/Graph/Check Node Wrappers")]
        private static void Report()
        {
            List<Finding> findings = Check();
            if (findings.Count == 0)
            {
                UnityEngine.Debug.Log("[Wrappers] every library task and condition has both wrappers.");
                return;
            }
            var lines = new List<string>();
            for (int i = 0; i < findings.Count; i++)
                lines.Add(findings[i].ToString());
            UnityEngine.Debug.LogWarning("[Wrappers] " + findings.Count + " finding(s):\n" + string.Join("\n", lines));
        }
    }
}
