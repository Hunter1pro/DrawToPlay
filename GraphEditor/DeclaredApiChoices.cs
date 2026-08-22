using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// THE BOOKKEEPING a declared-API node shares (M38.1): which pins feed which lists, what each
    /// list should be, and the three questions <see cref="ChoicePortRefresh"/> asks.
    ///
    /// A node registers its dependent pins as (pin, sources → choices). The sources are read
    /// from the node's other pins in <see cref="AdoptChoiceSources"/> — the only moment pins
    /// are readable — into a remembered snapshot the definition reads back. Port definition runs
    /// with the node half-built and cannot read a pin; this is the whole reason the snapshot
    /// exists, learned the hard way by the Registry Entry node.
    /// </summary>
    public sealed class DeclaredApiChoices
    {
        private sealed class Dependent
        {
            public string pin;
            public string[] sourcePins;
            public Func<string[], List<string>> choices;
        }

        private readonly Node m_Node;
        private readonly List<Dependent> m_Dependents = new List<Dependent>();
        private readonly Dictionary<string, string> m_Remembered = new Dictionary<string, string>();

        public DeclaredApiChoices(Node node)
        {
            m_Node = node;
        }

        /// <summary>Declare that <paramref name="pin"/>'s list is <paramref name="choices"/> of the
        /// values of <paramref name="sourcePins"/>.</summary>
        public void DependsOn(string pin, Func<string[], List<string>> choices, params string[] sourcePins)
        {
            m_Dependents.Add(new Dependent { pin = pin, sourcePins = sourcePins, choices = choices });
        }

        /// <summary>The list a pin should offer, from the REMEMBERED sources — safe in port
        /// definition.</summary>
        public List<string> Remembered(string pin)
        {
            Dependent dependent = Find(pin);
            return dependent != null ? dependent.choices(Snapshot(dependent, remembered: true)) : new List<string>();
        }

        /// <summary>The list a pin should offer, from the LIVE pins — only outside definition.</summary>
        public List<string> Wanted(string pin)
        {
            Dependent dependent = Find(pin);
            return dependent != null ? dependent.choices(Snapshot(dependent, remembered: false)) : new List<string>();
        }

        public bool AdoptChoiceSources()
        {
            var changed = false;
            for (int i = 0; i < m_Dependents.Count; i++)
            {
                string[] sources = m_Dependents[i].sourcePins;
                for (int s = 0; s < sources.Length; s++)
                {
                    string live = ReadPin(sources[s]);
                    if (!m_Remembered.TryGetValue(sources[s], out string known) || known != live)
                    {
                        m_Remembered[sources[s]] = live;
                        changed = true;
                    }
                }
            }
            return changed;
        }

        public bool IsStale()
        {
            for (int i = 0; i < m_Dependents.Count; i++)
            {
                IPort port = m_Node.GetInputPortByName(m_Dependents[i].pin);
                if (!PortChoices.Matches(port, Wanted(m_Dependents[i].pin)))
                    return true;
            }
            return false;
        }

        /// <summary>A value from the list the author just switched AWAY from is a leftover, and a
        /// dropdown shown a value outside its list re-clamps to some other row and shows THAT.</summary>
        public void DropUnoffered()
        {
            for (int i = 0; i < m_Dependents.Count; i++)
            {
                string pin = m_Dependents[i].pin;
                List<string> offered = Wanted(pin);
                if (offered.Count == 0)
                    continue;   // free text: anything goes
                string current = ReadPin(pin);
                if (string.IsNullOrEmpty(current) || offered.Contains(current))
                    continue;
                IPort port = m_Node.GetInputPortByName(pin);
                if (port != null)
                    LibraryParameterPorts.TryWriteValue(port, typeof(string), string.Empty);
            }
        }

        private Dependent Find(string pin)
        {
            for (int i = 0; i < m_Dependents.Count; i++)
            {
                if (m_Dependents[i].pin == pin)
                    return m_Dependents[i];
            }
            return null;
        }

        private string[] Snapshot(Dependent dependent, bool remembered)
        {
            var values = new string[dependent.sourcePins.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = remembered
                    ? (m_Remembered.TryGetValue(dependent.sourcePins[i], out string known) ? known : "")
                    : ReadPin(dependent.sourcePins[i]);
            }
            return values;
        }

        private string ReadPin(string pin)
        {
            try
            {
                IPort port = m_Node.GetInputPortByName(pin);
                return port != null
                    && LibraryParameterPorts.TryReadValue(port, typeof(string), out object value)
                    && value is string text
                    ? text
                    : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
