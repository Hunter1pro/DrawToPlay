using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>What kind of value a sketched setting holds — the same set <see cref="ServiceSettings"/>
    /// accepts on a class.</summary>
    public enum SketchSettingKind { Float, Int, Bool, String, Tag }

    /// <summary>One request the subsystem will serve — the def row and the class constant, sketched
    /// once.</summary>
    [Serializable]
    public sealed class SketchRequest
    {
        [Tooltip("What callers write to ask for this — 'bag.use', 'level.goto'.")]
        public string key = "";

        [Tooltip("The class's action name behind the key — 'use', 'goto'. Becomes a const.")]
        public string action = "";

        [Tooltip("What the request's value means — 'item name', 'level name'.")]
        public string valueHint = "";

        [Tooltip("Optional: the catalog the value names a row of, so a bad name is refused at the door.")]
        public StateTreeRegistryAsset namesRowOf;
    }

    [Serializable]
    public sealed class SketchAnnouncement
    {
        [Tooltip("The key others read this under — 'clock.dawn'.")]
        public string key = "";

        public string description = "";
    }

    /// <summary>One knob the class will declare with [ServiceSetting].</summary>
    [Serializable]
    public sealed class SketchSetting
    {
        [Tooltip("A C# identifier — 'secondsPerDay'.")]
        public string name = "";

        public SketchSettingKind kind = SketchSettingKind.Float;

        public float numberDefault;

        public string textDefault = "";

        public string description = "";
    }

    /// <summary>
    /// A SUBSYSTEM, SKETCHED (M37) — the form the creation flow edits, and the one place every
    /// face of a subsystem-to-be is written down before anything is generated from it.
    ///
    /// Everything a <see cref="ServiceDef"/> holds, plus the three things only a class can hold:
    /// the action constants, the setting fields, the capability. The def and the class are what
    /// this writes; the sketch stays, so drift between the three can be reported later.
    /// </summary>
    [CreateAssetMenu(fileName = "SubsystemSketch", menuName = "Draw To Play/Subsystem Sketch")]
    public sealed class SubsystemSketch : ScriptableObject, IStateTreeNeighbourhood
    {
        [Tooltip("The subsystem's name — 'clock'. Names the def, the class (ClockService) and "
            + "the capability (IClock).")]
        public string serviceName = "";

        [Tooltip("Which scope installs it.")]
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        [Tooltip("An existing catalog it manages, or none.")]
        public StateTreeRegistryAsset catalog;

        [Tooltip("Optional: declare a capability interface (IClock) that consumers ask for "
            + "instead of the class. Empty = none.")]
        public string capability = "";

        [Tooltip("What the class namespace is — defaults to the project's examples namespace.")]
        public string codeNamespace = "PowerOfFire.DrawToPlay.Examples";

        public List<SketchRequest> requests = new List<SketchRequest>();
        public List<SketchAnnouncement> announcements = new List<SketchAnnouncement>();

        [Tooltip("UI rows this subsystem shows when it starts.")]
        public List<StateTreeEntryRef<UiDef>> spawns = new List<StateTreeEntryRef<UiDef>>();

        public List<SketchSetting> settings = new List<SketchSetting>();

        [Tooltip("What its bodies HAVE — attribute rows, picked from a declared catalog.")]
        public List<StateTreeEntryRef<AttributeDef>> attributes =
            new List<StateTreeEntryRef<AttributeDef>>();

        [Tooltip("Contracts it claims to implement.")]
        public List<StateTreeEntryRef<ContractDef>> implements =
            new List<StateTreeEntryRef<ContractDef>>();

        [Tooltip("Catalogs the def declares it may name rows of (attributes, tags, contracts).")]
        public List<StateTreeRegistryAsset> declares = new List<StateTreeRegistryAsset>();

        [Header("Where the code goes")]
        [Tooltip("The folder the class, the capability and their test are written into. It must "
            + "be inside a RUNTIME assembly — an Editor folder would make an uninstallable service.")]
        public string codeFolder = "Assets/DrawToPlayExamples/Scripts/Subsystems";

        [Tooltip("The folder the generated test goes into — an Editor test assembly.")]
        public string testFolder = "Assets/DrawToPlay/Tests/Editor";

        [Header("What was generated")]
        [Tooltip("The def this sketch wrote, once it has.")]
        public ServiceDef generatedDef;

        [Tooltip("The class file this sketch wrote, once it has — never rewritten.")]
        public string generatedClassPath = "";

        /// <summary>What the ⛃ pickers on this sketch may offer: the catalog it manages and what
        /// it declares — the same neighbourhood rule the def it writes will live under.</summary>
        public IReadOnlyList<StateTreeRegistryAsset> DeclaredCatalogs
        {
            get
            {
                var all = new List<StateTreeRegistryAsset>(declares);
                if (catalog != null && !all.Contains(catalog))
                    all.Add(catalog);
                return all;
            }
        }

        /// <summary>'clock' → 'ClockService'.</summary>
        public string className => Capitalize(serviceName) + "Service";

        /// <summary>'clock' → 'IClock', or empty when no capability is declared.</summary>
        public string capabilityName =>
            string.IsNullOrEmpty(capability) ? "" : capability.StartsWith("I") ? capability
                : "I" + Capitalize(capability);

        internal static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text)
                ? ""
                : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
