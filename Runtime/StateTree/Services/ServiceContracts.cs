using System;
using System.Collections.Generic;
using System.Reflection;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT A CONTRACT PAYLOAD EXPOSES (M38.2) — the fields a reader may address as keys, and
    /// the key a contract type announces itself on.
    ///
    /// A request's TARGET is the contract it answers with (<see cref="ServiceActionContractAttribute.answersWith"/>):
    /// the class says "craft answers with a CraftResult", the contract says where it lands
    /// (its <c>Key</c> constant) and what it carries (its public fields). From those two facts a
    /// picker can offer "craft.last · line", and a board can hold <c>craft.last.line</c>.
    /// </summary>
    public static class ServiceContracts
    {
        private static readonly Dictionary<Type, FieldInfo[]> s_Fields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>The public fields of a payload type that can be keys: numbers, bools,
        /// strings and enums. Objects and lists stay on the whole payload.</summary>
        public static IReadOnlyList<FieldInfo> ExposedFields(Type payloadType)
        {
            if (payloadType == null)
                return Array.Empty<FieldInfo>();
            if (s_Fields.TryGetValue(payloadType, out FieldInfo[] known))
                return known;
            var exposed = new List<FieldInfo>();
            foreach (FieldInfo field in payloadType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
                    exposed.Add(field);
            }
            known = exposed.ToArray();
            s_Fields[payloadType] = known;
            return known;
        }

        /// <summary>The key a contract type announces itself on — its <c>public const string Key</c>,
        /// the convention every contract here follows (<c>CraftResult.Key</c>, <c>ItemUseResult.Key</c>).
        /// Empty when the type declares none.</summary>
        public static string KeyOf(Type payloadType)
        {
            FieldInfo key = payloadType?.GetField("Key", BindingFlags.Public | BindingFlags.Static);
            return key != null && key.IsLiteral && key.FieldType == typeof(string)
                ? (string)key.GetRawConstantValue()
                : "";
        }

        /// <summary>The key a contract's field lands on beside the payload.</summary>
        public static string FieldKey(string key, string field)
        {
            return string.IsNullOrEmpty(field) ? key : key + "." + field;
        }

        /// <summary>Write a payload's exposed fields beside its key. A primitive payload has no
        /// fields and is already its own value; a contract gets one key per field.</summary>
        public static void Flatten(Dictionary<string, object> board, string key, object payload)
        {
            if (board == null || payload == null || string.IsNullOrEmpty(key))
                return;
            Type type = payload.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
                return;
            IReadOnlyList<FieldInfo> fields = ExposedFields(type);
            for (int i = 0; i < fields.Count; i++)
            {
                object value = fields[i].GetValue(payload);
                board[FieldKey(key, fields[i].Name)] = value is float || value is int || value is bool
                    || value is string ? value
                    : value is Enum ? value.ToString()
                    : value is double d ? (float)d
                    : value is long l ? (int)l
                    : value;
            }
        }
    }
}
