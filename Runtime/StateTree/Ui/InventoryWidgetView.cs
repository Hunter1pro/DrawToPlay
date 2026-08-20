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
    [UiVerbContract("toggle")]
    [UiVerbContract("open")]
    [UiVerbContract("close")]
    [UiVerbContract("flash", "item name")]
    [UiVerbContract("announce", "ItemUseResult payload")]
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

            // The announce line: "used: <bound>" — the bound half is written by Unity's
            // runtime data binding against the routed contract object, never by hand.
            m_AnnounceLine = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 26f, right = 120f,
                    flexDirection = FlexDirection.Row,
                    display = DisplayStyle.None
                },
                pickingMode = PickingMode.Ignore
            };
            m_AnnounceLine.Add(new Label("used: ")
            {
                style = { fontSize = 12f, color = new Color(1f, 1f, 1f, 0.55f) },
                pickingMode = PickingMode.Ignore
            });
            m_Announce = new Label("")
            {
                style =
                {
                    fontSize = 12f,
                    color = new Color(0.55f, 0.9f, 0.55f),
                    unityFontStyleAndWeight = FontStyle.Bold
                },
                pickingMode = PickingMode.Ignore
            };
            m_AnnounceLine.Add(m_Announce);
            root.Add(m_AnnounceLine);
        }

        private VisualElement m_AnnounceLine;
        private Label m_Announce;

        /// <summary>§4e's binding demo, end to end: the flow routed a typed object here;
        /// the label's text is a DataBinding with a PropertyPath into that class —
        /// nameof-safe, refactor-following — shown for a beat and gone.</summary>
        private void Announce(object payload)
        {
            if (m_Announce == null || payload == null)
                return;
            m_Announce.dataSource = payload;
            m_Announce.SetBinding("text", new DataBinding
            {
                dataSourcePath = new Unity.Properties.PropertyPath(
                    nameof(ItemUseResult.itemName)),
                bindingMode = BindingMode.ToTarget
            });
            m_AnnounceLine.style.display = DisplayStyle.Flex;
            m_AnnounceLine.schedule
                .Execute(() => m_AnnounceLine.style.display = DisplayStyle.None)
                .StartingIn(1400);
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

        /// <summary>The payload flavor (§4e): "announce" takes the routed
        /// <see cref="ItemUseResult"/> whole and BINDS it — Unity's runtime data binding,
        /// a PropertyPath against the contract class, no label.text plumbing between.</summary>
        public override bool Call(string verb, string argument, object payload)
        {
            if (verb == "announce")
            {
                Announce(payload);
                return true;
            }
            return Call(verb, argument);
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
        /// <summary>
        /// THE BAG, UPDATED IN PLACE (M34) — cells live as long as the item is carried.
        ///
        /// This used to clear the grid and rebuild every cell and every button on every change:
        /// spending one ration destroyed and remade the whole panel. A cell keyed by its item
        /// binds its count instead, so a quantity change writes one string and nothing else
        /// moves — which also stops a press landing on an element that was replaced under the
        /// finger.
        ///
        /// What is still built and destroyed is what genuinely appeared or went: an item picked
        /// up, an item spent to zero.
        /// </summary>
        public void Redraw(IReadOnlyList<ItemStack> stacks, IReadOnlyList<BagSlotLine> slots)
        {
            if (m_Grid == null)
                return;

            // Keep the handed lines: the cells read them for their worn/wear split.
            m_LastSlots.Clear();
            for (int i = 0; slots != null && i < slots.Count; i++)
                m_LastSlots.Add(slots[i]);

            SyncSlots();
            SyncCells(stacks);
        }

        /// <summary>The equipment lines, one per declared slot, reused across changes.</summary>
        private void SyncSlots()
        {
            m_LiveSlots.Clear();
            for (int i = 0; i < m_LastSlots.Count; i++)
            {
                BagSlotLine line = m_LastSlots[i];
                m_LiveSlots.Add(line.slotId);
                if (!m_SlotRows.TryGetValue(line.slotId, out SlotRow row))
                {
                    row = BuildSlotRow(line);
                    m_SlotRows[line.slotId] = row;
                }
                if (row.root.parent != m_Slots)
                    m_Slots.Add(row.root);
                if (m_Slots.IndexOf(row.root) != i)
                    m_Slots.Insert(i, row.root);

                bool worn = !string.IsNullOrEmpty(line.wornItemName);
                row.model.line = line.slotLabel + ":  " + (worn
                    ? (string.IsNullOrEmpty(line.wornItemLabel)
                        ? line.wornItemName : line.wornItemLabel)
                    : "—");
                row.model.verb = worn ? "take off" : "";
                row.button.style.display = worn ? DisplayStyle.Flex : DisplayStyle.None;
                row.label.style.color = worn
                    ? new Color(0.75f, 0.9f, 1f)
                    : new Color(1f, 1f, 1f, 0.45f);
                row.slotName = line.slotName;
            }

            Prune(m_SlotRows, m_LiveSlots, row => row.root);
        }

        /// <summary>The carried items, one cell each, reused while the item is held.</summary>
        private void SyncCells(IReadOnlyList<ItemStack> stacks)
        {
            m_LiveCells.Clear();
            var index = 0;
            for (int i = 0; stacks != null && i < stacks.Count; i++)
            {
                ItemStack stack = stacks[i];
                if (stack.definition == null)
                    continue;
                string itemName = stack.definition.name;
                m_LiveCells.Add(itemName);

                if (!m_CellsByItem.TryGetValue(itemName, out BagCell cell))
                {
                    cell = BuildCell(stack.definition);
                    m_CellsByItem[itemName] = cell;
                    m_Cells[itemName] = cell.root;
                }
                if (cell.root.parent != m_Grid)
                    m_Grid.Add(cell.root);
                if (m_Grid.IndexOf(cell.root) != index)
                    m_Grid.Insert(index, cell.root);
                index++;

                cell.model.label = Label(stack.definition);
                cell.model.count = stack.count > 1 ? "x" + stack.count : "";
                cell.model.verb = VerbFor(stack.definition);
                cell.button.style.display = string.IsNullOrEmpty(cell.model.verb)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            }

            Prune(m_CellsByItem, m_LiveCells, cell => cell.root);
            foreach (string gone in m_Removed)
                m_Cells.Remove(gone);

            // "empty" is a state of the grid, not a cell: shown when nothing is carried.
            if (m_Empty == null)
            {
                m_Empty = Note("empty");
                m_Grid.Add(m_Empty);
            }
            m_Empty.style.display = m_LiveCells.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Grid.Add(m_Empty);   // keep it last
        }

        /// <summary>What this item's button says right now — the row decides, and the state of
        /// the slots decides between wearing and taking off.</summary>
        private string VerbFor(ItemDef definition)
        {
            if (!string.IsNullOrEmpty(definition.useEffect.entryName))
                return "use";
            if (string.IsNullOrEmpty(definition.slot.entryId))
                return "";
            return WornIn(definition.slot.entryId, definition.name) ? "worn ✓" : "wear";
        }

        /// <summary>One press, decided WHEN IT HAPPENS rather than when the cell was built —
        /// which is what lets a cell outlive a change in what it offers.</summary>
        private void PressCell(ItemDef definition)
        {
            if (definition == null)
                return;
            if (!string.IsNullOrEmpty(definition.useEffect.entryName))
            {
                Request(UseKey, definition.name);
                return;
            }
            if (string.IsNullOrEmpty(definition.slot.entryId))
                return;
            if (WornIn(definition.slot.entryId, definition.name))
                Request(TakeoffKey, definition.slot.entryName);
            else
                Request(WearKey, definition.name);
        }

        /// <summary>Drop what is no longer held, and remember what went so the flash lookup
        /// stays honest.</summary>
        private void Prune<T>(Dictionary<string, T> held, HashSet<string> live,
            System.Func<T, VisualElement> rootOf)
        {
            m_Removed.Clear();
            foreach (KeyValuePair<string, T> pair in held)
            {
                if (!live.Contains(pair.Key))
                    m_Removed.Add(pair.Key);
            }
            for (int i = 0; i < m_Removed.Count; i++)
            {
                if (held.TryGetValue(m_Removed[i], out T going))
                    rootOf(going)?.RemoveFromHierarchy();
                held.Remove(m_Removed[i]);
            }
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

        // ---- building, once per thing --------------------------------------------------

        private sealed class SlotRow
        {
            public VisualElement root;
            public Label label;
            public Button button;
            public BagSlotModel model;
            public string slotName;
        }

        private sealed class BagCell
        {
            public VisualElement root;
            public Button button;
            public BagCellModel model;
        }

        private SlotRow BuildSlotRow(BagSlotLine slot)
        {
            var row = new SlotRow { model = new BagSlotModel(), slotName = slot.slotName };
            row.root = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 4f
                }
            };
            row.label = new Label { style = { fontSize = 13f, flexGrow = 1f } };
            Bind(row.label, row.model, nameof(BagSlotModel.line));
            row.root.Add(row.label);

            // The typed request value is the slot ROW NAME (§4d): pickable, checkable. The
            // caption is bound, so the same button reads "take off" only while something is on.
            row.button = Verb("", () => Request(TakeoffKey, row.slotName));
            Bind(row.button, row.model, nameof(BagSlotModel.verb));
            row.root.Add(row.button);
            return row;
        }

        private BagCell BuildCell(ItemDef definition)
        {
            var cell = new BagCell { model = new BagCellModel() };
            cell.root = new VisualElement
            {
                style =
                {
                    width = cellSize, height = cellSize + 24f,
                    marginRight = 6f, marginBottom = 6f,
                    backgroundColor = k_CellColor,
                    alignItems = Align.Center
                }
            };
            Round(cell.root, 8f);

            var face = new VisualElement
            {
                style =
                {
                    width = cellSize, height = cellSize,
                    justifyContent = Justify.Center, alignItems = Align.Center
                },
                pickingMode = PickingMode.Ignore
            };
            if (definition.icon != null)
            {
                face.Add(new Image
                {
                    sprite = definition.icon,
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
                        backgroundColor = definition.tint
                    },
                    pickingMode = PickingMode.Ignore
                };
                Round(swatch, 6f);
                face.Add(swatch);
            }
            cell.root.Add(face);

            var name = new Label
            {
                style =
                {
                    position = Position.Absolute, top = 4f, left = 6f,
                    fontSize = 10f, color = new Color(1f, 1f, 1f, 0.55f)
                },
                pickingMode = PickingMode.Ignore
            };
            Bind(name, cell.model, nameof(BagCellModel.label));
            cell.root.Add(name);

            var count = new Label
            {
                style =
                {
                    position = Position.Absolute, top = 4f, right = 6f,
                    fontSize = 13f, color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold
                },
                pickingMode = PickingMode.Ignore
            };
            Bind(count, cell.model, nameof(BagCellModel.count));
            cell.root.Add(count);

            ItemDef row = definition;
            cell.button = Verb("", () => PressCell(row));
            Bind(cell.button, cell.model, nameof(BagCellModel.verb));
            cell.root.Add(cell.button);
            return cell;
        }

        /// <summary>One label, one property path — the whole of what a skin knows about a
        /// value. [CreateProperty] on the model is what makes it work.</summary>
        private static void Bind(VisualElement element, object model, string property)
        {
            element.dataSource = model;
            element.SetBinding("text", new DataBinding
            {
                dataSourcePath = new Unity.Properties.PropertyPath(property),
                bindingMode = BindingMode.ToTarget
            });
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

        private readonly Dictionary<string, SlotRow> m_SlotRows =
            new Dictionary<string, SlotRow>();

        private readonly Dictionary<string, BagCell> m_CellsByItem =
            new Dictionary<string, BagCell>();

        private readonly HashSet<string> m_LiveSlots = new HashSet<string>();
        private readonly HashSet<string> m_LiveCells = new HashSet<string>();
        private readonly List<string> m_Removed = new List<string>();
        private Label m_Empty;

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
