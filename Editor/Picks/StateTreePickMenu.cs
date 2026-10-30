using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>One thing a picker offers: what is written, what is shown, a one-line meaning, where it
    /// sits in the catalogue, whether this tree already uses it, and whether it fits the field.</summary>
    public sealed class StateTreePickItem
    {
        public StateTreePickItem(string value, string label = null, string meaning = "", string group = "")
        {
            this.value = value ?? "";
            this.label = string.IsNullOrEmpty(label) ? this.value : label;
            this.meaning = meaning ?? "";
            this.group = group ?? "";
        }

        /// <summary>What a pick writes.</summary>
        public string value;

        public string label;

        /// <summary>One line: what it is for.</summary>
        public string meaning;

        /// <summary>Where it sits in the catalogue, '/'-separated.</summary>
        public string group;

        /// <summary>Used somewhere in this tree already: offered first.</summary>
        public bool inTree;

        /// <summary>Narrowed out by the field (another kind of key, another level's placement): offered
        /// last, under "Other".</summary>
        public bool fits = true;

        /// <summary>Whatever the caller needs back (a row, a key offer).</summary>
        public object payload;
    }

    /// <summary>A "+ New …" at the top of a picker: a name is asked, <see cref="make"/> creates the row (or
    /// key) in its one target and hands back the item to pick; <see cref="reveal"/> is selected after, so
    /// the new row can be configured. No <see cref="make"/>: the target makes its own rows (a registry
    /// with a creator panel) and is only revealed.</summary>
    public sealed class StateTreePickCreate
    {
        public string label;
        public string prompt;
        public Func<string, StateTreePickItem> make;
        public UnityEngine.Object reveal;
    }

    /// <summary>
    /// THE ONE PICKER every reference field opens: a searchable dropdown (type to filter across every
    /// group). The "+ New …" items first (one per target the field can resolve from), then what this tree
    /// already uses, then the catalogue grouped by meaning, then what the field's narrowing set aside,
    /// then the commands (Declare…). Built only when opened — nothing here runs per repaint.
    /// </summary>
    public sealed class StateTreePickMenu
    {
        public const string InTreeHeader = "In this tree";
        public const string NoneLabel = "(none)";

        public string title = "Pick";

        /// <summary>The empty pick's label, or null when the field may not be empty.</summary>
        public string none = NoneLabel;

        /// <summary>The group the narrowed-out items go under.</summary>
        public string other = "Other";

        /// <summary>Said, disabled, when there is nothing to pick.</summary>
        public string empty = "Nothing to pick here";

        public readonly List<StateTreePickItem> items = new List<StateTreePickItem>();

        public readonly List<KeyValuePair<string, Action>> commands = new List<KeyValuePair<string, Action>>();

        public readonly List<StateTreePickCreate> creates = new List<StateTreePickCreate>();

        private Action<StateTreePickItem> m_Picked;
        private Rect m_At;

        /// <summary>The last menu shown — what a test reads back.</summary>
        internal static StateTreePickMenu lastShown;

        /// <summary>Tests: record the menu, open no window.</summary>
        internal static bool headless;

        public StateTreePickMenu Command(string label, Action run)
        {
            commands.Add(new KeyValuePair<string, Action>(label, run));
            return this;
        }

        /// <summary>A "+ New …" item: <paramref name="make"/> gets the typed name and returns the item to
        /// pick (null: nothing made).</summary>
        public StateTreePickMenu Create(string label, string prompt, Func<string, StateTreePickItem> make,
            UnityEngine.Object reveal = null)
        {
            if (!string.IsNullOrEmpty(label))
                creates.Add(new StateTreePickCreate { label = label, prompt = prompt, make = make, reveal = reveal });
            return this;
        }

        /// <summary>Make the <paramref name="index"/>th "+ New" with <paramref name="name"/>, pick what it
        /// made, then reveal its target — one undo step. What the dropdown runs once the name is typed.</summary>
        public StateTreePickItem RunCreate(int index, string name)
        {
            if (index < 0 || index >= creates.Count)
                return null;
            StateTreePickCreate create = creates[index];
            StateTreePickItem made = null;
            if (create.make != null && string.IsNullOrWhiteSpace(name))
                return null;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(create.label.TrimStart('+', ' ').TrimEnd('…'));
            if (create.make != null)
            {
                made = create.make(name.Trim());
                if (made != null)
                    m_Picked?.Invoke(made);
            }
            if (create.reveal != null && !headless)
            {
                Selection.activeObject = create.reveal;
                EditorGUIUtility.PingObject(create.reveal);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            return made;
        }

        /// <summary>What choosing a "+ New" item does: ask the name (a small prompt), then make it.</summary>
        internal void Choose(int index)
        {
            if (index < 0 || index >= creates.Count)
                return;
            StateTreePickCreate create = creates[index];
            if (create.make == null)
            {
                RunCreate(index, "");
                return;
            }
            StateTreeNamePrompt.Show(m_At, create.prompt ?? create.label, "", name => RunCreate(index, name), "Create");
        }

        /// <summary>The menu as the dropdown lays it out, top to bottom: section headers, groups as
        /// paths ("Catalogue/room204/door/room204.door.opened"). For tests and diagnostics.</summary>
        public List<string> Layout(string current)
        {
            var lines = new List<string>();
            Build(current, (path, item) => lines.Add(path), header => lines.Add("# " + header), () => lines.Add("---"),
                command => lines.Add("> " + command), (index, label) => lines.Add(label));
            return lines;
        }

        /// <summary>The layout, handed to whoever draws it.</summary>
        internal void Build(string current, Action<string, StateTreePickItem> item, Action<string> header,
            Action separator, Action<string> command, Action<int, string> create = null)
        {
            if (create != null && creates.Count > 0)
            {
                for (int i = 0; i < creates.Count; i++)
                    create(i, creates[i].label);
                separator();
            }
            if (none != null)
                item(Mark(none, string.IsNullOrEmpty(current)), new StateTreePickItem("", none));
            var inTree = items.FindAll(i => i.inTree);
            var catalogue = items.FindAll(i => !i.inTree && i.fits);
            var others = items.FindAll(i => !i.inTree && !i.fits);
            if (inTree.Count > 0)
            {
                header(InTreeHeader);
                foreach (StateTreePickItem each in Sorted(inTree))
                    item(Name(each, current), each);
            }
            if (catalogue.Count > 0 || others.Count > 0)
            {
                if (inTree.Count > 0 || none != null)
                    separator();
                foreach (StateTreePickItem each in Sorted(catalogue))
                    item(Path(each.group, Name(each, current)), each);
                foreach (StateTreePickItem each in Sorted(others))
                    item(Path(other + "/" + each.group, Name(each, current)), each);
            }
            if (items.Count == 0)
                header(empty);
            if (commands.Count > 0)
            {
                separator();
                foreach (KeyValuePair<string, Action> each in commands)
                    command(each.Key);
            }
        }

        private static List<StateTreePickItem> Sorted(List<StateTreePickItem> list)
        {
            list.Sort((a, b) =>
            {
                int byGroup = string.CompareOrdinal(a.group, b.group);
                return byGroup != 0 ? byGroup : string.CompareOrdinal(a.label, b.label);
            });
            return list;
        }

        private static string Path(string group, string name)
        {
            group = (group ?? "").Trim('/');
            return string.IsNullOrEmpty(group) ? name : group + "/" + name;
        }

        /// <summary>"label — meaning", the current one marked; a '/' in it is not a group.</summary>
        internal static string Name(StateTreePickItem item, string current)
        {
            string text = item.label + (string.IsNullOrEmpty(item.meaning) ? "" : "  —  " + item.meaning);
            return Mark(text, string.Equals(item.value, current, StringComparison.Ordinal)).Replace('/', '∕');
        }

        private static string Mark(string text, bool current) => (current ? "● " : "") + text;

        /// <summary>Open it under <paramref name="at"/> (a GUI rect, as a picker button's worldBound).</summary>
        public void Show(Rect at, string current, Action<StateTreePickItem> picked)
        {
            m_Picked = picked;
            m_At = at;
            lastShown = this;
            if (headless)
                return;
            new StateTreePickDropdown(this, current, picked).Show(at);
        }

        // --- what this tree already uses -------------------------------------------------------

        /// <summary>
        /// Every reference value written anywhere in <paramref name="tree"/> — task and condition fields,
        /// key fields, entry references and a step's parameter values. Walked when a picker opens, never
        /// per repaint.
        /// </summary>
        public static HashSet<string> UsedIn(StateTreeAsset tree)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (tree == null)
                return used;
            foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(tree))
            {
                if (node == null)
                    continue;
                foreach (StateTreeTaskAsset task in node.tasks)
                    Collect(task, used, 0);
                foreach (StateTreeTransition transition in node.transitions)
                    Collect(transition?.condition, used, 0);
            }
            foreach (StateTreeKeyDeclaration key in tree.keys)
            {
                if (key != null && !string.IsNullOrEmpty(key.name))
                    used.Add(key.name);
            }
            return used;
        }

        private static void Collect(object target, HashSet<string> used, int depth)
        {
            if (target == null || depth > 8 || (target is UnityEngine.Object unity && unity == null))
                return;
            if (target is RunSubTreeTask run && run.overrides != null)
            {
                foreach (GraphTaskParameterOverride row in run.overrides)
                {
                    if (row != null && row.enabled && !string.IsNullOrEmpty(row.stringValue))
                        used.Add(row.stringValue);
                }
            }
            foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = field.GetValue(target);
                switch (value)
                {
                    case string text when !string.IsNullOrEmpty(text):
                        used.Add(text);
                        break;
                    case StateTreeKeyField key when !string.IsNullOrEmpty(key.text):
                        used.Add(key.text);
                        break;
                    case IStateTreeEntryRef entry when !string.IsNullOrEmpty(entry.EntryName):
                        used.Add(entry.EntryName);
                        break;
                    case StateTreeConditionAsset condition:
                        Collect(condition, used, depth + 1);
                        break;
                    case System.Collections.IList list when !(value is string):
                        foreach (object element in list)
                        {
                            if (element is StateTreeConditionAsset inner)
                                Collect(inner, used, depth + 1);
                            else if (element is string text && !string.IsNullOrEmpty(text))
                                used.Add(text);
                        }
                        break;
                }
            }
        }

        /// <summary>Marks the items this tree uses.</summary>
        public StateTreePickMenu InTree(HashSet<string> used)
        {
            foreach (StateTreePickItem item in items)
                item.inTree = used != null && !string.IsNullOrEmpty(item.value) && used.Contains(item.value);
            return this;
        }

        /// <summary>A dotted key's group: its prefix ("room204.door.opened" → "room204/door").</summary>
        public static string PrefixGroup(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            int dot = name.LastIndexOf('.');
            int colon = name.LastIndexOf(':');
            int cut = Math.Max(dot, colon);
            return cut > 0 ? name.Substring(0, cut).Replace('.', '/').Replace(':', '/') : "";
        }
    }

    /// <summary>The searchable dropdown itself — Unity's <see cref="AdvancedDropdown"/>, laid out by
    /// <see cref="StateTreePickMenu.Build"/>.</summary>
    internal sealed class StateTreePickDropdown : AdvancedDropdown
    {
        private readonly StateTreePickMenu m_Menu;
        private readonly string m_Current;
        private readonly Action<StateTreePickItem> m_Picked;
        private readonly Dictionary<int, StateTreePickItem> m_Items = new Dictionary<int, StateTreePickItem>();
        private readonly Dictionary<int, Action> m_Commands = new Dictionary<int, Action>();
        private readonly Dictionary<int, int> m_Creates = new Dictionary<int, int>();

        public StateTreePickDropdown(StateTreePickMenu menu, string current, Action<StateTreePickItem> picked)
            : base(new AdvancedDropdownState())
        {
            m_Menu = menu;
            m_Current = current ?? "";
            m_Picked = picked;
            minimumSize = new Vector2(320f, 360f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(m_Menu.title);
            var groups = new Dictionary<string, AdvancedDropdownItem>();
            int next = 1;
            m_Menu.Build(m_Current,
                (path, item) =>
                {
                    string[] parts = path.Split('/');
                    AdvancedDropdownItem parent = root;
                    string at = "";
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        at += "/" + parts[i];
                        if (!groups.TryGetValue(at, out AdvancedDropdownItem group))
                        {
                            group = new AdvancedDropdownItem(parts[i]);
                            groups[at] = group;
                            parent.AddChild(group);
                        }
                        parent = group;
                    }
                    var leaf = new AdvancedDropdownItem(parts[parts.Length - 1]) { id = next++ };
                    m_Items[leaf.id] = item;
                    parent.AddChild(leaf);
                },
                header => root.AddChild(new AdvancedDropdownItem(header) { enabled = false, id = next++ }),
                root.AddSeparator,
                command =>
                {
                    var leaf = new AdvancedDropdownItem(command) { id = next++ };
                    foreach (KeyValuePair<string, Action> each in m_Menu.commands)
                    {
                        if (each.Key == command)
                            m_Commands[leaf.id] = each.Value;
                    }
                    root.AddChild(leaf);
                },
                (index, label) =>
                {
                    var leaf = new AdvancedDropdownItem(label) { id = next++ };
                    m_Creates[leaf.id] = index;
                    root.AddChild(leaf);
                });
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (m_Items.TryGetValue(item.id, out StateTreePickItem picked))
                m_Picked?.Invoke(picked);
            else if (m_Commands.TryGetValue(item.id, out Action command))
                command?.Invoke();
            else if (m_Creates.TryGetValue(item.id, out int create))
                m_Menu.Choose(create);
        }
    }
}
