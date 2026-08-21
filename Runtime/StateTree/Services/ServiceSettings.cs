using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT A SERVICE CLASS DECLARES IT CAN BE TUNED BY, and how a layer's rows land on it.
    ///
    /// Declared once per type and cached: the panel asks it what to offer, the map asks it which
    /// settings are tags, and the base constructor asks it to apply the def's rows. It is the one
    /// place the three agree on what a setting is.
    ///
    /// WHERE IT RUNS. <see cref="StateTreeService"/>'s constructor writes every declared
    /// default, then the def's overrides — and the derived constructor body, where a service
    /// does its real setup, already sees the final numbers. No hook, no lazy accessor, no "did
    /// you read it too early" class of bug.
    ///
    /// A row naming a setting the class does not declare is REFUSED, out loud, with the name in
    /// the message — the placement-attribute rule. A value sitting there doing nothing is the
    /// worst kind of typo.
    /// </summary>
    /// <summary>Which layer a setting's value came from.</summary>
    public enum ServiceSettingSource { Code, Def, Install }

    public static class ServiceSettings
    {
        /// <summary>One declared knob: the field, its default, its description, and whether
        /// it is a tag.</summary>
        public sealed class Declared
        {
            public FieldInfo field;
            public object defaultValue;
            public string description;
            public bool isTag;

            public string name => field.Name;
            public Type type => field.FieldType;
        }

        private static readonly Dictionary<Type, List<Declared>> s_Declared =
            new Dictionary<Type, List<Declared>>();

        /// <summary>Every <see cref="ServiceSettingAttribute"/> field on a service type, in
        /// declaration order, cached.</summary>
        public static IReadOnlyList<Declared> DeclaredOn(Type serviceType)
        {
            if (serviceType == null)
                return Array.Empty<Declared>();
            if (s_Declared.TryGetValue(serviceType, out List<Declared> known))
                return known;

            var found = new List<Declared>();
            FieldInfo[] fields = serviceType.GetFields(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < fields.Length; i++)
            {
                var marked = fields[i].GetCustomAttribute<ServiceSettingAttribute>(true);
                if (marked == null)
                    continue;
                if (!Supported(fields[i].FieldType))
                {
                    Debug.LogError("[Settings] " + serviceType.Name + "." + fields[i].Name
                        + " is marked [ServiceSetting] but is a " + fields[i].FieldType.Name
                        + " — a setting is a float, int, bool, string or enum.");
                    continue;
                }
                object fallback = Coerce(fields[i].FieldType, marked.defaultValue);
                if (fallback == null)
                {
                    Debug.LogError("[Settings] " + serviceType.Name + "." + fields[i].Name
                        + " declares a default of '" + marked.defaultValue + "', which is not a "
                        + fields[i].FieldType.Name + ".");
                    continue;
                }
                found.Add(new Declared
                {
                    field = fields[i],
                    defaultValue = fallback,
                    description = marked.description,
                    isTag = fields[i].FieldType == typeof(string)
                        && fields[i].IsDefined(typeof(WorldTagAttribute), true)
                });
            }
            s_Declared[serviceType] = found;
            return found;
        }

        /// <summary>The declared knob with this name, or null.</summary>
        public static Declared Find(Type serviceType, string settingName)
        {
            IReadOnlyList<Declared> declared = DeclaredOn(serviceType);
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].name == settingName)
                    return declared[i];
            }
            return null;
        }

        /// <summary>Write every declared default onto a fresh service — the bottom layer,
        /// before any override. Called first by the base constructor.</summary>
        public static void Initialize(object service)
        {
            if (service == null)
                return;
            IReadOnlyList<Declared> declared = DeclaredOn(service.GetType());
            for (int i = 0; i < declared.Count; i++)
                declared[i].field.SetValue(service, declared[i].defaultValue);
        }

        /// <summary>Every declared setting marked as coming from the class — the record the
        /// layers above overwrite as they land.</summary>
        public static Dictionary<string, ServiceSettingSource> Sources(object service)
        {
            var sources = new Dictionary<string, ServiceSettingSource>();
            IReadOnlyList<Declared> declared = DeclaredOn(service != null ? service.GetType() : null);
            for (int i = 0; i < declared.Count; i++)
                sources[declared[i].name] = ServiceSettingSource.Code;
            return sources;
        }

        /// <summary>
        /// Land one layer's rows on a service. Called by the base constructor with the def's
        /// layer, then the install's, so the later call wins where both speak.
        /// </summary>
        public static void Apply(object service, ServiceSettingSet layer, string layerLabel,
            Dictionary<string, ServiceSettingSource> sources = null,
            ServiceSettingSource source = ServiceSettingSource.Def)
        {
            if (service == null || layer == null || layer.isEmpty)
                return;

            Type type = service.GetType();
            for (int i = 0; i < layer.values.Count; i++)
            {
                ServiceSettingValue row = layer.values[i];
                if (row == null || string.IsNullOrEmpty(row.name))
                    continue;

                Declared knob = Find(type, row.name);
                if (knob == null)
                {
                    Debug.LogError("[Settings] " + layerLabel + " sets '" + row.name + "', which "
                        + type.Name + " does not declare — it declares "
                        + Vocabulary(type) + ". The value is refused.");
                    continue;
                }

                object value = Convert(knob.type, row);
                if (value == null)
                {
                    Debug.LogError("[Settings] " + layerLabel + " sets '" + row.name + "' to '"
                        + row.stringValue + "', which is not a " + knob.type.Name + ".");
                    continue;
                }
                knob.field.SetValue(service, value);
                if (sources != null)
                    sources[row.name] = source;
            }
        }

        /// <summary>A row's value as the declared field's type, or null when it is not one.</summary>
        public static object Convert(Type type, ServiceSettingValue row)
        {
            if (type == typeof(float))
                return row.floatValue;
            if (type == typeof(int))
                return Mathf.RoundToInt(row.floatValue);
            if (type == typeof(bool))
                return row.floatValue > 0.5f;
            if (type == typeof(string))
                return row.stringValue ?? "";
            if (type.IsEnum)
            {
                try
                {
                    return Enum.Parse(type, row.stringValue ?? "", true);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>Write a typed value into a row, the inverse of <see cref="Convert"/>.</summary>
        public static void Store(ServiceSettingValue row, object value)
        {
            switch (value)
            {
                case float f: row.floatValue = f; break;
                case int n: row.floatValue = n; break;
                case bool b: row.floatValue = b ? 1f : 0f; break;
                case string s: row.stringValue = s; break;
                case Enum e: row.stringValue = e.ToString(); break;
            }
        }

        /// <summary>An attribute constant as the field's type — 2.4 written as a double, 256
        /// as an int, an enum member as itself — or null when it cannot be.</summary>
        public static object Coerce(Type type, object constant)
        {
            if (constant == null)
                return type == typeof(string) ? "" : null;
            try
            {
                if (type.IsEnum)
                    return constant.GetType() == type ? constant
                        : Enum.Parse(type, constant.ToString(), true);
                if (type == typeof(string))
                    return constant as string;
                if (type == typeof(bool))
                    return constant is bool b ? b : (object)null;
                if (type == typeof(int))
                    return System.Convert.ToInt32(constant);
                if (type == typeof(float))
                    return System.Convert.ToSingle(constant);
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        public static bool Supported(Type type)
        {
            return type == typeof(float) || type == typeof(int) || type == typeof(bool)
                || type == typeof(string) || type.IsEnum;
        }

        private static string Vocabulary(Type type)
        {
            IReadOnlyList<Declared> declared = DeclaredOn(type);
            if (declared.Count == 0)
                return "nothing";
            var names = new List<string>();
            for (int i = 0; i < declared.Count; i++)
                names.Add("'" + declared[i].name + "'");
            return string.Join(", ", names);
        }
    }
}
