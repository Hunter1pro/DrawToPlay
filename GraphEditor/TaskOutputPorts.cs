using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The RETURN half of a library call's port surface: one named OUTPUT data pin per
    /// [TaskOutput] field of the wrapped type, so a call's return flows INSIDE the program —
    /// `var result = await task()` as a wire, no blackboard in between. The bake lowers each
    /// consumed pin into a GetTaskOutput pull reading the call's per-activation copy.
    /// </summary>
    public static class TaskOutputPorts
    {
        private static readonly Dictionary<Type, FieldInfo[]> s_Fields =
            new Dictionary<Type, FieldInfo[]>();

        /// <summary>The [TaskOutput] fields of a library type, blackboard-boxable kinds only —
        /// the same surface the executor captures.</summary>
        public static FieldInfo[] Fields(Type libraryType)
        {
            if (libraryType == null)
                return Array.Empty<FieldInfo>();
            if (s_Fields.TryGetValue(libraryType, out FieldInfo[] cached))
                return cached;

            var collected = new List<FieldInfo>();
            foreach (FieldInfo field in libraryType.GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (!Attribute.IsDefined(field, typeof(TaskOutputAttribute)))
                    continue;
                Type type = field.FieldType;
                if (type == typeof(float) || type == typeof(int) || type == typeof(bool)
                    || type == typeof(string))
                    collected.Add(field);
            }
            cached = collected.ToArray();
            s_Fields[libraryType] = cached;
            return cached;
        }

        /// <summary>True when <paramref name="portName"/> is one of the type's return pins.</summary>
        public static FieldInfo Find(Type libraryType, string portName)
        {
            FieldInfo[] fields = Fields(libraryType);
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.Equals(fields[i].Name, portName, StringComparison.Ordinal))
                    return fields[i];
            }
            return null;
        }

        /// <summary>Declare the return pins on a call node.</summary>
        public static void DefineOutputs(Node.IPortDefinitionContext context, Type libraryType)
        {
            foreach (FieldInfo field in Fields(libraryType))
            {
                var attribute = (TaskOutputAttribute)Attribute.GetCustomAttribute(field,
                    typeof(TaskOutputAttribute));
                string tooltip = attribute != null && !string.IsNullOrEmpty(attribute.description)
                    ? attribute.description
                    : "Returned by the task when it finishes; a call that has not run reads "
                        + "the type default.";
                Type type = field.FieldType;
                if (type == typeof(string))
                    TaskGraphPorts.AddDataOut<string>(context, field.Name, field.Name, tooltip);
                else if (type == typeof(bool))
                    TaskGraphPorts.AddDataOut<bool>(context, field.Name, field.Name, tooltip);
                else
                    TaskGraphPorts.AddDataOut<float>(context, field.Name, field.Name, tooltip);
            }
        }

        /// <summary>The pull kind reading a return of this field's type.</summary>
        public static GraphTaskNodeKind PullKind(FieldInfo field)
        {
            if (field.FieldType == typeof(string))
                return GraphTaskNodeKind.GetTaskOutputString;
            if (field.FieldType == typeof(bool))
                return GraphTaskNodeKind.GetTaskOutputBool;
            return GraphTaskNodeKind.GetTaskOutputFloat;
        }
    }
}
