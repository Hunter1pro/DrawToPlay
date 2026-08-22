using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE SKETCH'S FORM (M37.2) — what is still wrong at the top, the fields below, the button
    /// that writes it all at the bottom, and where the three descriptions drift after.
    ///
    /// A UI TOOLKIT HOST, by the project rule: every typed reference on the sketch is a UI
    /// Toolkit drawer (⛃ from what the sketch declares), and a UI Toolkit drawer inside an
    /// IMGUI editor draws the words "No GUI Implemented" — which is exactly what the first
    /// version of this file did without anyone looking. The fields are plain PropertyFields;
    /// the validators re-run on every change and say so where the author is looking.
    /// </summary>
    [CustomEditor(typeof(SubsystemSketch))]
    internal sealed class SubsystemSketchEditor : UnityEditor.Editor
    {
        private HelpBox m_Findings;
        private HelpBox m_Drift;
        private Button m_Generate;
        private List<SketchFinding> m_Current;

        public override VisualElement CreateInspectorGUI()
        {
            var sketch = (SubsystemSketch)target;
            var root = new VisualElement();

            m_Findings = new HelpBox("", HelpBoxMessageType.Info);
            m_Findings.style.marginBottom = 6f;
            root.Add(m_Findings);

            // THE FIELDS ARE UNITY'S OWN — and because this host is UI Toolkit, the sketch's
            // entry references come up as the pickers their drawers make them.
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "m_Script")
                    continue;
                var field = new PropertyField(property.Copy());
                field.RegisterValueChangeCallback(_ => Revalidate(sketch));
                root.Add(field);
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 8f;
            m_Generate = new Button(() =>
            {
                SubsystemGenerator.Generate(sketch);
                Revalidate(sketch);
            });
            m_Generate.style.flexGrow = 1f;
            m_Generate.style.height = 26f;
            row.Add(m_Generate);
            var toc = new Button(SubsystemsWindow.Open)
            {
                text = "Subsystems…", tooltip = "The project's table of contents."
            };
            toc.style.width = 100f;
            toc.style.height = 26f;
            row.Add(toc);
            root.Add(row);

            var driftTitle = new Label("Drift — sketch · def · class");
            driftTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            driftTitle.style.marginTop = 8f;
            root.Add(driftTitle);
            m_Drift = new HelpBox("", HelpBoxMessageType.Info);
            root.Add(m_Drift);

            root.Bind(serializedObject);
            Revalidate(sketch);
            return root;
        }

        private void Revalidate(SubsystemSketch sketch)
        {
            m_Current = SubsystemSketchValidator.Validate(sketch);
            bool blocked = SubsystemSketchValidator.Blocks(m_Current);

            if (m_Current.Count == 0)
            {
                m_Findings.text = (sketch.generatedDef != null ? "Generated. " : "Ready to generate. ")
                    + sketch.className + (string.IsNullOrEmpty(sketch.capabilityName)
                        ? "" : " : " + sketch.capabilityName)
                    + " on " + sketch.scope + " — asks " + sketch.requests.Count + ", says "
                    + sketch.announcements.Count + ", shows " + sketch.spawns.Count + ", has "
                    + sketch.attributes.Count + ", tuned by " + sketch.settings.Count + ".";
                m_Findings.messageType = HelpBoxMessageType.Info;
            }
            else
            {
                m_Findings.text = Lines(m_Current);
                m_Findings.messageType = blocked ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
            }

            m_Generate.SetEnabled(!blocked);
            m_Generate.text = sketch.generatedDef == null ? "Generate" : "Regenerate def";
            m_Generate.tooltip = sketch.generatedDef == null
                ? "Write the def, the class, the capability, a test and an installer row."
                : "Rewrite the def from this sketch. The class is never rewritten.";

            if (sketch.generatedDef == null)
            {
                m_Drift.text = "Nothing generated yet.";
                m_Drift.messageType = HelpBoxMessageType.None;
                return;
            }
            List<SketchFinding> drift = SubsystemDrift.Find(sketch.generatedDef, sketch);
            if (drift.Count == 0)
            {
                m_Drift.text = "The sketch, the def and " + sketch.className + " agree.";
                m_Drift.messageType = HelpBoxMessageType.Info;
            }
            else
            {
                m_Drift.text = Lines(drift);
                m_Drift.messageType = SubsystemSketchValidator.Blocks(drift)
                    ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
            }
        }

        private static string Lines(List<SketchFinding> findings)
        {
            var lines = new List<string>();
            for (int i = 0; i < findings.Count; i++)
                lines.Add(findings[i].ToString());
            return string.Join("\n", lines);
        }
    }
}
