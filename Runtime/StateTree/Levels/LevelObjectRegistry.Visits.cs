using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A VISIT of a level: the same scene come back to later in the story, with some things gone and some new. A
    /// manifest whose <see cref="visitOf"/> names the level's own manifest holds only what this visit adds, and
    /// <see cref="hidden"/> names the level's rows it takes away. What stands in the level on the visit is the level's
    /// rows less the hidden, then the visit's own (<see cref="CollectRows"/>); <see cref="Row"/> finds one the same way.
    /// </summary>
    public sealed partial class LevelObjectRegistry
    {
        [Tooltip("The level's own manifest this one is a visit of. Its rows stand here too, less the hidden ones; "
            + "this manifest holds only what the visit adds. Empty: this is a level's own manifest.")]
        public LevelObjectRegistry visitOf;

        [Tooltip("Ids of the level's rows that are gone on this visit.")]
        public List<string> hidden = new List<string>();

        /// <summary>Whether this is a visit of another manifest.</summary>
        public bool isVisit => visitOf != null && visitOf != this;

        /// <summary>The level's own manifest: the one this visits, else this.</summary>
        public LevelObjectRegistry levelManifest => isVisit ? visitOf : this;

        /// <summary>Whether the level's <paramref name="row"/> is gone on this visit.</summary>
        public bool Hides(LevelObjectDef row) => row != null && Hides(row.id);

        public bool Hides(string id) => isVisit && !string.IsNullOrEmpty(id) && hidden.Contains(id);

        /// <summary>A row standing on this visit, by id, else by name: this manifest's own, else the level's unless
        /// hidden here. Null when none is.</summary>
        public LevelObjectDef Row(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName))
                return null;
            var own = (FindById(idOrName) ?? FindByName(idOrName)) as LevelObjectDef;
            if (own != null || !isVisit)
                return own;
            var row = (visitOf.FindById(idOrName) ?? visitOf.FindByName(idOrName)) as LevelObjectDef;
            return row != null && !Hides(row) ? row : null;
        }

        /// <summary>Every row standing on this visit: the level's in their order, less the hidden, then this
        /// manifest's own. A level's own manifest: its rows.</summary>
        public void CollectRows(List<LevelObjectDef> into)
        {
            if (into == null)
                return;
            if (isVisit)
            {
                foreach (LevelObjectDef row in visitOf.entries)
                {
                    if (row != null && !Hides(row))
                        into.Add(row);
                }
            }
            foreach (LevelObjectDef row in entries)
            {
                if (row != null)
                    into.Add(row);
            }
        }

        /// <summary>The manifest a row standing on this visit belongs to: this one, or the level's.</summary>
        public LevelObjectRegistry OwnerOf(LevelObjectDef row) =>
            isVisit && row != null && !entries.Contains(row) ? visitOf : this;
    }
}
