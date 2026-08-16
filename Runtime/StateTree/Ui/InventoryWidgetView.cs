using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE BAG AS ITS OWN PANEL (M25) — a UI ROW's view like the HUD and the dialog, not a
    /// corner of the HUD anymore. A toggle button opens a panel showing everything carried,
    /// and the panel is where the item VERBS live: a consumable row gets a USE button (spend
    /// one, apply its effect), an equipment row gets WEAR/TAKE OFF (slot swap semantics are
    /// the service's). The view never decides what an item does — it just reads the row's
    /// declared behaviour and offers the matching button.
    ///
    /// Redraws on the service's <c>changed</c>/<c>equipmentChanged</c> events rather than
    /// polling; the player scope is resolved per redraw because the player is a spawned
    /// citizen that is a different object after a level change.
    ///
    /// WIRING (the law): the service arrives by INJECTION — filled by UiService at spawn,
    /// re-injected at the point of use if this view was placed by hand — never by an
    /// Update loop that polls until something stops being null.
    /// </summary>
    [AddComponentMenu("Draw To Play/UI/Inventory Widget")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryWidgetView : UiViewBehaviour
    {
        [Tooltip("Edge length of one item cell in the grid.")]
        public float cellSize = 64f;

        private VisualElement m_Panel;
        private VisualElement m_Grid;
        private VisualElement m_Slots;
        private Button m_Toggle;
        private bool m_Open;

        [InjectService] private InventoryService m_Inventory;

        /// <summary>The service, injected at the point of use when spawn-time injection has
        /// not happened (a hand-placed widget) — the OutpostNpc form, with a graceful null.</summary>
        private InventoryService Inventory
        {
            get
            {
                if (m_Inventory == null)
                    StateTreeServiceInjector.Inject(this, gameObject);
                return m_Inventory;
            }
        }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            // The document root is unpickable by design (the joystick lesson); everything
            // clickable sits on an explicit pickable layer above it.
            root.pickingMode = PickingMode.Ignore;

            m_Toggle = new Button(Toggle) { text = "▮ BAG" };
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

        private void OnDisable()
        {
            Unwire();
        }

        /// <summary>Called by UiService after injection — the subscribe moment. Re-shows
        /// re-Bind, so the idiom is unsubscribe-then-subscribe rather than a flag.</summary>
        public override void Bind(System.Collections.Generic.IReadOnlyList<GraphTaskParameter> arguments)
        {
            if (Inventory == null)
                return;
            Unwire();
            m_Inventory.changed += OnInventoryChanged;
            m_Inventory.equipmentChanged += OnInventoryChanged;
            if (m_Open)
                Redraw();
        }

        private void Unwire()
        {
            if (m_Inventory == null)
                return;
            m_Inventory.changed -= OnInventoryChanged;
            m_Inventory.equipmentChanged -= OnInventoryChanged;
        }

        private void OnInventoryChanged()
        {
            if (m_Open)
                Redraw();
        }

        private void Toggle()
        {
            m_Open = !m_Open;
            m_Panel.style.display = m_Open ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_Open)
                Redraw();
        }

        private void Redraw()
        {
            if (m_Grid == null || Inventory == null)
                return;

            m_Slots.Clear();
            m_Grid.Clear();

            StateTreeContextHost player = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Player);
            if (player == null || player.Context == null)
            {
                m_Grid.Add(Note("no player"));
                return;
            }

            BuildSlots();

            IReadOnlyList<ItemStack> stacks = m_Inventory.Stacks(player.Context);
            if (stacks.Count == 0)
            {
                m_Grid.Add(Note("empty"));
                return;
            }
            for (int i = 0; i < stacks.Count; i++)
                m_Grid.Add(Cell(stacks[i]));
        }

        /// <summary>One line per slot ROW — the slot catalog is whatever slot registry the
        /// item registry depends on, so a new slot is a new row there and a new line here.</summary>
        private void BuildSlots()
        {
            EquipmentSlotRegistry slots = SlotCatalog();
            if (slots == null)
                return;
            foreach (EquipmentSlotDef slot in slots.entries)
            {
                if (slot == null)
                    continue;
                string wornName = m_Inventory.EquippedIn(slot.id);
                ItemDef worn = string.IsNullOrEmpty(wornName) ? null : m_Inventory.Row(wornName);
                string slotLabel = string.IsNullOrEmpty(slot.displayName)
                    ? slot.name : slot.displayName;

                var line = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        marginBottom = 4f
                    }
                };
                line.Add(new Label(slotLabel + ":  "
                    + (worn != null ? Label(worn) : wornName != "" ? wornName : "—"))
                {
                    style =
                    {
                        color = worn != null
                            ? new Color(0.75f, 0.9f, 1f)
                            : new Color(1f, 1f, 1f, 0.45f),
                        fontSize = 13f, flexGrow = 1f
                    }
                });
                if (!string.IsNullOrEmpty(wornName))
                {
                    string slotId = slot.id;
                    line.Add(Verb("take off", () => m_Inventory.Unequip(slotId)));
                }
                m_Slots.Add(line);
            }
        }

        private EquipmentSlotRegistry SlotCatalog()
        {
            if (m_Inventory == null || m_Inventory.registry == null)
                return null;
            var reachable = new List<StateTreeRegistryAsset>();
            m_Inventory.registry.CollectWithDependencies(reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i] is EquipmentSlotRegistry slots)
                    return slots;
            }
            return null;
        }

        private VisualElement Cell(ItemStack stack)
        {
            var cell = new VisualElement
            {
                style =
                {
                    width = cellSize, height = cellSize + 24f,
                    marginRight = 6f, marginBottom = 6f,
                    backgroundColor = new Color(1f, 1f, 1f, 0.06f),
                    alignItems = Align.Center
                }
            };
            Round(cell, 8f);

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

            // The verb the ROW declares, if any. Captured name because the stack list is
            // rebuilt under the button's feet on every change.
            string itemName = stack.definition.name;
            if (!string.IsNullOrEmpty(stack.definition.useEffect.entryName))
                cell.Add(Verb("use", () => m_Inventory.Use(itemName)));
            else if (!string.IsNullOrEmpty(stack.definition.slot.entryId))
            {
                cell.Add(m_Inventory.IsEquipped(itemName)
                    ? Verb("worn ✓", () => m_Inventory.Unequip(stack.definition.slot.entryId))
                    : Verb("wear", () => m_Inventory.Equip(itemName)));
            }
            return cell;
        }

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
