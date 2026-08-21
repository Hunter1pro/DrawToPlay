using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// ONE INSTALLER ROW (M36.3) — the def, and under it THIS scope's tuning as the same panel a
    /// def's settings and a placement's options use: every declared knob, the DEF's value dimmed
    /// beside it (or the class default where the def says nothing), a tick where this install
    /// differs. The third caller of <see cref="DeclaredOptionsPanel"/>, and the one that proves
    /// it was worth making general: it needed no new drawing at all.
    /// </summary>
    [CustomPropertyDrawer(typeof(ServiceInstall))]
    internal sealed class ServiceInstallDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect area, SerializedProperty property, GUIContent label)
        {
            SerializedProperty defProperty = property.FindPropertyRelative("def");
            SerializedProperty rows = property.FindPropertyRelative("settings.values");
            var line = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, defProperty, GUIContent.none);

            var def = defProperty.objectReferenceValue as ServiceDef;
            if (def == null || rows == null)
                return;

            List<DeclaredOption> declared = DeclaredOptions.OfInstall(def);
            if (declared.Count == 0 && rows.arraySize == 0)
                return;

            var panel = new Rect(area.x + 14f, line.yMax + 2f, area.width - 14f,
                area.height - line.height - 2f);
            DeclaredOptionsPanel.Draw(panel,
                new GUIContent("Tuned here", "What this scope's install of '" + def.name
                    + "' differs in. Unticked follows the def."),
                def.serviceType == null ? "the def names no service type"
                    : def.serviceType.Name + " declares no settings",
                declared, rows, DeclaredOptionRowShape.ServiceSetting);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            var def = property.FindPropertyRelative("def").objectReferenceValue as ServiceDef;
            SerializedProperty rows = property.FindPropertyRelative("settings.values");
            if (def == null || rows == null)
                return height;
            List<DeclaredOption> declared = DeclaredOptions.OfInstall(def);
            if (declared.Count == 0 && rows.arraySize == 0)
                return height;
            return height + 2f + DeclaredOptionsPanel.Height(declared, rows,
                DeclaredOptionRowShape.ServiceSetting);
        }
    }
}
