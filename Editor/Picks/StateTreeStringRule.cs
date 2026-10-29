using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    public enum StateTreeStringKind
    {
        /// <summary>Not a string field.</summary>
        NotString,

        /// <summary>Names something, and is drawn with a picker (an attribute that has a drawer, or a
        /// vocabulary a game registered).</summary>
        Picked,

        /// <summary>Words a person reads: <see cref="HumanTextAttribute"/>, [TextArea], [Multiline].</summary>
        HumanText,

        /// <summary>A literal value written or compared verbatim: <see cref="LiteralValueAttribute"/>.</summary>
        Literal,

        /// <summary>Written by the task, never authored: [TaskOutput].</summary>
        Output,

        /// <summary>A free text box that names something — the shape the rule forbids.</summary>
        Free
    }

    /// <summary>
    /// "A parameter is a reference to something — never a string": the one place that decides what a
    /// string field of a task, a condition or a template is, read by the guard tests and by the
    /// mission inspector (which refuses to draw a text box for a parameter bound into a Free field).
    /// </summary>
    public static class StateTreeStringRule
    {
        private static HashSet<Type> s_Drawn;

        public static bool IsStringField(FieldInfo field)
        {
            Type type = field != null ? field.FieldType : null;
            return type == typeof(string) || type == typeof(string[]) || type == typeof(List<string>);
        }

        public static StateTreeStringKind Classify(FieldInfo field)
        {
            if (!IsStringField(field))
                return StateTreeStringKind.NotString;
            if (field.IsDefined(typeof(TaskOutputAttribute), true))
                return StateTreeStringKind.Output;
            if (field.IsDefined(typeof(HumanTextAttribute), true) || field.IsDefined(typeof(TextAreaAttribute), true)
                || field.IsDefined(typeof(MultilineAttribute), true))
                return StateTreeStringKind.HumanText;
            if (field.IsDefined(typeof(LiteralValueAttribute), true))
                return StateTreeStringKind.Literal;
            foreach (object attribute in field.GetCustomAttributes(true))
            {
                if (attribute is PropertyAttribute && Drawn().Contains(attribute.GetType()))
                    return StateTreeStringKind.Picked;
            }
            if (StateTreeFieldOffers.Claims(field)
                || field.FieldType == typeof(string) && StateTreeFieldOffers.For(field) != null)
                return StateTreeStringKind.Picked;
            return StateTreeStringKind.Free;
        }

        /// <summary>The serialized string fields of a task or condition type that break the rule.</summary>
        public static List<FieldInfo> FreeFields(Type type)
        {
            var free = new List<FieldInfo>();
            for (Type at = type; at != null && at != typeof(ScriptableObject); at = at.BaseType)
            {
                foreach (FieldInfo field in at.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    bool serialized = field.IsPublic
                        ? !field.IsDefined(typeof(NonSerializedAttribute), false)
                        : field.IsDefined(typeof(SerializeField), false);
                    if (serialized && Classify(field) == StateTreeStringKind.Free)
                        free.Add(field);
                }
            }
            return free;
        }

        /// <summary>Every attribute type a [CustomPropertyDrawer] draws — types change only on a
        /// domain reload, which clears this.</summary>
        private static HashSet<Type> Drawn()
        {
            if (s_Drawn != null)
                return s_Drawn;
            s_Drawn = new HashSet<Type>();
            FieldInfo target = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (Type drawer in TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>())
            {
                foreach (CustomPropertyDrawer marker in drawer.GetCustomAttributes<CustomPropertyDrawer>(false))
                {
                    if (target?.GetValue(marker) is Type drawn && typeof(PropertyAttribute).IsAssignableFrom(drawn))
                        s_Drawn.Add(drawn);
                }
            }
            return s_Drawn;
        }
    }
}
