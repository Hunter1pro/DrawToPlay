using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// OUR OWN GRAPH SURFACE (M30.6) — where a <see cref="TaskGraphDocument"/> is authored.
    ///
    /// It exists because the borrowed one could not say four things this toolset needs: a
    /// parameter made OUTSIDE a node and wired in (the left panel, and one button per parameter
    /// that drops its reader on the canvas), a value whose type is more than float/string/bool
    /// (§E's type model, offered from the catalogs this document declares), a return that means
    /// something to whoever calls it, and a graph that is worth reusing.
    ///
    /// The canvas is deliberately plain — boxes, pins, wires — because the interesting part is
    /// what a wire IS: two node ids and a pin, so moving, renaming and reordering nodes never
    /// breaks a connection. Indices exist only in the bake, and the bake is checked against the
    /// program the old surface produced.
    ///
    /// EVERY EDIT GOES THROUGH Undo AND RE-BAKES, so what is on disk beside the document is
    /// always what the canvas shows. A graph that had to be told to bake is a graph that ships
    /// stale.
    /// </summary>
    internal sealed class TaskGraphWindow : EditorWindow
    {
        [MenuItem("Tools/Draw To Play/Task Graph")]
        internal static void Open()
        {
            GetWindow<TaskGraphWindow>("Task Graph").Show();
        }

        [OnOpenAsset]
        internal static bool OpenDocument(int instanceId, int line)
        {
            var document = EditorUtility.EntityIdToObject(instanceId) as TaskGraphDocument;
            if (document == null)
                return false;
            var window = GetWindow<TaskGraphWindow>("Task Graph");
            window.m_Document = document;
            window.Rebake();
            window.Show();
            return true;
        }

        private const float k_NodeWidth = 200f;
        private const float k_Row = 16f;
        private const float k_Header = 24f;
        private const float k_Pin = 11f;

        [SerializeField] private TaskGraphDocument m_Document;
        [SerializeField] private Vector2 m_Pan;
        [SerializeField] private bool m_AutoBake = true;

        private string m_Selected;
        private string m_Dragging;
        private Vector2 m_LeftScroll;
        private Vector2 m_RightScroll;
        private readonly List<string> m_Problems = new List<string>();

        private bool m_WiringData;
        private bool m_WiringFromOutput;
        private string m_WiringNode;
        private int m_WiringPin;

        private void OnGUI()
        {
            DrawToolbar();
            if (m_Document == null)
            {
                EditorGUILayout.HelpBox("Pick a task graph document, or make one from "
                    + "Assets ▸ Create ▸ Draw To Play ▸ Task Graph.", MessageType.Info);
                return;
            }

            float top = EditorStyles.toolbar.fixedHeight;
            var left = new Rect(0f, top, 230f, position.height - top);
            var right = new Rect(position.width - 250f, top, 250f, position.height - top);
            var canvas = new Rect(left.xMax, top, right.xMin - left.xMax, position.height - top);

            DrawCanvas(canvas);
            DrawLeft(left);
            DrawRight(right);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var picked = (TaskGraphDocument)EditorGUILayout.ObjectField(m_Document,
                typeof(TaskGraphDocument), false, GUILayout.Width(220f));
            if (picked != m_Document)
            {
                m_Document = picked;
                m_Selected = null;
                Rebake();
            }

            if (m_Document != null)
            {
                if (GUILayout.Button("Bake", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                    Rebake(save: true);
                m_AutoBake = GUILayout.Toggle(m_AutoBake, "auto", EditorStyles.toolbarButton,
                    GUILayout.Width(44f));
                if (GUILayout.Button("+ node", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                    ShowPalette(new Vector2(120f, 80f) - m_Pan);
                GUILayout.Label(m_Problems.Count == 0
                    ? "bakes clean"
                    : m_Problems.Count + " problem(s) — see the panel",
                    EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ---- the canvas ----------------------------------------------------------------

        private void DrawCanvas(Rect canvas)
        {
            GUI.Box(canvas, GUIContent.none, EditorStyles.helpBox);
            GUI.BeginClip(canvas);
            Event current = Event.current;

            Handles.BeginGUI();
            for (int i = 0; i < m_Document.wires.Count; i++)
                DrawWire(m_Document.wires[i]);
            if (m_WiringNode != null)
            {
                Vector2 from = PinCentre(m_Document.Node(m_WiringNode), m_WiringPin, m_WiringData,
                    m_WiringFromOutput);
                Handles.DrawBezier(from, current.mousePosition, from + Vector2.right * 40f,
                    current.mousePosition + Vector2.left * 40f, Color.white, null, 2f);
                Repaint();
            }
            Handles.EndGUI();

            for (int i = 0; i < m_Document.nodes.Count; i++)
                DrawNode(m_Document.nodes[i]);

            // Empty canvas: pan with a drag, palette on a right-click, and a click clears the
            // selection so the right panel stops describing something nobody is looking at.
            if (current.type == EventType.MouseDrag && m_Dragging == null && m_WiringNode == null)
            {
                m_Pan += current.delta;
                current.Use();
                Repaint();
            }
            if (current.type == EventType.MouseUp)
            {
                m_Dragging = null;
                if (m_WiringNode != null)
                {
                    m_WiringNode = null;
                    Repaint();
                }
            }
            if (current.type == EventType.ContextClick)
            {
                ShowPalette(current.mousePosition - m_Pan);
                current.Use();
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Delete
                && !string.IsNullOrEmpty(m_Selected))
            {
                Remove(m_Selected);
                current.Use();
            }

            GUI.EndClip();
        }

        private void DrawNode(TaskGraphDocNode node)
        {
            Rect rect = NodeRect(node);
            Event current = Event.current;

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = node.id == m_Selected
                ? new Color(0.45f, 0.75f, 1f)
                : ColourOf(node);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.backgroundColor = previous;

            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, 18f),
                node.IsMarker ? "▶ " + node.entry : node.Label, EditorStyles.boldLabel);
            if (!node.IsMarker && !string.IsNullOrEmpty(node.title))
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + k_Header - 4f, rect.width - 16f, 14f),
                    node.kind.ToString(), EditorStyles.miniLabel);
            }

            if (!node.IsMarker)
            {
                if (!GraphTaskProgram.IsValue(node.kind))
                    Pin(node, 0, data: false, output: false, label: "in");
                int dataPins = GraphTaskProgram.DataPins(node.kind);
                for (int i = 0; i < dataPins; i++)
                    Pin(node, i, data: true, output: false, label: DataLabel(node.kind, i));
                if (GraphTaskProgram.IsValue(node.kind))
                    Pin(node, 0, data: true, output: true, label: "value");
            }

            int execPins = node.IsMarker ? 1 : GraphTaskProgram.ExecPins(node.kind);
            for (int i = 0; i < execPins; i++)
                Pin(node, i, data: false, output: true, label: ExecLabel(node, i));

            if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                m_Selected = node.id;
                m_Dragging = node.id;
                GUI.FocusControl(null);
                current.Use();
                Repaint();
            }
            if (current.type == EventType.MouseDrag && m_Dragging == node.id)
            {
                Undo.RecordObject(m_Document, "Move Node");
                node.position += current.delta;
                current.Use();
                Repaint();
            }
            if (current.type == EventType.ContextClick && rect.Contains(current.mousePosition))
            {
                var menu = new GenericMenu();
                string id = node.id;
                menu.AddItem(new GUIContent("Disconnect"), false, () => Disconnect(id));
                menu.AddItem(new GUIContent("Delete"), false, () => Remove(id));
                menu.ShowAsContext();
                current.Use();
            }
        }

        /// <summary>One pin: a square you can start or finish a wire on, and its name.</summary>
        private void Pin(TaskGraphDocNode node, int index, bool data, bool output, string label)
        {
            Rect rect = PinRect(node, index, data, output);
            EditorGUI.DrawRect(rect, data
                ? new Color(0.55f, 0.8f, 0.55f)
                : new Color(0.9f, 0.85f, 0.5f));

            var text = new Rect(output ? rect.x - 96f : rect.xMax + 4f, rect.y - 3f, 92f, 16f);
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = output ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft
            };
            GUI.Label(text, label, style);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                // CLICKING A FILLED INPUT TAKES THE WIRE OFF, which is the only way to undo a
                // connection without hunting for the line that made it.
                if (!output && Disconnect(node.id, index, data))
                {
                    current.Use();
                    return;
                }
                m_WiringNode = node.id;
                m_WiringPin = index;
                m_WiringData = data;
                m_WiringFromOutput = output;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && m_WiringNode != null
                && rect.Contains(current.mousePosition))
            {
                Connect(node.id, index, data, output);
                m_WiringNode = null;
                current.Use();
            }
        }

        private void DrawWire(TaskGraphDocWire wire)
        {
            TaskGraphDocNode from = m_Document.Node(wire.from);
            TaskGraphDocNode to = m_Document.Node(wire.to);
            if (from == null || to == null)
                return;

            Vector2 a = PinCentre(from, wire.data ? 0 : wire.fromPin, wire.data, true);
            Vector2 b = PinCentre(to, wire.data ? wire.toPin : 0, wire.data, false);
            Color colour = wire.data
                ? new Color(0.45f, 0.85f, 0.45f)
                : new Color(0.95f, 0.85f, 0.4f);
            if (m_Selected == wire.from || m_Selected == wire.to)
                colour = Color.Lerp(colour, Color.white, 0.5f);
            Handles.DrawBezier(a, b, a + Vector2.right * 50f, b + Vector2.left * 50f, colour,
                null, 2.4f);
        }

        // ---- editing -------------------------------------------------------------------

        private void Connect(string node, int pin, bool data, bool output)
        {
            if (m_WiringNode == null || m_WiringData != data || m_WiringFromOutput == output)
                return;   // pins of different kinds, or two of the same end

            string producer = m_WiringFromOutput ? m_WiringNode : node;
            int producerPin = m_WiringFromOutput ? m_WiringPin : pin;
            string consumer = m_WiringFromOutput ? node : m_WiringNode;
            int consumerPin = m_WiringFromOutput ? pin : m_WiringPin;
            if (producer == consumer)
                return;

            Undo.RecordObject(m_Document, "Connect");
            // ONE WIRE PER INPUT and one per exec out-pin: both ends are single-valued in the
            // program, so replacing beats adding a second the bake would have to choose between.
            if (data)
                Disconnect(consumer, consumerPin, true);
            else
                DisconnectOut(producer, producerPin);

            m_Document.wires.Add(new TaskGraphDocWire
            {
                from = producer,
                fromPin = data ? 0 : producerPin,
                to = consumer,
                toPin = data ? consumerPin : 0,
                data = data
            });
            Changed();
        }

        private bool Disconnect(string node, int pin, bool data)
        {
            for (int i = m_Document.wires.Count - 1; i >= 0; i--)
            {
                TaskGraphDocWire wire = m_Document.wires[i];
                if (wire.data != data || wire.to != node)
                    continue;
                if (data && wire.toPin != pin)
                    continue;
                Undo.RecordObject(m_Document, "Disconnect");
                m_Document.wires.RemoveAt(i);
                Changed();
                return true;
            }
            return false;
        }

        private void DisconnectOut(string node, int pin)
        {
            for (int i = m_Document.wires.Count - 1; i >= 0; i--)
            {
                TaskGraphDocWire wire = m_Document.wires[i];
                if (wire.data || wire.from != node || wire.fromPin != pin)
                    continue;
                m_Document.wires.RemoveAt(i);
            }
        }

        private void Disconnect(string node)
        {
            Undo.RecordObject(m_Document, "Disconnect Node");
            for (int i = m_Document.wires.Count - 1; i >= 0; i--)
            {
                TaskGraphDocWire wire = m_Document.wires[i];
                if (wire.from == node || wire.to == node)
                    m_Document.wires.RemoveAt(i);
            }
            Changed();
        }

        private void Remove(string id)
        {
            TaskGraphDocNode node = m_Document.Node(id);
            if (node == null)
                return;
            Undo.RecordObject(m_Document, "Delete Node");
            for (int i = m_Document.wires.Count - 1; i >= 0; i--)
            {
                TaskGraphDocWire wire = m_Document.wires[i];
                if (wire.from == id || wire.to == id)
                    m_Document.wires.RemoveAt(i);
            }
            m_Document.nodes.Remove(node);
            if (m_Selected == id)
                m_Selected = null;
            Changed();
        }

        private TaskGraphDocNode Add(GraphTaskNodeKind kind, Vector2 where,
            TaskGraphEntry entry = TaskGraphEntry.None)
        {
            Undo.RecordObject(m_Document, "Add Node");
            var node = new TaskGraphDocNode
            {
                id = m_Document.MintId(entry == TaskGraphEntry.None ? kind.ToString() : entry + ""),
                kind = kind,
                entry = entry,
                position = where
            };
            m_Document.nodes.Add(node);
            m_Selected = node.id;
            Changed();
            return node;
        }

        private void Changed()
        {
            EditorUtility.SetDirty(m_Document);
            if (m_AutoBake)
                Rebake(save: true);
            else
                Rebake();
            Repaint();
        }

        private void Rebake(bool save = false)
        {
            m_Problems.Clear();
            if (m_Document == null)
                return;
            if (save)
            {
                TaskGraphBakeOps.Bake(m_Document, m_Problems);
                return;
            }
            GraphTaskAsset preview = TaskGraphDocBaker.Bake(m_Document, m_Problems);
            if (preview != null)
                DestroyImmediate(preview);
        }

        // ---- the palette ---------------------------------------------------------------

        private void ShowPalette(Vector2 where)
        {
            var menu = new GenericMenu();
            AddItem(menu, "Start/on Tick", GraphTaskNodeKind.ReturnSuccess, where,
                TaskGraphEntry.Tick);
            AddItem(menu, "Start/on Enter", GraphTaskNodeKind.ReturnSuccess, where,
                TaskGraphEntry.Enter);
            AddItem(menu, "Start/on Exit", GraphTaskNodeKind.ReturnSuccess, where,
                TaskGraphEntry.Exit);

            AddItem(menu, "Flow/Call a task", GraphTaskNodeKind.DoTask, where);
            AddItem(menu, "Flow/Branch", GraphTaskNodeKind.Branch, where);
            AddItem(menu, "Flow/Wait", GraphTaskNodeKind.Wait, where);
            AddItem(menu, "Flow/Fire a cue", GraphTaskNodeKind.FireCue, where);
            AddItem(menu, "Flow/Return success", GraphTaskNodeKind.ReturnSuccess, where);
            AddItem(menu, "Flow/Return failure", GraphTaskNodeKind.ReturnFailure, where);
            AddItem(menu, "Flow/Return running", GraphTaskNodeKind.ReturnRunning, where);

            AddItem(menu, "Blackboard/Read a number", GraphTaskNodeKind.GetBlackboardFloat, where);
            AddItem(menu, "Blackboard/Read text", GraphTaskNodeKind.GetBlackboardString, where);
            AddItem(menu, "Blackboard/Has a key", GraphTaskNodeKind.HasBlackboardKey, where);
            AddItem(menu, "Blackboard/Write a number", GraphTaskNodeKind.SetBlackboardFloat, where);
            AddItem(menu, "Blackboard/Write text", GraphTaskNodeKind.SetBlackboardString, where);

            AddItem(menu, "Values/Number", GraphTaskNodeKind.ConstFloat, where);
            AddItem(menu, "Values/Text", GraphTaskNodeKind.ConstString, where);
            AddItem(menu, "Values/Checkbox", GraphTaskNodeKind.ConstBool, where);
            AddItem(menu, "Values/A row's name", GraphTaskNodeKind.RegistryEntry, where);
            AddItem(menu, "Values/Compare numbers", GraphTaskNodeKind.CompareFloat, where);
            AddItem(menu, "Values/And", GraphTaskNodeKind.BoolAnd, where);
            AddItem(menu, "Values/Or", GraphTaskNodeKind.BoolOr, where);
            AddItem(menu, "Values/Not", GraphTaskNodeKind.BoolNot, where);
            AddItem(menu, "Values/Evaluate a condition", GraphTaskNodeKind.EvaluateCondition, where);
            AddItem(menu, "Values/How this exited", GraphTaskNodeKind.ExitStatus, where);

            AddItem(menu, "Returns/Return a number", GraphTaskNodeKind.SetOutputFloat, where);
            AddItem(menu, "Returns/Return text", GraphTaskNodeKind.SetOutputString, where);
            AddItem(menu, "Returns/Return a checkbox", GraphTaskNodeKind.SetOutputBool, where);
            AddItem(menu, "Returns/A call's number", GraphTaskNodeKind.GetTaskOutputFloat, where);
            AddItem(menu, "Returns/A call's text", GraphTaskNodeKind.GetTaskOutputString, where);
            AddItem(menu, "Returns/A call's checkbox", GraphTaskNodeKind.GetTaskOutputBool, where);
            menu.ShowAsContext();
        }

        private void AddItem(GenericMenu menu, string path, GraphTaskNodeKind kind, Vector2 where,
            TaskGraphEntry entry = TaskGraphEntry.None)
        {
            menu.AddItem(new GUIContent(path), false, () => Add(kind, where, entry));
        }

        // ---- panels --------------------------------------------------------------------

        private void DrawLeft(Rect area)
        {
            GUILayout.BeginArea(area, EditorStyles.helpBox);
            m_LeftScroll = EditorGUILayout.BeginScrollView(m_LeftScroll);

            EditorGUILayout.LabelField(new GUIContent("Parameters",
                "Made here, outside every node — the wiring complaint answered. 'node' drops a "
                + "reader on the canvas."), EditorStyles.boldLabel);
            for (int i = 0; i < m_Document.parameters.Count; i++)
            {
                GraphTaskParameter parameter = m_Document.parameters[i];
                if (parameter == null)
                    continue;
                int index = i;

                EditorGUILayout.BeginHorizontal();
                string name = EditorGUILayout.DelayedTextField(parameter.name ?? "",
                    GUILayout.Width(84f));
                if (name != parameter.name)
                {
                    Undo.RecordObject(m_Document, "Rename Parameter");
                    parameter.name = name;
                    Changed();
                }
                var kind = (GraphTaskParameterKind)EditorGUILayout.EnumPopup(parameter.kind,
                    GUILayout.Width(62f));
                if (kind != parameter.kind)
                {
                    Undo.RecordObject(m_Document, "Retype Parameter");
                    parameter.kind = kind;
                    Changed();
                }
                if (GUILayout.Button("node", EditorStyles.miniButton, GUILayout.Width(38f)))
                {
                    TaskGraphDocNode reader = Add(ReaderFor(parameter.kind),
                        new Vector2(40f, 40f) - m_Pan);
                    reader.stringValue = parameter.name;
                    reader.title = parameter.name;
                    Changed();
                }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20f)))
                {
                    Undo.RecordObject(m_Document, "Remove Parameter");
                    m_Document.parameters.RemoveAt(index);
                    Changed();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel++;
                if (parameter.kind == GraphTaskParameterKind.String)
                {
                    string value = EditorGUILayout.DelayedTextField("default",
                        parameter.stringValue ?? "");
                    if (value != parameter.stringValue)
                    {
                        Undo.RecordObject(m_Document, "Parameter Default");
                        parameter.stringValue = value;
                        Changed();
                    }
                }
                else
                {
                    float value = EditorGUILayout.DelayedFloatField("default",
                        parameter.floatValue);
                    if (!Mathf.Approximately(value, parameter.floatValue))
                    {
                        Undo.RecordObject(m_Document, "Parameter Default");
                        parameter.floatValue = value;
                        Changed();
                    }
                }
                EditorGUI.indentLevel--;
            }
            if (GUILayout.Button("+ parameter"))
            {
                Undo.RecordObject(m_Document, "Add Parameter");
                m_Document.parameters.Add(new GraphTaskParameter
                {
                    name = "value" + (m_Document.parameters.Count + 1),
                    id = System.Guid.NewGuid().ToString("N")
                });
                Changed();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent("Returns",
                "Read off what the graph writes — what a caller may route."),
                EditorStyles.boldLabel);
            var outputs = new List<string>();
            for (int i = 0; i < m_Document.nodes.Count; i++)
            {
                TaskGraphDocNode node = m_Document.nodes[i];
                if (node == null || string.IsNullOrEmpty(node.stringValue))
                    continue;
                if (node.kind != GraphTaskNodeKind.SetOutputFloat
                    && node.kind != GraphTaskNodeKind.SetOutputString
                    && node.kind != GraphTaskNodeKind.SetOutputBool)
                    continue;
                if (!outputs.Contains(node.stringValue))
                    outputs.Add(node.stringValue);
                EditorGUILayout.LabelField("• " + node.stringValue,
                    node.kind.ToString().Replace("SetOutput", "").ToLowerInvariant());
            }
            if (outputs.Count == 0)
                EditorGUILayout.LabelField("nothing yet", EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent("Declares",
                "The catalogs this graph may name rows from — the neighbourhood rule."),
                EditorStyles.boldLabel);
            for (int i = 0; i < m_Document.declares.Count; i++)
            {
                var picked = (StateTreeRegistryAsset)EditorGUILayout.ObjectField(
                    m_Document.declares[i], typeof(StateTreeRegistryAsset), false);
                if (picked != m_Document.declares[i])
                {
                    Undo.RecordObject(m_Document, "Declare");
                    m_Document.declares[i] = picked;
                    Changed();
                }
            }
            if (GUILayout.Button("+ declare"))
            {
                Undo.RecordObject(m_Document, "Declare");
                m_Document.declares.Add(null);
                Changed();
            }

            if (m_Problems.Count > 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Problems", EditorStyles.boldLabel);
                for (int i = 0; i < m_Problems.Count; i++)
                    EditorGUILayout.HelpBox(m_Problems[i], MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRight(Rect area)
        {
            GUILayout.BeginArea(area, EditorStyles.helpBox);
            m_RightScroll = EditorGUILayout.BeginScrollView(m_RightScroll);

            TaskGraphDocNode node = m_Document.Node(m_Selected);
            if (node == null)
            {
                EditorGUILayout.LabelField("Nothing selected", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Right-click the canvas to add a node. Drag from a "
                    + "pin to another to wire; click a filled input to unwire.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            EditorGUILayout.LabelField(node.IsMarker ? node.entry + " marker" : node.kind.ToString(),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("id " + node.id, EditorStyles.miniLabel);

            if (node.IsMarker)
            {
                EditorGUILayout.HelpBox("A marker is not an instruction. What its wire arrives at "
                    + "is where this chain starts.", MessageType.None);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            string title = EditorGUILayout.DelayedTextField("Title", node.title ?? "");
            if (title != node.title)
            {
                Undo.RecordObject(m_Document, "Rename Node");
                node.title = title;
                Changed();
            }

            if (NeedsString(node.kind))
            {
                string label = StringLabel(node.kind);
                string value = EditorGUILayout.DelayedTextField(label, node.stringValue ?? "");
                if (value != node.stringValue)
                {
                    Undo.RecordObject(m_Document, "Edit Node");
                    node.stringValue = value;
                    Changed();
                }
            }
            if (NeedsFloat(node.kind))
            {
                float value = EditorGUILayout.DelayedFloatField(FloatLabel(node.kind),
                    node.floatValue);
                if (!Mathf.Approximately(value, node.floatValue))
                {
                    Undo.RecordObject(m_Document, "Edit Node");
                    node.floatValue = value;
                    Changed();
                }
            }
            if (node.kind == GraphTaskNodeKind.SetBlackboardString)
            {
                string value = EditorGUILayout.DelayedTextField("Value (unwired)",
                    node.stringValue2 ?? "");
                if (value != node.stringValue2)
                {
                    Undo.RecordObject(m_Document, "Edit Node");
                    node.stringValue2 = value;
                    Changed();
                }
            }
            if (node.kind == GraphTaskNodeKind.DoTask)
            {
                var picked = (StateTreeTaskAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Call", "The task this node runs — a sub-asset of this "
                        + "document, so its settings are authored here."),
                    node.task, typeof(StateTreeTaskAsset), false);
                if (picked != node.task)
                {
                    Undo.RecordObject(m_Document, "Set Call");
                    node.task = picked;
                    Changed();
                }
                if (node.task != null)
                    DrawEmbedded(node.task);
            }
            if (node.kind == GraphTaskNodeKind.EvaluateCondition)
            {
                var picked = (StateTreeConditionAsset)EditorGUILayout.ObjectField("Condition",
                    node.condition, typeof(StateTreeConditionAsset), false);
                if (picked != node.condition)
                {
                    Undo.RecordObject(m_Document, "Set Condition");
                    node.condition = picked;
                    Changed();
                }
                if (node.condition != null)
                    DrawEmbedded(node.condition);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>The call's own fields, edited in place — a node whose settings live somewhere
        /// else is the thing this surface exists to stop.</summary>
        private void DrawEmbedded(Object asset)
        {
            EditorGUILayout.Space(4f);
            var serialized = new SerializedObject(asset);
            SerializedProperty property = serialized.GetIterator();
            bool first = true;
            while (property.NextVisible(first))
            {
                first = false;
                if (property.name == "m_Script")
                    continue;
                EditorGUILayout.PropertyField(property, true);
            }
            if (serialized.ApplyModifiedProperties())
                Changed();
        }

        // ---- geometry and labels -------------------------------------------------------

        private Rect NodeRect(TaskGraphDocNode node)
        {
            int rows = node.IsMarker
                ? 1
                : Mathf.Max(1 + GraphTaskProgram.DataPins(node.kind),
                    Mathf.Max(GraphTaskProgram.ExecPins(node.kind), 1));
            return new Rect(node.position + m_Pan, new Vector2(k_NodeWidth,
                k_Header + rows * k_Row + 8f));
        }

        private Rect PinRect(TaskGraphDocNode node, int index, bool data, bool output)
        {
            Rect rect = NodeRect(node);
            float y = rect.y + k_Header + (data && !output ? (index + 1) : index) * k_Row;
            float x = output ? rect.xMax - k_Pin * 0.5f : rect.x - k_Pin * 0.5f;
            return new Rect(x, y, k_Pin, k_Pin);
        }

        private Vector2 PinCentre(TaskGraphDocNode node, int index, bool data, bool output)
        {
            return node == null ? Vector2.zero : PinRect(node, index, data, output).center;
        }

        private static Color ColourOf(TaskGraphDocNode node)
        {
            if (node.IsMarker)
                return new Color(0.6f, 0.85f, 0.6f);
            if (GraphTaskProgram.IsValue(node.kind))
                return new Color(0.7f, 0.78f, 0.9f);
            return node.kind == GraphTaskNodeKind.DoTask
                ? new Color(0.9f, 0.78f, 0.55f)
                : Color.white;
        }

        private static GraphTaskNodeKind ReaderFor(GraphTaskParameterKind kind)
        {
            switch (kind)
            {
                case GraphTaskParameterKind.String: return GraphTaskNodeKind.GetParamString;
                case GraphTaskParameterKind.Bool: return GraphTaskNodeKind.GetParamBool;
                default: return GraphTaskNodeKind.GetParamFloat;
            }
        }

        private static string ExecLabel(TaskGraphDocNode node, int pin)
        {
            if (node.IsMarker)
                return "starts";
            switch (node.kind)
            {
                case GraphTaskNodeKind.Branch: return pin == 0 ? "true" : "false";
                case GraphTaskNodeKind.DoTask: return pin == 0 ? "success" : "failure";
                default: return "then";
            }
        }

        private static string DataLabel(GraphTaskNodeKind kind, int pin)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.Branch: return "if";
                case GraphTaskNodeKind.CompareFloat: return pin == 0 ? "left" : "right";
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr: return pin == 0 ? "a" : "b";
                case GraphTaskNodeKind.BoolNot: return "of";
                case GraphTaskNodeKind.Wait: return "seconds";
                case GraphTaskNodeKind.GetTaskOutputFloat:
                case GraphTaskNodeKind.GetTaskOutputString:
                case GraphTaskNodeKind.GetTaskOutputBool: return "call";
                default: return "value";
            }
        }

        private static bool NeedsString(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.SetBlackboardFloat:
                case GraphTaskNodeKind.SetBlackboardString:
                case GraphTaskNodeKind.GetBlackboardFloat:
                case GraphTaskNodeKind.GetBlackboardString:
                case GraphTaskNodeKind.HasBlackboardKey:
                case GraphTaskNodeKind.FireCue:
                case GraphTaskNodeKind.ConstString:
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.GetParamFloat:
                case GraphTaskNodeKind.GetParamString:
                case GraphTaskNodeKind.GetParamBool:
                case GraphTaskNodeKind.SetOutputFloat:
                case GraphTaskNodeKind.SetOutputString:
                case GraphTaskNodeKind.SetOutputBool:
                case GraphTaskNodeKind.GetTaskOutputFloat:
                case GraphTaskNodeKind.GetTaskOutputString:
                case GraphTaskNodeKind.GetTaskOutputBool:
                case GraphTaskNodeKind.RegistryEntry:
                    return true;
                default:
                    return false;
            }
        }

        private static string StringLabel(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.FireCue: return "Cue";
                case GraphTaskNodeKind.ConstString: return "Text";
                case GraphTaskNodeKind.CompareFloat: return "Operator";
                case GraphTaskNodeKind.GetParamFloat:
                case GraphTaskNodeKind.GetParamString:
                case GraphTaskNodeKind.GetParamBool: return "Parameter";
                case GraphTaskNodeKind.SetOutputFloat:
                case GraphTaskNodeKind.SetOutputString:
                case GraphTaskNodeKind.SetOutputBool: return "Returns";
                case GraphTaskNodeKind.GetTaskOutputFloat:
                case GraphTaskNodeKind.GetTaskOutputString:
                case GraphTaskNodeKind.GetTaskOutputBool: return "Output name";
                case GraphTaskNodeKind.RegistryEntry: return "Row";
                default: return "Key";
            }
        }

        private static bool NeedsFloat(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.ConstFloat:
                case GraphTaskNodeKind.ConstBool:
                case GraphTaskNodeKind.Wait:
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.SetBlackboardFloat:
                case GraphTaskNodeKind.SetOutputFloat:
                case GraphTaskNodeKind.SetOutputBool:
                    return true;
                default:
                    return false;
            }
        }

        private static string FloatLabel(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.Wait: return "Seconds (unwired)";
                case GraphTaskNodeKind.CompareFloat: return "Right (unwired)";
                case GraphTaskNodeKind.ConstBool: return "1 = true";
                default: return "Value (unwired)";
            }
        }
    }
}
