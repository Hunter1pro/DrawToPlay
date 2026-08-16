using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The def's inspector, with THE API READABLE (§4d): under the plain fields, one line
    /// per declared request — "ui.bag.use — item of M21Items — Use one of the named item" —
    /// so the subsystem root answers "how do I call this?" at a glance, typed included.
    /// </summary>
    [CustomEditor(typeof(ServiceDef))]
    public sealed class ServiceDefEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var def = (ServiceDef)target;
            if (def.requests.Count > 0 || def.flows != null)
            {
                EditorGUILayout.Space(4f);
                if (GUILayout.Button("Subsystem APIs…", GUILayout.Width(140f)))
                    SubsystemApisWindow.Open();
            }
            if (def.requests.Count == 0)
                return;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Request API", EditorStyles.boldLabel);
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row == null || string.IsNullOrEmpty(row.key))
                    continue;
                string typed = row.namesRowOf != null
                    ? " — row of " + row.namesRowOf.name
                    : "";
                string served = !string.IsNullOrEmpty(row.stateId)
                    ? " · state '" + row.stateId + "'"
                    : (string.IsNullOrEmpty(row.action) ? "" : " · " + row.action)
                        + (row.reactions != null && row.reactions.Count > 0
                            ? " · " + row.reactions.Count + " beat"
                                + (row.reactions.Count == 1 ? "" : "s")
                            : "");
                EditorGUILayout.LabelField("  " + row.key + typed + served,
                    EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(row.description))
                {
                    EditorGUILayout.LabelField("      " + row.description,
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            for (int i = 0; i < def.announcements.Count; i++)
            {
                ServiceAnnouncement announced = def.announcements[i];
                if (announced == null || string.IsNullOrEmpty(announced.key))
                    continue;
                if (i == 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Announces", EditorStyles.boldLabel);
                }
                string suffix = string.IsNullOrEmpty(announced.payloadTypeName)
                    ? ""
                    : " : " + announced.payloadTypeName;
                EditorGUILayout.LabelField("  " + announced.key + suffix,
                    EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(announced.description))
                    EditorGUILayout.LabelField("      " + announced.description,
                        EditorStyles.wordWrappedMiniLabel);
            }

            DrawScreenSurface(def);
        }

        /// <summary>THE SCREEN SURFACE (§4g): what this subsystem's spawned skins can DO —
        /// their declared verbs and public fields, read from the prefab, so the def's
        /// visual is the WIDGET, not a tree of states.</summary>
        private static void DrawScreenSurface(ServiceDef def)
        {
            if (def.spawns == null || def.spawns.Count == 0)
                return;
            var drewHeader = false;
            for (int i = 0; i < def.spawns.Count; i++)
            {
                var spawn = def.spawns[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.entryName))
                    continue;
                GameObject prefab = SpawnPrefab(spawn.entryName);
                if (prefab == null)
                    continue;
                UiViewBehaviour[] views = prefab.GetComponentsInChildren<UiViewBehaviour>(true);
                for (int v = 0; v < views.Length; v++)
                {
                    if (views[v] == null)
                        continue;
                    if (!drewHeader)
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField("Screen — spawned skins",
                            EditorStyles.boldLabel);
                        drewHeader = true;
                    }
                    System.Type type = views[v].GetType();
                    EditorGUILayout.LabelField("  " + spawn.entryName + " · " + type.Name,
                        EditorStyles.miniLabel);

                    var verbs = (UiVerbContractAttribute[])type.GetCustomAttributes(
                        typeof(UiVerbContractAttribute), true);
                    if (verbs.Length > 0)
                    {
                        var text = new System.Text.StringBuilder("      verbs: ");
                        for (int k = 0; k < verbs.Length; k++)
                        {
                            if (k > 0)
                                text.Append(", ");
                            text.Append(verbs[k].verb);
                            if (!string.IsNullOrEmpty(verbs[k].argumentHint))
                                text.Append('(').Append(verbs[k].argumentHint).Append(')');
                        }
                        EditorGUILayout.LabelField(text.ToString(),
                            EditorStyles.wordWrappedMiniLabel);
                    }

                    var fields = type.GetFields(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly);
                    if (fields.Length > 0)
                    {
                        var text = new System.Text.StringBuilder("      fields: ");
                        for (int k = 0; k < fields.Length; k++)
                        {
                            if (k > 0)
                                text.Append(", ");
                            text.Append(fields[k].Name);
                        }
                        EditorGUILayout.LabelField(text.ToString(),
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        /// <summary>The prefab behind a spawned UI row name — found through the UI
        /// registries, because the def's own registry is the DOMAIN's.</summary>
        private static GameObject SpawnPrefab(string rowName)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(UiRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<UiRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                var row = registry != null ? registry.FindByName(rowName) as UiDef : null;
                if (row != null && row.prefab != null)
                    return row.prefab;
            }
            return null;
        }
    }
}
