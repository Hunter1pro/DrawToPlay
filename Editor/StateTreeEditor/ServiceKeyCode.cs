using System;
using System.Collections.Generic;
using System.Reflection;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE OTHER HALF OF "WHO NAMES THIS" — the names that live in C#.
    ///
    /// The usage index walks authored assets, so it answers honestly and incompletely: a key
    /// whose readers are a `const string` and a service's own default beat looks unused, and the
    /// ⛓ said so in the only words it had ("skins and C# callers do not scan"). For a key like
    /// `ui.bag.last-use` that is exactly backwards — it is declared in code, which makes it MORE
    /// of a contract, not less.
    ///
    /// So the constants are scanned too. What this finds cannot be rewritten by any rename this
    /// editor performs, and that is the point: a key a const declares must be renamed in the
    /// source, and an inspector that let you retype it would be quietly forking the name.
    ///
    /// One reflection pass per domain reload over the project's own assemblies — Unity's
    /// constants are not this project's vocabulary and scanning them would cost more and mean
    /// less.
    /// </summary>
    internal static class ServiceKeyCode
    {
        /// <summary>"ItemUseResult.Key" for every constant declaring this exact string.</summary>
        internal static IReadOnlyList<string> Owners(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Array.Empty<string>();
            return Index().TryGetValue(key, out List<string> owners) ? owners : Array.Empty<string>();
        }

        private static Dictionary<string, List<string>> Index()
        {
            if (s_Index != null)
                return s_Index;

            s_Index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!Ours(assembly.GetName().Name))
                    continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                for (int i = 0; i < types.Length; i++)
                    Collect(types[i]);
            }
            return s_Index;
        }

        private static void Collect(Type type)
        {
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Static | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch { return; }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                    continue;
                string value;
                try { value = field.GetRawConstantValue() as string; }
                catch { continue; }
                if (string.IsNullOrEmpty(value))
                    continue;

                if (!s_Index.TryGetValue(value, out List<string> owners))
                    s_Index[value] = owners = new List<string>();
                string named = type.Name + "." + field.Name;
                if (!owners.Contains(named))
                    owners.Add(named);
            }
        }

        /// <summary>This project's own code — where its vocabulary is declared.</summary>
        private static bool Ours(string assemblyName)
        {
            return assemblyName.StartsWith("PowerOfFire", StringComparison.Ordinal)
                || assemblyName.StartsWith("Assembly-CSharp", StringComparison.Ordinal);
        }

        /// <summary>Rebuilt on domain reload with everything else static — a constant cannot
        /// change without one.</summary>
        private static Dictionary<string, List<string>> s_Index;
    }
}
