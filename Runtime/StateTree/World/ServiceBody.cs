using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE BODY A DEF OWNS (M30.3) — what this def looks like when it is standing in a level.
    ///
    /// The correction that shapes this whole milestone: the def is ON TOP of the world object, not
    /// beside it. The manifest and the world registry see the DEF; the def spawns and controls the
    /// <see cref="WorldObjectBehaviour"/>. So everything the old spawner's per-kind switch knew —
    /// which prefab, how high it floats, which of its parts the row's entry names, which colour it
    /// wears, whether its tree waits for the world — moves here, as data.
    ///
    /// WHY THAT IS THE WHOLE POINT. Nine kinds of object meant nine cases in one game's spawner,
    /// which meant the TENTH kind cost a code edit in a file that has nothing to do with it. Nine
    /// defs cost nothing: the spawner asks the def, and a new kind of thing is a new asset.
    ///
    /// It stays deliberately thin. A def that spawns, declares, serves and announces is a
    /// god-object waiting to happen (the brief says so outright), so this holds the SHAPE of the
    /// body and nothing about its behaviour — behaviour is the tree the placement names and the
    /// requests the def already declares.
    /// </summary>
    [Serializable]
    public sealed class ServiceBody
    {
        [Tooltip("What to build. Empty means this def has no body — a subsystem, not a thing.")]
        public GameObject prefab;

        [Tooltip("How far above the level's ground plane it sits. A person stands on the floor; "
            + "a pickup floats a little so it reads as takeable.")]
        public float height;

        [Tooltip("Which part of the body IS the placement, by type name — the one that carries "
            + "the row's id. Empty takes the first citizen on the root, which is right for a "
            + "body made of one part.")]
        public string identityPart = "";

        [Tooltip("Does the object take the name of the row it is an instance of? True for a "
            + "thing that IS its row — a person is their conversation, a bench is its recipe. "
            + "False when the entry means something else about it: a raider's entry is what it "
            + "DROPS, and calling the raider 'wood' would be a small lie the craft ability reads.")]
        public bool wearsEntryName;

        [Tooltip("Put in front of the row's entry name when it wears one — the way the yard's "
            + "people are known as 'npc-keeper' rather than 'keeper'.")]
        public string entryNamePrefix = "";

        [Tooltip("What everything this def builds is CALLED — the tags its bodies wear, picked "
            + "from a declared vocabulary. A placement adds its own on top; this is what the "
            + "KIND is, and it lives here because the def owns the body.")]
        [WorldTag]
        public List<string> tags = new List<string>();

        [Tooltip("Which of the body's parts the placement's ENTRY names — 'the pickup's item', "
            + "'the trigger's scene'. Each link is a component and the reference field on it.")]
        public List<ServiceBodyLink> links = new List<ServiceBodyLink>();

        [Tooltip("Parts to expose as facets of the citizen, so a contract can be dereferenced on "
            + "this body however it happens to be assembled (M30.2).")]
        public List<string> exposes = new List<string>();

        [Tooltip("Wear the colour of the definition row this object is an instance of — the "
            + "'tint' of the row in this def's own catalog, when it has one.")]
        public bool tintFromDefinition;

        [Tooltip("The part that wears it, by type name; added if the prefab has none. Must "
            + "implement IWorldTintable.")]
        public string tintPart = "";

        [Tooltip("Does its tree start itself, or wait to be started once the world knows it?")]
        public ServiceBodyMind mind = ServiceBodyMind.HeldForTheWorld;

        /// <summary>True when this def is a THING in the world rather than a subsystem — the one
        /// question a spawner asks before anything else.</summary>
        public bool IsThing => prefab != null;
    }

    /// <summary>
    /// One part of a body that the placement's ENTRY names.
    ///
    /// Nearly every case in the old switch was this single sentence written nine times: take the
    /// row's entry reference and put it on the component that acts on it — the pickup's item, the
    /// NPC's dialog, the trigger's scene, the corpse's drop. Naming the field rather than the type
    /// keeps it honest for a component with two of them.
    /// </summary>
    [Serializable]
    public sealed class ServiceBodyLink
    {
        [Tooltip("The component that holds the reference, by type name (plain or full).")]
        public string component = "";

        [Tooltip("The field on it. An entry reference takes the id and the name; a plain string "
            + "field takes the name.")]
        public string field = "";

        [Tooltip("What to read it as — usually the definition row this placement is an instance "
            + "of.")]
        public string description = "";
    }

    /// <summary>When a spawned body's tree may start.</summary>
    public enum ServiceBodyMind
    {
        /// <summary>Held: the spawner starts it once the world has adopted the citizen. A tree
        /// that starts in the frame its object was built asks the world about something the world
        /// has not heard of, and [InjectOwner] refuses — one frame is the whole fix.</summary>
        HeldForTheWorld = 0,

        /// <summary>Left alone: the body starts its own tree when it is ready, which is what a
        /// gameplay character does through its own registration.</summary>
        StartsItself = 1,

        /// <summary>No tree at all — scenery with a job, like a zone volume.</summary>
        None = 2
    }
}
