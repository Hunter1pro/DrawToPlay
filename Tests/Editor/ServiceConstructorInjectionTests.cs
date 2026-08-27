using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A subsystem that names its collaborator in its constructor.</summary>
    internal sealed class NeedsWorld : StateTreeService
    {
        public readonly WorldService world;

        public NeedsWorld(StateTreeContextHost scope, ServiceDef definition, WorldService world)
            : base(scope, definition)
        {
            this.world = world;
        }
    }

    /// <summary>
    /// After (scope, def), a constructor parameter is a subsystem handed from the scope at
    /// install — so the installer's list is dependency order, and a collaborator that is not
    /// there yet is an install failure that names it, not a null later.
    /// </summary>
    [TestFixture]
    public sealed class ServiceConstructorInjectionTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeContextHost m_Scope;
        private StateTreeServiceInstaller m_Installer;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Scope") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Scope = go.AddComponent<StateTreeContextHost>();
            m_Scope.kind = StateTreeContextKind.Root;
            m_Scope.autoStart = false;
            m_Scope.Register();
            m_Installer = go.AddComponent<StateTreeServiceInstaller>();
            m_Installer.scope = m_Scope;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Scope != null)
                m_Scope.Unregister();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ACollaboratorInstalledBefore_IsHandedToTheConstructor()
        {
            m_Installer.Install(Def("world", nameof(WorldService)));
            StateTreeSubsystem built = m_Installer.Install(Def("needs", nameof(NeedsWorld)));

            Assert.That(built, Is.Not.Null);
            var service = built.service as NeedsWorld;
            Assert.That(service, Is.Not.Null);
            Assert.That(service.world, Is.SameAs(m_Scope.GetService<WorldService>()),
                "the very instance the scope resolves, not a second one");
        }

        [Test]
        public void ACollaboratorNotInstalledYet_FailsTheInstall_NamingIt()
        {
            LogAssert.Expect(LogType.Error, new Regex("NeedsWorld.*needs a 'WorldService'"));
            Assert.That(m_Installer.Install(Def("needs", nameof(NeedsWorld))), Is.Null,
                "a subsystem missing a required collaborator is not installed at all");
        }

        private ServiceDef Def(string serviceName, string typeName)
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = serviceName;
            def.serviceTypeName = typeName;
            def.scope = StateTreeContextKind.Root;
            m_Junk.Add(def);
            return def;
        }
    }
}
