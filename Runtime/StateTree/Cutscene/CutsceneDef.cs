using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE PART IN A SCENE — a role the script speaks about, and the tag that finds whoever is
    /// playing it tonight.
    ///
    /// This is HT's `TimelineTrackBinding.Tags` with the timeline taken out: there, a track is
    /// bound at PLAY TIME to whichever character carries the tag, never to a reference an
    /// author wired. Everything good about that survives the port — the same scene works in
    /// another level, with another actor, after a rename, and it says out loud who it needs.
    /// </summary>
    [Serializable]
    public sealed class CutsceneRole
    {
        [Tooltip("What the beats call this part — 'hero', 'the keeper'. The script speaks "
            + "roles; only this row knows who fills them. The ⚿ offers the keys the picked "
            + "beats tree actually declares, so a part is chosen rather than retyped.")]
        [StateTreeKeysOf("beats", StateTreeKeyKind.Object)]
        public string role = "";

        [Tooltip("The world tag that finds the actor. Nearest match wins when a level has "
            + "several.")]
        public string tag = "";

        [Tooltip("The scene cannot play without this one. Off for a part that is nice to have "
            + "— a bystander who may have wandered off, or died three quests ago.")]
        public bool required = true;
    }

    /// <summary>
    /// A CUTSCENE, AS A ROW (M27) — the cast and the script, and nothing else.
    ///
    /// The script is a state TREE, because this project already has one of those and every
    /// beat it could want is a task in it: a state is a beat, the tasks inside a state are its
    /// tracks (one per actor), and the beat ends when the blocking ones finish. Read top to
    /// bottom that IS a timeline; what it gives up is a scrub bar and what it gains is that
    /// every beat is data the dashboard edits, the wire map draws and a quest can gate.
    ///
    /// The cast is resolved ONCE when the scene starts and published on the director's board,
    /// so a beat says "the keeper walks to the well" and the tasks never search.
    /// </summary>
    [Serializable]
    public sealed class CutsceneDef : StateTreeRegistryEntry
    {
        [Tooltip("What to call it on screen or in a log — the row's name stays the key.")]
        public string displayName = "";

        [Tooltip("The script: a tree whose states are the beats. Its declared keys are where "
            + "the cast lands, one per role.")]
        [StateTreePick]
        public StateTreeAsset beats;

        [Tooltip("Who is in it. Each role is resolved by tag when the scene starts.")]
        public List<CutsceneRole> cast = new List<CutsceneRole>();

        [Tooltip("Takes the controls while it plays: the player is held in its watching state "
            + "and land verbs refuse. Off for a scene that plays around you.")]
        public bool takesControl = true;

        [Tooltip("Plays once per placement and is then written off, so a reload does not "
            + "replay it — the felled-tree rule, applied to a moment.")]
        public bool playsOnce = true;

        /// <summary>One dim line the registry dashboard shows: the cast, at a glance.</summary>
        public override string Describe()
        {
            string parts = "";
            for (int i = 0; i < cast.Count; i++)
            {
                if (cast[i] == null || string.IsNullOrEmpty(cast[i].role))
                    continue;
                if (parts.Length > 0)
                    parts += ", ";
                parts += cast[i].role + " = " + cast[i].tag;
            }
            return (beats != null ? beats.name : "NO SCRIPT")
                + (parts.Length > 0 ? " · " + parts : " · no cast")
                + (takesControl ? " · takes control" : "")
                + (playsOnce ? " · once" : "");
        }
    }
}
