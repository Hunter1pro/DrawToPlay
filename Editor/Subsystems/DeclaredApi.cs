using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHAT THE PROJECT'S SUBSYSTEMS DECLARE, as dropdown choices (M38.1) — the defs read once
    /// and remembered until the project changes, so a node's ports can be defined from them
    /// without an asset walk per keystroke.
    ///
    /// This is the Subsystem APIs window's knowledge handed to the graph: which subsystems
    /// exist, what each can be asked, what each announces, which UI rows there are and what
    /// each row's skin answers to. A new def is in every list the moment it exists; nothing is
    /// generated.
    /// </summary>
    public static class DeclaredApi
    {
        /// <summary>The choice a dropdown offers for "nothing picked" — always first.</summary>
        public const string None = "";

        private static List<ServiceDef> s_Subsystems;
        private static List<UiDef> s_UiRows;
        private static Dictionary<string, GameObject> s_UiPrefabs;

        [InitializeOnLoadMethod]
        private static void ForgetOnProjectChange()
        {
            EditorApplication.projectChanged += Forget;
        }

        public static void Forget()
        {
            s_Subsystems = null;
            s_UiRows = null;
            s_UiPrefabs = null;
        }

        // ---- subsystems ---------------------------------------------------------------------

        /// <summary>Every def that names a class — a subsystem, not a kind — by asset name, which
        /// is the one name two bags in two demos cannot share.</summary>
        public static List<string> Subsystems()
        {
            var choices = new List<string> { None };
            foreach (ServiceDef def in AllSubsystems())
                choices.Add(def.name);
            return choices;
        }

        public static ServiceDef Subsystem(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;
            foreach (ServiceDef def in AllSubsystems())
            {
                if (def.name == defName)
                    return def;
            }
            return null;
        }

        /// <summary>The request keys a subsystem serves — what an Ask may pick.</summary>
        public static List<string> RequestKeys(string defName)
        {
            var choices = new List<string> { None };
            ServiceDef def = Subsystem(defName);
            for (int i = 0; def != null && i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row != null && !string.IsNullOrEmpty(row.key)
                    && !choices.Contains(row.key))
                    choices.Add(row.key);
            }
            return choices;
        }

        /// <summary>The request row behind a key on a subsystem, or null.</summary>
        public static ServiceRequest Request(string defName, string key)
        {
            ServiceDef def = Subsystem(defName);
            for (int i = 0; def != null && !string.IsNullOrEmpty(key) && i < def.requests.Count; i++)
            {
                if (def.requests[i] != null && def.requests[i].key == key)
                    return def.requests[i];
            }
            return null;
        }

        /// <summary>What a request's VALUE may be: the rows of the catalog it names rows of, or
        /// an empty list when the value is free text.</summary>
        public static List<string> ValueChoices(string defName, string key)
        {
            var choices = new List<string>();
            ServiceRequest row = Request(defName, key);
            StateTreeRegistryAsset catalog = row != null ? row.namesRowOf : null;
            if (catalog == null || catalog.Count == 0)
                return choices;
            choices.Add(None);
            for (int i = 0; i < catalog.Count; i++)
            {
                StateTreeRegistryEntry entry = catalog.EntryAt(i);
                if (entry != null && !string.IsNullOrEmpty(entry.name) && !choices.Contains(entry.name))
                    choices.Add(entry.name);
            }
            return choices;
        }

        /// <summary>
        /// The announcement keys a subsystem makes — what a When may wait on. FROM THE CLASS
        /// (M41.1): every <see cref="ServiceAnnouncementAttribute"/> it declares, plus the key of
        /// every contract one of its actions answers with — the def types none of it.
        /// </summary>
        public static List<string> AnnouncementKeys(string defName)
        {
            var choices = new List<string> { None };
            foreach (Announced row in Announcements(defName))
            {
                if (!choices.Contains(row.key))
                    choices.Add(row.key);
            }
            return choices;
        }

        /// <summary>A payload type as a designer reads it: a contract by its name, a number or
        /// text by what it is, nothing for a bare signal.</summary>
        public static string PayloadLabel(Type payload)
        {
            if (payload == null) return "";
            if (payload == typeof(float) || payload == typeof(int) || payload == typeof(double)) return "number";
            if (payload == typeof(string)) return "text";
            if (payload == typeof(bool)) return "yes/no";
            return payload.Name;
        }

        /// <summary>One thing a subsystem announces: the key, what rides with it, and what it means.</summary>
        public readonly struct Announced
        {
            public readonly string key;
            public readonly Type payload;
            public readonly string description;

            public Announced(string key, Type payload, string description)
            {
                this.key = key;
                this.payload = payload;
                this.description = description;
            }
        }

        /// <summary>Everything a subsystem announces, read from its class — the declared
        /// announcements first, then the answer contracts of its actions (under each contract's
        /// own key). The order the API window lists them in.</summary>
        public static List<Announced> Announcements(string defName)
        {
            var rows = new List<Announced>();
            ServiceDef def = Subsystem(defName);
            Type type = def != null ? def.serviceType : null;
            if (type == null)
                return rows;
            foreach (ServiceAnnouncementAttribute declared in
                type.GetCustomAttributes(typeof(ServiceAnnouncementAttribute), true))
            {
                if (!string.IsNullOrEmpty(declared.key) && rows.FindIndex(r => r.key == declared.key) < 0)
                    rows.Add(new Announced(declared.key, declared.payload, declared.description));
            }
            foreach (ServiceActionContractAttribute contract in
                type.GetCustomAttributes(typeof(ServiceActionContractAttribute), true))
            {
                if (contract.answersWith == null)
                    continue;
                string key = ServiceContracts.KeyOf(contract.answersWith);
                if (!string.IsNullOrEmpty(key) && rows.FindIndex(r => r.key == key) < 0)
                    rows.Add(new Announced(key, contract.answersWith,
                        "what '" + contract.action + "' came to"));
            }
            return rows;
        }

        // ---- targets (M38.2) ------------------------------------------------------------------

        /// <summary>The contract a request ANSWERS WITH — what its action's
        /// <see cref="ServiceActionContractAttribute.answersWith"/> names — or null.</summary>
        public static Type AnswerOf(string defName, string key)
        {
            ServiceDef def = Subsystem(defName);
            ServiceRequest row = Request(defName, key);
            Type type = def != null ? def.serviceType : null;
            if (type == null || row == null || string.IsNullOrEmpty(row.action))
                return null;
            foreach (ServiceActionContractAttribute contract in
                type.GetCustomAttributes(typeof(ServiceActionContractAttribute), true))
            {
                if (contract.action == row.action)
                    return contract.answersWith;
            }
            return null;
        }

        /// <summary>The contract an announcement carries — from the class (M41.1) — or null for
        /// a bare payload (a number, a name).</summary>
        public static Type PayloadOf(string defName, string announcementKey)
        {
            foreach (Announced row in Announcements(defName))
            {
                if (row.key == announcementKey)
                    return row.payload != null && !row.payload.IsPrimitive && row.payload != typeof(string)
                        ? row.payload
                        : null;
            }
            return null;
        }

        /// <summary>The fields of a contract a reader may pick — "" first, for the whole payload.</summary>
        public static List<string> FieldChoices(Type payloadType)
        {
            var choices = new List<string>();
            IReadOnlyList<System.Reflection.FieldInfo> fields = ServiceContracts.ExposedFields(payloadType);
            if (fields.Count == 0)
                return choices;
            choices.Add(None);
            for (int i = 0; i < fields.Count; i++)
                choices.Add(fields[i].Name);
            return choices;
        }

        /// <summary>Whether a contract field reads as a NUMBER (numbers and bools, which read as
        /// 1/0) rather than text.</summary>
        public static bool IsNumeric(Type payloadType, string field)
        {
            IReadOnlyList<System.Reflection.FieldInfo> fields = ServiceContracts.ExposedFields(payloadType);
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Name != field)
                    continue;
                Type t = fields[i].FieldType;
                return t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(double) || t == typeof(long);
            }
            return false;
        }

        // ---- screens ------------------------------------------------------------------------

        /// <summary>Every UI row in the project, by name — a screen is project-wide.</summary>
        public static List<string> UiRows()
        {
            var choices = new List<string> { None };
            foreach (UiDef row in AllUiRows())
            {
                if (!choices.Contains(row.name))
                    choices.Add(row.name);
            }
            return choices;
        }

        public static UiDef UiRow(string rowName)
        {
            if (string.IsNullOrEmpty(rowName))
                return null;
            foreach (UiDef row in AllUiRows())
            {
                if (row.name == rowName)
                    return row;
            }
            return null;
        }

        /// <summary>The verbs a row's skins declare (<see cref="UiVerbContractAttribute"/>) —
        /// what a Say To may pick. Empty when the row has no prefab or its skins declare none.</summary>
        public static List<string> Verbs(string rowName)
        {
            var choices = new List<string>();
            UiDef row = UiRow(rowName);
            GameObject prefab = row != null ? row.prefab : null;
            if (prefab == null)
                return choices;
            choices.Add(None);
            foreach (UiViewBehaviour skin in prefab.GetComponentsInChildren<UiViewBehaviour>(true))
            {
                foreach (UiVerbContractAttribute contract in
                    skin.GetType().GetCustomAttributes(typeof(UiVerbContractAttribute), true))
                {
                    if (!string.IsNullOrEmpty(contract.verb) && !choices.Contains(contract.verb))
                        choices.Add(contract.verb);
                }
            }
            return choices;
        }

        /// <summary>The parameters a row declares — what a Show grows pins for.</summary>
        public static IReadOnlyList<GraphTaskParameter> Parameters(string rowName)
        {
            UiDef row = UiRow(rowName);
            return row != null && row.parameters != null
                ? row.parameters
                : (IReadOnlyList<GraphTaskParameter>)Array.Empty<GraphTaskParameter>();
        }

        // ---- the reads ----------------------------------------------------------------------

        private static List<ServiceDef> AllSubsystems()
        {
            if (s_Subsystems != null)
                return s_Subsystems;
            s_Subsystems = new List<ServiceDef>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ServiceDef)))
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.serviceTypeName))
                    s_Subsystems.Add(def);
            }
            s_Subsystems.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return s_Subsystems;
        }

        private static List<UiDef> AllUiRows()
        {
            if (s_UiRows != null)
                return s_UiRows;
            s_UiRows = new List<UiDef>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(UiRegistry)))
            {
                var registry = AssetDatabase.LoadAssetAtPath<UiRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                for (int i = 0; registry != null && i < registry.entries.Count; i++)
                {
                    if (registry.entries[i] != null && !string.IsNullOrEmpty(registry.entries[i].name))
                        s_UiRows.Add(registry.entries[i]);
                }
            }
            return s_UiRows;
        }
    }
}
