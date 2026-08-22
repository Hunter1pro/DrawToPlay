using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE SKETCH'S FORM (M37.2) — what is still wrong at the top, the fields below, the button
    /// that writes it all at the bottom.
    ///
    /// The fields are Unity's own: every typed reference on the sketch is already a ⛃ picker
    /// (the sketch is a neighbourhood, so the pickers offer what it declares), registries are
    /// object fields, rows are lists. What this editor adds is the VALIDATORS, re-run on every
    /// change and shown where the author is looking — the questions the runtime would ask
    /// later, asked now.
    /// </summary>
    [CustomEditor(typeof(SubsystemSketch))]
    internal sealed class SubsystemSketchEditor : UnityEditor.Editor
    {
        private List<SketchFinding> m_Findings;

        public override void OnInspectorGUI()
        {
            var sketch = (SubsystemSketch)target;
            m_Findings ??= SubsystemSketchValidator.Validate(sketch);

            DrawFindings(sketch);

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                m_Findings = SubsystemSketchValidator.Validate(sketch);

            EditorGUILayout.Space(8f);
            DrawGenerate(sketch);
        }

        private void DrawFindings(SubsystemSketch sketch)
        {
            bool blocked = SubsystemSketchValidator.Blocks(m_Findings);
            if (m_Findings.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    (sketch.generatedDef != null ? "Generated. " : "Ready to generate. ")
                    + sketch.className + (string.IsNullOrEmpty(sketch.capabilityName)
                        ? "" : " : " + sketch.capabilityName)
                    + " on " + sketch.scope + " — asks " + sketch.requests.Count + ", says "
                    + sketch.announcements.Count + ", shows " + sketch.spawns.Count + ", has "
                    + sketch.attributes.Count + ", tuned by " + sketch.settings.Count + ".",
                    MessageType.Info);
                return;
            }
            var lines = new List<string>();
            for (int i = 0; i < m_Findings.Count; i++)
                lines.Add(m_Findings[i].ToString());
            EditorGUILayout.HelpBox(string.Join("\n", lines),
                blocked ? MessageType.Error : MessageType.Warning);
        }

        private void DrawGenerate(SubsystemSketch sketch)
        {
            bool blocked = SubsystemSketchValidator.Blocks(m_Findings);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(blocked))
            {
                string label = sketch.generatedDef == null ? "Generate" : "Regenerate def";
                if (GUILayout.Button(new GUIContent(label, sketch.generatedDef == null
                        ? "Write the def, the class, the capability, a test and an installer row."
                        : "Rewrite the def from this sketch. The class is never rewritten."),
                    GUILayout.Height(26f)))
                {
                    SubsystemGenerator.Generate(sketch);
                    m_Findings = SubsystemSketchValidator.Validate(sketch);
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button(new GUIContent("Subsystems…", "The project's table of contents."),
                GUILayout.Width(100f), GUILayout.Height(26f)))
                SubsystemsWindow.Open();
            EditorGUILayout.EndHorizontal();
        }
    }
}
