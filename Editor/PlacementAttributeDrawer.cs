using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// ONE OPTION ON A PLACEMENT (M34) — the attribute name picked from what this placement's
    /// KIND says it has, and the number beside it.
    ///
    /// This is the row that makes a placement read like a device's options panel rather than a
    /// pair of typed fields. The offer comes from the same road the entry picker walks: the
    /// row's sibling `kind`, that kind's def, and the attributes that def declares. A name the
    /// def does not have is refused at spawn with a warning — offering only the declared ones is
    /// how it stops being possible to type instead.
    /// </summary>
    [CustomPropertyDrawer(typeof(PlacementAttribute))]
    internal sealed class PlacementAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect area, SerializedProperty property, GUIContent label)
        {
            SerializedProperty name = property.FindPropertyRelative("attribute");
            SerializedProperty value = property.FindPropertyRelative("value");
            if (name == null || value == null)
            {
                EditorGUI.PropertyField(area, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(area, label, property);

            float pickWidth = 24f;
            float numberWidth = Mathf.Min(80f, area.width * 0.3f);
            var nameArea = new Rect(area.x, area.y,
                area.width - numberWidth - pickWidth - 8f, EditorGUIUtility.singleLineHeight);
            var pickArea = new Rect(nameArea.xMax + 2f, area.y, pickWidth,
                EditorGUIUtility.singleLineHeight);
            var numberArea = new Rect(pickArea.xMax + 6f, area.y, numberWidth,
                EditorGUIUtility.singleLineHeight);

            List<string> offers = Offers(property);
            bool declared = offers.Contains(name.stringValue);

            using (new EditorGUI.DisabledScope(declared))
            {
                // PICKED IS LOCKED, as everywhere: a name the kind declares is a link to it, and
                // a link you can retype is one that silently stops matching.
                string typed = EditorGUI.TextField(nameArea, GUIContent.none, name.stringValue);
                if (typed != name.stringValue)
                    name.stringValue = typed;
            }

            if (GUI.Button(pickArea, new GUIContent("▾", offers.Count > 0
                ? "Pick one of the attributes this kind declares."
                : "This placement's kind declares no attributes.")))
            {
                var menu = new GenericMenu();
                if (offers.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent("the kind's def declares no attributes"));
                }
                for (int i = 0; i < offers.Count; i++)
                {
                    string chosen = offers[i];
                    menu.AddItem(new GUIContent(chosen), chosen == name.stringValue, () =>
                    {
                        name.stringValue = chosen;
                        name.serializedObject.ApplyModifiedProperties();
                    });
                }
                if (declared)
                {
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Unpick — type it freely"), false, () =>
                    {
                        name.stringValue = "";
                        name.serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.DropDown(pickArea);
            }

            value.floatValue = EditorGUI.FloatField(numberArea, GUIContent.none,
                value.floatValue);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>What this placement's kind says it HAS: sibling kind → its row → the def
        /// behind it → the attributes that def declares.</summary>
        private static List<string> Offers(SerializedProperty property)
        {
            var offers = new List<string>();
            string path = property.propertyPath;
            int list = path.LastIndexOf(".attributes.Array", System.StringComparison.Ordinal);
            if (list < 0)
                return offers;

            SerializedProperty kind = property.serializedObject.FindProperty(
                path.Substring(0, list + 1) + "kind");
            string kindId = kind?.FindPropertyRelative("entryId")?.stringValue;
            string kindName = kind?.FindPropertyRelative("entryName")?.stringValue;
            if (string.IsNullOrEmpty(kindId) && string.IsNullOrEmpty(kindName))
                return offers;

            foreach (string guid in AssetDatabase.FindAssets(
                "t:" + nameof(LevelObjectKindRegistry)))
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guid));
                var row = (string.IsNullOrEmpty(kindId)
                    ? registry?.FindByName(kindName)
                    : registry?.FindById(kindId)) as LevelObjectKindDef;
                ServiceDef def = row != null ? row.service : null;
                for (int i = 0; def != null && i < def.attributes.Count; i++)
                {
                    string named = def.attributes[i] != null ? def.attributes[i].Name : "";
                    if (!string.IsNullOrEmpty(named) && !offers.Contains(named))
                        offers.Add(named);
                }
                if (offers.Count > 0)
                    break;
            }
            return offers;
        }
    }
}
