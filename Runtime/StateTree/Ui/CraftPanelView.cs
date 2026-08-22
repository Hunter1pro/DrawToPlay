using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE STATION'S PANEL (M26, after the first play-through) — what a bench offers, what it
    /// costs, what you are carrying, and a button.
    ///
    /// The milestone shipped without one on the argument that pressing the action button at a
    /// bench IS the interface and the announcement says what happened. That is true and it is
    /// not enough: a player who has never been told a shipyard exists walks past a white cube.
    /// Crafting that cannot be SEEN is crafting that does not exist, which is exactly the
    /// report that produced this file.
    ///
    /// THE STATION'S SCREEN, HT-shaped (M39): the bench that showed it holds it and tells it
    /// what to draw — <see cref="Show"/> with a <see cref="CraftOffer"/> whose numbers are
    /// already counted, <see cref="Announce"/> with what a craft came to — and its one button
    /// asks the bench to start the craft. It subscribes to nothing, resolves nothing and
    /// counts nothing.
    /// </summary>
    [AddComponentMenu("Draw To Play/UI/Craft Panel")]
    [RequireComponent(typeof(UIDocument))]
    [UiVerbContract("close")]
    public sealed class CraftPanelView : UiViewBehaviour
    {
        /// <summary>The bench this panel speaks for — handed over at spawn, never looked up.</summary>
        [InjectService] private CraftService m_Craft;

        private VisualElement m_Panel;
        private Label m_Title;
        private Label m_Result;
        private VisualElement m_Costs;
        private Button m_Make;

        private static readonly Color k_Met = new Color(0.62f, 0.88f, 0.55f);
        private static readonly Color k_Short = new Color(0.95f, 0.62f, 0.45f);

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            // Unpickable root, pickable panel — the joystick lesson, again.
            root.pickingMode = PickingMode.Ignore;

            m_Panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    bottom = 150f,
                    left = Length.Percent(50f),
                    marginLeft = -140f,
                    width = 280f,
                    paddingLeft = 12f, paddingRight = 12f,
                    paddingTop = 10f, paddingBottom = 12f,
                    backgroundColor = new Color(0.07f, 0.08f, 0.11f, 0.94f),
                    display = DisplayStyle.None
                }
            };
            Round(m_Panel, 10f);
            root.Add(m_Panel);

            m_Title = new Label("")
            {
                style =
                {
                    fontSize = 15f,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 6f
                }
            };
            m_Panel.Add(m_Title);

            m_Costs = new VisualElement();
            m_Panel.Add(m_Costs);

            m_Make = new Button(() => m_Craft?.StartCrafting()) { text = "CRAFT" };
            m_Make.style.height = 40f;
            m_Make.style.marginTop = 8f;
            m_Make.style.fontSize = 14f;
            m_Make.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Make.style.color = Color.white;
            Round(m_Make, 8f);
            m_Panel.Add(m_Make);

            m_Result = new Label("")
            {
                style =
                {
                    fontSize = 12f,
                    marginTop = 6f,
                    color = new Color(0.85f, 0.92f, 0.75f),
                    whiteSpace = WhiteSpace.Normal
                }
            };
            m_Panel.Add(m_Result);
        }

        public override bool Call(string verb, string argument)
        {
            switch (verb)
            {
                case "close": Close(); return true;
                default: return false;
            }
        }

        /// <summary>Draw an offer. Null closes — "there is no bench here" and "the bench has
        /// nothing" are the same thing to a panel.</summary>
        public void Show(CraftOffer offer)
        {
            if (m_Panel == null)
                return;
            if (offer == null)
            {
                Close();
                return;
            }

            m_Title.text = offer.stationName + " — " + (string.IsNullOrEmpty(offer.displayName)
                ? offer.recipeName : offer.displayName);

            m_Costs.Clear();
            for (int i = 0; i < offer.costs.Count; i++)
            {
                CraftCostLine cost = offer.costs[i];
                if (cost == null)
                    continue;
                var line = new Label(cost.itemName + "   " + cost.held + " / " + cost.need)
                {
                    style =
                    {
                        fontSize = 13f,
                        color = cost.met ? k_Met : k_Short,
                        unityFontStyleAndWeight = cost.met ? FontStyle.Normal : FontStyle.Bold
                    }
                };
                m_Costs.Add(line);
            }

            // The button's LOOK follows the offer's own answer; it is never disabled, because
            // a press that says why is worth more than a press that does nothing.
            m_Make.style.backgroundColor = offer.affordable
                ? new Color(0.24f, 0.42f, 0.28f, 0.95f)
                : new Color(0.20f, 0.21f, 0.26f, 0.95f);
            m_Make.text = offer.affordable ? "CRAFT" : "CRAFT  (" + offer.blocker + ")";

            m_Panel.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (m_Panel != null)
                m_Panel.style.display = DisplayStyle.None;
            if (m_Result != null)
                m_Result.text = "";
        }

        /// <summary>What the last craft came to, on the panel that asked for it. Bound rather
        /// than assigned, like the bag's announce line — the contract's own sentence.</summary>
        public void Announce(CraftResult result)
        {
            if (m_Result == null || result == null)
                return;
            m_Result.dataSource = result;
            m_Result.SetBinding("text", new DataBinding
            {
                dataSourcePath = new Unity.Properties.PropertyPath(nameof(CraftResult.line)),
                bindingMode = BindingMode.ToTarget
            });
            m_Result.style.color = result.made ? k_Met : k_Short;

            // AND IT GOES. A panel that keeps the last answer forever is a panel showing a
            // refusal you already fixed — the reading that caught this had "needs 3 wood
            // (carrying 0)" sitting above a live CRAFT button.
            m_Result.schedule.Execute(() =>
            {
                // The BINDING first: clearing only the text leaves the data binding to write
                // the same sentence straight back on the next update.
                m_Result.ClearBinding("text");
                m_Result.dataSource = null;
                m_Result.text = "";
            }).StartingIn(3200);
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
