using System.Collections.Generic;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The def's inspector as TYPED OFFERS (§4g review): the definition is the subsystem
    /// root, so its rows are picks from the contracts the project already declares — the
    /// action from the service class's [ServiceActionContract] vocabulary, the reaction's
    /// UI row from the UI registries, its verb from the skin's [UiVerbContract]s, the
    /// argument key from the announcements. Free text stays legal everywhere (the ▾ is
    /// an offer, not a cage), and the bottom shows the SCREEN SURFACE — the spawned
    /// widgets' verbs and fields, the subsystem's visual.
    /// </summary>
    [CustomEditor(typeof(ServiceDef))]
    public sealed class ServiceDefEditor : UnityEditor.Editor
    {
        private const string k_Undo = "Edit Service Def";

        /// <summary>The fields with bespoke IMGUI sections below; everything else is a
        /// PropertyField so its drawer decides how it looks.</summary>
        private static readonly HashSet<string> k_Bespoke = new HashSet<string>
        {
            "m_Script", "serviceTypeName", "requests", "spawns", "body",
            "attributes", "settings", "treeKind", "nestingRules", "kindSeeds"
        };

        /// <summary>
        /// A UI TOOLKIT HOST (the project rule). The def's body wears tags and its catalogs are
        /// picked, and those are UI Toolkit drawers — inside the IMGUI version of this editor
        /// every kind def's tags read "No GUI Implemented", unnoticed because the subsystem
        /// defs have none. The identity fields are PropertyFields now, so their drawers draw;
        /// the bespoke sections below keep their IMGUI in one container, to be ported a
        /// section at a time or never — an IMGUI section inside a UI Toolkit host is fine, it
        /// is the other way round that breaks.
        /// </summary>
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            // The identity every service has. The FLOW-BACKED half (tree kind, flows,
            // nesting rules, kind seeds) is folded away below: it is meaningful only for
            // a subsystem whose handlers wait, and on a def-only one it is four empty
            // fields pretending to be part of the declaration.
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (k_Bespoke.Contains(property.name))
                    continue;
                root.Add(new PropertyField(property.Copy()));
            }

            // THE FOUR VERBS (M41.2): what a designer can draw with, each only when it applies.
            // The body is a UI Toolkit drawer (tags, picks) and stays a PropertyField, placed
            // under the IS heading between two IMGUI containers.
            root.Add(new IMGUIContainer(DrawAsksAnnouncesShows));
            m_Body = new PropertyField(serializedObject.FindProperty("body"));
            root.Add(m_Body);
            root.Add(new IMGUIContainer(DrawIsAndTheRest));
            root.Bind(serializedObject);
            return root;
        }

        private PropertyField m_Body;

        /// <summary>A def with no class, no rows, no screen and no body is infrastructure, and
        /// says so in one sentence rather than five empty sections.</summary>
        private static bool OffersNothing(ServiceDef def)
        {
            return ActionOffers(def).Length == 0 && def.requests.Count == 0
                && def.spawns.Count == 0 && !def.body.IsThing && def.attributes.Count == 0;
        }

        private void DrawAsksAnnouncesShows()
        {
            var def = (ServiceDef)target;
            if (def == null)
                return;
            serializedObject.Update();

            DrawServiceType(def);
            bool hasClass = ResolveServiceType(def.serviceTypeName) != null;
            bool isBody = def.body.IsThing || def.attributes.Count > 0;
            if (m_Body != null)
                m_Body.style.display = isBody ? DisplayStyle.Flex : DisplayStyle.None;

            if (hasClass)
                DrawSettings(def);
            if (OffersNothing(def))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox("This subsystem offers nothing to a flow — it is "
                    + "infrastructure. A class that declares actions, a screen, or a body "
                    + "would appear here as Asks, Announces, Shows and Is.", MessageType.None);
                serializedObject.ApplyModifiedProperties();
                return;
            }
            DrawAsks(def);
            if (hasClass)
                DrawAnnouncements(def);
            DrawShows(def);
            if (isBody)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Is — the body it builds", EditorStyles.boldLabel);
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIsAndTheRest()
        {
            var def = (ServiceDef)target;
            if (def == null)
                return;
            serializedObject.Update();
            // HAS belongs to IS: a body's starting attributes, and the requests they derive.
            if (def.body.IsThing || def.attributes.Count > 0)
            {
                DrawAttributes(def);
                DrawDerived(def);
            }
            DrawTreeKind(def);

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Subsystem APIs…", GUILayout.Width(140f)))
                SubsystemApisWindow.Open();

            DrawScreenSurface(def);
            serializedObject.ApplyModifiedProperties();
        }

        private void Commit(System.Action edit)
        {
            Undo.RecordObject(target, k_Undo);
            edit();
            EditorUtility.SetDirty(target);
        }

        /// <summary>The lock rule, uniform (§4g review): a value that MATCHES a declared
        /// offer is BOUND — the field renders read-only and the ▾ is how it changes
        /// (pick another offer, or the menu's clear item to go free-text). A value no
        /// offer knows is free text and stays editable. Derived, not stored: bound IS
        /// "the contract recognises it".</summary>
        private void BoundTextField(string label, string tooltip, string current,
            bool bound, System.Action<string> set)
        {
            if (bound)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(new GUIContent(label, tooltip
                        + " — BOUND to a declared offer; change it from ▾."), current);
                }
            }
            else
            {
                string typed = EditorGUILayout.DelayedTextField(
                    new GUIContent(label, tooltip), current);
                if (typed != current)
                    Commit(() => set(typed));
            }
        }

        private static bool Offered(string[] offers, string value)
        {
            for (int i = 0; i < offers.Length; i++)
            {
                if (string.Equals(offers[i], value, System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // ---- the service type: who runs this def ---------------------------------------

        private void DrawServiceType(ServiceDef def)
        {
            EditorGUILayout.BeginHorizontal();
            BoundTextField("Service Type",
                "The service class that runs this def — the source of the action "
                + "vocabulary below.",
                def.serviceTypeName,
                ResolveServiceType(def.serviceTypeName) != null,
                value => def.serviceTypeName = value);
            if (GUILayout.Button("▾", GUILayout.Width(22f)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("(none)"),
                    string.IsNullOrEmpty(def.serviceTypeName),
                    () => Commit(() => def.serviceTypeName = ""));
                foreach (System.Type type in
                    TypeCache.GetTypesDerivedFrom<StateTreeService>())
                {
                    if (type.IsAbstract)
                        continue;
                    string name = type.Name;
                    menu.AddItem(new GUIContent(name),
                        name == def.serviceTypeName,
                        () => Commit(() => def.serviceTypeName = name));
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }



        // ---- attributes: what it HAS, and what that lets anybody do ---------------------

        /// <summary>
        /// WHAT THIS DEF HAS (M30.4) — the data half of its API, from which the request rows
        /// below are derived rather than typed.
        ///
        /// The offer is the neighbourhood again: the attribute catalogs this def's registry
        /// declares, never the project's. WRITABLE is the permission and it is not decoration —
        /// a read-only attribute derives the ask alone, and the runtime refuses the rest by
        /// asking the same question this checkbox answers.
        /// </summary>
        private void DrawAttributes(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent("Has — its attributes",
                "What this kind of thing has. Its read and change requests follow from these."),
                EditorStyles.boldLabel);

            StateTreeOffers.RowsOfKind(def, m_Attributes);

            for (int i = 0; i < def.attributes.Count; i++)
            {
                ServiceAttribute has = def.attributes[i];
                if (has == null)
                    continue;
                int index = i;

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(new GUIContent("Attribute",
                        "Linked to a catalog row — change it from ▾."),
                        string.IsNullOrEmpty(has.Name) ? "(none)" : has.Name);
                }
                if (GUILayout.Button("▾", GUILayout.Width(22f)))
                    ShowAttributeMenu(def, index);
                bool writable = GUILayout.Toggle(has.writable,
                    new GUIContent("writable", "Off derives only the ask — and the runtime "
                        + "refuses set and add, not just this inspector."),
                    GUILayout.Width(78f));
                if (writable != has.writable)
                    Commit(() => has.writable = writable);
                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                {
                    Commit(() => def.attributes.RemoveAt(index));
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(has.Name) && !Offered(m_Attributes, has.Name))
                {
                    EditorGUILayout.HelpBox("Nothing this def declares holds an attribute called '"
                        + has.Name + "'. Add its catalog to the registry's Depends On.",
                        MessageType.Warning);
                }
            }

            if (GUILayout.Button("+ has…", GUILayout.Width(140f)))
                ShowAttributeMenu(def, -1);
        }

        private void ShowAttributeMenu(ServiceDef def, int index)
        {
            var menu = new GenericMenu();
            if (m_Attributes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(def.registry == null
                    ? "this def manages no catalog — nothing to read attributes from"
                    : "'" + def.registry.name + "' declares no attribute catalog"));
            }
            for (int i = 0; i < m_Attributes.Count; i++)
            {
                AttributeDef row = m_Attributes[i];
                bool taken = HasAttribute(def, row.name);
                menu.AddItem(new GUIContent(row.name), taken, () => Commit(() =>
                {
                    var has = new ServiceAttribute();
                    has.attribute.entryId = row.id;
                    has.attribute.entryName = row.name;
                    if (index >= 0 && index < def.attributes.Count)
                    {
                        has.writable = def.attributes[index].writable;
                        def.attributes[index] = has;
                    }
                    else if (!taken)
                    {
                        def.attributes.Add(has);
                    }
                }));
            }
            menu.ShowAsContext();
        }

        private static bool HasAttribute(ServiceDef def, string name)
        {
            for (int i = 0; i < def.attributes.Count; i++)
            {
                if (def.attributes[i] != null && def.attributes[i].Name == name)
                    return true;
            }
            return false;
        }

        private static bool Offered(List<AttributeDef> offers, string name)
        {
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i] != null && offers[i].name == name)
                    return true;
            }
            return false;
        }

        // ---- derived: the rows nobody types --------------------------------------------

        /// <summary>
        /// THE API THAT WROTE ITSELF (M30.4) — read-only, because editing a derived row would be
        /// editing the attribute it came from through a copy.
        ///
        /// Each row carries the same ⛓ as an authored one, and it works for the same reason: a
        /// request key travels as text, so whoever writes 'health.add' anywhere in the project
        /// is a caller and the usage index already knows it. That is the "links of usage stay
        /// visible" half of the brief, and it is what makes a generated surface worth reading.
        /// </summary>
        private void DrawDerived(ServiceDef def)
        {
            def.DerivedRequests(m_Derived);
            if (m_Derived.Count == 0)
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(new GUIContent("Derived — from what it has",
                "Nobody typed these. They follow from the attributes above and disappear with "
                + "them."), EditorStyles.boldLabel);

            for (int i = 0; i < m_Derived.Count; i++)
            {
                ServiceRequest row = m_Derived[i];
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(row.key, row.description);
                CallersButton(row.key);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.HelpBox("Served on the OBJECT, not on the board: an Object Request "
                + "task calls these on whatever body this def built. A def that declares an "
                + "attribute its prefab has not got is refused at the call and says so.",
                MessageType.None);
        }

        private readonly List<AttributeDef> m_Attributes = new List<AttributeDef>();
        private readonly List<ServiceRequest> m_Derived = new List<ServiceRequest>();

        // ---- requests ------------------------------------------------------------------

        // ---- asks: one row per declared action, plus the rows a graph serves --------------

        /// <summary>
        /// ASKS — what a flow may ask this subsystem, and what it answers (M41.2). One row per
        /// action the CLASS declares ([ServiceActionContract]), generated: the action and its
        /// answer are read-only, and the designer picks what is theirs — the key callers use
        /// (default serviceName.action), the registry that types the value, what an empty value
        /// means, the graph that reacts. An action with no row yet is offered as one button.
        /// Below them, the rows a GRAPH serves (no action, a reaction graph) — a subsystem with
        /// no class has only these, and that is a whole subsystem.
        /// </summary>
        private void DrawAsks(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Asks — what a flow may ask, and what it answers",
                EditorStyles.boldLabel);

            System.Type type = ResolveServiceType(def.serviceTypeName);
            var contracts = type != null
                ? (ServiceActionContractAttribute[])type.GetCustomAttributes(
                    typeof(ServiceActionContractAttribute), true)
                : new ServiceActionContractAttribute[0];
            var declared = new HashSet<string>();
            for (int i = 0; i < contracts.Length; i++)
            {
                declared.Add(contracts[i].action);
                ServiceRequest row = def.requests.Find(r => r != null && r.action == contracts[i].action);
                DrawAsk(def, contracts[i], row);
            }

            // Rows the class does not know: served by a graph (no action), or stale.
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row == null || (!string.IsNullOrEmpty(row.action) && declared.Contains(row.action)))
                    continue;
                if (string.IsNullOrEmpty(row.action))
                    DrawGraphServedAsk(def, row, i);
                else
                    DrawStaleAsk(def, row, i);
            }

            if (GUILayout.Button(new GUIContent("+ ask served by a graph",
                "A request with no class verb: its reaction graph IS the handler."),
                GUILayout.Width(170f)))
                Commit(() => def.requests.Add(new ServiceRequest
                {
                    key = (string.IsNullOrEmpty(def.serviceName) ? def.name : def.serviceName) + ".ask"
                }));
        }

        private void DrawAsk(ServiceDef def, ServiceActionContractAttribute contract, ServiceRequest row)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string answers = contract.answersWith != null ? "  →  " + contract.answersWith.Name : "";
            EditorGUILayout.LabelField(new GUIContent(contract.action + answers,
                string.IsNullOrEmpty(contract.valueHint) ? "declared by " + def.serviceTypeName
                    : contract.valueHint), EditorStyles.boldLabel);
            if (row == null)
            {
                if (GUILayout.Button("offer it", GUILayout.Width(70f)))
                {
                    string action = contract.action;
                    Commit(() => def.requests.Add(new ServiceRequest
                    {
                        key = (string.IsNullOrEmpty(def.serviceName) ? def.name : def.serviceName)
                            + "." + action,
                        action = action
                    }));
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("    not offered to flows", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }
            if (RemoveButton())
            {
                ServiceRequest going = row;
                Commit(() => def.requests.Remove(going));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            DrawPicks(def, row, typed: true);
            EditorGUILayout.EndVertical();
        }

        private void DrawGraphServedAsk(ServiceDef def, ServiceRequest row, int index)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("served by a graph",
                "No class verb: the reaction graph below runs as the handler."), EditorStyles.boldLabel);
            if (RemoveButton())
            {
                Commit(() => def.requests.RemoveAt(index));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            DrawPicks(def, row, typed: true);
            if (row.reactionGraph == null)
                EditorGUILayout.HelpBox("Pick the graph that serves it — without one this ask does nothing.",
                    MessageType.Warning);
            EditorGUILayout.EndVertical();
        }

        private void DrawStaleAsk(ServiceDef def, ServiceRequest row, int index)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(row.key + "  —  '" + row.action + "' is not an action "
                + (string.IsNullOrEmpty(def.serviceTypeName) ? "of any class" : def.serviceTypeName + " declares"),
                EditorStyles.boldLabel);
            if (RemoveButton())
            {
                Commit(() => def.requests.RemoveAt(index));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>The designer's half of an ask: the key, what the value names, what an empty
        /// value means, the description, and the graph that reacts.</summary>
        private void DrawPicks(ServiceDef def, ServiceRequest row, bool typed)
        {
            ServiceRequest keyRow = row;
            EditorGUILayout.BeginHorizontal();
            DrawKey("Key", "What callers write to ask for this.", row.key, value => keyRow.key = value);
            EditorGUILayout.EndHorizontal();
            DrawRenamePanel(row.key, value => keyRow.key = value);

            string description = EditorGUILayout.DelayedTextField("Description", row.description);
            if (description != row.description)
                Commit(() => row.description = description);

            var registry = (StateTreeRegistryAsset)EditorGUILayout.ObjectField(
                new GUIContent("Value names a row of", "Typed callers and the Ask ▾ dropdown "
                    + "are held to this registry's rows. Empty: any string."),
                row.namesRowOf, typeof(StateTreeRegistryAsset), false);
            if (registry != row.namesRowOf)
                Commit(() => row.namesRowOf = registry);
            if (row.namesRowOf != null)
            {
                string empty = EditorGUILayout.DelayedTextField(new GUIContent("Empty means",
                    "What an EMPTY value asks for — 'the station you are at'. Blank: an empty "
                    + "value is refused like any other non-row."), row.emptyMeans);
                if (empty != row.emptyMeans)
                    Commit(() => row.emptyMeans = empty);
            }

            EditorGUI.BeginChangeCheck();
            var graph = (GraphTaskAsset)EditorGUILayout.ObjectField(new GUIContent("Reacts with",
                    "A graph run on the subsystem's scope each time this is served — the drawn "
                    + "continuation. The value is under 'key.asked'; the answer on its own key."),
                row.reactionGraph, typeof(GraphTaskAsset), false);
            if (EditorGUI.EndChangeCheck())
                Commit(() => row.reactionGraph = graph);
        }


        // ---- spawns --------------------------------------------------------------------

        private void DrawShows(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Shows — the screen it owns",
                EditorStyles.boldLabel);
            for (int i = 0; i < def.spawns.Count; i++)
            {
                var spawn = def.spawns[i];
                if (spawn == null)
                    continue;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(string.IsNullOrEmpty(spawn.entryName)
                    ? "(pick ui row)" : spawn.entryName, EditorStyles.popup))
                {
                    UiRowMenu(spawn.entryName, (id, rowName) =>
                    {
                        spawn.entryId = id;
                        spawn.entryName = rowName;
                    });
                }
                if (RemoveButton())
                {
                    int index = i;
                    Commit(() => def.spawns.RemoveAt(index));
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ show", GUILayout.Width(80f)))
                Commit(() => def.spawns.Add(new StateTreeEntryRef<UiDef>()));
        }

        // ---- announcements -------------------------------------------------------------

        /// <summary>
        /// WHAT THIS SUBSYSTEM ANNOUNCES — read from its class (M41.1), never typed: every
        /// [ServiceAnnouncement] it declares and the answer contract of every action it serves.
        /// A designer picks nothing here; this is the menu a When Announced ▾ offers.
        /// </summary>
        private void DrawAnnouncements(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Announces — what a flow may wait on", EditorStyles.boldLabel);
            List<DeclaredApi.Announced> rows = DeclaredApi.Announcements(def.name);
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("    nothing — the class declares no [ServiceAnnouncement] "
                    + "and no action answers with a contract", EditorStyles.miniLabel);
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                DeclaredApi.Announced row = rows[i];
                string payload = row.payload != null ? " : " + DeclaredApi.PayloadLabel(row.payload) : "";
                EditorGUILayout.LabelField("    " + row.key + payload, row.description, EditorStyles.label);
            }
        }

        /// <summary>The ability-tree authoring rules a def of a TREE KIND carries (M23): shown
        /// only when it is one, because nothing else has a use for them.</summary>
        private void DrawTreeKind(ServiceDef def)
        {
            if (string.IsNullOrEmpty(def.treeKind))
                return;
            EditorGUILayout.Space(6f);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("treeKind"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nestingRules"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("kindSeeds"));
            serializedObject.ApplyModifiedProperties();
        }


        // ---- the flow-backed half, folded away --------------------------------------


        /// <summary>
        /// HOW THIS KIND IS TUNED (M36) — every knob the class declares, its default dimmed
        /// beside it, a tick where this def differs; a tag-typed knob PICKED from what the def
        /// declares. Drawn by the same panel a placement's options and an install's overrides
        /// use (<see cref="DeclaredOptionsPanel"/>): this method only says who declares.
        /// </summary>
        private void DrawSettings(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            System.Type type = def.serviceType;
            List<DeclaredOption> declared = DeclaredOptions.OfService(def);

            serializedObject.Update();
            SerializedProperty rows = serializedObject.FindProperty("settings.values");
            float height = DeclaredOptionsPanel.Height(declared, rows,
                DeclaredOptionRowShape.ServiceSetting);
            Rect area = EditorGUILayout.GetControlRect(false, height);
            DeclaredOptionsPanel.Draw(area,
                new GUIContent("Settings — how it is tuned",
                    "What the service class declares it can be tuned by. Tick to give this def "
                    + "its own value; unticked follows the class default."),
                type == null ? "name a service type first" : type.Name + " declares no settings",
                declared, rows, DeclaredOptionRowShape.ServiceSetting);
            serializedObject.ApplyModifiedProperties();
        }


        // ---- the offers: what the project's contracts declare --------------------------

        private static string[] ActionOffers(ServiceDef def)
        {
            System.Type type = ResolveServiceType(def.serviceTypeName);
            if (type == null)
                return System.Array.Empty<string>();
            var contracts = (ServiceActionContractAttribute[])type.GetCustomAttributes(
                typeof(ServiceActionContractAttribute), true);
            var offers = new string[contracts.Length];
            for (int i = 0; i < contracts.Length; i++)
                offers[i] = contracts[i].action;
            return offers;
        }



        /// <summary>Every payload class any task contract declares — the type names the
        /// project actually sends, which is what an announcement may carry.</summary>


        // ---- the pickers: offers from declared contracts -------------------------------





        private void UiRowMenu(string current, System.Action<string, string> set)
        {
            var menu = new GenericMenu();
            var any = false;
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(UiRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<UiRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null)
                    continue;
                for (int k = 0; k < registry.entries.Count; k++)
                {
                    UiDef row = registry.entries[k];
                    if (row == null || string.IsNullOrEmpty(row.name))
                        continue;
                    any = true;
                    string id = row.id;
                    string name = row.name;
                    menu.AddItem(new GUIContent(registry.name + "/" + name),
                        name == current, () => Commit(() => set(id, name)));
                }
            }
            if (!any)
                menu.AddDisabledItem(new GUIContent("no UI rows found"));
            menu.ShowAsContext();
        }

        private static bool RemoveButton()
        {
            return GUILayout.Button("✕", GUILayout.Width(22f));
        }

        /// <summary>
        /// WHO CALLS THIS (the other half of an API): every asset that writes this request
        /// key, from the project-wide wire scan — pick one to ping it. An API whose callers
        /// cannot be listed is a promise nobody can audit, and the answer "nobody yet" is
        /// worth seeing too: it names a request that exists for no one.
        /// </summary>
        /// <summary>
        /// A KEY SOMETHING ALREADY CALLS IS A LINK, and a link you can retype is a link that
        /// silently stops matching — the rule every wired field in this toolset follows, applied
        /// where it was missing: a request key and an announcement key are contracts other assets
        /// hold BY NAME.
        ///
        /// Locked is derived, never stored: it means "the usage index found callers". A key
        /// nobody calls yet is free text, because a name being invented is not a contract. And
        /// the way to change a locked one is ✎, which renames it everywhere at once.
        /// </summary>
        private void DrawKey(string label, string tooltip, string current,
            System.Action<string> set)
        {
            List<AssetWireScan.WireUse> callers = ServiceKeyRename.Callers(current);
            IReadOnlyList<string> inCode = ServiceKeyCode.Owners(current);
            bool linked = callers.Count > 0 || inCode.Count > 0;

            if (linked)
            {
                // A CONSTANT COUNTS, and counts hardest: a name C# declares cannot be renamed
                // from here at all, so the field being editable would fork the name silently.
                string who = inCode.Count > 0
                    ? "declared in code as " + string.Join(", ", inCode)
                    : callers.Count + " place(s) name it";
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(new GUIContent(label, tooltip + " — LINKED: "
                        + who + "."), current);
                }
            }
            else
            {
                string typed = EditorGUILayout.DelayedTextField(new GUIContent(label,
                    tooltip + " — nothing names it yet, so it is still free text."), current);
                if (typed != current)
                    Commit(() => set(typed));
            }

            CallersButton(current);
            if (linked && inCode.Count == 0
                && GUILayout.Button(new GUIContent("✎", "Rename it here AND in every "
                    + "place that names it."), GUILayout.Width(24f)))
            {
                m_RenamingKey = current;
                m_RenameTo = current;
                GUI.FocusControl(null);
            }
        }

        /// <summary>The rename, spelled out before it happens: the new name, who moves with it,
        /// and a way out. Nothing is written until Rename is pressed.</summary>
        private void DrawRenamePanel(string current, System.Action<string> set)
        {
            if (m_RenamingKey != current || string.IsNullOrEmpty(current))
                return;

            List<AssetWireScan.WireUse> callers = ServiceKeyRename.Callers(current);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            m_RenameTo = EditorGUILayout.TextField("Rename to", m_RenameTo);

            var names = new List<string>();
            for (int i = 0; i < callers.Count; i++)
            {
                string where = callers[i].context != null ? callers[i].context.name : "?";
                if (!names.Contains(where))
                    names.Add(where);
            }
            EditorGUILayout.LabelField("moves with it: " + string.Join(", ", names),
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            bool valid = !string.IsNullOrEmpty(m_RenameTo) && m_RenameTo != current;
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Rename everywhere", GUILayout.Width(150f)))
                {
                    string from = current;
                    string to = m_RenameTo;
                    int moved = ServiceKeyRename.Apply(target, () => set(to), from, to);
                    Debug.Log("[ServiceDef] '" + from + "' → '" + to + "', with " + moved
                        + " caller asset(s) repointed.", target);
                    m_RenamingKey = null;
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("Cancel", GUILayout.Width(70f)))
                m_RenamingKey = null;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void CallersButton(string key)
        {
            if (!GUILayout.Button(new GUIContent("⛓", "Who names this?"), GUILayout.Width(24f)))
                return;

            List<AssetWireScan.WireUse> callers = ServiceKeyRename.Callers(key);
            IReadOnlyList<string> inCode = ServiceKeyCode.Owners(key);
            var menu = new GenericMenu();

            for (int i = 0; i < callers.Count; i++)
            {
                AssetWireScan.WireUse use = callers[i];
                UnityEngine.Object target = use.context;
                menu.AddItem(new GUIContent(use.description.Replace('/', '∕')),
                    false, () => EditorGUIUtility.PingObject(target));
            }

            // THE TWO KINDS OF NAMER, told apart, because only one of them can be renamed from
            // here: an asset moves with a rename, a constant is the source of the name.
            if (inCode.Count > 0)
            {
                if (callers.Count > 0)
                    menu.AddSeparator("");
                for (int i = 0; i < inCode.Count; i++)
                    menu.AddDisabledItem(new GUIContent("declared in C# · " + inCode[i]));
            }

            if (callers.Count == 0 && inCode.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("nothing authored or declared names '"
                    + key + "'"));
                menu.AddDisabledItem(new GUIContent("(a skin binding it by hand does not scan)"));
            }
            menu.ShowAsContext();
        }

        private string m_RenamingKey;

        private string m_RenameTo = "";

        private static System.Type ResolveServiceType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            foreach (System.Type type in
                TypeCache.GetTypesDerivedFrom<StateTreeService>())
            {
                if (type.Name == typeName || type.FullName == typeName)
                    return type;
            }
            return null;
        }

        // ---- the screen surface (§4g): the widget, not states --------------------------

        /// <summary>What this subsystem's spawned skins can DO — their declared verbs and
        /// public fields, read from the prefab, so the def's visual is the WIDGET.</summary>
        private static void DrawScreenSurface(ServiceDef def)
        {
            if (def.spawns == null || def.spawns.Count == 0)
                return;
            var drewHeader = false;
            for (int i = 0; i < def.spawns.Count; i++)
            {
                var spawn = def.spawns[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.entryName))
                    continue;
                GameObject prefab = SpawnPrefab(spawn.entryName);
                if (prefab == null)
                    continue;
                UiViewBehaviour[] views = prefab.GetComponentsInChildren<UiViewBehaviour>(true);
                for (int v = 0; v < views.Length; v++)
                {
                    if (views[v] == null)
                        continue;
                    if (!drewHeader)
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField("Screen — spawned skins",
                            EditorStyles.boldLabel);
                        drewHeader = true;
                    }
                    System.Type type = views[v].GetType();
                    EditorGUILayout.LabelField("  " + spawn.entryName + " · " + type.Name,
                        EditorStyles.miniLabel);

                    var verbs = (UiVerbContractAttribute[])type.GetCustomAttributes(
                        typeof(UiVerbContractAttribute), true);
                    if (verbs.Length > 0)
                    {
                        var text = new System.Text.StringBuilder("      verbs: ");
                        for (int k = 0; k < verbs.Length; k++)
                        {
                            if (k > 0)
                                text.Append(", ");
                            text.Append(verbs[k].verb);
                            if (!string.IsNullOrEmpty(verbs[k].argumentHint))
                                text.Append('(').Append(verbs[k].argumentHint).Append(')');
                        }
                        EditorGUILayout.LabelField(text.ToString(),
                            EditorStyles.wordWrappedMiniLabel);
                    }

                    var fields = type.GetFields(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly);
                    if (fields.Length > 0)
                    {
                        var text = new System.Text.StringBuilder("      fields: ");
                        for (int k = 0; k < fields.Length; k++)
                        {
                            if (k > 0)
                                text.Append(", ");
                            text.Append(fields[k].Name);
                        }
                        EditorGUILayout.LabelField(text.ToString(),
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        /// <summary>The prefab behind a spawned UI row name — found through the UI
        /// registries, because the def's own registry is the DOMAIN's.</summary>
        /// <summary>UI row name → prefab, remembered until the project changes: this is asked
        /// once per reaction row per repaint, and an asset search per ask was a visible part of
        /// the inspector's second.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, GameObject> s_SpawnPrefabs =
            new System.Collections.Generic.Dictionary<string, GameObject>();

        [InitializeOnLoadMethod]
        private static void ForgetSpawnPrefabsOnProjectChange()
        {
            EditorApplication.projectChanged += s_SpawnPrefabs.Clear;
        }

        private static GameObject SpawnPrefab(string rowName)
        {
            if (string.IsNullOrEmpty(rowName))
                return null;
            if (s_SpawnPrefabs.TryGetValue(rowName, out GameObject remembered) && remembered != null)
                return remembered;
            GameObject found = FindSpawnPrefab(rowName);
            s_SpawnPrefabs[rowName] = found;
            return found;
        }

        private static GameObject FindSpawnPrefab(string rowName)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(UiRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<UiRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                var row = registry != null ? registry.FindByName(rowName) as UiDef : null;
                if (row != null && row.prefab != null)
                    return row.prefab;
            }
            return null;
        }
    }
}
