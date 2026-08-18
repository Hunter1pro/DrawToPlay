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
                "requests", "spawns", "announcements", "implements",
                "treeKind", "flows", "nestingRules", "kindSeeds");
            serializedObject.ApplyModifiedProperties();

            DrawServiceType(def);
            DrawImplements(def);
            DrawRequests(def);
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
                    TypeCache.GetTypesDerivedFrom<StateTreeServiceBehaviour>())
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
                string key = EditorGUILayout.DelayedTextField("Key", row.key);
                if (key != row.key)
                    Commit(() => row.key = key);
                CallersButton(row);
                if (RemoveButton())
                {
                    int index = i;
                    Commit(() => def.requests.RemoveAt(index));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

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
                string key = EditorGUILayout.DelayedTextField("Key", announced.key);
                if (key != announced.key)
                    Commit(() => announced.key = key);
                if (RemoveButton())
                {
                    int index = i;
                    Commit(() => def.announcements.RemoveAt(index));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
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
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ announcement", GUILayout.Width(120f)))
                Commit(() => def.announcements.Add(new ServiceAnnouncement()));
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
        private static void CallersButton(ServiceRequest row)
        {
            if (!GUILayout.Button(new GUIContent("⛓", "Who writes this request?"),
                GUILayout.Width(24f)))
                return;

            AssetWireScan.Index index = AssetWireScan.Get();
            var menu = new GenericMenu();
            if (index.requestCallers.TryGetValue(row.key, out var callers)
                && callers.Count > 0)
            {
                for (int i = 0; i < callers.Count; i++)
                {
                    AssetWireScan.WireUse use = callers[i];
                    UnityEngine.Object target = use.context;
                    menu.AddItem(new GUIContent(use.description.Replace('/', '∕')),
                        false, () => EditorGUIUtility.PingObject(target));
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("no authored caller writes '"
                    + row.key + "'"));
                menu.AddDisabledItem(new GUIContent("(skins and C# callers do not scan)"));
            }
            menu.ShowAsContext();
        }

        private static System.Type ResolveServiceType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            foreach (System.Type type in
                TypeCache.GetTypesDerivedFrom<StateTreeServiceBehaviour>())
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
