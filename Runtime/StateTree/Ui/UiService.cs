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
    public sealed class UiService : StateTreeService
    {
        /// <summary>Built by its scope's installer (M33) — the screen ledger every other
        /// subsystem asks for, so it is installed first.</summary>
        public UiService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
            if (definition == null)
                Debug.LogError("[Ui] built with no ServiceDef — no row can resolve.");
            else if (catalog == null)
                Debug.LogError("[Ui] the ServiceDef's registry is not a UiRegistry.", definition);
        }


        /// <summary>The catalog, through the def — null when missing or of another kind.</summary>
        public UiRegistry catalog =>
            definition != null ? definition.registry as UiRegistry : null;

        private readonly Dictionary<string, GameObject> m_Open =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private bool m_Validated;

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

        /// <summary>The live view of a shown row, or null — how flow TASKS reach the systems
        /// they drive. The hub's whole job: hold the references, forward the reach.</summary>
        /// <summary>A shown view that carries a <typeparamref name="T"/>, whatever row showed
        /// it — what a service born AFTER the session's HUD went up asks for once, at its
        /// start, to hold the piece of screen it tells (the quest line and its widget).</summary>
        public T Shown<T>() where T : Component
        {
            foreach (KeyValuePair<string, GameObject> pair in m_Open)
            {
                if (pair.Value == null)
                    continue;
                T view = pair.Value.GetComponentInChildren<T>(true);
                if (view != null)
                    return view;
            }
            return null;
        }

        public GameObject ShownView(string uiName)
        {
            return !string.IsNullOrEmpty(uiName)
                && m_Open.TryGetValue(uiName, out GameObject view) && view != null
                ? view
                : null;
        }

        /// <summary>
        /// Put a row on screen. Already shown = the SAME view, re-bound with the new
        /// arguments (a re-entered state re-asserts, it does not duplicate). A Screen hides
        /// its sibling screens first — exclusivity is the kind's rule, not every caller's
        /// chore. Returns the view, or null for a row with no prefab.
        /// </summary>
        public GameObject Show(UiDef row, List<GraphTaskParameterOverride> arguments = null)
        {
            return Show(row, arguments, null);
        }

        /// <summary>
        /// Show ON BEHALF OF another scope (M43.11): a subsystem whose def declares
        /// <c>spawns</c> shows its screen through the root's UiService, but the screen is
        /// THAT subsystem's — its presses must land on the scope that serves them, and its
        /// life is that scope's life. A level def's card that stamped the root here wrote its
        /// requests onto a board nobody read: the button "did nothing".
        /// </summary>
        public GameObject Show(UiDef row, List<GraphTaskParameterOverride> arguments,
            StateTreeContextHost onBehalfOf)
        {
            StateTreeContextHost owner = onBehalfOf != null ? onBehalfOf : scope;
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
                BindArguments(held, row, arguments, owner);
                return held;
            }

            if (row.prefab == null)
            {
                Debug.LogError("[Ui] row '" + row.name + "' has no prefab — nothing to "
                    + "show.");
                return null;
            }

            // PARENTED TO THE SCOPE: a screen belongs to the scope that shows it, so unloading
                // a level takes its screens with it — which is what the component's own transform
                // used to mean, said explicitly.
                GameObject view = UnityEngine.Object.Instantiate(row.prefab, owner.transform);
            view.name = row.prefab.name;
            var document = view.GetComponentInChildren<UIDocument>(true);
            if (document != null)
                document.sortingOrder = row.sortingOrder;
            view.SetActive(true);
            BindArguments(view, row, arguments, owner);
            m_Open[row.name] = view;
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
                    UnityEngine.Object.Destroy(view);
                else
                    UnityEngine.Object.DestroyImmediate(view);
            }
        }

        /// <summary>Row defaults with the show-site's enabled overrides applied (by id — the
        /// M7h wire), handed to every <see cref="UiViewBehaviour"/> on the view.
        ///
        /// SPAWN-TIME IS BIND-TIME (the wiring law): the views' [InjectService] fields are
        /// filled here, before Bind — a view is HANDED its dependencies by the thing that
        /// spawned it, never left to poll for them. Shows come from tree states, which run
        /// when services are valid, so the injection can be loud. Re-binding an already
        /// shown view repeats it harmlessly (filled fields are left alone).</summary>
        private static void BindArguments(GameObject view, UiDef row,
            List<GraphTaskParameterOverride> arguments, StateTreeContextHost scope)
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
            {
                StateTreeServiceInjector.Inject(views[i], views[i].gameObject);
                // WHO SHOWED IT, before it can be pressed: a skin's one output edge lands on
                // this scope, so it is told rather than left to guess from its parents.
                views[i].ShownBy(scope);
                views[i].Bind(effective);
            }
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
                        + "draws on top (and takes the press) is undefined.");
                }
                else
                {
                    seen.Add(row.sortingOrder, row.name);
                }
            }
        }
    }
}
