using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The "Add node" picker: a searchable, categorised popup over every concrete subclass of a
    /// base type, in the shape of Graph Toolkit's own "Add a graph node" window.
    ///
    /// WHY THIS REPLACED A DROPDOWN. A DropdownField is a flat alphabetical list of type names.
    /// That is fine for six tasks and unusable at sixty — and the node library is the thing that
    /// grows fastest in this toolset, because every new gameplay idea is a new task or condition.
    /// So the picker is the feature-integration workflow: write a task, tag it with
    /// <see cref="StateTreeCategoryAttribute"/>, and it is findable by name, by category and by
    /// description without anyone editing this file or a registry.
    ///
    /// WINDOW MODE. This is <c>ShowPopup()</c> + an explicit <c>OnLostFocus</c> close, which is
    /// exactly what Unity's own <c>ItemLibraryWindow</c> (the GT picker) does — verified against
    /// the 6000.5.3f1 GraphToolkit module, not assumed. The alternatives lose something we need:
    /// <c>ShowAsDropDown</c> owns its own size (no resize grip, no remembered size),
    /// <c>ShowAuxWindow</c>/<c>ShowUtility</c> add OS chrome and a taskbar entry for a control
    /// that should feel like a menu. A chromeless popup means the title bar, the border and the
    /// resize grip are drawn here; that is the cost of the mode, and it is deliberate.
    ///
    /// KEYBOARD IS THE PRIMARY PATH. Open, type, Enter — the search field takes focus on show,
    /// arrows move the selection through the *visible* rows (a row inside a collapsed category is
    /// not reachable, which is what "visible" has to mean for the highlight to be honest), and
    /// Enter commits. The mouse path (double-click) is the alternative, not the reverse.
    /// </summary>
    internal sealed class StateTreeNodePicker : EditorWindow
    {
        /// <summary>Per-kind favourites, type full names, comma joined. The kind suffix
        /// ("Tasks"/"Conditions") is derived from the base type, so a third node family would get
        /// its own list for free.</summary>
        private const string k_FavoritesPrefix = "PowerOfFire.DrawToPlay.PickerFavorites.";

        private const string k_ExpandedPrefix = "PowerOfFire.DrawToPlay.PickerExpanded.";
        private const string k_SizeXKey = "PowerOfFire.DrawToPlay.PickerSize.X";
        private const string k_SizeYKey = "PowerOfFire.DrawToPlay.PickerSize.Y";

        private const string k_TasksKind = "Tasks";
        private const string k_ConditionsKind = "Conditions";
        private const string k_Uncategorized = "Uncategorized";
        private const string k_FavoritesPath = "Favorites";
        private const string k_FavoritesLabel = "★ Favorites";
        private const string k_Placeholder = "Start typing to search...";
        private const string k_FooterHint = "'Double-click' or hit 'Enter' to add a node.";
        private const string k_StarOn = "★";
        private const string k_StarOff = "☆";

        /// <summary>A category path deeper than this is authored nonsense; the guard exists so a
        /// typo in an attribute cannot build an unbounded foldout chain.</summary>
        private const int k_MaxCategoryDepth = 8;

        private const float k_RowHeight = 19f;
        private const float k_StarWidth = 16f;

        private static readonly Vector2 k_MinSize = new Vector2(280f, 240f);
        private static readonly Vector2 k_MaxSize = new Vector2(2400f, 2000f);
        private static readonly Vector2 k_DefaultSize = new Vector2(340f, 420f);

        private static readonly Color k_RowSelected = new Color(0.24f, 0.48f, 0.90f, 0.45f);
        private static readonly Color k_RowClear = new Color(0f, 0f, 0f, 0f);

        private static StateTreeNodePicker s_Open;

        private readonly List<Entry> m_Entries = new List<Entry>();
        private readonly List<Row> m_Rows = new List<Row>();

        private Type m_BaseType;
        private Action<Type> m_OnPicked;
        private string m_Title = "Add Node";
        private string m_Kind = k_TasksKind;
        private HashSet<string> m_Favorites = new HashSet<string>();

        private EditorWindow m_Host;
        private TextField m_Search;
        private Label m_PlaceholderLabel;
        private ScrollView m_List;
        private string m_Query = string.Empty;
        private string m_LastQuery = string.Empty;
        private int m_Selected = -1;

        private bool m_Closing;
        private bool m_Resizing;
        private Vector2 m_ResizeStartPointer;
        private Vector2 m_ResizeStartSize;

        // --- entry point ------------------------------------------------------------------

        /// <summary>Open the picker under <paramref name="activatorScreenRect"/> (screen space —
        /// use <see cref="ScreenRectOf"/> to derive it from the button that opened it).
        /// <paramref name="onPicked"/> is invoked with the chosen concrete type AFTER the popup
        /// has closed, so the callback is free to rebuild the UI that owns the activator.</summary>
        internal static StateTreeNodePicker Show(Rect activatorScreenRect, Type baseType,
            Action<Type> onPicked, string title)
        {
            if (baseType == null || onPicked == null)
                return null;

            if (s_Open != null)
                s_Open.CloseSelf();

            var window = CreateInstance<StateTreeNodePicker>();
            window.m_BaseType = baseType;
            window.m_OnPicked = onPicked;
            window.m_Title = string.IsNullOrEmpty(title) ? "Add Node" : title;
            window.m_Kind = KindOf(baseType);
            window.m_Favorites = LoadFavorites(window.m_Kind);
            window.m_Host = focusedWindow;
            window.titleContent = new GUIContent(window.m_Title);
            window.minSize = k_MinSize;
            window.maxSize = k_MaxSize;
            window.position = PlaceBelow(activatorScreenRect, LoadSize());

            s_Open = window;
            window.ShowPopup();
            window.Focus();
            return window;
        }

        /// <summary>Screen-space rect of a UI Toolkit element, for anchoring the popup to the
        /// control that opened it. Mirrors ItemLibraryWindow.GetRectStartingInWindow: panel
        /// coordinates clamped into the host window, then offset by the host's screen position.
        /// The host is the focused window, which is by definition the one whose button was just
        /// clicked (or whose button just took Enter).</summary>
        internal static Rect ScreenRectOf(VisualElement anchor)
        {
            var bounds = anchor != null ? anchor.worldBound : new Rect(0f, 0f, 1f, 1f);
            if (float.IsNaN(bounds.x) || float.IsNaN(bounds.y) || float.IsNaN(bounds.width)
                || float.IsNaN(bounds.height))
                bounds = new Rect(0f, 0f, 1f, 1f);

            var host = focusedWindow != null ? focusedWindow : mouseOverWindow;
            if (host == null)
                return bounds;

            var hostRect = host.position;
            bounds.x = hostRect.x + Mathf.Clamp(bounds.x, 0f, Mathf.Max(0f, hostRect.width));
            bounds.y = hostRect.y + Mathf.Clamp(bounds.y, 0f, Mathf.Max(0f, hostRect.height));
            return bounds;
        }

        /// <summary>The label a type gets in the picker and in the inspector: the type name minus
        /// its Task/Condition suffix, nicified. "LineOfSightCondition" reads as "Line Of Sight",
        /// which is what the author called it before they had to name a class.</summary>
        internal static string DisplayNameOf(Type type)
        {
            if (type == null)
                return "(none)";

            var name = type.Name;
            if (name.Length > 4 && name.EndsWith("Task", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 4);
            else if (name.Length > 9 && name.EndsWith("Condition", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 9);

            return ObjectNames.NicifyVariableName(name);
        }

        // --- window lifecycle -------------------------------------------------------------

        private void CreateGUI()
        {
            CollectEntries();
            BuildChrome();
            RebuildList();
        }

        /// <summary>Closing on focus loss is the popup's only dismissal contract, so it has to
        /// survive the two cases that legitimately steal focus mid-gesture: our own Close (the
        /// pick path) and a resize drag that wandered outside the window. Deferring one frame is
        /// ItemLibraryWindow's own idiom — closing inside the focus callback destroys the panel
        /// that is still dispatching.</summary>
        private void OnLostFocus()
        {
            if (m_Closing || m_Resizing)
                return;

            rootVisualElement.schedule.Execute(CloseSelf).ExecuteLater(0L);
        }

        private void OnDestroy()
        {
            m_Closing = true;
            if (s_Open == this)
                s_Open = null;

            // Hand the keyboard back to the window that opened us, or Enter-to-add would end with
            // focus nowhere and the next keystroke lost.
            if (m_Host != null)
                m_Host.Focus();
        }

        private void CloseSelf()
        {
            if (m_Closing)
                return;

            m_Closing = true;
            Close();
        }

        // --- model ------------------------------------------------------------------------

        private sealed class Entry
        {
            internal Type type;
            internal string displayName;
            internal string category;
            internal string description;
            internal string persistKey;
            internal string lowerName;
            internal string compactName;
            internal string lowerCategory;
            internal string lowerDescription;
        }

        private sealed class Row
        {
            internal VisualElement element;
            internal Label star;
            internal Entry entry;

            /// <summary>Category path this row sits in, or null in search results (which are
            /// flat and therefore always visible). Visibility is computed from this plus the
            /// persisted expansion flags rather than from resolved layout: a row must be
            /// keyboard-unreachable the instant its group collapses, not one layout pass later.
            /// </summary>
            internal string groupPath;
        }

        private void CollectEntries()
        {
            m_Entries.Clear();
            if (m_BaseType == null)
                return;

            var byName = new Dictionary<string, int>();
            foreach (var type in TypeCache.GetTypesDerivedFrom(m_BaseType))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                var attribute = (StateTreeCategoryAttribute)Attribute.GetCustomAttribute(
                    type, typeof(StateTreeCategoryAttribute), false);

                var category = attribute != null && !string.IsNullOrWhiteSpace(attribute.path)
                    ? attribute.path.Trim().Trim('/')
                    : k_Uncategorized;
                if (category.Length == 0)
                    category = k_Uncategorized;

                var description = attribute != null && attribute.description != null
                    ? attribute.description
                    : string.Empty;

                var displayName = DisplayNameOf(type);
                byName.TryGetValue(displayName, out var count);
                byName[displayName] = count + 1;

                m_Entries.Add(new Entry
                {
                    type = type,
                    displayName = displayName,
                    category = category,
                    description = description,
                    persistKey = type.FullName
                });
            }

            for (var i = 0; i < m_Entries.Count; ++i)
            {
                var entry = m_Entries[i];

                // Two types with the same nicified name are two types the author cannot tell
                // apart, which is exactly when the namespace stops being noise.
                if (byName.TryGetValue(entry.displayName, out var count) && count > 1)
                    entry.displayName = $"{entry.displayName} ({entry.type.Namespace})";

                entry.lowerName = entry.displayName.ToLowerInvariant();
                entry.compactName = entry.lowerName.Replace(" ", string.Empty);
                entry.lowerCategory = entry.category.ToLowerInvariant();
                entry.lowerDescription = entry.description.ToLowerInvariant();
            }

            m_Entries.Sort(CompareEntries);
        }

        private static int CompareEntries(Entry a, Entry b)
        {
            var byCategory = string.CompareOrdinal(a.category, b.category);
            return byCategory != 0
                ? byCategory
                : string.CompareOrdinal(a.displayName, b.displayName);
        }

        private static string KindOf(Type baseType)
        {
            if (typeof(StateTreeTaskAsset).IsAssignableFrom(baseType))
                return k_TasksKind;
            if (typeof(StateTreeConditionAsset).IsAssignableFrom(baseType))
                return k_ConditionsKind;
            return baseType.Name;
        }

        // --- chrome -----------------------------------------------------------------------

        private void BuildChrome()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1f;
            root.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.78f, 0.78f, 0.78f, 1f);

            var border = EditorGUIUtility.isProSkin
                ? new Color(0.13f, 0.13f, 0.13f, 1f)
                : new Color(0.51f, 0.51f, 0.51f, 1f);
            root.style.borderTopWidth = 1f;
            root.style.borderBottomWidth = 1f;
            root.style.borderLeftWidth = 1f;
            root.style.borderRightWidth = 1f;
            root.style.borderTopColor = border;
            root.style.borderBottomColor = border;
            root.style.borderLeftColor = border;
            root.style.borderRightColor = border;

            // A trickle-down handler: the search field would otherwise eat the arrow keys before
            // the list ever sees them, and the search field is where focus lives by design.
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            root.Add(BuildTitleBar());
            root.Add(BuildSearchBar());
            root.Add(BuildList());
            root.Add(BuildFooter());

            // Focus after the first layout pass: focusing an element the panel has not measured
            // yet is silently dropped, which is the classic "my popup opened unfocused" bug.
            root.RegisterCallback<GeometryChangedEvent>(OnFirstLayout);
        }

        private VisualElement BuildTitleBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 6f;
            bar.style.paddingRight = 6f;
            bar.style.paddingTop = 4f;
            bar.style.paddingBottom = 4f;
            bar.style.flexShrink = 0f;
            bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);

            var title = new Label(m_Title);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            bar.Add(title);

            var close = new Button(CloseSelf) { text = "✕" };
            close.tooltip = "Close without adding (Esc).";
            close.style.width = 20f;
            close.style.marginRight = 0f;
            bar.Add(close);

            return bar;
        }

        private VisualElement BuildSearchBar()
        {
            var box = new VisualElement();
            box.style.paddingLeft = 4f;
            box.style.paddingRight = 4f;
            box.style.paddingTop = 3f;
            box.style.paddingBottom = 3f;
            box.style.flexShrink = 0f;

            m_Search = new TextField();
            m_Search.style.marginLeft = 0f;
            m_Search.style.marginRight = 0f;
            m_Search.RegisterValueChangedCallback(evt =>
            {
                m_Query = evt.newValue ?? string.Empty;
                UpdatePlaceholder();
                RebuildList();
            });
            box.Add(m_Search);

            // Hand-drawn placeholder rather than the text field's own: the built-in one is styled
            // by the inspector theme, and this popup is not an inspector.
            m_PlaceholderLabel = new Label(k_Placeholder);
            m_PlaceholderLabel.pickingMode = PickingMode.Ignore;
            m_PlaceholderLabel.style.position = Position.Absolute;
            m_PlaceholderLabel.style.left = 8f;
            m_PlaceholderLabel.style.top = 3f;
            m_PlaceholderLabel.style.bottom = 3f;
            m_PlaceholderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_PlaceholderLabel.style.opacity = 0.45f;
            box.Add(m_PlaceholderLabel);

            return box;
        }

        private VisualElement BuildList()
        {
            m_List = new ScrollView(ScrollViewMode.Vertical);
            m_List.style.flexGrow = 1f;
            m_List.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            return m_List;
        }

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.flexShrink = 0f;
            footer.style.paddingLeft = 6f;
            footer.style.paddingTop = 2f;
            footer.style.paddingBottom = 2f;
            footer.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);

            var hint = new Label(k_FooterHint);
            hint.style.opacity = 0.6f;
            hint.style.fontSize = 10f;
            hint.style.flexGrow = 1f;
            hint.style.overflow = Overflow.Hidden;
            hint.style.textOverflow = TextOverflow.Ellipsis;
            hint.style.whiteSpace = WhiteSpace.NoWrap;
            footer.Add(hint);

            footer.Add(BuildResizeGrip());
            return footer;
        }

        /// <summary>A chromeless popup has no OS resize handle, so it gets one here: the drag
        /// writes <c>EditorWindow.position</c> directly (the same thing ItemLibraryWindow does)
        /// and the final size is remembered for every future picker.</summary>
        private VisualElement BuildResizeGrip()
        {
            var grip = new Label("◢");
            grip.tooltip = "Drag to resize. The size is remembered.";
            grip.style.width = 14f;
            grip.style.flexShrink = 0f;
            grip.style.opacity = 0.5f;
            grip.style.fontSize = 10f;
            grip.style.unityTextAlign = TextAnchor.MiddleRight;

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                m_Resizing = true;
                m_ResizeStartPointer = evt.position;
                m_ResizeStartSize = position.size;
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!grip.HasPointerCapture(evt.pointerId))
                    return;

                // Panel coordinates stay anchored to the window's top-left, which does not move
                // while only the size changes — so the pointer delta is the size delta.
                Vector2 pointer = evt.position;
                var size = m_ResizeStartSize + (pointer - m_ResizeStartPointer);
                size.x = Mathf.Clamp(size.x, k_MinSize.x, k_MaxSize.x);
                size.y = Mathf.Clamp(size.y, k_MinSize.y, k_MaxSize.y);
                position = new Rect(position.position, size);
                evt.StopPropagation();
            });

            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!grip.HasPointerCapture(evt.pointerId))
                    return;

                grip.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            grip.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!m_Resizing)
                    return;

                m_Resizing = false;
                SaveSize(position.size);
            });

            return grip;
        }

        private void OnFirstLayout(GeometryChangedEvent evt)
        {
            rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnFirstLayout);
            FocusSearch();

            // One retry: the popup can still be acquiring OS focus when the first layout lands,
            // and a focus request made in that window is silently dropped. Guarded on an empty
            // field so the retry can never steal a keystroke that arrived first.
            rootVisualElement.schedule.Execute(() =>
            {
                if (m_Search != null && string.IsNullOrEmpty(m_Search.value))
                    FocusSearch();
            }).ExecuteLater(1L);
        }

        private void FocusSearch()
        {
            if (m_Search == null)
                return;

            // TextField delegates focus to its inner input element, so focusing the field is
            // enough; SelectAll makes a re-open overwrite the previous query on first keystroke.
            m_Search.Focus();
            m_Search.SelectAll();
        }

        private void UpdatePlaceholder()
        {
            if (m_PlaceholderLabel == null)
                return;

            m_PlaceholderLabel.style.display = string.IsNullOrEmpty(m_Query)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        // --- list -------------------------------------------------------------------------

        private void RebuildList()
        {
            if (m_List == null)
                return;

            var query = (m_Query ?? string.Empty).Trim();

            // Selection survives a rebuild that the author did not ask for (starring a row,
            // collapsing a category) but never one caused by typing: after a keystroke the
            // highlight must be on the top hit, because that is what Enter commits.
            var keepSelection = string.Equals(query, m_LastQuery, StringComparison.Ordinal);
            m_LastQuery = query;

            var previous = keepSelection && m_Selected >= 0 && m_Selected < m_Rows.Count
                ? m_Rows[m_Selected].entry.type
                : null;

            m_List.Clear();
            m_Rows.Clear();
            m_Selected = -1;

            if (m_Entries.Count == 0)
            {
                m_List.Add(Hint($"No {m_BaseType?.Name} types exist in this project yet."));
                return;
            }

            if (query.Length == 0)
                BuildBrowse();
            else
                BuildResults(query);

            if (m_Rows.Count == 0)
            {
                m_List.Add(Hint($"Nothing matches \"{query}\"."));
                return;
            }

            var restored = IndexOfType(previous);
            SetSelected(restored >= 0 ? restored : FirstVisibleIndex());
        }

        /// <summary>Empty search: favourites first (only when there are any — an empty starred
        /// section is a permanent reminder of a feature you are not using), then the category
        /// tree built from the attribute paths.</summary>
        private void BuildBrowse()
        {
            var favorites = new List<Entry>();
            for (var i = 0; i < m_Entries.Count; ++i)
            {
                if (m_Favorites.Contains(m_Entries[i].persistKey))
                    favorites.Add(m_Entries[i]);
            }

            if (favorites.Count > 0)
            {
                favorites.Sort((a, b) => string.CompareOrdinal(a.displayName, b.displayName));
                var group = MakeGroup(k_FavoritesLabel, k_FavoritesPath, m_List.contentContainer);
                for (var i = 0; i < favorites.Count; ++i)
                    group.Add(MakeRow(favorites[i], favorites[i].description, k_FavoritesPath));
            }

            var groups = new Dictionary<string, VisualElement>();
            for (var i = 0; i < m_Entries.Count; ++i)
            {
                var entry = m_Entries[i];
                var parent = ResolveGroup(entry.category, groups);
                parent.Add(MakeRow(entry, entry.description, entry.category));
            }
        }

        /// <summary>Typing collapses the tree into one ranked list. The ranking is the obvious
        /// one — what you typed at the start of the name beats what you typed in the middle of it
        /// — with category and description as the last two tiers so "perception" or "cooldown"
        /// still find something when the type name does not contain the word.</summary>
        private void BuildResults(string query)
        {
            var lower = query.ToLowerInvariant();
            var hits = new List<Entry>();
            var scores = new Dictionary<Entry, int>();

            for (var i = 0; i < m_Entries.Count; ++i)
            {
                var score = Score(m_Entries[i], lower);
                if (score < 0)
                    continue;

                scores[m_Entries[i]] = score;
                hits.Add(m_Entries[i]);
            }

            hits.Sort((a, b) =>
            {
                var byScore = scores[a].CompareTo(scores[b]);
                if (byScore != 0)
                    return byScore;

                var aFavorite = m_Favorites.Contains(a.persistKey);
                var bFavorite = m_Favorites.Contains(b.persistKey);
                if (aFavorite != bFavorite)
                    return aFavorite ? -1 : 1;

                return string.CompareOrdinal(a.displayName, b.displayName);
            });

            for (var i = 0; i < hits.Count; ++i)
                m_List.Add(MakeRow(hits[i], hits[i].category, null));
        }

        private static int Score(Entry entry, string lower)
        {
            if (entry.lowerName.StartsWith(lower, StringComparison.Ordinal))
                return 0;
            if (StartsAtWordBoundary(entry.lowerName, lower))
                return 1;

            // "lineofsight" has to find "Line Of Sight": the nicified name is the one on screen,
            // but the name people type is the class name they wrote.
            if (entry.compactName.StartsWith(lower, StringComparison.Ordinal))
                return 1;
            if (entry.lowerName.IndexOf(lower, StringComparison.Ordinal) >= 0)
                return 2;
            if (entry.compactName.IndexOf(lower, StringComparison.Ordinal) >= 0)
                return 2;
            if (entry.lowerCategory.IndexOf(lower, StringComparison.Ordinal) >= 0)
                return 3;
            if (entry.lowerDescription.Length > 0
                && entry.lowerDescription.IndexOf(lower, StringComparison.Ordinal) >= 0)
                return 4;

            return -1;
        }

        private static bool StartsAtWordBoundary(string haystack, string needle)
        {
            var index = 0;
            while (index <= haystack.Length - needle.Length)
            {
                index = haystack.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0)
                    return false;
                if (index == 0 || haystack[index - 1] == ' ')
                    return true;
                ++index;
            }

            return false;
        }

        private VisualElement ResolveGroup(string category, Dictionary<string, VisualElement> cache)
        {
            var segments = category.Split('/');
            var parent = m_List.contentContainer;
            var path = string.Empty;

            for (var i = 0; i < segments.Length && i < k_MaxCategoryDepth; ++i)
            {
                var segment = segments[i].Trim();
                if (segment.Length == 0)
                    continue;

                path = path.Length == 0 ? segment : path + "/" + segment;
                if (!cache.TryGetValue(path, out var group))
                {
                    group = MakeGroup(segment, path, parent);
                    cache[path] = group;
                }

                parent = group;
            }

            return parent;
        }

        private VisualElement MakeGroup(string label, string path, VisualElement parent)
        {
            var foldout = new Foldout { text = label, value = IsExpanded(path) };
            foldout.style.flexShrink = 0f;

            var text = foldout.Q<Toggle>()?.Q<Label>();
            if (text != null)
                text.style.unityFontStyleAndWeight = FontStyle.Bold;

            foldout.RegisterValueChangedCallback(evt =>
            {
                // A nested foldout's change bubbles through its ancestors; without this guard a
                // child collapse would persist itself onto every category above it.
                if (evt.target != foldout)
                    return;

                SetExpanded(path, evt.newValue);
                EnsureSelectionVisible();
            });

            parent.Add(foldout);
            return foldout.contentContainer;
        }

        private VisualElement MakeRow(Entry entry, string secondary, string groupPath)
        {
            var element = new VisualElement();
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.minHeight = k_RowHeight;
            element.style.flexShrink = 0f;
            element.style.paddingLeft = 2f;
            element.style.paddingRight = 2f;
            element.tooltip = TooltipOf(entry);

            var star = new Label(k_StarOff);
            star.style.width = k_StarWidth;
            star.style.flexShrink = 0f;
            star.style.unityTextAlign = TextAnchor.MiddleCenter;
            element.Add(star);

            var name = new Label(entry.displayName);
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            element.Add(name);

            if (!string.IsNullOrEmpty(secondary))
            {
                var dim = new Label(secondary);
                dim.style.opacity = 0.5f;
                dim.style.fontSize = 10f;
                dim.style.flexShrink = 1f;
                dim.style.marginLeft = 6f;
                dim.style.maxWidth = new StyleLength(new Length(50f, LengthUnit.Percent));
                dim.style.unityTextAlign = TextAnchor.MiddleRight;
                dim.style.overflow = Overflow.Hidden;
                dim.style.textOverflow = TextOverflow.Ellipsis;
                dim.style.whiteSpace = WhiteSpace.NoWrap;
                element.Add(dim);
            }

            var row = new Row
            {
                element = element,
                star = star,
                entry = entry,
                groupPath = groupPath
            };
            var index = m_Rows.Count;
            m_Rows.Add(row);

            star.RegisterCallback<PointerDownEvent>(evt =>
            {
                ToggleFavorite(row);
                evt.StopPropagation();
            });
            star.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            element.RegisterCallback<PointerDownEvent>(_ => SetSelected(index));
            element.RegisterCallback<ClickEvent>(evt =>
            {
                SetSelected(index);
                if (evt.clickCount >= 2)
                    PickSelected();
            });
            element.RegisterCallback<MouseEnterEvent>(_ => PaintStar(row, true));
            element.RegisterCallback<MouseLeaveEvent>(_ => PaintStar(row, false));

            PaintStar(row, false);
            return element;
        }

        private static string TooltipOf(Entry entry)
        {
            return string.IsNullOrEmpty(entry.description)
                ? entry.type.FullName
                : entry.description + "\n\n" + entry.type.FullName;
        }

        private static Label Hint(string text)
        {
            var label = new Label(text);
            label.style.opacity = 0.7f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 8f;
            label.style.marginLeft = 8f;
            label.style.marginRight = 8f;
            return label;
        }

        // --- selection --------------------------------------------------------------------

        private void SetSelected(int index)
        {
            if (m_Selected >= 0 && m_Selected < m_Rows.Count)
                m_Rows[m_Selected].element.style.backgroundColor = k_RowClear;

            m_Selected = index;
            if (m_Selected < 0 || m_Selected >= m_Rows.Count)
                return;

            var row = m_Rows[m_Selected];
            row.element.style.backgroundColor = k_RowSelected;
            m_List.ScrollTo(row.element);
        }

        private void MoveSelection(int delta)
        {
            var visible = VisibleIndices();
            if (visible.Count == 0)
                return;

            var current = visible.IndexOf(m_Selected);
            var next = current < 0
                ? (delta > 0 ? 0 : visible.Count - 1)
                : Mathf.Clamp(current + delta, 0, visible.Count - 1);
            SetSelected(visible[next]);
        }

        private void EnsureSelectionVisible()
        {
            var visible = VisibleIndices();
            if (visible.Count == 0)
            {
                SetSelected(-1);
                return;
            }

            if (m_Selected >= 0 && visible.Contains(m_Selected))
                return;

            SetSelected(visible[0]);
        }

        private List<int> VisibleIndices()
        {
            var visible = new List<int>(m_Rows.Count);
            for (var i = 0; i < m_Rows.Count; ++i)
            {
                if (IsRowVisible(m_Rows[i]))
                    visible.Add(i);
            }

            return visible;
        }

        private bool IsRowVisible(Row row)
        {
            var path = row.groupPath;
            if (string.IsNullOrEmpty(path))
                return true;

            var index = 0;
            var depth = 0;
            while (depth++ <= k_MaxCategoryDepth)
            {
                var slash = path.IndexOf('/', index);
                var prefix = slash < 0 ? path : path.Substring(0, slash);
                if (!IsExpanded(prefix))
                    return false;
                if (slash < 0)
                    return true;

                index = slash + 1;
            }

            return true;
        }

        private int FirstVisibleIndex()
        {
            for (var i = 0; i < m_Rows.Count; ++i)
            {
                if (IsRowVisible(m_Rows[i]))
                    return i;
            }

            return -1;
        }

        private int IndexOfType(Type type)
        {
            if (type == null)
                return -1;

            for (var i = 0; i < m_Rows.Count; ++i)
            {
                if (m_Rows[i].entry.type == type && IsRowVisible(m_Rows[i]))
                    return i;
            }

            return -1;
        }

        // --- commands ---------------------------------------------------------------------

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveSelection(1);
                    break;
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    break;
                case KeyCode.PageDown:
                    MoveSelection(10);
                    break;
                case KeyCode.PageUp:
                    MoveSelection(-10);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    PickSelected();
                    break;
                case KeyCode.Escape:
                    CloseSelf();
                    break;
                default:
                    return;
            }

            evt.StopPropagation();
        }

        private void PickSelected()
        {
            if (m_Selected < 0 || m_Selected >= m_Rows.Count)
                return;

            var type = m_Rows[m_Selected].entry.type;

            // Close first, then call back: the callback rebuilds the pane that owns the button we
            // were anchored to, and nothing of ours may be touched after Close destroys it.
            var callback = m_OnPicked;
            m_OnPicked = null;
            CloseSelf();
            callback?.Invoke(type);
        }

        private void ToggleFavorite(Row row)
        {
            if (m_Favorites.Contains(row.entry.persistKey))
                m_Favorites.Remove(row.entry.persistKey);
            else
                m_Favorites.Add(row.entry.persistKey);

            SaveFavorites();
            PaintStar(row, true);

            // The favourites section changes shape, so the list is rebuilt — deferred, because
            // rebuilding destroys the element whose pointer event is still being dispatched.
            m_List.schedule.Execute(RebuildList).ExecuteLater(0L);
        }

        private void PaintStar(Row row, bool hovered)
        {
            var favorite = m_Favorites.Contains(row.entry.persistKey);
            row.star.text = favorite ? k_StarOn : k_StarOff;

            // Hidden rather than removed so every row's name starts at the same x; unpickable
            // while hidden so an invisible control can never be clicked by accident.
            row.star.style.opacity = favorite ? 1f : (hovered ? 0.55f : 0f);
            row.star.pickingMode = favorite || hovered ? PickingMode.Position : PickingMode.Ignore;
            row.star.tooltip = favorite ? "Remove from Favorites." : "Add to Favorites.";
        }

        // --- preferences ------------------------------------------------------------------

        private static HashSet<string> LoadFavorites(string kind)
        {
            var favorites = new HashSet<string>();
            var raw = EditorPrefs.GetString(k_FavoritesPrefix + kind, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return favorites;

            var parts = raw.Split(',');
            for (var i = 0; i < parts.Length; ++i)
            {
                var part = parts[i].Trim();
                if (part.Length > 0)
                    favorites.Add(part);
            }

            return favorites;
        }

        private void SaveFavorites()
        {
            var ordered = new List<string>(m_Favorites);
            ordered.Sort(StringComparer.Ordinal);
            EditorPrefs.SetString(k_FavoritesPrefix + m_Kind, string.Join(",", ordered));
        }

        private bool IsExpanded(string path) => EditorPrefs.GetBool(ExpandedKey(path), true);

        private void SetExpanded(string path, bool value)
            => EditorPrefs.SetBool(ExpandedKey(path), value);

        private string ExpandedKey(string path)
            => k_ExpandedPrefix + m_Kind + "." + path.Replace('/', '.');

        private static Vector2 LoadSize()
        {
            var size = new Vector2(
                EditorPrefs.GetFloat(k_SizeXKey, k_DefaultSize.x),
                EditorPrefs.GetFloat(k_SizeYKey, k_DefaultSize.y));
            size.x = Mathf.Clamp(size.x, k_MinSize.x, k_MaxSize.x);
            size.y = Mathf.Clamp(size.y, k_MinSize.y, k_MaxSize.y);
            return size;
        }

        private static void SaveSize(Vector2 size)
        {
            EditorPrefs.SetFloat(k_SizeXKey, Mathf.Clamp(size.x, k_MinSize.x, k_MaxSize.x));
            EditorPrefs.SetFloat(k_SizeYKey, Mathf.Clamp(size.y, k_MinSize.y, k_MaxSize.y));
        }

        /// <summary>Below the control that opened it, flipped above when there is no room, and
        /// clamped into the main editor window so a button near the screen edge cannot push the
        /// popup off-screen.</summary>
        private static Rect PlaceBelow(Rect activator, Vector2 size)
        {
            var origin = new Vector2(activator.x, activator.yMax + 2f);
            var main = EditorGUIUtility.GetMainWindowPosition();

            if (main.width > 1f && main.height > 1f)
            {
                if (origin.y + size.y > main.yMax && activator.y - size.y > main.yMin)
                    origin.y = activator.y - size.y - 2f;

                var maxX = Mathf.Max(main.xMin, main.xMax - size.x);
                var maxY = Mathf.Max(main.yMin, main.yMax - size.y);
                origin.x = Mathf.Clamp(origin.x, main.xMin, maxX);
                origin.y = Mathf.Clamp(origin.y, main.yMin, maxY);
            }

            return new Rect(origin, size);
        }
    }
}
