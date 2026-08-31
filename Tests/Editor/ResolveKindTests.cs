using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>0.3.2: an NPC's host is Character-kind, so Resolve(Player) stays unique with
    /// actors in the level.</summary>
    [TestFixture]
    public sealed class ResolveKindTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            m_Objects.Clear();
            m_Hosts.Clear();
        }

        [Test]
        public void ACharacterHost_DoesNotShadowThePlayer()
        {
            StateTreeContextHost level = Host("Level", StateTreeContextKind.Level, null);
            StateTreeContextHost player = Host("Player", StateTreeContextKind.Player, level);
            Host("Npc", StateTreeContextKind.Character, level);

            Assert.AreSame(player,
                StateTreeContextHost.Resolve(level.gameObject, StateTreeContextKind.Player),
                "one Player-kind host in the level resolves uniquely from anywhere");
        }

        private StateTreeContextHost Host(string hostName, StateTreeContextKind kind,
            StateTreeContextHost parent)
        {
            var go = new GameObject(hostName) { hideFlags = HideFlags.HideAndDontSave };
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }
    }
}
