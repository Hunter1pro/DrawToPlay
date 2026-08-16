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
                EditorGUILayout.LabelField("  " + row.key + typed, EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(row.description))
                {
                    EditorGUILayout.LabelField("      " + row.description,
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }
    }
}
