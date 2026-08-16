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
            DrawPropertiesExcluding(serializedObject, "m_Script", "serviceTypeName",
                "requests", "spawns", "announcements");
            serializedObject.ApplyModifiedProperties();

            DrawServiceType(def);
            DrawRequests(def);
            DrawSpawns(def);
            DrawAnnouncements(def);

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

        // ---- the service type: who runs this def ---------------------------------------

        private void DrawServiceType(ServiceDef def)
        {
            EditorGUILayout.BeginHorizontal();
            string typed = EditorGUILayout.DelayedTextField(new GUIContent("Service Type",
                "The service class that runs this def — the source of the action "
                + "vocabulary below."), def.serviceTypeName);
            if (typed != def.serviceTypeName)
                Commit(() => def.serviceTypeName = typed);
            if (GUILayout.Button("▾", GUILayout.Width(22f)))
            {
                var menu = new GenericMenu();
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

        // ---- requests ------------------------------------------------------------------

        private void DrawRequests(ServiceDef def)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Requests — what it answers to",
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
                string action = EditorGUILayout.DelayedTextField(new GUIContent("Action",
                    "The domain verb the service interprets — picked from its declared "
                    + "vocabulary."), row.action);
                if (action != row.action)
                    Commit(() => row.action = action);
                ActionPicker(def, row);
                EditorGUILayout.EndHorizontal();

                string stateId = EditorGUILayout.DelayedTextField(new GUIContent("State Id",
                    "Only for a handler that WAITS — routes to the def's flow tree."),
                    row.stateId);
                if (stateId != row.stateId)
                    Commit(() => row.stateId = stateId);

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
                string verb = EditorGUILayout.DelayedTextField("Verb", beat.verb);
                if (verb != beat.verb)
                    Commit(() => beat.verb = verb);
                VerbPicker(beat);
                EditorGUILayout.EndHorizontal();

                bool valueArgument = EditorGUILayout.Toggle(new GUIContent("Value Argument",
                    "Pass the request's value as the verb's argument."),
                    beat.valueArgument);
                if (valueArgument != beat.valueArgument)
                    Commit(() => beat.valueArgument = valueArgument);

                EditorGUILayout.BeginHorizontal();
                string argumentKey = EditorGUILayout.DelayedTextField(
                    new GUIContent("Argument Key", "A blackboard key whose held value "
                        + "rides along — an announcement's payload travels whole."),
                    beat.argumentKey);
                if (argumentKey != beat.argumentKey)
                    Commit(() => beat.argumentKey = argumentKey);
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
                string payload = EditorGUILayout.DelayedTextField("Payload Type",
                    announced.payloadTypeName);
                if (payload != announced.payloadTypeName)
                    Commit(() => announced.payloadTypeName = payload);
                string description = EditorGUILayout.DelayedTextField("Description",
                    announced.description);
                if (description != announced.description)
                    Commit(() => announced.description = description);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ announcement", GUILayout.Width(120f)))
                Commit(() => def.announcements.Add(new ServiceAnnouncement()));
        }

        // ---- the pickers: offers from declared contracts -------------------------------

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
