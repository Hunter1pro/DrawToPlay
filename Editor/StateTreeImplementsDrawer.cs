using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A field that asks by PROMISE (M30.2b) — <see cref="StateTreeImplementsAttribute"/> on a
    /// string, drawn as the things that keep it.
    ///
    /// The offers are the implementer rows this asset's declared catalogs hold, which is the same
    /// rule every row picker follows and the reason the menu is short enough to read. What lands
    /// in the field is a row NAME, so the runtime lookup underneath is untouched.
    ///
    /// PICKED IS LOCKED, as everywhere else in this toolset: a value the contract recognises is a
    /// LINK, and a link you can also type into is a link that silently stops matching. The way out
    /// is in the menu, because an author may legitimately name a row a catalog does not hold yet —
    /// and a name nothing offers stays editable and says so, rather than being quietly accepted.
    /// </summary>
    [CustomPropertyDrawer(typeof(StateTreeImplementsAttribute))]
    internal sealed class StateTreeImplementsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new PropertyField(property);

            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath,
                property.displayName);
            return container;
        }

        private void Build(VisualElement container, SerializedObject owner, string path,
            string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;

            string contractName = Attribute()?.contractName ?? "";
            var offers = new List<StateTreeRegistryEntry>();
            ContractDef contract = Offers(owner, contractName, offers);
            bool bound = Offered(offers, property.stringValue);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            container.Add(row);

            var text = new TextField(label);
            text.AddToClassList(TextField.alignedFieldUssClassName);
            text.style.flexGrow = 1f;
            text.BindProperty(property);
            text.SetEnabled(!bound);
            text.tooltip = bound
                ? "Linked — this names something that keeps '" + contractName
                    + "'. Change it from ◇, or unlink there to type freely."
                : "Names something that keeps '" + contractName
                    + "'. ◇ offers what your declared catalogs hold.";
            row.Add(text);

            var pick = new Button { text = "◇" };
            pick.style.width = 26f;
            pick.style.flexShrink = 0f;
            pick.tooltip = contract == null
                ? "No contract called '" + contractName + "' is reachable from here."
                : "Pick something that keeps '" + contractName + "'.";
            pick.clicked += () => ShowMenu(pick, container, owner, path, label);
            row.Add(pick);
        }

        private void ShowMenu(VisualElement anchor, VisualElement container, SerializedObject owner,
            string path, string label)
        {
            string contractName = Attribute()?.contractName ?? "";
            var offers = new List<StateTreeRegistryEntry>();
            ContractDef contract = Offers(owner, contractName, offers);

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"), false, () => Set(owner, path, container, label,
                ""));

            if (contract == null)
            {
                // TWO DIFFERENT SILENCES, and telling them apart saves the hour: a contract
                // nobody declared is a missing dependency, while a declared contract nobody keeps
                // is a catalog waiting for its first implementer.
                menu.AddDisabledItem(new GUIContent("no contract called '" + contractName
                    + "' in this asset's dependencies"));
            }
            else if (offers.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("nothing you declare claims '"
                    + contractName + "' yet"));
            }

            for (int i = 0; i < offers.Count; i++)
            {
                StateTreeRegistryEntry offer = offers[i];
                string chosen = offer.name;
                string entry = string.IsNullOrEmpty(offer.group) ? chosen : offer.group + "/" + chosen;
                menu.AddItem(new GUIContent(entry), chosen == CurrentValue(owner, path),
                    () => Set(owner, path, container, label, chosen));
            }

            if (Offered(offers, CurrentValue(owner, path)))
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Unlink — type this name freely"), false, () =>
                {
                    // Unlinking cannot mean "keep the name and unlock", because the name IS the
                    // link: the same text would re-link on the next repaint. It clears instead,
                    // which is the honest way to say "I am about to name something else".
                    Set(owner, path, container, label, "");
                });
            }
            menu.DropDown(anchor.worldBound);
        }

        private static ContractDef Offers(SerializedObject owner, string contractName,
            List<StateTreeRegistryEntry> into)
        {
            into.Clear();
            Object asset = owner != null ? owner.targetObject : null;
            ContractDef contract = StateTreeOffers.ContractNamed(contractName, asset);
            if (contract != null)
                StateTreeOffers.ImplementerRowsOf(contract, asset, into);
            return contract;
        }

        private StateTreeImplementsAttribute Attribute()
        {
            return fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo,
                    typeof(StateTreeImplementsAttribute)) as StateTreeImplementsAttribute
                : null;
        }

        private static bool Offered(List<StateTreeRegistryEntry> offers, string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i] != null && offers[i].name == value)
                    return true;
            }
            return false;
        }

        private static string CurrentValue(SerializedObject owner, string path)
        {
            owner.Update();
            SerializedProperty live = owner.FindProperty(path);
            return live != null ? live.stringValue : "";
        }

        private void Set(SerializedObject owner, string path, VisualElement container, string label,
            string value)
        {
            owner.Update();
            SerializedProperty target = owner.FindProperty(path);
            if (target == null)
                return;
            target.stringValue = value;
            owner.ApplyModifiedProperties();
            Build(container, owner, path, label);
        }
    }
}
