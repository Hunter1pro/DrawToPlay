using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE BAG AS A DUMB SYSTEM (the UI wiring brief, variant B) — pixels in, requests out,
    /// and nothing else. The panel never finds a service, never subscribes, never decides:
    /// it draws exactly what <see cref="Redraw"/> hands it, and every press becomes ONE
    /// request key on the root blackboard (<see cref="UiViewBehaviour.Request"/>). What a
    /// press MEANS — the domain verb, the flash, the HUD pulse, the redraw — is a flow
    /// STATE in the session tree, readable in the dashboard as a task list. Swapping this
    /// skin for a radial one changes a prefab reference; the flows never notice.
    /// </summary>
    [AddComponentMenu("Draw To Play/UI/Inventory Widget")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryWidgetView : UiViewBehaviour
    {
        // ---- the request vocabulary: the whole surface between this skin and the flows --

        public const string ToggleKey = "ui.bag.toggle";

        /// <summary>Value = the item's registry name.</summary>
        public const string UseKey = "ui.bag.use";

        /// <summary>Value = the item's registry name.</summary>
        public const string WearKey = "ui.bag.wear";

        /// <summary>Value = the slot row's id.</summary>
        public const string TakeoffKey = "ui.bag.takeoff";

        /// <summary>Written by whoever observes an outside change (a pickup on the floor)
        /// so the refresh FLOW redraws an open bag.</summary>
        public const string RefreshKey = "ui.bag.refresh";

        [Tooltip("Edge length of one item cell in the grid.")]
        public float cellSize = 64f;

        private VisualElement m_Panel;
        private VisualElement m_Grid;
        private VisualElement m_Slots;
        private Button m_Toggle;
        private bool m_Open;

        private readonly Dictionary<string, VisualElement> m_Cells =
            new Dictionary<string, VisualElement>(System.StringComparer.Ordinal);

        private static readonly Color k_CellColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color k_FlashColor = new Color(0.55f, 0.9f, 0.55f, 0.45f);

        public bool isOpen => m_Open;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            // The document root is unpickable by design (the joystick lesson); everything
            // clickable sits on an explicit pickable layer above it.
            root.pickingMode = PickingMode.Ignore;

            m_Toggle = new Button(() => Request(ToggleKey)) { text = "▮ BAG" };
            m_Toggle.style.position = Position.Absolute;
            m_Toggle.style.top = 16f;
            m_Toggle.style.right = 16f;
            m_Toggle.style.width = 96f;
            m_Toggle.style.height = 44f;
            m_Toggle.style.fontSize = 14f;
            m_Toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Toggle.style.color = Color.white;
            m_Toggle.style.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 0.88f);
            Round(m_Toggle, 8f);
            root.Add(m_Toggle);

            m_Panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 72f, right = 16f,
                    width = cellSize * 4f + 28f,
                    paddingLeft = 10f, paddingRight = 10f,
                    paddingTop = 10f, paddingBottom = 10f,
                    backgroundColor = new Color(0.07f, 0.08f, 0.11f, 0.92f),
                    display = DisplayStyle.None
                }
            };
            Round(m_Panel, 10f);

            m_Slots = new VisualElement { style = { marginBottom = 6f } };
            m_Panel.Add(m_Slots);

            m_Grid = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap }
            };
            m_Panel.Add(m_Grid);
            root.Add(m_Panel);
        }

        // ---- the verbs: what a flow can tell this skin to do ---------------------------

        /// <summary>The generic verb surface (§4c) — what UiCallTask speaks.</summary>
        public override bool Call(string verb, string argument)
        {
            switch (verb)
            {
                case "toggle": ToggleOpen(); return true;
                case "open": Open(); return true;
                case "close": Close(); return true;
                case "flash": Flash(argument); return true;
                default: return false;
            }
        }

        public void Open()
        {
            m_Open = true;
            if (m_Panel != null)
                m_Panel.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            m_Open = false;
            if (m_Panel != null)
                m_Panel.style.display = DisplayStyle.None;
        }

        public void ToggleOpen()
        {
            if (m_Open)
                Close();
            else
                Open();
        }

        /// <summary>Draw exactly this — the flow's redraw task built it, this skin shows
        /// it. Called with empty lists it shows an empty bag; it never asks for more.</summary>
        public void Redraw(IReadOnlyList<ItemStack> stacks, IReadOnlyList<BagSlotLine> slots)
        {
            if (m_Grid == null)
                return;

            m_Slots.Clear();
            m_Grid.Clear();
            m_Cells.Clear();

            // Keep the handed lines: the cells below read them for their worn/wear split.
            m_LastSlots.Clear();
            for (int i = 0; slots != null && i < slots.Count; i++)
                m_LastSlots.Add(slots[i]);

            for (int i = 0; i < m_LastSlots.Count; i++)
                m_Slots.Add(SlotLine(m_LastSlots[i]));

            if (stacks == null || stacks.Count == 0)
            {
                m_Grid.Add(Note("empty"));
                return;
            }
            for (int i = 0; i < stacks.Count; i++)
                m_Grid.Add(Cell(stacks[i]));
        }

        /// <summary>A short accent on an item's cell — the flow's "that one just did
        /// something" beat. A name with no cell is a quiet no-op.</summary>
        public void Flash(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)
                || !m_Cells.TryGetValue(itemName, out VisualElement cell) || cell == null)
                return;
            cell.style.backgroundColor = k_FlashColor;
            cell.schedule.Execute(() => cell.style.backgroundColor = k_CellColor)
                .StartingIn(400);
        }

        // ---- drawing -------------------------------------------------------------------

        private VisualElement SlotLine(BagSlotLine slot)
        {
            bool worn = !string.IsNullOrEmpty(slot.wornItemName);
            var line = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 4f
                }
            };
            line.Add(new Label(slot.slotLabel + ":  "
                + (worn
                    ? (string.IsNullOrEmpty(slot.wornItemLabel)
                        ? slot.wornItemName : slot.wornItemLabel)
                    : "—"))
            {
                style =
                {
                    color = worn
                        ? new Color(0.75f, 0.9f, 1f)
                        : new Color(1f, 1f, 1f, 0.45f),
                    fontSize = 13f, flexGrow = 1f
                }
            });
            if (worn)
            {
                string slotId = slot.slotId;
                line.Add(Verb("take off", () => Request(TakeoffKey, slotId)));
            }
            return line;
        }

        private VisualElement Cell(ItemStack stack)
        {
            var cell = new VisualElement
            {
                style =
                {
                    width = cellSize, height = cellSize + 24f,
                    marginRight = 6f, marginBottom = 6f,
                    backgroundColor = k_CellColor,
                    alignItems = Align.Center
                }
            };
            Round(cell, 8f);
            m_Cells[stack.definition.name] = cell;

            var face = new VisualElement
            {
                style =
                {
                    width = cellSize, height = cellSize,
                    justifyContent = Justify.Center, alignItems = Align.Center
                },
                pickingMode = PickingMode.Ignore
            };
            if (stack.definition.icon != null)
            {
                face.Add(new Image
                {
                    sprite = stack.definition.icon,
                    scaleMode = ScaleMode.ScaleToFit,
                    style = { width = cellSize * 0.62f, height = cellSize * 0.62f },
                    pickingMode = PickingMode.Ignore
                });
            }
            else
            {
                var swatch = new VisualElement
                {
                    style =
                    {
                        width = cellSize * 0.4f, height = cellSize * 0.4f,
                        backgroundColor = stack.definition.tint
                    },
                    pickingMode = PickingMode.Ignore
                };
                Round(swatch, 6f);
                face.Add(swatch);
            }
            cell.Add(face);

            var name = new Label(Label(stack.definition))
            {
                style =
                {
                    position = Position.Absolute, top = 4f, left = 6f,
                    fontSize = 10f, color = new Color(1f, 1f, 1f, 0.55f)
                },
                pickingMode = PickingMode.Ignore
            };
            cell.Add(name);

            if (stack.count > 1)
            {
                cell.Add(new Label("x" + stack.count)
                {
                    style =
                    {
                        position = Position.Absolute, top = 4f, right = 6f,
                        fontSize = 13f, color = Color.white,
                        unityFontStyleAndWeight = FontStyle.Bold
                    },
                    pickingMode = PickingMode.Ignore
                });
            }

            // The verb the ROW declares becomes a REQUEST — what it does is the flow's say.
            string itemName = stack.definition.name;
            if (!string.IsNullOrEmpty(stack.definition.useEffect.entryName))
                cell.Add(Verb("use", () => Request(UseKey, itemName)));
            else if (!string.IsNullOrEmpty(stack.definition.slot.entryId))
            {
                string slotId = stack.definition.slot.entryId;
                cell.Add(WornIn(slotId, itemName)
                    ? Verb("worn ✓", () => Request(TakeoffKey, slotId))
                    : Verb("wear", () => Request(WearKey, itemName)));
            }
            return cell;
        }

        /// <summary>Whether the last-drawn slot lines say this item sits in this slot —
        /// the skin reads its own handed data, never the domain.</summary>
        private bool WornIn(string slotId, string itemName)
        {
            for (int i = 0; i < m_LastSlots.Count; i++)
            {
                if (m_LastSlots[i].slotId == slotId
                    && m_LastSlots[i].wornItemName == itemName)
                    return true;
            }
            return false;
        }

        private readonly List<BagSlotLine> m_LastSlots = new List<BagSlotLine>();

        private static Button Verb(string text, System.Action action)
        {
            var button = new Button(action) { text = text };
            button.style.height = 20f;
            button.style.fontSize = 11f;
            button.style.color = Color.white;
            button.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            button.style.marginLeft = 2f;
            button.style.marginRight = 2f;
            Round(button, 5f);
            return button;
        }

        private static string Label(ItemDef definition)
        {
            return string.IsNullOrEmpty(definition.displayName)
                ? definition.name
                : definition.displayName;
        }

        private static Label Note(string text)
        {
            return new Label(text)
            {
                style = { color = new Color(1f, 1f, 1f, 0.5f), fontSize = 13f }
            };
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
