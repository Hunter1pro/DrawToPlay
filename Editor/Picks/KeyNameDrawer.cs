using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>A key, picked — the drawer for <see cref="KeyNameAttribute"/> on a string that names a
    /// blackboard key. A name nothing declares is shown as an error; a new key is declared, not typed.</summary>
    [CustomPropertyDrawer(typeof(KeyNameAttribute))]
    internal sealed class KeyNameDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new Label(property.displayName + ": [KeyName] needs a string");
            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath, property.displayName);
            return container;
        }

        private static void Build(VisualElement container, SerializedObject owner, string path, string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            StateTreeAsset tree = StateTreeKeyPick.TreeOf(owner.targetObject);
            List<StateTreeKeyOffer> offers = StateTreeKeyOffers.For(owner.targetObject, tree);
            string current = property.stringValue;
            bool known = StateTreeKeyOffers.TryFind(offers, current, out StateTreeKeyOffer offer);
            container.Add(new StateTreePickField(label, current, !known && !string.IsNullOrEmpty(current),
                StateTreeKeyPick.Tooltip(known, offer),
                at => StateTreeKeyPick.Menu(at, offers, tree, StateTreeKeyKind.String, current, true,
                    picked => Set(container, owner, path, label, picked.name))));
        }

        private static void Set(VisualElement container, SerializedObject owner, string path, string label, string value)
        {
            owner.Update();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            property.stringValue = value ?? "";
            owner.ApplyModifiedProperties();
            Build(container, owner, path, label);
        }
    }

    /// <summary>The key menu every key picker shares: offers, (none), and "Declare a new key…".</summary>
    internal static class StateTreeKeyPick
    {
        public static StateTreeAsset TreeOf(Object owner)
        {
            if (owner is StateTreeAsset tree)
                return tree;
            string path = owner != null ? AssetDatabase.GetAssetPath(owner) : "";
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path) as StateTreeAsset;
        }

        public static string Tooltip(bool known, StateTreeKeyOffer offer)
        {
            if (!known)
                return "A blackboard key. This name is declared nowhere this asset can see — pick a declared "
                    + "key, or declare it (▾ → Declare).";
            return offer.name + (offer.source == "declared" ? " — declared" : " — " + offer.source)
                + (string.IsNullOrEmpty(offer.description) ? "" : ": " + offer.description);
        }

        public static void Menu(Rect at, List<StateTreeKeyOffer> offers, StateTreeAsset declareOn,
            StateTreeKeyKind kind, string current, bool allowNone, System.Action<StateTreeKeyOffer> picked,
            StateTreeKeyKind? fits = null, bool presence = false, StateTreeAsset usedIn = null)
        {
            StateTreePickMenu menu = StateTreePickItems.Keys(offers, usedIn != null ? usedIn : declareOn, fits, presence);
            if (!allowNone)
                menu.none = null;
            if (declareOn != null)
            {
                bool known = StateTreeKeyOffers.TryFind(offers, current, out _);
                if (!string.IsNullOrEmpty(current) && !known)
                    menu.Command("Declare '" + current + "' on " + declareOn.name, () => Declared(declareOn, current, kind, picked));
                menu.Command("Declare a new key on " + declareOn.name + "…",
                    () => StateTreeNamePrompt.Show(at, "A new key on " + declareOn.name, "",
                        name => Declared(declareOn, name, kind, picked)));
            }
            menu.Show(at, current, item => picked(item.payload is StateTreeKeyOffer offer
                ? offer
                : new StateTreeKeyOffer("", "", "", "")));
        }

        private static void Declared(StateTreeAsset tree, string name, StateTreeKeyKind kind,
            System.Action<StateTreeKeyOffer> picked)
        {
            StateTreeKeyDeclaration declaration = StateTreeKeyOffers.Declare(tree, name, kind, "Declare Key");
            if (declaration != null)
                picked(new StateTreeKeyOffer(declaration.name, declaration.id, "declared", declaration.description));
        }
    }
}
