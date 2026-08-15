using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE SCREEN'S LEDGER (the UI pass) — the catalog plus what is currently SHOWN, and
    /// nothing else: no flow. When a piece appears and disappears is a state tree's business
    /// (<see cref="ShowUiTask"/> shows on enter and hides on exit — a popup IS a state); this
    /// service owns the rules every showing shares: a Screen row hides its sibling screens,
    /// Popups stack, Widgets mind their own business, and every spawned view gets its panel
    /// order asserted FROM THE ROW — the load-bearing number, applied from data instead of
    /// remembered in builders.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Ui Service")]
    public sealed class UiService : StateTreeServiceBehaviour
    {
        [Tooltip("The declaration this service runs: scope and the UI registry.")]
        public ServiceDef definition;

        /// <summary>The catalog, through the def — null when missing or of another kind.</summary>
        public UiRegistry catalog =>
            definition != null ? definition.registry as UiRegistry : null;

        /// <summary>(row, spawned view) — after the view exists and its arguments bound.</summary>
        public event Action<UiDef, GameObject> shown;

        /// <summary>The row was hidden and its view destroyed.</summary>
        public event Action<UiDef> hidden;

        private readonly Dictionary<string, GameObject> m_Open =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private bool m_Validated;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (definition == null)
                Debug.LogError("[Ui] no ServiceDef assigned — no row can resolve.", this);
            else if (catalog == null)
                Debug.LogError("[Ui] the ServiceDef's registry is not a UiRegistry.", this);
        }

        public UiDef Find(string uiName)
        {
            UiRegistry registry = catalog;
            return registry != null && !string.IsNullOrEmpty(uiName)
                ? registry.FindByName(uiName) as UiDef
                : null;
        }

        public bool IsShown(string uiName)
        {
            return !string.IsNullOrEmpty(uiName)
                && m_Open.TryGetValue(uiName, out GameObject view) && view != null;
        }

        /// <summary>
        /// Put a row on screen. Already shown = the SAME view, re-bound with the new
        /// arguments (a re-entered state re-asserts, it does not duplicate). A Screen hides
        /// its sibling screens first — exclusivity is the kind's rule, not every caller's
        /// chore. Returns the view, or null for a row with no prefab.
        /// </summary>
        public GameObject Show(UiDef row, List<GraphTaskParameterOverride> arguments = null)
        {
            if (row == null)
                return null;
            ValidateRows();

            if (row.kind == UiKind.Screen)
            {
                UiRegistry registry = catalog;
                for (int i = 0; registry != null && i < registry.entries.Count; i++)
                {
                    UiDef other = registry.entries[i];
                    if (other != null && other != row && other.kind == UiKind.Screen
                        && IsShown(other.name))
                        Hide(other);
                }
            }

            if (m_Open.TryGetValue(row.name, out GameObject held) && held != null)
            {
                BindArguments(held, row, arguments);
                return held;
            }

            if (row.prefab == null)
            {
                Debug.LogError("[Ui] row '" + row.name + "' has no prefab — nothing to "
                    + "show.", this);
                return null;
            }

            GameObject view = Instantiate(row.prefab, transform);
            view.name = row.prefab.name;
            var document = view.GetComponentInChildren<UIDocument>(true);
            if (document != null)
                document.sortingOrder = row.sortingOrder;
            view.SetActive(true);
            BindArguments(view, row, arguments);
            m_Open[row.name] = view;
            shown?.Invoke(row, view);
            return view;
        }

        public void Hide(UiDef row)
        {
            if (row != null)
                Hide(row.name);
        }

        public void Hide(string uiName)
        {
            if (string.IsNullOrEmpty(uiName) || !m_Open.TryGetValue(uiName, out GameObject view))
                return;
            m_Open.Remove(uiName);
            if (view != null)
            {
                // The edit/play split every spawner in this toolset uses: tests and tooling
                // own their objects' lifetimes the same way they own everything they make.
                if (Application.isPlaying)
                    Destroy(view);
                else
                    DestroyImmediate(view);
            }
            hidden?.Invoke(Find(uiName));
        }

        /// <summary>Row defaults with the show-site's enabled overrides applied (by id — the
        /// M7h wire), handed to every <see cref="UiViewBehaviour"/> on the view.</summary>
        private static void BindArguments(GameObject view, UiDef row,
            List<GraphTaskParameterOverride> arguments)
        {
            var effective = new List<GraphTaskParameter>();
            for (int i = 0; row.parameters != null && i < row.parameters.Count; i++)
            {
                GraphTaskParameter declared = row.parameters[i];
                if (declared == null)
                    continue;
                var value = new GraphTaskParameter
                {
                    name = declared.name,
                    kind = declared.kind,
                    floatValue = declared.floatValue,
                    stringValue = declared.stringValue,
                    id = declared.id
                };
                for (int j = 0; arguments != null && j < arguments.Count; j++)
                {
                    GraphTaskParameterOverride over = arguments[j];
                    if (over == null || !over.enabled || !over.Matches(declared))
                        continue;
                    value.floatValue = over.floatValue;
                    value.stringValue = over.stringValue;
                    break;
                }
                effective.Add(value);
            }

            UiViewBehaviour[] views = view.GetComponentsInChildren<UiViewBehaviour>(true);
            for (int i = 0; i < views.Length; i++)
                views[i].Bind(effective);
        }

        /// <summary>The panel-order law, enforced as data: two rows sharing an order is the
        /// press-eating bug waiting to happen, said once per session.</summary>
        public void ValidateRows()
        {
            if (m_Validated)
                return;
            m_Validated = true;
            UiRegistry registry = catalog;
            if (registry == null)
                return;
            var seen = new Dictionary<float, string>();
            for (int i = 0; i < registry.entries.Count; i++)
            {
                UiDef row = registry.entries[i];
                if (row == null)
                    continue;
                if (seen.TryGetValue(row.sortingOrder, out var holder))
                {
                    Debug.LogWarning("[Ui] rows '" + holder + "' and '" + row.name
                        + "' share sorting order " + row.sortingOrder + " — which of them "
                        + "draws on top (and takes the press) is undefined.", this);
                }
                else
                {
                    seen.Add(row.sortingOrder, row.name);
                }
            }
        }
    }
}
