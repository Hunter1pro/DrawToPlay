using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE PROJECT'S SUBSYSTEMS, AS A TABLE OF CONTENTS (M37.1) — every def, by scope, with its
    /// class, its counts and where it is installed. Pure: no GUI, so a test can ask what the
    /// window would show.
    ///
    /// This is the engineer's view of a project — "this game has a bag, a bench, travel, …" —
    /// and it exists whether or not the flow is ever used to add to it.
    /// </summary>
    internal static class SubsystemCatalog
    {
        internal sealed class Entry
        {
            public ServiceDef def;
            public string path;
            public Type serviceType;
            public string typeName;
            public int requests, announcements, spawns, attributes, settings;
            public readonly List<string> installedIn = new List<string>();
            public SubsystemSketch sketch;

            public bool hasClass => serviceType != null;

            /// <summary>A def that names no class and builds a body is a KIND — a thing a
            /// level spawns by placement — not a subsystem a scope installs. Listed apart.</summary>
            public bool isKind => string.IsNullOrEmpty(typeName) && def.body != null && def.body.IsThing;
        }

        /// <summary>Every def in the project, in scope order then by name.</summary>
        internal static List<Entry> Build()
        {
            var entries = new List<Entry>();
            Dictionary<ServiceDef, SubsystemSketch> sketches = SketchesByDef();
            Dictionary<string, List<string>> scenesByDefPath = ScenesReferencing();

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ServiceDef)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(path);
                if (def == null)
                    continue;
                var entry = new Entry
                {
                    def = def,
                    path = path,
                    serviceType = def.serviceType,
                    typeName = def.serviceTypeName,
                    requests = def.requests != null ? def.requests.Count : 0,
                    announcements = DeclaredApi.Announcements(def.name).Count,
                    spawns = def.spawns != null ? def.spawns.Count : 0,
                    attributes = def.attributes != null ? def.attributes.Count : 0,
                    settings = def.settings != null ? def.settings.values.Count : 0
                };
                if (scenesByDefPath.TryGetValue(path, out List<string> scenes))
                    entry.installedIn.AddRange(scenes);
                sketches.TryGetValue(def, out entry.sketch);
                entries.Add(entry);
            }

            entries.Sort((a, b) =>
            {
                int byScope = a.def.scope.CompareTo(b.def.scope);
                return byScope != 0 ? byScope
                    : string.Compare(a.def.name, b.def.name, StringComparison.Ordinal);
            });
            return entries;
        }

        /// <summary>
        /// Which scenes reference each def — read from the asset database's dependency list, so
        /// a BINARY scene (every M21 level is one) answers as well as a text one. A scene that
        /// depends on a def directly is a scene with an installer row for it; nothing else in
        /// a scene holds a def by reference.
        /// </summary>
        private static Dictionary<string, List<string>> ScenesReferencing()
        {
            var result = new Dictionary<string, List<string>>();
            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scenePath.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;
                string[] dependencies = AssetDatabase.GetDependencies(scenePath, false);
                for (int i = 0; i < dependencies.Length; i++)
                {
                    if (!dependencies[i].EndsWith(".asset", StringComparison.Ordinal))
                        continue;
                    if (!result.TryGetValue(dependencies[i], out List<string> scenes))
                    {
                        scenes = new List<string>();
                        result.Add(dependencies[i], scenes);
                    }
                    scenes.Add(System.IO.Path.GetFileNameWithoutExtension(scenePath));
                }
            }
            return result;
        }

        private static Dictionary<ServiceDef, SubsystemSketch> SketchesByDef()
        {
            var result = new Dictionary<ServiceDef, SubsystemSketch>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SubsystemSketch)))
            {
                var sketch = AssetDatabase.LoadAssetAtPath<SubsystemSketch>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (sketch != null && sketch.generatedDef != null)
                    result[sketch.generatedDef] = sketch;
            }
            return result;
        }

        /// <summary>Every sketch in the project, generated or not.</summary>
        internal static List<SubsystemSketch> Sketches()
        {
            var sketches = new List<SubsystemSketch>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SubsystemSketch)))
            {
                var sketch = AssetDatabase.LoadAssetAtPath<SubsystemSketch>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (sketch != null)
                    sketches.Add(sketch);
            }
            return sketches;
        }
    }
}
