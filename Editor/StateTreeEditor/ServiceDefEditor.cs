using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

        public override void OnInspectorGUI()
        {
            var def = (ServiceDef)target;

            serializedObject.Update();
            // The identity every service has. The FLOW-BACKED half (tree kind, flows,
            // nesting rules, kind seeds) is folded away below: it is meaningful only for
            // a subsystem whose handlers wait, and on a def-only one it is four empty
            // fields pretending to be part of the declaration.
            DrawPropertiesExcluding(serializedObject, "m_Script", "serviceTypeName",
                "requests", "spawns", "announcements", "implements", "attributes", "settings",
                "treeKind", "flows", "nestingRules", "kindSeeds");
            serializedObject.ApplyModifiedProperties();

            DrawServiceType(def);
            DrawSettings(def);
            DrawImplements(def);
            DrawAttributes(def);
            DrawRequests(def);
            DrawDerived(def);
            DrawSpawns(def);
            DrawAnnouncements(def);
            DrawFlowBacked(def);

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Subsystem APIs…", GUILayout.Width(140f)))
                SubsystemApisWindow.Open();

            DrawScreenSurface(def);
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

        // ---- implements: the promises this def keeps ------------------------------------

        /// <summary>
        /// WHAT THIS DEF PROMISES TO BE (M30.2b), and whether it delivers.
        ///
        /// A claim is a LINK — picked from the contracts this def's catalogs declare, shown locked,
        /// changed from the ▾. That is the same bargain every wired field in this toolset offers,
        /// and it is why the name here follows the contract being renamed instead of quietly
        /// pointing at nothing.
        ///
        /// The line UNDER each claim is the point of contracts existing: what the def owes and has
        /// not delivered. A promise nobody checks is a label, so it is checked here, where it is
        /// made — and where a missing request can be served with one button, because the row it
        /// wants is the row the contract already named.
        /// </summary>
        private void DrawImplements(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent("Implements",
                "The contracts this def claims to keep. Fields elsewhere can then ask for the "
                + "promise instead of naming this def."), EditorStyles.boldLabel);

            StateTreeOffers.ContractsFor(def, m_Contracts);

            for (int i = 0; i < def.implements.Count; i++)
            {
                StateTreeEntryRef<ContractDef> claim = def.implements[i];
                if (claim == null)
                    continue;
                ContractDef contract = Resolve(claim);

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(new GUIContent("Keeps",
                        contract != null ? contract.Describe()
                            : "This contract is not in any catalog this def declares."),
                        string.IsNullOrEmpty(claim.entryName) ? "(none)" : claim.entryName);
                }
                int index = i;
                if (GUILayout.Button("▾", GUILayout.Width(22f)))
                    ShowContractMenu(def, index);
                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                {
                    Commit(() => def.implements.RemoveAt(index));
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                if (contract == null)
                {
                    EditorGUILayout.HelpBox("Nothing this def declares holds a contract called '"
                        + claim.entryName + "'. Add its catalog to the registry's Depends On, or "
                        + "the claim points at a promise nobody can read.", MessageType.Warning);
                    continue;
                }

                StateTreeContracts.Missing(def, contract, m_Missing);
                if (m_Missing.Count == 0)
                    continue;

                EditorGUILayout.HelpBox("Claimed but not delivered: "
                    + string.Join(", ", m_Missing), MessageType.Warning);
                // ONE BUTTON FOR THE HALF THAT IS AUTHORABLE HERE. A missing request is a row this
                // def is free to add; a missing attribute lives in a catalog and is somebody
                // else's edit, so it is reported and not offered.
                for (int r = 0; r < contract.requests.Count; r++)
                {
                    string wanted = contract.requests[r];
                    if (string.IsNullOrEmpty(wanted) || def.RequestFor(wanted) != null)
                        continue;
                    if (!GUILayout.Button("Serve '" + wanted + "'", GUILayout.Width(160f)))
                        continue;
                    Commit(() => def.requests.Add(new ServiceRequest
                    {
                        key = wanted,
                        description = "Promised by the '" + contract.name + "' contract."
                    }));
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("+ Implement…", GUILayout.Width(140f)))
                ShowContractMenu(def, -1);
        }

        /// <summary>The contracts this def can name — its declared neighbourhood, never the
        /// project's. Index -1 adds a claim; anything else replaces one.</summary>
        private void ShowContractMenu(ServiceDef def, int index)
        {
            var menu = new GenericMenu();
            if (m_Contracts.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(def.registry == null
                    ? "this def manages no catalog — nothing to read contracts from"
                    : "'" + def.registry.name + "' declares no contract catalog"));
            }
            for (int i = 0; i < m_Contracts.Count; i++)
            {
                ContractDef contract = m_Contracts[i];
                bool claimed = StateTreeContracts.Claims(def, contract);
                menu.AddItem(new GUIContent(contract.name), claimed, () => Commit(() =>
                {
                    var claim = new StateTreeEntryRef<ContractDef>
                    {
                        entryId = contract.id,
                        entryName = contract.name
                    };
                    if (index >= 0 && index < def.implements.Count)
                        def.implements[index] = claim;
                    else if (!claimed)
                        def.implements.Add(claim);
                }));
            }
            menu.ShowAsContext();
        }

        /// <summary>The contract behind a claim, by id first — so renaming the contract renames
        /// the claim instead of breaking it.</summary>
        private ContractDef Resolve(StateTreeEntryRef<ContractDef> claim)
        {
            for (int i = 0; i < m_Contracts.Count; i++)
            {
                if (!string.IsNullOrEmpty(claim.entryId) && m_Contracts[i].id == claim.entryId)
                {
                    if (m_Contracts[i].name != claim.entryName)
                        Commit(() => claim.entryName = m_Contracts[i].name);
                    return m_Contracts[i];
                }
            }
            for (int i = 0; i < m_Contracts.Count; i++)
            {
                if (!string.IsNullOrEmpty(claim.entryName) && m_Contracts[i].name == claim.entryName)
                    return m_Contracts[i];
            }
            return null;
        }

        private readonly List<ContractDef> m_Contracts = new List<ContractDef>();
        private readonly List<string> m_Missing = new List<string>();

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

        private void DrawRequests(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Requests — what OTHER systems may ask",
                EditorStyles.boldLabel);
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                ServiceRequest keyRow = row;
                DrawKey("Key", "What callers write to ask for this.", row.key,
                    value => keyRow.key = value);
                if (RemoveButton())
                {
                    int index = i;
                    Commit(() => def.requests.RemoveAt(index));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                DrawRenamePanel(row.key, value => keyRow.key = value);

                string description = EditorGUILayout.DelayedTextField("Description",
                    row.description);
                if (description != row.description)
                    Commit(() => row.description = description);

                var registry = (StateTreeRegistryAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Names Row Of", "The value names a row of this "
                        + "registry — typed callers are validated against it."),
                    row.namesRowOf, typeof(StateTreeRegistryAsset), false);
                if (registry != row.namesRowOf)
                    Commit(() => row.namesRowOf = registry);

                EditorGUILayout.BeginHorizontal();
                BoundTextField("Action",
                    "The domain verb the service interprets — picked from its declared "
                    + "vocabulary.",
                    row.action,
                    !string.IsNullOrEmpty(row.action)
                        && Offered(ActionOffers(def), row.action),
                    value => row.action = value);
                ActionPicker(def, row);
                EditorGUILayout.EndHorizontal();

                // Only where it can mean something: a def with no flow tree has nowhere
                // for a stateId to point, so the field would be a question with no answers.
                if (FlowBacked(def))
                {
                    string stateId = EditorGUILayout.DelayedTextField(
                        new GUIContent("State Id",
                            "Only for a handler that WAITS — routes to the def's flow tree."),
                        row.stateId);
                    if (stateId != row.stateId)
                        Commit(() => row.stateId = stateId);
                }

                DrawReactions(def, row);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ request", GUILayout.Width(90f)))
                Commit(() => def.requests.Add(new ServiceRequest()));
        }

        private void DrawReactions(ServiceDef def, ServiceRequest row)
        {
            EditorGUILayout.LabelField("Reactions — the UI beats, in order",
                EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            for (int j = 0; j < row.reactions.Count; j++)
            {
                UiReaction beat = row.reactions[j];
                if (beat == null)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Ui Row");
                if (GUILayout.Button(string.IsNullOrEmpty(beat.ui.entryName)
                    ? "(pick ui row)" : beat.ui.entryName, EditorStyles.popup))
                {
                    UiRowMenu(beat.ui.entryName, (id, rowName) =>
                    {
                        beat.ui.entryId = id;
                        beat.ui.entryName = rowName;
                    });
                }
                if (RemoveButton())
                {
                    int index = j;
                    Commit(() => row.reactions.RemoveAt(index));
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                BoundTextField("Verb", "The verb, in the view's vocabulary.",
                    beat.verb,
                    !string.IsNullOrEmpty(beat.verb)
                        && Offered(VerbOffers(beat.ui.entryName), beat.verb),
                    value => beat.verb = value);
                VerbPicker(beat);
                EditorGUILayout.EndHorizontal();

                bool valueArgument = EditorGUILayout.Toggle(new GUIContent("Value Argument",
                    "Pass the request's value as the verb's argument."),
                    beat.valueArgument);
                if (valueArgument != beat.valueArgument)
                    Commit(() => beat.valueArgument = valueArgument);

                EditorGUILayout.BeginHorizontal();
                BoundTextField("Argument Key",
                    "A blackboard key whose held value rides along — an announcement's "
                    + "payload travels whole.",
                    beat.argumentKey,
                    !string.IsNullOrEmpty(beat.argumentKey)
                        && Offered(AnnouncementKeys(def), beat.argumentKey),
                    value => beat.argumentKey = value);
                ArgumentKeyPicker(def, beat);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2f);
            }
            EditorGUI.indentLevel--;
            if (GUILayout.Button("+ beat", GUILayout.Width(70f)))
                Commit(() => row.reactions.Add(new UiReaction()));
        }

        // ---- spawns --------------------------------------------------------------------

        private void DrawSpawns(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Spawns — the screen it owns",
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
            if (GUILayout.Button("+ spawn", GUILayout.Width(80f)))
                Commit(() => def.spawns.Add(new StateTreeEntryRef<UiDef>()));
        }

        // ---- announcements -------------------------------------------------------------

        private void DrawAnnouncements(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Announcements — what it writes for others",
                EditorStyles.boldLabel);
            for (int i = 0; i < def.announcements.Count; i++)
            {
                ServiceAnnouncement announced = def.announcements[i];
                if (announced == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                ServiceAnnouncement keyed = announced;
                DrawKey("Key", "The name others read this under.", announced.key,
                    value => keyed.key = value);
                if (RemoveButton())
                {
                    int index = i;
                    Commit(() => def.announcements.RemoveAt(index));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                DrawRenamePanel(announced.key, value => keyed.key = value);
                EditorGUILayout.BeginHorizontal();
                BoundTextField("Payload Type",
                    "The contract class this key carries — the project already declares "
                    + "these ([TaskOutputContract] payloads).",
                    announced.payloadTypeName,
                    !string.IsNullOrEmpty(announced.payloadTypeName)
                        && Offered(PayloadTypeOffers(), announced.payloadTypeName),
                    value => announced.payloadTypeName = value);
                PayloadTypePicker(announced);
                EditorGUILayout.EndHorizontal();
                string description = EditorGUILayout.DelayedTextField("Description",
                    announced.description);
                if (description != announced.description)
                    Commit(() => announced.description = description);

                DrawDeliveries(def, announced);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ announcement", GUILayout.Width(120f)))
                Commit(() => def.announcements.Add(new ServiceAnnouncement()));
        }

        /// <summary>
        /// WHO IS TOLD (M34.2) — the wiring an announcement was missing.
        ///
        /// An announcement is a name this subsystem writes; a reaction on one of its requests is
        /// what carries it to a screen. Both are rows on this def, and until now connecting them
        /// meant scrolling up, finding the right request, adding a beat and retyping the key.
        /// Here the announcement shows what already delivers it and offers to add one — which is
        /// the "wire it by picking" half of a device panel.
        /// </summary>
        private void DrawDeliveries(ServiceDef def, ServiceAnnouncement announced)
        {
            if (string.IsNullOrEmpty(announced.key))
                return;

            var delivered = 0;
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                for (int r = 0; row != null && r < row.reactions.Count; r++)
                {
                    UiReaction beat = row.reactions[r];
                    if (beat == null || beat.argumentKey != announced.key)
                        continue;
                    delivered++;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(12f);
                    GUILayout.Label("→ on '" + row.key + "', " + beat.ui.entryName + " · "
                        + beat.verb, EditorStyles.miniLabel);
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    {
                        ServiceRequest owner = row;
                        UiReaction going = beat;
                        Commit(() => owner.reactions.Remove(going));
                        EditorGUILayout.EndHorizontal();
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12f);
            if (delivered == 0)
            {
                // NOT A FAULT, and worth saying which kind of quiet it is: a payload read from
                // code (a bound skin, a task) needs no beat at all.
                GUILayout.Label("nothing on this def delivers it", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("+ deliver…", GUILayout.Width(90f)))
            {
                var menu = new GenericMenu();
                if (def.requests.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent("this def declares no requests to "
                        + "carry it"));
                }
                for (int i = 0; i < def.requests.Count; i++)
                {
                    ServiceRequest row = def.requests[i];
                    if (row == null || string.IsNullOrEmpty(row.key))
                        continue;
                    ServiceRequest chosen = row;
                    menu.AddItem(new GUIContent("when '" + row.key + "' is served"), false,
                        () => Commit(() => chosen.reactions.Add(new UiReaction
                        {
                            // THE PAYLOAD RIDES WHOLE: a beat that carries an announcement is
                            // not passing the request's value, so valueArgument stays off.
                            argumentKey = announced.key,
                            valueArgument = false,
                            verb = "announce"
                        })));
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ---- the flow-backed half, folded away --------------------------------------

        /// <summary>Whether this def uses a flow TREE at all — the only condition under
        /// which the tree kind, nesting rules and kind seeds mean anything.</summary>
        private static bool FlowBacked(ServiceDef def)
        {
            if (def.flows != null || !string.IsNullOrEmpty(def.treeKind))
                return true;
            for (int i = 0; i < def.requests.Count; i++)
            {
                if (def.requests[i] != null && !string.IsNullOrEmpty(def.requests[i].stateId))
                    return true;
            }
            return false;
        }

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

        private void DrawFlowBacked(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            bool backed = FlowBacked(def);
            m_FlowsOpen = EditorGUILayout.Foldout(m_FlowsOpen || backed,
                backed ? "Flow tree" : "Flow tree — none (handlers are single-frame)", true);
            if (!m_FlowsOpen)
                return;

            EditorGUI.indentLevel++;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flows"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("treeKind"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nestingRules"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("kindSeeds"));
            serializedObject.ApplyModifiedProperties();
            EditorGUI.indentLevel--;
        }

        private bool m_FlowsOpen;

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

        private static string[] VerbOffers(string rowName)
        {
            GameObject prefab = SpawnPrefab(rowName);
            if (prefab == null)
                return System.Array.Empty<string>();
            var offers = new System.Collections.Generic.List<string>();
            UiViewBehaviour[] views = prefab.GetComponentsInChildren<UiViewBehaviour>(true);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null)
                    continue;
                var contracts = (UiVerbContractAttribute[])views[i].GetType()
                    .GetCustomAttributes(typeof(UiVerbContractAttribute), true);
                for (int k = 0; k < contracts.Length; k++)
                    offers.Add(contracts[k].verb);
            }
            return offers.ToArray();
        }

        private static string[] AnnouncementKeys(ServiceDef def)
        {
            var offers = new System.Collections.Generic.List<string>();
            for (int i = 0; i < def.announcements.Count; i++)
            {
                if (def.announcements[i] != null
                    && !string.IsNullOrEmpty(def.announcements[i].key))
                    offers.Add(def.announcements[i].key);
            }
            return offers.ToArray();
        }

        /// <summary>Every payload class any task contract declares — the type names the
        /// project actually sends, which is what an announcement may carry.</summary>
        private static string[] PayloadTypeOffers()
        {
            var offers = new System.Collections.Generic.List<string>();
            foreach (System.Type type in TypeCache.GetTypesDerivedFrom<StateTreeTaskAsset>())
            {
                var contracts = (TaskOutputContractAttribute[])type.GetCustomAttributes(
                    typeof(TaskOutputContractAttribute), true);
                for (int i = 0; i < contracts.Length; i++)
                {
                    if (contracts[i].payloadType != null
                        && !offers.Contains(contracts[i].payloadType.Name))
                        offers.Add(contracts[i].payloadType.Name);
                }
            }
            return offers.ToArray();
        }

        // ---- the pickers: offers from declared contracts -------------------------------

        private void PayloadTypePicker(ServiceAnnouncement announced)
        {
            if (!GUILayout.Button("▾", GUILayout.Width(22f)))
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"),
                string.IsNullOrEmpty(announced.payloadTypeName),
                () => Commit(() => announced.payloadTypeName = ""));
            string[] offers = PayloadTypeOffers();
            if (offers.Length == 0)
                menu.AddDisabledItem(new GUIContent("no [TaskOutputContract] payloads yet"));
            for (int i = 0; i < offers.Length; i++)
            {
                string offer = offers[i];
                menu.AddItem(new GUIContent(offer), offer == announced.payloadTypeName,
                    () => Commit(() => announced.payloadTypeName = offer));
            }
            menu.ShowAsContext();
        }

        private void ActionPicker(ServiceDef def, ServiceRequest row)
        {
            if (!GUILayout.Button("▾", GUILayout.Width(22f)))
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(row.action),
                () => Commit(() => row.action = ""));
            System.Type type = ResolveServiceType(def.serviceTypeName);
            if (type == null)
            {
                menu.AddDisabledItem(new GUIContent("set Service Type for the vocabulary"));
            }
            else
            {
                var contracts = (ServiceActionContractAttribute[])type.GetCustomAttributes(
                    typeof(ServiceActionContractAttribute), true);
                if (contracts.Length == 0)
                    menu.AddDisabledItem(new GUIContent(type.Name
                        + " declares no [ServiceActionContract]"));
                for (int i = 0; i < contracts.Length; i++)
                {
                    string action = contracts[i].action;
                    string label = string.IsNullOrEmpty(contracts[i].valueHint)
                        ? action
                        : action + " — " + contracts[i].valueHint;
                    menu.AddItem(new GUIContent(label), action == row.action,
                        () => Commit(() => row.action = action));
                }
            }
            menu.ShowAsContext();
        }

        private void VerbPicker(UiReaction beat)
        {
            if (!GUILayout.Button("▾", GUILayout.Width(22f)))
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(beat.verb),
                () => Commit(() => beat.verb = ""));
            var any = false;
            GameObject prefab = SpawnPrefab(beat.ui.entryName);
            if (prefab != null)
            {
                UiViewBehaviour[] views = prefab.GetComponentsInChildren<UiViewBehaviour>(true);
                for (int i = 0; i < views.Length; i++)
                {
                    if (views[i] == null)
                        continue;
                    var contracts = (UiVerbContractAttribute[])views[i].GetType()
                        .GetCustomAttributes(typeof(UiVerbContractAttribute), true);
                    for (int k = 0; k < contracts.Length; k++)
                    {
                        any = true;
                        string verb = contracts[k].verb;
                        string label = string.IsNullOrEmpty(contracts[k].argumentHint)
                            ? verb
                            : verb + " — " + contracts[k].argumentHint;
                        menu.AddItem(new GUIContent(label), verb == beat.verb,
                            () => Commit(() => beat.verb = verb));
                    }
                }
            }
            if (!any)
                menu.AddDisabledItem(new GUIContent(
                    "pick a UI row whose views declare [UiVerbContract]"));
            menu.ShowAsContext();
        }

        private void ArgumentKeyPicker(ServiceDef def, UiReaction beat)
        {
            if (!GUILayout.Button("▾", GUILayout.Width(22f)))
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(beat.argumentKey),
                () => Commit(() => beat.argumentKey = ""));
            var any = false;
            for (int i = 0; i < def.announcements.Count; i++)
            {
                ServiceAnnouncement announced = def.announcements[i];
                if (announced == null || string.IsNullOrEmpty(announced.key))
                    continue;
                any = true;
                string key = announced.key;
                string label = string.IsNullOrEmpty(announced.payloadTypeName)
                    ? key
                    : key + " : " + announced.payloadTypeName;
                menu.AddItem(new GUIContent(label), key == beat.argumentKey,
                    () => Commit(() => beat.argumentKey = key));
            }
            if (!any)
                menu.AddDisabledItem(new GUIContent("no announcements declared"));
            menu.ShowAsContext();
        }

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
        private static GameObject SpawnPrefab(string rowName)
        {
            if (string.IsNullOrEmpty(rowName))
                return null;
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
