using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE WRITER (M37.3) — from a sketch that validates: the def, the class, the capability, a
    /// test, and an installer row.
    ///
    /// The DEF is data and is regenerated from the sketch every time. The CLASS, the capability
    /// and the test are written ONCE: a file an engineer may have edited is never overwritten —
    /// drift between the sketch and the class is reported (37.4), not "fixed". The C# is written
    /// by code that knows what a service is, not by a text template with holes, so when the
    /// shape of a service changes there is one method to update.
    /// </summary>
    internal static class SubsystemGenerator
    {
        internal sealed class Result
        {
            public ServiceDef def;
            public readonly List<string> written = new List<string>();
            public readonly List<string> kept = new List<string>();
            public string installedOn = "";
            public string note = "";
        }

        internal static Result Generate(SubsystemSketch sketch)
        {
            var result = new Result();
            List<SketchFinding> findings = SubsystemSketchValidator.Validate(sketch);
            if (SubsystemSketchValidator.Blocks(findings))
            {
                Debug.LogError("[Subsystems] '" + sketch.serviceName + "' does not validate:\n"
                    + string.Join("\n", findings));
                return result;
            }

            Undo.RecordObject(sketch, "Generate subsystem");
            result.def = WriteDef(sketch);
            WriteCode(sketch, result);
            Install(sketch, result);

            sketch.generatedDef = result.def;
            EditorUtility.SetDirty(sketch);
            EditorUtility.SetDirty(result.def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Subsystems] '" + sketch.serviceName + "': def " + AssetDatabase.GetAssetPath(result.def)
                + (result.written.Count > 0 ? "; wrote " + string.Join(", ", result.written) : "")
                + (result.kept.Count > 0 ? "; kept " + string.Join(", ", result.kept) : "")
                + (string.IsNullOrEmpty(result.installedOn) ? "" : "; installed on " + result.installedOn)
                + (string.IsNullOrEmpty(result.note) ? "" : "; " + result.note), sketch);
            return result;
        }

        // ---- the def ----------------------------------------------------------------------

        /// <summary>Beside the sketch, named for the class. Regenerated in full every time.</summary>
        internal static ServiceDef WriteDef(SubsystemSketch sketch)
        {
            ServiceDef def = sketch.generatedDef;
            if (def == null)
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sketch));
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    Path.Combine(folder ?? "Assets", sketch.className + ".asset").Replace('\\', '/'));
                def = ScriptableObject.CreateInstance<ServiceDef>();
                AssetDatabase.CreateAsset(def, path);
            }

            def.serviceName = sketch.serviceName;
            def.serviceTypeName = sketch.codeNamespace + "." + sketch.className;
            def.scope = sketch.scope;
            def.registry = sketch.catalog;
            def.treeKind = "";
            def.nestingRules.Clear();
            def.kindSeeds.Clear();

            def.declares.Clear();
            foreach (StateTreeRegistryAsset declared in DeclaredCatalogs(sketch))
                def.declares.Add(declared);

            def.requests.Clear();
            for (int i = 0; i < sketch.requests.Count; i++)
            {
                SketchRequest row = sketch.requests[i];
                def.requests.Add(new ServiceRequest
                {
                    key = row.key,
                    action = row.action,
                    description = row.valueHint,
                    namesRowOf = row.namesRowOf
                });
            }

            // ANNOUNCEMENTS ARE THE CLASS'S (M41.1): emitted as [ServiceAnnouncement] on the
            // generated class, never rows on the def.

            def.spawns.Clear();
            for (int i = 0; i < sketch.spawns.Count; i++)
            {
                def.spawns.Add(new StateTreeEntryRef<UiDef>
                {
                    entryId = sketch.spawns[i].entryId, entryName = sketch.spawns[i].entryName
                });
            }

            def.attributes.Clear();
            for (int i = 0; i < sketch.attributes.Count; i++)
            {
                var has = new ServiceAttribute();
                has.attribute.entryId = sketch.attributes[i].entryId;
                has.attribute.entryName = sketch.attributes[i].entryName;
                def.attributes.Add(has);
            }

            // SETTINGS stay empty on a generated def: the class carries the defaults, and a def
            // that repeated them would be a second place for the same number.
            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>What the def must declare to NAME what the sketch picked: the sketch's own
        /// declarations, plus the home catalog of every picked row and every names-row-of.</summary>
        internal static List<StateTreeRegistryAsset> DeclaredCatalogs(SubsystemSketch sketch)
        {
            var all = new List<StateTreeRegistryAsset>();
            void Add(StateTreeRegistryAsset registry)
            {
                if (registry != null && registry != sketch.catalog && !all.Contains(registry))
                    all.Add(registry);
            }
            for (int i = 0; i < sketch.declares.Count; i++)
                Add(sketch.declares[i]);
            for (int i = 0; i < sketch.requests.Count; i++)
                Add(sketch.requests[i]?.namesRowOf);
            for (int i = 0; i < sketch.attributes.Count; i++)
                Add(SubsystemSketchValidator.HomeOf(sketch.attributes[i]?.entryId, typeof(AttributeRegistry)));
            return all;
        }

        // ---- the code ---------------------------------------------------------------------

        private static void WriteCode(SubsystemSketch sketch, Result result)
        {
            Directory.CreateDirectory(sketch.codeFolder);
            string classPath = Path.Combine(sketch.codeFolder, sketch.className + ".cs").Replace('\\', '/');
            string capabilityPath = string.IsNullOrEmpty(sketch.capabilityName) ? null
                : Path.Combine(sketch.codeFolder, sketch.capabilityName + ".cs").Replace('\\', '/');
            string testPath = Path.Combine(sketch.testFolder, sketch.className + "Tests.cs").Replace('\\', '/');

            WriteOnce(classPath, ClassSource(sketch), result);
            if (capabilityPath != null)
                WriteOnce(capabilityPath, CapabilitySource(sketch), result);
            if (Directory.Exists(sketch.testFolder))
                WriteOnce(testPath, TestSource(sketch, result.def), result);
            else
                result.note += "no test folder at " + sketch.testFolder;

            sketch.generatedClassPath = classPath;
        }

        /// <summary>A file that exists is somebody's; it is kept, and said to be kept.</summary>
        private static void WriteOnce(string path, string source, Result result)
        {
            if (File.Exists(path))
            {
                result.kept.Add(path);
                return;
            }
            File.WriteAllText(path, source);
            result.written.Add(path);
        }

        internal static string ClassSource(SubsystemSketch sketch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + sketch.codeNamespace);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// " + sketch.serviceName.ToUpperInvariant() + " — sketched by the subsystem flow ("
                + DateTime.Now.ToString("yyyy-MM-dd") + "). What it manages, asks, says, shows and is tuned");
            sb.AppendLine("    /// by is on its def; what it DOES is below, and every verb starts out saying so loudly.");
            sb.AppendLine("    /// </summary>");
            for (int i = 0; i < sketch.requests.Count; i++)
            {
                SketchRequest row = sketch.requests[i];
                sb.AppendLine("    [ServiceActionContract(" + ConstName(row.action, "Action") + ", "
                    + Quote(row.valueHint) + ")]");
            }
            for (int i = 0; i < sketch.announcements.Count; i++)
            {
                SketchAnnouncement row = sketch.announcements[i];
                sb.AppendLine("    [ServiceAnnouncement(" + ConstName(LastSegment(row.key), "Key")
                    + ", typeof(float), " + Quote(row.description) + ")]");
            }
            sb.Append("    public sealed class " + sketch.className + " : StateTreeService");
            if (!string.IsNullOrEmpty(sketch.capabilityName))
                sb.Append(", " + sketch.capabilityName);
            sb.AppendLine();
            sb.AppendLine("    {");

            for (int i = 0; i < sketch.requests.Count; i++)
            {
                SketchRequest row = sketch.requests[i];
                sb.AppendLine("        /// <summary>Served for '" + row.key + "'"
                    + (string.IsNullOrEmpty(row.valueHint) ? "" : " — " + row.valueHint) + ".</summary>");
                sb.AppendLine("        public const string " + ConstName(row.action, "Action") + " = "
                    + Quote(row.action) + ";");
                sb.AppendLine();
            }
            for (int i = 0; i < sketch.announcements.Count; i++)
            {
                SketchAnnouncement row = sketch.announcements[i];
                sb.AppendLine("        /// <summary>Announced as '" + row.key + "'"
                    + (string.IsNullOrEmpty(row.description) ? "" : " — " + row.description)
                    + ". Say it with Announce(" + ConstName(LastSegment(row.key), "Key") + ", value).</summary>");
                sb.AppendLine("        public const string " + ConstName(LastSegment(row.key), "Key") + " = "
                    + Quote(row.key) + ";");
                sb.AppendLine();
            }
            for (int i = 0; i < sketch.settings.Count; i++)
            {
                SketchSetting row = sketch.settings[i];
                sb.AppendLine("        [ServiceSetting(" + DefaultLiteral(row) + ", " + Quote(row.description) + ")]");
                if (row.kind == SketchSettingKind.Tag)
                    sb.AppendLine("        [WorldTag]");
                sb.AppendLine("        public " + FieldType(row.kind) + " " + row.name + ";");
                sb.AppendLine();
            }

            sb.AppendLine("        public " + sketch.className + "(StateTreeContextHost scope, ServiceDef definition)");
            sb.AppendLine("            : base(scope, definition)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");

            if (sketch.requests.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("        protected override void OnRequest(ServiceRequest request, string value)");
                sb.AppendLine("        {");
                sb.AppendLine("            switch (request.action)");
                sb.AppendLine("            {");
                for (int i = 0; i < sketch.requests.Count; i++)
                {
                    string constant = ConstName(sketch.requests[i].action, "Action");
                    sb.AppendLine("                case " + constant + ":");
                    sb.AppendLine("                    // TODO: the verb. Loud until then — a request that does nothing");
                    sb.AppendLine("                    // looks exactly like one that worked.");
                    sb.AppendLine("                    Debug.LogError(\"[" + SubsystemSketch.Capitalize(sketch.serviceName)
                        + "] '\" + " + constant + " + \"' is not implemented yet — asked with '\" + value + \"'.\");");
                    sb.AppendLine("                    break;");
                }
                sb.AppendLine("            }");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        internal static string CapabilitySource(SubsystemSketch sketch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("namespace " + sketch.codeNamespace);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// What a consumer asks the scope for instead of " + sketch.className
                + " — the verbs it may call, and");
            sb.AppendLine("    /// nothing about how they are done. A second implementation in the same slot is a def");
            sb.AppendLine("    /// naming another class that implements this.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public interface " + sketch.capabilityName);
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        internal static string TestSource(SubsystemSketch sketch, ServiceDef def)
        {
            string defPath = AssetDatabase.GetAssetPath(def);
            var sb = new StringBuilder();
            sb.AppendLine("using NUnit.Framework;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace PowerOfFire.DrawToPlay.Tests");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Generated with " + sketch.className + ": the def and the class agree, and the");
            sb.AppendLine("    /// class builds from its def. Passes the day it is written; fails the day they drift.</summary>");
            sb.AppendLine("    [TestFixture]");
            sb.AppendLine("    public sealed class " + sketch.className + "Tests");
            sb.AppendLine("    {");
            sb.AppendLine("        private const string k_DefPath = " + Quote(defPath) + ";");
            sb.AppendLine();
            sb.AppendLine("        [Test]");
            sb.AppendLine("        public void TheDefAndTheClassAgree_AndItBuilds()");
            sb.AppendLine("        {");
            sb.AppendLine("            var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(k_DefPath);");
            sb.AppendLine("            Assert.That(def, Is.Not.Null, \"the def this class was sketched with\");");
            sb.AppendLine("            Assert.That(def.serviceType, Is.EqualTo(typeof(" + sketch.codeNamespace + "."
                + sketch.className + ")));");
            sb.AppendLine();
            sb.AppendLine("            // Every request the def serves is an action the class declares.");
            sb.AppendLine("            var declared = new System.Collections.Generic.HashSet<string>();");
            sb.AppendLine("            foreach (ServiceActionContractAttribute contract in typeof(" + sketch.codeNamespace
                + "." + sketch.className + ")");
            sb.AppendLine("                .GetCustomAttributes(typeof(ServiceActionContractAttribute), true))");
            sb.AppendLine("                declared.Add(contract.action);");
            sb.AppendLine("            for (int i = 0; i < def.requests.Count; i++)");
            sb.AppendLine("                Assert.That(declared, Does.Contain(def.requests[i].action), def.requests[i].key);");
            sb.AppendLine();
            sb.AppendLine("            // And it builds from its def on a bare scope, with the class defaults in place.");
            sb.AppendLine("            var go = new GameObject(\"Scope\") { hideFlags = HideFlags.HideAndDontSave };");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var host = go.AddComponent<StateTreeContextHost>();");
            sb.AppendLine("                host.kind = def.scope;");
            sb.AppendLine("                host.autoStart = false;");
            sb.AppendLine("                var service = new " + sketch.codeNamespace + "." + sketch.className + "(host, def);");
            for (int i = 0; i < sketch.settings.Count; i++)
            {
                SketchSetting row = sketch.settings[i];
                if (row.kind == SketchSettingKind.Tag || row.kind == SketchSettingKind.String)
                    continue;
                sb.AppendLine("                Assert.That(service." + row.name + ", Is.EqualTo(" + DefaultLiteral(row) + "));");
            }
            sb.AppendLine("                service.Dispose();");
            sb.AppendLine("            }");
            sb.AppendLine("            finally");
            sb.AppendLine("            {");
            sb.AppendLine("                Object.DestroyImmediate(go);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ---- the installer row ------------------------------------------------------------

        /// <summary>On the open scene's host of the sketch's scope kind, when there is one; a
        /// generated scene (the M21 demo) gets its row in the builder, by hand, and is told so.</summary>
        private static void Install(SubsystemSketch sketch, Result result)
        {
            StateTreeContextHost host = null;
            foreach (StateTreeContextHost candidate in UnityEngine.Object.FindObjectsByType<StateTreeContextHost>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.kind == sketch.scope && candidate.gameObject.scene.IsValid())
                {
                    host = candidate;
                    break;
                }
            }
            if (host == null)
            {
                result.note += "no " + sketch.scope + " scope is open to install on — add the row to "
                    + "that scene's installer (or its builder) by hand";
                return;
            }

            StateTreeServiceInstaller installer = host.GetComponent<StateTreeServiceInstaller>()
                ?? Undo.AddComponent<StateTreeServiceInstaller>(host.gameObject);
            if (installer.RowFor(result.def) == null)
            {
                Undo.RecordObject(installer, "Install subsystem");
                installer.install.Add(new ServiceInstall(result.def));
                EditorUtility.SetDirty(installer);
                EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            }
            result.installedOn = host.name + " (" + host.gameObject.scene.name + ")";
        }

        // ---- spelling ---------------------------------------------------------------------

        /// <summary>'craft-start' + 'Action' → 'CraftStartAction'.</summary>
        internal static string ConstName(string word, string suffix)
        {
            var sb = new StringBuilder();
            var upper = true;
            for (int i = 0; word != null && i < word.Length; i++)
            {
                char c = word[i];
                if (!char.IsLetterOrDigit(c))
                {
                    upper = true;
                    continue;
                }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            if (sb.Length == 0 || char.IsDigit(sb[0]))
                sb.Insert(0, "The");
            return sb + suffix;
        }

        private static string LastSegment(string key)
        {
            int dot = key != null ? key.LastIndexOf('.') : -1;
            return dot >= 0 ? key.Substring(dot + 1) : key;
        }

        private static string Quote(string text)
        {
            return "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string FieldType(SketchSettingKind kind)
        {
            switch (kind)
            {
                case SketchSettingKind.Float: return "float";
                case SketchSettingKind.Int: return "int";
                case SketchSettingKind.Bool: return "bool";
                default: return "string";
            }
        }

        private static string DefaultLiteral(SketchSetting row)
        {
            switch (row.kind)
            {
                case SketchSettingKind.Float:
                    return row.numberDefault.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) + "f";
                case SketchSettingKind.Int:
                    return Mathf.RoundToInt(row.numberDefault).ToString();
                case SketchSettingKind.Bool:
                    return row.numberDefault > 0.5f ? "true" : "false";
                default:
                    return Quote(row.textDefault);
            }
        }
    }
}
