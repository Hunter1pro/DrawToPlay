using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHERE THE THREE DESCRIPTIONS OF ONE SUBSYSTEM DISAGREE (M37.4) — the sketch, the def and
    /// the class will, the first week, and the class is never rewritten to make them agree. So
    /// the disagreement is a FINDING, naming both sides, read on demand.
    ///
    /// Def-vs-class works for every def in the project, sketched or not: an action the def
    /// serves that the class does not declare is a request that will be refused at the door;
    /// an action the class declares that no def row serves is a verb nobody can ask for. Pure,
    /// so the tests can ask.
    /// </summary>
    internal static class SubsystemDrift
    {
        internal static List<SketchFinding> Find(ServiceDef def, SubsystemSketch sketch)
        {
            var findings = new List<SketchFinding>();
            if (def == null)
            {
                if (sketch != null)
                    findings.Add(Warn("def", "not generated yet"));
                return findings;
            }

            Type type = def.serviceType;
            if (type == null)
            {
                findings.Add(Block("class", "the def names '" + def.serviceTypeName
                    + "', which the project does not have"));
            }
            else
            {
                DefVersusClass(def, type, findings);
            }
            if (sketch != null)
                SketchVersusDef(sketch, def, type, findings);
            return findings;
        }

        // ---- def ↔ class ------------------------------------------------------------------

        private static void DefVersusClass(ServiceDef def, Type type, List<SketchFinding> into)
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (ServiceActionContractAttribute contract in
                type.GetCustomAttributes(typeof(ServiceActionContractAttribute), true))
            {
                if (!string.IsNullOrEmpty(contract.action))
                    declared.Add(contract.action);
            }

            var served = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row == null || string.IsNullOrEmpty(row.action))
                    continue;   // a flow-served request names a state, not an action
                served.Add(row.action);
                if (!declared.Contains(row.action))
                {
                    into.Add(Block("def ↔ class", "'" + row.key + "' is served by action '"
                        + row.action + "', which " + type.Name + " does not declare — it will be "
                        + "refused at the door"));
                }
            }
            foreach (string action in declared)
            {
                if (!served.Contains(action))
                {
                    into.Add(Warn("def ↔ class", type.Name + " declares action '" + action
                        + "', and no row on the def serves it — nobody can ask for it"));
                }
            }

            IReadOnlyList<ServiceSettings.Declared> knobs = ServiceSettings.DeclaredOn(type);
            for (int i = 0; i < def.settings.values.Count; i++)
            {
                ServiceSettingValue row = def.settings.values[i];
                if (row != null && ServiceSettings.Find(type, row.name) == null)
                {
                    into.Add(Block("def ↔ class", "the def tunes '" + row.name + "', which "
                        + type.Name + " no longer declares — refused at construction"));
                }
            }
        }

        // ---- sketch ↔ def / class ---------------------------------------------------------

        private static void SketchVersusDef(SubsystemSketch sketch, ServiceDef def, Type type,
            List<SketchFinding> into)
        {
            if (def.scope != sketch.scope)
                into.Add(Warn("sketch ↔ def", "the sketch says " + sketch.scope + ", the def says "
                    + def.scope + " — Regenerate def"));

            Compare("request", Keys(sketch.requests, r => r.key), Keys(def.requests, r => r.key), into);
            // Announcements live on the CLASS (M41.1): the sketch is compared with what the
            // class declares, which is what the def shows.
            var announced = new HashSet<string>();
            foreach (DeclaredApi.Announced row in DeclaredApi.Announcements(def.name))
                announced.Add(row.key);
            Compare("announcement", Keys(sketch.announcements, a => a.key), announced, into);
            Compare("spawn", Keys(sketch.spawns, s => s.entryName), Keys(def.spawns, s => s.entryName), into);
            Compare("attribute", Keys(sketch.attributes, a => a.entryName),
                Keys(def.attributes, a => a.Name), into);

            if (type == null)
                return;

            // THE CLASS'S CONSTANTS are the sketch's actions, spelt once each.
            var constants = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Reflection.FieldInfo field in type.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                    constants.Add((string)field.GetRawConstantValue());
            }
            for (int i = 0; i < sketch.requests.Count; i++)
            {
                string action = sketch.requests[i]?.action;
                if (!string.IsNullOrEmpty(action) && !constants.Contains(action))
                {
                    into.Add(Warn("sketch ↔ class", "the sketch's action '" + action + "' is no "
                        + "constant on " + type.Name + " — renamed in code?"));
                }
            }
            for (int i = 0; i < sketch.settings.Count; i++)
            {
                string name = sketch.settings[i]?.name;
                if (!string.IsNullOrEmpty(name) && ServiceSettings.Find(type, name) == null)
                {
                    into.Add(Warn("sketch ↔ class", "the sketch's setting '" + name + "' is not a "
                        + "[ServiceSetting] on " + type.Name));
                }
            }
            if (!string.IsNullOrEmpty(sketch.capabilityName))
            {
                var implemented = false;
                foreach (Type face in type.GetInterfaces())
                    if (face.Name == sketch.capabilityName) implemented = true;
                if (!implemented)
                {
                    into.Add(Warn("sketch ↔ class", type.Name + " does not implement "
                        + sketch.capabilityName + " — consumers asking for the capability will find nothing"));
                }
            }
        }

        private static void Compare(string what, HashSet<string> inSketch, HashSet<string> onDef,
            List<SketchFinding> into)
        {
            foreach (string key in inSketch)
            {
                if (!onDef.Contains(key))
                    into.Add(Warn("sketch ↔ def", what + " '" + key + "' is sketched but not on the def — Regenerate def"));
            }
            foreach (string key in onDef)
            {
                if (!inSketch.Contains(key))
                    into.Add(Warn("sketch ↔ def", what + " '" + key + "' is on the def but not in the sketch — edited by hand?"));
            }
        }

        private static HashSet<string> Keys<T>(List<T> rows, Func<T, string> key)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                string k = rows[i] != null ? key(rows[i]) : null;
                if (!string.IsNullOrEmpty(k))
                    keys.Add(k);
            }
            return keys;
        }

        private static SketchFinding Block(string section, string message)
        {
            return new SketchFinding { section = section, message = message, blocks = true };
        }

        private static SketchFinding Warn(string section, string message)
        {
            return new SketchFinding { section = section, message = message, blocks = false };
        }
    }
}
