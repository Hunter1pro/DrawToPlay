using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHO DECLARES WHAT — the builders that turn each declarer into the panel's option list.
    /// Pure: no GUI, so a test can ask "what would the panel offer here, and with what fallback"
    /// without drawing anything.
    /// </summary>
    internal static class DeclaredOptions
    {
        /// <summary>
        /// A KIND's options for a placement (M34.1c): every attribute its def declares, with the
        /// body's own seed as the fallback — or no fallback at all where the prefab seeds
        /// nothing, because half the kinds take their numbers from the unit row and a confident
        /// 0 beside those would be the panel's first lie.
        /// </summary>
        public static List<DeclaredOption> OfKind(ServiceDef def)
        {
            var options = new List<DeclaredOption>();
            for (int i = 0; def != null && i < def.attributes.Count; i++)
            {
                ServiceAttribute has = def.attributes[i];
                string named = has != null ? has.Name : "";
                if (string.IsNullOrEmpty(named))
                    continue;
                float seed = Seeded(def, named, out bool seeded);
                options.Add(new DeclaredOption
                {
                    name = named,
                    description = "An attribute '" + def.serviceName + "' declares this kind has.",
                    kind = DeclaredOptionKind.Float,
                    fallback = seeded ? seed : (object)null,
                    fallbackLabel = "— whatever the body starts at"
                });
            }
            return options;
        }

        /// <summary>
        /// A SERVICE's settings for its def (M36.1): every <see cref="ServiceSettingAttribute"/>
        /// field on the class, with the attribute's default as the fallback. A tag-typed setting
        /// offers the vocabularies the def declares; one whose default is empty has no fallback
        /// to show and says so.
        /// </summary>
        public static List<DeclaredOption> OfService(ServiceDef def)
        {
            var options = new List<DeclaredOption>();
            Type type = def != null ? def.serviceType : null;
            IReadOnlyList<ServiceSettings.Declared> declared = ServiceSettings.DeclaredOn(type);
            for (int i = 0; i < declared.Count; i++)
            {
                ServiceSettings.Declared knob = declared[i];
                bool emptyTag = knob.isTag && string.IsNullOrEmpty(knob.defaultValue as string);
                options.Add(new DeclaredOption
                {
                    name = knob.name,
                    description = knob.description,
                    kind = DeclaredOption.KindOf(knob.type, knob.isTag),
                    enumType = knob.type.IsEnum ? knob.type : null,
                    fallback = emptyTag ? null : knob.defaultValue,
                    fallbackLabel = "— no tag; pick one",
                    tagOffers = knob.isTag ? () => TagsOf(def) : (Func<List<WorldTagDef>>)null
                });
            }
            return options;
        }

        /// <summary>
        /// An INSTALL's options (M36.3): the same knobs as <see cref="OfService"/>, but the
        /// fallback is the DEF's value where the def overrides — the layer below an install is
        /// the def, not the class. A tag the def picked is what the install would follow.
        /// </summary>
        public static List<DeclaredOption> OfInstall(ServiceDef def)
        {
            List<DeclaredOption> options = OfService(def);
            for (int i = 0; def != null && i < options.Count; i++)
            {
                DeclaredOption option = options[i];
                ServiceSettingValue row = def.settings.Find(option.name);
                if (row == null)
                    continue;
                ServiceSettings.Declared knob = ServiceSettings.Find(def.serviceType, option.name);
                object fromDef = knob != null ? ServiceSettings.Convert(knob.type, row) : null;
                if (option.kind == DeclaredOptionKind.Tag)
                    fromDef = string.IsNullOrEmpty(row.stringValue) ? null : row.stringValue;
                if (fromDef != null)
                    option.fallback = fromDef;
            }
            return options;
        }

        /// <summary>What a body starts an attribute at: its prefab's seed, when it has one.</summary>
        internal static float Seeded(ServiceDef def, string attribute, out bool seeded)
        {
            seeded = false;
            GameObject prefab = def != null && def.body != null ? def.body.prefab : null;
            if (prefab == null)
                return 0f;
            var attributes = prefab.GetComponentInChildren<AttributeComponent>(true);
            for (int i = 0; attributes != null && i < attributes.seeds.Count; i++)
            {
                AttributeComponent.Seed seed = attributes.seeds[i];
                if (seed != null && seed.attribute.entryName == attribute)
                {
                    seeded = true;
                    return seed.baseValue;
                }
            }
            return 0f;
        }

        private static List<WorldTagDef> TagsOf(ServiceDef def)
        {
            var offers = new List<WorldTagDef>();
            StateTreeOffers.TagsFor(def, offers);
            return offers;
        }
    }
}
