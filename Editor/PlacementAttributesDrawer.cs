using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE PLACEMENT'S OPTIONS, AS A PANEL (M34) — every attribute this kind declares, the value
    /// it would have, and a tick where this one differs.
    ///
    /// The same shape the graph parameters use, for the same reason: a checkbox per declared
    /// knob rather than a list you add rows to. "3 typed into an unticked row" and "3 typed into
    /// a ticked one" mean different things — follow the kind, or pin THIS one at 3 — and the
    /// difference has to survive somebody changing the prefab's seed later.
    ///
    /// THE UNTICKED VALUE IS THE BODY'S OWN SEED, so the panel shows what this placement will
    /// actually start at rather than an empty box — and a DASH where the body seeds nothing,
    /// because a confident 0 next to a number that comes from somewhere else is worse than
    /// saying nothing.
    ///
    /// It draws on the SET rather than the list because Unity gives a field attribute to a
    /// list's ELEMENTS: a per-row drawer can only decorate what somebody already added, and
    /// what an author needs is the row that is not there yet.
    ///
    /// M36.2: the drawing is <see cref="DeclaredOptionsPanel"/>'s, shared with a def's settings
    /// and an install's overrides. This class only says WHO declares (the kind's def) and WHAT
    /// the fallback is (the seed) — see <see cref="DeclaredOptions.OfKind"/>.
    /// </summary>
    [CustomPropertyDrawer(typeof(PlacementAttributesAttribute))]
    internal sealed class PlacementAttributesDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect area, SerializedProperty property, GUIContent label)
        {
            SerializedProperty rows = Rows(property);
            if (rows == null)
            {
                EditorGUI.PropertyField(area, property, label, true);
                return;
            }

            LevelObjectKindDef kind = Kind(property);
            ServiceDef def = kind != null ? kind.service : null;
            DeclaredOptionsPanel.Draw(area,
                new GUIContent("Options", def != null
                    ? "What '" + def.serviceName + "' says this kind has. Tick to give THIS one "
                        + "its own number."
                    : "This placement's kind has no def, so it declares no options."),
                kind == null ? "pick a kind first" : "this kind declares no attributes",
                DeclaredOptions.OfKind(def), rows, DeclaredOptionRowShape.PlacementAttribute);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty rows = Rows(property);
            if (rows == null)
                return EditorGUI.GetPropertyHeight(property, label, true);

            LevelObjectKindDef kind = Kind(property);
            return DeclaredOptionsPanel.Height(DeclaredOptions.OfKind(kind != null ? kind.service : null),
                rows, DeclaredOptionRowShape.PlacementAttribute);
        }

        /// <summary>The rows this panel edits — the set's own list. The panel is drawn on the
        /// SET rather than on a bare list because Unity hands a field attribute to a list's
        /// ELEMENTS, and a per-element drawer cannot offer what nobody has overridden yet.</summary>
        private static SerializedProperty Rows(SerializedProperty property)
        {
            SerializedProperty rows = property.FindPropertyRelative("values");
            return rows != null && rows.isArray ? rows : null;
        }

        /// <summary>This placement's kind row, through the sibling field the attribute names.</summary>
        private LevelObjectKindDef Kind(SerializedProperty property)
        {
            var marked = fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo,
                    typeof(PlacementAttributesAttribute)) as PlacementAttributesAttribute
                : null;
            if (marked == null || string.IsNullOrEmpty(marked.kindField))
                return null;

            string path = property.propertyPath;
            int cut = path.LastIndexOf('.');
            if (cut < 0)
                return null;
            SerializedProperty kind = property.serializedObject.FindProperty(
                path.Substring(0, cut + 1) + marked.kindField);
            string id = kind?.FindPropertyRelative("entryId")?.stringValue;
            string named = kind?.FindPropertyRelative("entryName")?.stringValue;
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(named))
                return null;

            foreach (string guid in AssetDatabase.FindAssets(
                "t:" + nameof(LevelObjectKindRegistry)))
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guid));
                var row = (string.IsNullOrEmpty(id)
                    ? registry?.FindByName(named)
                    : registry?.FindById(id)) as LevelObjectKindDef;
                if (row != null)
                    return row;
            }
            return null;
        }
    }
}
