using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE API BROWSER (ui-wiring brief §4f) — every declared subsystem in one window:
    /// its REQUESTS (what it answers to, typed and described) and its ANNOUNCEMENTS
    /// (the declared keys it writes for others, payload contracts included). The other
    /// half is the SCAFFOLDS, because finding an API is worthless if using it is still
    /// boilerplate: "call" drops a ready SetBlackboardTask into a picked state (value
    /// row-picked when the request is registry-typed); "react" creates a reaction state
    /// in the target tree — interrupt on the key, consume at the end — leaving the
    /// author only the middle: what the reaction MEANS.
    /// </summary>
    public sealed class SubsystemApisWindow : EditorWindow
    {
        [MenuItem("Tools/Draw To Play/Subsystem APIs")]
        public static void Open()
        {
            var window = GetWindow<SubsystemApisWindow>();
            window.titleContent = new GUIContent("Subsystem APIs");
            window.minSize = new Vector2(360f, 300f);
        }

        private ObjectField m_Target;
        private ScrollView m_List;

        private void OnEnable()
        {
            EditorApplication.projectChanged += Rebuild;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Rebuild;
        }

        private void CreateGUI()
        {
            rootVisualElement.style.paddingLeft = 8f;
            rootVisualElement.style.paddingRight = 8f;
            rootVisualElement.style.paddingTop = 6f;

            m_Target = new ObjectField("Scaffold into tree")
            {
                objectType = typeof(StateTreeAsset),
                tooltip = "Where 'call' and 'react' land — any state tree: the session, a "
                    + "tutorial tree, another subsystem's flows."
            };
            rootVisualElement.Add(m_Target);

            m_List = new ScrollView();
            m_List.style.flexGrow = 1f;
            m_List.style.marginTop = 4f;
            rootVisualElement.Add(m_List);

            Rebuild();
        }

        private void Rebuild()
        {
            if (m_List == null)
                return;
            m_List.Clear();

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(ServiceDef));
            var any = false;
            for (var i = 0; i < guids.Length; i++)
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (def == null || (def.requests.Count == 0 && def.flows == null))
                    continue;
                any = true;
                m_List.Add(BuildDefSection(def));
            }
            if (!any)
                m_List.Add(Note("No subsystem declares requests or flows yet."));
        }

        private VisualElement BuildDefSection(ServiceDef def)
        {
            var fold = new Foldout
            {
                text = (string.IsNullOrEmpty(def.serviceName) ? def.name : def.serviceName)
                    + "  ·  " + def.scope,
                value = true
            };
            fold.style.marginBottom = 6f;

            var ping = new Button(() => EditorGUIUtility.PingObject(def)) { text = "ping def" };
            ping.style.alignSelf = Align.FlexStart;
            fold.Add(ping);

            if (def.requests.Count > 0)
            {
                fold.Add(Header("Requests — what it answers to"));
                for (var i = 0; i < def.requests.Count; i++)
                {
                    ServiceRequest row = def.requests[i];
                    if (row == null || string.IsNullOrEmpty(row.key))
                        continue;
                    fold.Add(RequestRow(def, row));
                }
            }

            List<StateTreeKeyDeclaration> flowAnnouncements = Announcements(def);
            var anyAnnounced = def.announcements.Count > 0 || flowAnnouncements.Count > 0;
            if (anyAnnounced)
                fold.Add(Header("Announcements — what it writes for others"));
            for (var i = 0; i < def.announcements.Count; i++)
            {
                ServiceAnnouncement announced = def.announcements[i];
                if (announced == null || string.IsNullOrEmpty(announced.key))
                    continue;
                var suffix = string.IsNullOrEmpty(announced.payloadTypeName)
                    ? ""
                    : " : " + announced.payloadTypeName;
                fold.Add(AnnouncementRow(announced.key + suffix, announced.description,
                    announced.key));
            }
            for (var i = 0; i < flowAnnouncements.Count; i++)
            {
                StateTreeKeyDeclaration declared = flowAnnouncements[i];
                var suffix = !string.IsNullOrEmpty(declared.payloadTypeName)
                    ? " : " + declared.payloadTypeName
                    : declared.namesRowOf != null
                        ? " — row of " + declared.namesRowOf.name
                        : " (" + declared.kind + ")";
                fold.Add(AnnouncementRow(declared.name + suffix, declared.description,
                    declared.name));
            }

            return fold;
        }

        /// <summary>The flows tree's declared keys that are NOT requests: what the
        /// subsystem publishes rather than serves — its outbound API.</summary>
        private static List<StateTreeKeyDeclaration> Announcements(ServiceDef def)
        {
            var found = new List<StateTreeKeyDeclaration>();
            var keys = def.flows != null ? def.flows.keys : null;
            for (var i = 0; keys != null && i < keys.Count; i++)
            {
                StateTreeKeyDeclaration declared = keys[i];
                if (declared != null && !string.IsNullOrEmpty(declared.name)
                    && def.RequestFor(declared.name) == null)
                    found.Add(declared);
            }
            return found;
        }

        private VisualElement RequestRow(ServiceDef def, ServiceRequest request)
        {
            var row = ApiRow(
                request.key + (request.namesRowOf != null
                    ? " — row of " + request.namesRowOf.name
                    : ""),
                request.description);
            var call = new Button(() => ShowCallMenu(request)) { text = "call ▸" };
            call.tooltip = "Drop a ready SetBlackboard task writing this request into a "
                + "state of the target tree. A registry-typed request offers its rows.";
            row.Insert(1, call);
            return row;
        }

        private VisualElement AnnouncementRow(string title, string description, string keyName)
        {
            var row = ApiRow(title, description);
            var react = new Button(() => ShowReactMenu(keyName)) { text = "react ▸" };
            react.tooltip = "Create a reaction state in the target tree: an interrupt on "
                + "this key from the picked state, the consume at the end — the middle is "
                + "yours to fill.";
            row.Insert(1, react);
            return row;
        }

        // ---- the scaffolds -------------------------------------------------------------

        private void ShowCallMenu(ServiceRequest request)
        {
            StateTreeAsset tree = TargetTree();
            if (tree == null)
                return;
            var menu = new GenericMenu();
            foreach (StateTreeNodeAsset state in States(tree))
            {
                StateTreeNodeAsset target = state;
                string stateLabel = StateLabel(state);
                if (request.namesRowOf != null)
                {
                    for (var i = 0; i < request.namesRowOf.Count; i++)
                    {
                        StateTreeRegistryEntry entry = request.namesRowOf.EntryAt(i);
                        if (entry == null || string.IsNullOrEmpty(entry.name))
                            continue;
                        string value = entry.name;
                        menu.AddItem(new GUIContent(stateLabel + "/" + value), false,
                            () => AddCall(tree, target, request.key, value));
                    }
                }
                else
                {
                    menu.AddItem(new GUIContent(stateLabel), false,
                        () => AddCall(tree, target, request.key, "1"));
                }
            }
            menu.ShowAsContext();
        }

        private static void AddCall(StateTreeAsset tree, StateTreeNodeAsset state,
            string requestKey, string value)
        {
            const string undo = "Add API Call";
            var task = StateTreeEditorOps.CreateTask(tree, state, typeof(SetBlackboardTask),
                undo) as SetBlackboardTask;
            if (task == null)
                return;
            task.kind = SetBlackboardTask.ValueKind.String;
            task.key = new StateTreeKeyField(requestKey);
            task.stringValue = value;
            EditorUtility.SetDirty(task);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(tree);
        }

        private void ShowReactMenu(string keyName)
        {
            StateTreeAsset tree = TargetTree();
            if (tree == null)
                return;
            var menu = new GenericMenu();
            foreach (StateTreeNodeAsset state in States(tree))
            {
                StateTreeNodeAsset watch = state;
                menu.AddItem(new GUIContent("watch from " + StateLabel(state)), false,
                    () => AddReaction(tree, watch, keyName));
            }
            menu.ShowAsContext();
        }

        /// <summary>The reaction scaffold: the boilerplate half of "when X lands, do Y" —
        /// interrupt in, consume out, an explicit way back — generated; the Y is authored.</summary>
        private static void AddReaction(StateTreeAsset tree, StateTreeNodeAsset watch,
            string keyName)
        {
            const string undo = "Add API Reaction";

            StateTreeNodeAsset parent = StateTreeEditorOps.FindParent(tree, watch) ?? watch;
            StateTreeNodeAsset reaction = StateTreeEditorOps.CreateNode(tree, parent,
                "on-" + keyName.Replace('.', '-'), "On " + keyName, undo);
            if (reaction == null)
                return;

            Undo.RecordObject(reaction, undo);
            reaction.transitions.Add(new StateTreeTransition { targetNodeId = watch.nodeId });

            var consume = StateTreeEditorOps.CreateTask(tree, reaction,
                typeof(SetBlackboardTask), undo) as SetBlackboardTask;
            if (consume != null)
            {
                consume.kind = SetBlackboardTask.ValueKind.Clear;
                consume.key = new StateTreeKeyField(keyName);
                EditorUtility.SetDirty(consume);
            }

            Undo.RecordObject(watch, undo);
            var interrupt = new StateTreeTransition
            {
                targetNodeId = reaction.nodeId, checkWhileRunning = true
            };
            watch.transitions.Add(interrupt);
            var condition = StateTreeEditorOps.SetTransitionCondition(tree, watch, interrupt,
                typeof(HasBlackboardKeyCondition), undo) as HasBlackboardKeyCondition;
            if (condition != null)
            {
                condition.key = new StateTreeKeyField(keyName);
                EditorUtility.SetDirty(condition);
            }

            EditorUtility.SetDirty(watch);
            EditorUtility.SetDirty(reaction);
            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(tree);
        }

        // ---- small parts ---------------------------------------------------------------

        private StateTreeAsset TargetTree()
        {
            var tree = m_Target != null ? m_Target.value as StateTreeAsset : null;
            if (tree == null)
                ShowNotification(new GUIContent("Pick a target tree first."));
            return tree;
        }

        private static IEnumerable<StateTreeNodeAsset> States(StateTreeAsset tree)
        {
            var pending = new Stack<StateTreeNodeAsset>();
            if (tree != null && tree.root != null)
                pending.Push(tree.root);
            while (pending.Count > 0)
            {
                StateTreeNodeAsset node = pending.Pop();
                if (node == null)
                    continue;
                yield return node;
                for (var i = node.children.Count - 1; i >= 0; i--)
                    pending.Push(node.children[i]);
            }
        }

        private static string StateLabel(StateTreeNodeAsset node)
        {
            return string.IsNullOrEmpty(node.displayName) || node.displayName == node.nodeId
                ? node.nodeId
                : node.nodeId + " — " + node.displayName;
        }

        private static VisualElement ApiRow(string title, string description)
        {
            var container = new VisualElement { style = { marginBottom = 4f } };
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center }
            };
            row.Add(new Label(title)
            {
                style = { flexGrow = 1f, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 12f }
            });
            container.Add(row);
            if (!string.IsNullOrEmpty(description))
            {
                container.Add(new Label(description)
                {
                    style =
                    {
                        fontSize = 11f, color = new Color(1f, 1f, 1f, 0.55f),
                        whiteSpace = WhiteSpace.Normal, marginLeft = 8f
                    }
                });
            }
            return container;
        }

        private static Label Header(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 6f, marginBottom = 2f
                }
            };
        }

        private static Label Note(string text)
        {
            return new Label(text)
            {
                style = { color = new Color(1f, 1f, 1f, 0.5f) }
            };
        }
    }
}
