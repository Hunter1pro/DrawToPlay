using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE SPINE, AS A TREE YOU CAN LOOK AT (M34) — root → level → player, live.
    ///
    /// The dependency map answers "what points at what" about ASSETS. This answers the other
    /// half, the one Godot's scene dock answers for free and we had nowhere: what is actually UP
    /// right now — which scopes exist, what each one installed, what its tree is doing, and what
    /// is on its board. Until now the only way to know was to read the installer list and guess.
    ///
    /// It is deliberately read-only and deliberately live: this is the window you keep open
    /// while playing, and every line of it is something the running game already knows about
    /// itself. Selecting a scope pings its object; a subsystem shows its def and can reveal it.
    /// </summary>
    internal sealed class ScopeTreeWindow : EditorWindow
    {
        [MenuItem("Tools/Draw To Play/Scope Tree")]
        internal static void Open()
        {
            GetWindow<ScopeTreeWindow>("Scopes").Show();
        }

        private Vector2 m_Scroll;
        private string m_Filter = "";
        private bool m_ShowBoards = true;
        private bool m_ShowServices = true;
        private readonly HashSet<EntityId> m_Closed = new HashSet<EntityId>();

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_ShowServices = GUILayout.Toggle(m_ShowServices, "subsystems",
                EditorStyles.toolbarButton, GUILayout.Width(88f));
            m_ShowBoards = GUILayout.Toggle(m_ShowBoards, "boards",
                EditorStyles.toolbarButton, GUILayout.Width(64f));
            GUILayout.FlexibleSpace();
            m_Filter = GUILayout.TextField(m_Filter, EditorStyles.toolbarSearchField,
                GUILayout.Width(180f));
            EditorGUILayout.EndHorizontal();

            IReadOnlyList<StateTreeContextHost> hosts = StateTreeContextHost.registered;
            if (hosts.Count == 0)
            {
                EditorGUILayout.HelpBox(Application.isPlaying
                    ? "No scope is registered. Something is very wrong, or the session has not "
                        + "started yet."
                    : "Nothing is running. Enter play mode — this window shows the live spine.",
                    MessageType.Info);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < hosts.Count; i++)
            {
                StateTreeContextHost host = hosts[i];
                if (host != null && host.ParentHost == null)
                    DrawScope(host, 0, hosts);
            }
            // A scope whose parent is not registered (a level mid-unload) still deserves to be
            // seen rather than silently missing from the picture.
            for (int i = 0; i < hosts.Count; i++)
            {
                StateTreeContextHost host = hosts[i];
                if (host != null && host.ParentHost != null && !Contains(hosts, host.ParentHost))
                    DrawScope(host, 0, hosts);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawScope(StateTreeContextHost host, int depth,
            IReadOnlyList<StateTreeContextHost> all)
        {
            EntityId id = host.GetEntityId();
            bool open = !m_Closed.Contains(id);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 14f);
            bool nowOpen = EditorGUILayout.Foldout(open, GUIContent.none, true,
                EditorStyles.foldout);
            if (nowOpen != open)
            {
                if (nowOpen)
                    m_Closed.Remove(id);
                else
                    m_Closed.Add(id);
            }
            GUILayout.Space(-28f);

            var label = new GUIContent(host.kind + "  ·  " + host.name,
                "Click to select the object this scope lives on.");
            if (GUILayout.Button(label, EditorStyles.boldLabel, GUILayout.Width(240f)))
            {
                Selection.activeObject = host.gameObject;
                EditorGUIUtility.PingObject(host.gameObject);
            }

            // WHAT ITS TREE IS DOING, on the same line, because that is the question asked most:
            // a scope with a tree that is not running is the shape of most "nothing happens".
            GUILayout.Label(host.runningTree != null
                ? (host.isRunning
                    ? "▶ " + host.runningTree.name + " · " + host.activeNodeId
                    : "■ " + host.runningTree.name + " (stopped)")
                : "no tree", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (nowOpen)
            {
                if (m_ShowServices)
                    DrawSubsystems(host, depth + 1);
                if (m_ShowBoards)
                    DrawBoard(host, depth + 1);
            }

            for (int i = 0; i < all.Count; i++)
            {
                StateTreeContextHost child = all[i];
                if (child != null && child != host && child.ParentHost == host)
                    DrawScope(child, depth + 1, all);
            }
        }

        private void DrawSubsystems(StateTreeContextHost host, int depth)
        {
            IReadOnlyList<StateTreeService> subsystems = host.subsystems;
            var drew = 0;
            for (int i = 0; i < subsystems.Count; i++)
            {
                StateTreeService service = subsystems[i];
                if (service == null || !Matches(service.GetType().Name))
                    continue;
                drew++;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUILayout.Label("⚙ " + service.GetType().Name, GUILayout.Width(190f));
                ServiceDef def = service.definition;
                if (def != null && GUILayout.Button(new GUIContent(def.name,
                    "Reveal the def this subsystem runs."), EditorStyles.miniButton,
                    GUILayout.Width(150f)))
                {
                    Selection.activeObject = def;
                    EditorGUIUtility.PingObject(def);
                }

                // OUT AND BACK IN (M34.5), while everything around it keeps running — the thing
                // a per-scope container could not do, offered where you can see the result.
                StateTreeServiceInstaller installer = InstallerOf(host, def);
                if (installer != null && GUILayout.Button(new GUIContent("↺",
                    "Rebuild this subsystem now: its screens close, it is disposed, and a fresh "
                    + "one is installed from the same def."), GUILayout.Width(24f)))
                {
                    installer.Reinstall(def);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                DrawSettings(service, depth + 1);
            }

            // Provided instances that are NOT subsystems — the plain classes an installer or a
            // root handed over (the dialog, the save). They are part of what this scope is.
            foreach (KeyValuePair<Type, object> entry in host.provided)
            {
                if (entry.Value is StateTreeService || !Matches(entry.Key.Name))
                    continue;
                drew++;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUILayout.Label("· " + entry.Key.Name, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            if (drew == 0 && string.IsNullOrEmpty(m_Filter))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUILayout.Label("no subsystems", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>The installer on this scope that owns a def, or null — a subsystem built
        /// some other way has no handle to rebuild it by, and says so by having no button.</summary>
        /// <summary>
        /// WHAT IT IS TUNED TO, AND BY WHOM (M36.3): every declared setting's effective value
        /// with the layer it came from — code · def · install. "Why is the bench reach 6 here"
        /// is the question this window exists to answer, and a number with no provenance is a
        /// number somebody will change in the wrong place.
        /// </summary>
        private static void DrawSettings(StateTreeService service, int depth)
        {
            var declared = ServiceSettings.DeclaredOn(service.GetType());
            IReadOnlyDictionary<string, ServiceSettingSource> sources = service.settingSources;
            for (int i = 0; i < declared.Count; i++)
            {
                ServiceSettings.Declared knob = declared[i];
                object value = knob.field.GetValue(service);
                string from = sources != null && sources.TryGetValue(knob.name,
                    out ServiceSettingSource source)
                    ? source.ToString().ToLowerInvariant()
                    : "?";
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUILayout.Label(new GUIContent("· " + knob.name, knob.description),
                    EditorStyles.miniLabel, GUILayout.Width(190f));
                GUILayout.Label(Describe(value), EditorStyles.miniLabel, GUILayout.Width(120f));
                GUILayout.Label(new GUIContent(from, from == "code"
                        ? "The class default — nothing overrides it."
                        : from == "def"
                            ? "Set on the def, for every install of this kind."
                            : "Set on this scope's installer row, for this install alone."),
                    EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50f));
                EditorGUILayout.EndHorizontal();
            }
        }

        private static StateTreeServiceInstaller InstallerOf(StateTreeContextHost host,
            ServiceDef def)
        {
            if (def == null || host == null)
                return null;
            StateTreeServiceInstaller[] installers =
                host.GetComponents<StateTreeServiceInstaller>();
            for (int i = 0; i < installers.Length; i++)
            {
                IReadOnlyList<StateTreeSubsystem> held = installers[i].installed;
                for (int j = 0; j < held.Count; j++)
                {
                    if (held[j] != null && held[j].definition == def)
                        return installers[i];
                }
            }
            return null;
        }

        private void DrawBoard(StateTreeContextHost host, int depth)
        {
            StateTreeContext context = host.Context;
            if (context == null || context.blackboard.Count == 0)
                return;

            foreach (KeyValuePair<string, object> entry in context.blackboard)
            {
                if (!Matches(entry.Key))
                    continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUILayout.Label("· " + entry.Key, EditorStyles.miniLabel, GUILayout.Width(190f));
                GUILayout.Label(Describe(entry.Value), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>A board value in one line — a payload says what it IS, not its address.</summary>
        private static string Describe(object value)
        {
            switch (value)
            {
                case null: return "(null)";
                case string text: return "\"" + text + "\"";
                case float number: return number.ToString("0.###");
                case UnityEngine.Object asset: return asset.name;
                default: return value.GetType().Name;
            }
        }

        private bool Matches(string text)
        {
            return string.IsNullOrEmpty(m_Filter)
                || (text != null
                    && text.IndexOf(m_Filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool Contains(IReadOnlyList<StateTreeContextHost> hosts,
            StateTreeContextHost host)
        {
            for (int i = 0; i < hosts.Count; i++)
            {
                if (hosts[i] == host)
                    return true;
            }
            return false;
        }
    }
}
