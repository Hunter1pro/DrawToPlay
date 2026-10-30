using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The Level Manifest's side column: one tile per kind the level's kind registry offers — its
    /// prefab's preview (or its initial on the kind's colour) and its name. Drag a tile into the Scene
    /// view or onto the list to place one; double-click it to place one at the view's centre (a single click
    /// places nothing: tapping tiles made duplicates).
    /// </summary>
    internal sealed class LevelKindPalette : VisualElement
    {
        public const string TileClass = "dtp-manifest-tile";

        /// <summary>The DragAndDrop generic-data key a dragged tile carries its kind name under.</summary>
        public const string DragKey = "PowerOfFire.DrawToPlay.LevelKind";

        /// <summary>Above this many kinds the column gets a search field.</summary>
        private const int k_SearchFrom = 7;

        private readonly Action<string> m_Clicked;
        private readonly VisualElement m_Tiles = new VisualElement();
        private readonly ToolbarSearchField m_Search = new ToolbarSearchField();
        private LevelObjectKindRegistry m_Shown;

        public LevelKindPalette(Action<string> clicked)
        {
            m_Clicked = clicked;
            style.paddingLeft = 4f;
            style.paddingRight = 4f;
            style.paddingTop = 6f;
            Add(StateTreeInspectorLook.Section("Kinds"));
            m_Search.style.width = StyleKeyword.Auto;
            m_Search.style.marginLeft = 0f;
            m_Search.style.marginRight = 0f;
            m_Search.tooltip = "Filter the kinds by name.";
            m_Search.RegisterValueChangedCallback(changed => Filter(changed.newValue));
            Add(m_Search);
            Add(m_Tiles);
        }

        /// <summary>The kinds a tile stands for, in the registry's order — nothing is rebuilt when the
        /// same registry is shown again.</summary>
        public void Show(LevelObjectKindRegistry kinds)
        {
            if (kinds == m_Shown && m_Tiles.childCount > 0)
                return;
            m_Shown = kinds;
            m_Tiles.Clear();

            var count = 0;
            if (kinds != null)
            {
                foreach (LevelObjectKindDef kind in kinds.entries)
                {
                    if (kind == null || string.IsNullOrEmpty(kind.name))
                        continue;
                    m_Tiles.Add(Tile(kind, LevelManifestEdits.KindColour(kinds, kind.name)));
                    count++;
                }
            }
            if (count == 0)
                m_Tiles.Add(StateTreeInspectorLook.Note(
                    "No kinds: this level's catalog names no LevelObjectKindRegistry."));
            m_Search.style.display = count >= k_SearchFrom ? DisplayStyle.Flex : DisplayStyle.None;
            Filter(m_Search.value);
        }

        /// <summary>Drops what is shown, so the next <see cref="Show"/> rebuilds — the registry's rows
        /// may have changed under the same asset.</summary>
        public void Forget()
        {
            m_Shown = null;
            m_Tiles.Clear();
        }

        private void Filter(string filter)
        {
            foreach (VisualElement tile in m_Tiles.Children())
            {
                if (!(tile.userData is string kind))
                    continue;
                bool shown = string.IsNullOrWhiteSpace(filter)
                    || kind.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
                tile.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private VisualElement Tile(LevelObjectKindDef kind, Color colour)
        {
            VisualElement tile = StateTreeInspectorLook.Card(StateTreeInspectorLook.CardBackground, colour);
            tile.AddToClassList(TileClass);
            tile.userData = kind.name;
            tile.style.flexDirection = FlexDirection.Row;
            tile.style.alignItems = Align.Center;
            tile.style.paddingTop = 3f;
            tile.style.paddingBottom = 3f;
            tile.tooltip = (string.IsNullOrEmpty(kind.description) ? kind.name : kind.description)
                + "\n\nDrag into the Scene view to place one there, or double-click to place one at the view's centre.";

            tile.Add(Icon(kind, kind.name, colour, 28f));
            var label = new Label(kind.name);
            label.style.marginLeft = 6f;
            label.style.flexShrink = 1f;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            tile.Add(label);

            string name = kind.name;
            Vector2 pressedAt = default;
            var pressed = false;
            var clicks = 0;
            tile.RegisterCallback<PointerDownEvent>(down =>
            {
                if (down.button != 0)
                    return;
                pressed = true;
                clicks = down.clickCount;
                pressedAt = down.position;
                tile.CapturePointer(down.pointerId);
            });
            tile.RegisterCallback<PointerMoveEvent>(move =>
            {
                if (!pressed || !tile.HasPointerCapture(move.pointerId)
                    || ((Vector2)move.position - pressedAt).sqrMagnitude < 36f)
                    return;
                pressed = false;
                tile.ReleasePointer(move.pointerId);
                StartDrag(name);
            });
            tile.RegisterCallback<PointerUpEvent>(up =>
            {
                if (!tile.HasPointerCapture(up.pointerId))
                    return;
                tile.ReleasePointer(up.pointerId);
                if (!pressed)
                    return;
                pressed = false;
                if (Places(clicks))
                    m_Clicked?.Invoke(name);
            });
            tile.RegisterCallback<PointerCaptureOutEvent>(_ => pressed = false);
            return tile;
        }

        /// <summary>A click places a row only when it is a double-click.</summary>
        internal static bool Places(int clickCount) => clickCount >= 2;

        private static void StartDrag(string kind)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.paths = Array.Empty<string>();
            DragAndDrop.SetGenericData(DragKey, kind);
            DragAndDrop.StartDrag(kind);
        }

        /// <summary>The kind a palette tile is being dragged with, or null.</summary>
        public static string DraggedKind()
        {
            return DragAndDrop.GetGenericData(DragKey) as string;
        }

        /// <summary>
        /// A kind's picture: its prefab's asset preview once Unity has rendered it, over its initial on
        /// the kind's colour (which is all a kind without a prefab, or with nothing renderable, shows).
        /// </summary>
        public static VisualElement Icon(LevelObjectKindDef kind, string name, Color colour, float size)
        {
            var badge = new VisualElement();
            badge.style.width = size;
            badge.style.height = size;
            badge.style.flexShrink = 0f;
            badge.style.backgroundColor = new Color(colour.r, colour.g, colour.b, 0.35f);
            float radius = size * 0.2f;
            badge.style.borderTopLeftRadius = radius;
            badge.style.borderTopRightRadius = radius;
            badge.style.borderBottomLeftRadius = radius;
            badge.style.borderBottomRightRadius = radius;
            badge.style.justifyContent = Justify.Center;
            badge.style.alignItems = Align.Center;

            var initial = new Label(string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant());
            initial.style.unityFontStyleAndWeight = FontStyle.Bold;
            initial.style.fontSize = Mathf.RoundToInt(size * 0.5f);
            initial.style.color = colour;
            initial.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.Add(initial);

            GameObject prefab = kind != null ? kind.prefab : null;
            if (prefab == null)
                return badge;

            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute;
            image.style.left = 0f;
            image.style.top = 0f;
            image.style.right = 0f;
            image.style.bottom = 0f;
            badge.Add(image);

            // The preview renders asynchronously: asked again a few times, then the initial stays.
            var tries = 0;
            bool Fill()
            {
                Texture2D preview = AssetPreview.GetAssetPreview(prefab);
                if (preview == null)
                    return ++tries > 40;
                image.image = preview;
                initial.style.display = DisplayStyle.None;
                return true;
            }
            bool done = Fill();
            if (!done)
                image.schedule.Execute(() => done = Fill()).Every(150).Until(() => done);
            return badge;
        }

        /// <summary>The tiles' kind names in order — what the column offers.</summary>
        internal List<string> Offered()
        {
            var names = new List<string>();
            foreach (VisualElement tile in m_Tiles.Children())
            {
                if (tile.userData is string kind)
                    names.Add(kind);
            }
            return names;
        }
    }
}
