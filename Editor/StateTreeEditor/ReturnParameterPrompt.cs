using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The tiny "declare a return" prompt behind the Returns section's "+": name, kind, Add.
    /// It edits the GRAPH (the callee owns its signature) through
    /// <c>TaskGraphReturnAuthoring</c>, reached by reflection because this assembly
    /// deliberately never references the graph one (the §7.3 boundary, same as
    /// <see cref="Flow.StateTreeGraphBridge"/>).
    /// </summary>
    internal sealed class ReturnParameterPrompt : EditorWindow
    {
        private static readonly string[] k_Kinds = { "Float", "String", "Bool" };

        private string m_GraphPath;
        private Action m_Changed;
        private TextField m_Name;
        private DropdownField m_Kind;
        private HelpBox m_Error;

        public static void Show(string graphPath, Action changed)
        {
            var window = CreateInstance<ReturnParameterPrompt>();
            window.m_GraphPath = graphPath;
            window.m_Changed = changed;
            window.titleContent = new GUIContent("Add Return");
            window.minSize = new Vector2(320f, 132f);
            window.maxSize = new Vector2(480f, 132f);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            rootVisualElement.style.paddingLeft = 8f;
            rootVisualElement.style.paddingRight = 8f;
            rootVisualElement.style.paddingTop = 8f;

            m_Name = new TextField("Name") { value = "result" };
            rootVisualElement.Add(m_Name);
            m_Kind = new DropdownField("Kind",
                new System.Collections.Generic.List<string>(k_Kinds), 0);
            rootVisualElement.Add(m_Kind);

            m_Error = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            m_Error.style.display = DisplayStyle.None;
            rootVisualElement.Add(m_Error);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 8f;
            var cancel = new Button(Close) { text = "Cancel" };
            var add = new Button(Add) { text = "Add" };
            row.Add(cancel);
            row.Add(add);
            rootVisualElement.Add(row);
        }

        private void Add()
        {
            string error = InvokeAuthoring(m_GraphPath, m_Name.value, m_Kind.value);
            if (error != null)
            {
                m_Error.text = error;
                m_Error.style.display = DisplayStyle.Flex;
                return;
            }
            m_Changed?.Invoke();
            Close();
        }

        /// <summary>Reflective bridge to
        /// <c>PowerOfFire.DrawToPlay.GraphEditor.TaskGraphReturnAuthoring.AddReturnParameter
        /// (string, string, string)</c>. Null = success, else the reason.</summary>
        private static string InvokeAuthoring(string graphPath, string name, string kind)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(
                    "PowerOfFire.DrawToPlay.GraphEditor.TaskGraphReturnAuthoring");
                if (type == null)
                    continue;
                MethodInfo method = type.GetMethod("AddReturnParameter",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    break;
                try
                {
                    return method.Invoke(null, new object[] { graphPath, name, kind }) as string;
                }
                catch (Exception exception)
                {
                    return "Adding the return failed: " + exception.Message;
                }
            }
            return "The graph tooling assembly is not present, so the return cannot be added "
                + "from here. Declare an Output variable on the graph's Blackboard instead.";
        }
    }
}
