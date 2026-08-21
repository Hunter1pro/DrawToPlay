using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE DECLARED-OPTIONS PANEL (M36.2) — every option somebody declares, the value it would
    /// have dimmed beside it, a tick where THIS layer differs, strays after.
    ///
    /// One widget, three callers: a placement's attributes against the body's seeds (M34.1c), a
    /// def's settings against the class defaults (M36.1), an install's overrides against the
    /// def (M36.3). Building it twice was the tell that one of them was wrong; it is the
    /// general "declared knobs with defaults" control, and everything that has such knobs
    /// draws with it.
    ///
    /// It works on a SERIALIZED list of override rows so it serves a PropertyDrawer and a
    /// custom editor alike, and so undo, prefab overrides and multi-object editing come from
    /// Unity rather than from here. The rows' field names are a <see cref="DeclaredOptionRowShape"/>.
    /// </summary>
    internal static class DeclaredOptionsPanel
    {
        private const float k_Line = 2f;

        private static float lineHeight => EditorGUIUtility.singleLineHeight + k_Line;

        /// <summary>How tall the panel is for these options and rows.</summary>
        public static float Height(IReadOnlyList<DeclaredOption> declared, SerializedProperty rows,
            DeclaredOptionRowShape shape)
        {
            int lines = 1;   // the title
            lines += declared != null && declared.Count > 0 ? declared.Count : 1;
            lines += Strays(rows, declared, shape).Count;
            return lines * lineHeight + k_Line;
        }

        /// <summary>Draw the panel into <paramref name="area"/>.</summary>
        public static void Draw(Rect area, GUIContent title, string emptyMessage,
            IReadOnlyList<DeclaredOption> declared, SerializedProperty rows,
            DeclaredOptionRowShape shape)
        {
            var line = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(line, title, EditorStyles.boldLabel);
            line.y += lineHeight;

            if (declared == null || declared.Count == 0)
            {
                EditorGUI.LabelField(line, emptyMessage, EditorStyles.miniLabel);
                line.y += lineHeight;
                DrawStrays(ref line, rows, declared, shape);
                return;
            }

            for (int i = 0; i < declared.Count; i++)
            {
                DeclaredOption option = declared[i];
                if (option == null || string.IsNullOrEmpty(option.name))
                    continue;

                int index = IndexOf(rows, option.name, shape);
                bool overridden = index >= 0;

                var tickArea = new Rect(line.x, line.y, 18f, line.height);
                var nameArea = new Rect(tickArea.xMax + 2f, line.y,
                    Mathf.Max(80f, line.width * 0.45f), line.height);
                var valueArea = new Rect(nameArea.xMax + 6f, line.y,
                    Mathf.Max(60f, line.width - nameArea.width - 30f), line.height);

                bool tick = EditorGUI.Toggle(tickArea, overridden);
                EditorGUI.LabelField(nameArea, new GUIContent(option.name, option.description));

                if (tick != overridden)
                {
                    if (tick)
                        Add(rows, option, shape);
                    else
                        rows.DeleteArrayElementAtIndex(index);
                    rows.serializedObject.ApplyModifiedProperties();
                    index = IndexOf(rows, option.name, shape);
                    overridden = index >= 0;
                }

                if (overridden)
                    DrawValue(valueArea, rows.GetArrayElementAtIndex(index), option, shape);
                else
                    DrawFallback(valueArea, option);
                line.y += lineHeight;
            }

            DrawStrays(ref line, rows, declared, shape);
        }

        // ---- the two halves of a line -----------------------------------------------------

        private static void DrawFallback(Rect area, DeclaredOption option)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                if (option.fallback == null)
                {
                    EditorGUI.LabelField(area, option.fallbackLabel);
                    return;
                }
                switch (option.kind)
                {
                    case DeclaredOptionKind.Bool:
                        EditorGUI.Toggle(area, (bool)option.fallback);
                        break;
                    case DeclaredOptionKind.Enum:
                        EditorGUI.EnumPopup(area, (Enum)option.fallback);
                        break;
                    default:
                        EditorGUI.TextField(area, Convert.ToString(option.fallback));
                        break;
                }
            }
        }

        private static void DrawValue(Rect area, SerializedProperty row, DeclaredOption option,
            DeclaredOptionRowShape shape)
        {
            SerializedProperty number = Field(row, shape.floatField);
            SerializedProperty text = Field(row, shape.stringField);

            switch (option.kind)
            {
                case DeclaredOptionKind.Float:
                    if (number != null)
                        number.floatValue = EditorGUI.FloatField(area, number.floatValue);
                    break;

                case DeclaredOptionKind.Int:
                    if (number != null)
                        number.floatValue = EditorGUI.IntField(area, Mathf.RoundToInt(number.floatValue));
                    break;

                case DeclaredOptionKind.Bool:
                    if (number != null)
                        number.floatValue = EditorGUI.Toggle(area, number.floatValue > 0.5f) ? 1f : 0f;
                    break;

                case DeclaredOptionKind.Enum:
                    if (text != null && option.enumType != null)
                    {
                        Enum current;
                        try { current = (Enum)Enum.Parse(option.enumType, text.stringValue, true); }
                        catch (ArgumentException) { current = (Enum)(option.fallback ?? Activator.CreateInstance(option.enumType)); }
                        Enum next = EditorGUI.EnumPopup(area, current);
                        if (!Equals(next, current))
                            text.stringValue = next.ToString();
                    }
                    break;

                case DeclaredOptionKind.Tag:
                    DrawTag(area, row, option, shape);
                    break;

                default:
                    if (text != null)
                        text.stringValue = EditorGUI.TextField(area, text.stringValue);
                    break;
            }
        }

        /// <summary>A tag is PICKED: the name read-only, a ▾ offering what the owner declares,
        /// and the row's id written beside the name so a rename travels.</summary>
        private static void DrawTag(Rect area, SerializedProperty row, DeclaredOption option,
            DeclaredOptionRowShape shape)
        {
            SerializedProperty text = Field(row, shape.stringField);
            SerializedProperty id = Field(row, shape.idField);
            if (text == null)
                return;

            var nameArea = new Rect(area.x, area.y, area.width - 26f, area.height);
            var pickArea = new Rect(nameArea.xMax + 2f, area.y, 22f, area.height);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(nameArea, string.IsNullOrEmpty(text.stringValue)
                    ? "(pick a tag)" : text.stringValue);
            }
            if (!GUI.Button(pickArea, "▾"))
                return;

            List<WorldTagDef> offers = option.tagOffers != null
                ? option.tagOffers() : new List<WorldTagDef>();
            var menu = new GenericMenu();
            if (offers.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("nothing declares a tag vocabulary here"));
            }
            string current = text.stringValue;
            string path = row.propertyPath;
            SerializedObject owner = row.serializedObject;
            for (int i = 0; i < offers.Count; i++)
            {
                WorldTagDef tag = offers[i];
                menu.AddItem(new GUIContent(tag.name), tag.name == current, () =>
                {
                    owner.Update();
                    SerializedProperty live = owner.FindProperty(path);
                    SerializedProperty liveText = Field(live, shape.stringField);
                    SerializedProperty liveId = Field(live, shape.idField);
                    if (liveText != null)
                        liveText.stringValue = tag.name;
                    if (liveId != null)
                        liveId.stringValue = tag.id;
                    owner.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }

        // ---- strays -----------------------------------------------------------------------

        /// <summary>Rows nobody declares — a class that lost a knob, a kind that lost an
        /// attribute. The runtime refuses them out loud; this is where they get deleted.</summary>
        private static void DrawStrays(ref Rect line, SerializedProperty rows,
            IReadOnlyList<DeclaredOption> declared, DeclaredOptionRowShape shape)
        {
            List<int> strays = Strays(rows, declared, shape);
            for (int i = 0; i < strays.Count; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(strays[i]);
                var nameArea = new Rect(line.x, line.y, line.width - 26f, line.height);
                var dropArea = new Rect(nameArea.xMax + 2f, line.y, 22f, line.height);
                SerializedProperty name = Field(row, shape.nameField);
                EditorGUI.LabelField(nameArea, "⚠ '" + (name != null ? name.stringValue : "?")
                    + "' is not declared here", EditorStyles.miniLabel);
                if (GUI.Button(dropArea, "✕"))
                {
                    rows.DeleteArrayElementAtIndex(strays[i]);
                    rows.serializedObject.ApplyModifiedProperties();
                    return;
                }
                line.y += lineHeight;
            }
        }

        /// <summary>Indices of the rows that name no declared option.</summary>
        internal static List<int> Strays(SerializedProperty rows,
            IReadOnlyList<DeclaredOption> declared, DeclaredOptionRowShape shape)
        {
            var strays = new List<int>();
            for (int i = 0; rows != null && rows.isArray && i < rows.arraySize; i++)
            {
                SerializedProperty name = Field(rows.GetArrayElementAtIndex(i), shape.nameField);
                string named = name != null ? name.stringValue : "";
                var known = false;
                for (int d = 0; declared != null && d < declared.Count; d++)
                {
                    if (declared[d] != null && declared[d].name == named)
                        known = true;
                }
                if (!known)
                    strays.Add(i);
            }
            return strays;
        }

        // ---- rows -------------------------------------------------------------------------

        private static int IndexOf(SerializedProperty rows, string name, DeclaredOptionRowShape shape)
        {
            for (int i = 0; rows != null && rows.isArray && i < rows.arraySize; i++)
            {
                SerializedProperty named = Field(rows.GetArrayElementAtIndex(i), shape.nameField);
                if (named != null && named.stringValue == name)
                    return i;
            }
            return -1;
        }

        /// <summary>A new row for an option, started at its fallback so ticking changes
        /// nothing until a number is typed — "this one, pinned at what it already was".</summary>
        private static void Add(SerializedProperty rows, DeclaredOption option,
            DeclaredOptionRowShape shape)
        {
            int index = rows.arraySize;
            rows.InsertArrayElementAtIndex(index);
            SerializedProperty row = rows.GetArrayElementAtIndex(index);
            SerializedProperty name = Field(row, shape.nameField);
            SerializedProperty number = Field(row, shape.floatField);
            SerializedProperty text = Field(row, shape.stringField);
            SerializedProperty id = Field(row, shape.idField);
            if (name != null)
                name.stringValue = option.name;
            if (number != null)
                number.floatValue = option.fallback switch
                {
                    float f => f,
                    int n => n,
                    bool b => b ? 1f : 0f,
                    _ => 0f
                };
            if (text != null)
                text.stringValue = option.fallback is string s ? s
                    : option.fallback is Enum e ? e.ToString() : "";
            if (id != null)
                id.stringValue = "";
        }

        private static SerializedProperty Field(SerializedProperty row, string name)
        {
            return row != null && !string.IsNullOrEmpty(name) ? row.FindPropertyRelative(name) : null;
        }
    }
}
