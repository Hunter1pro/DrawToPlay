using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A part a def can name to carry a colour, added to a body that has none.</summary>
    internal sealed class TestTintView : MonoBehaviour, IWorldTintable
    {
        public Color worn = Color.white;
        public void SetTint(Color tint) => worn = tint;
    }

    /// <summary>
    /// THE TINT A DEF PUTS ON A BODY, and the lookup behind it.
    ///
    /// A def may name the component to add when a body carries no <see cref="IWorldTintable"/> of
    /// its own, so a kind that has never been painted does not need its prefab opened first. The
    /// name is a plain type name, which misses <c>Type.GetType</c>, and the fallback used to walk
    /// every assembly in the AppDomain asking each for every type it holds — 755 ms cold over 354
    /// assemblies and 55 789 types in the sandbox, ONCE PER SPAWNED ROW. On the Gully, whose 60
    /// tinting rows made a level's first frame cost 932 ms, that was the whole of the freeze the
    /// player felt travelling from one place to another.
    ///
    /// So the lookup is cached, misses included, on a static a domain reload clears. These tests
    /// pin both halves: the body still gets its part and its colour (the second one from the
    /// cache, which is where a bad cache would show), and the name is really remembered.
    /// </summary>
    [TestFixture]
    public sealed class ServiceBodyTintTests
    {
        private readonly List<UnityEngine.Object> m_Junk = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    UnityEngine.Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ADefThatTints_AddsThePartItNames_AndTheSecondBodyIsTheSameAsTheFirst()
        {
            ServiceDef def = Tinting(nameof(TestTintView));
            for (int round = 0; round < 2; round++)
            {
                GameObject made = Build(def, round);
                var worn = made.GetComponent<TestTintView>();
                Assert.That(worn, Is.Not.Null,
                    "round " + round + ": the def names the part to add when the prefab has none");
                Assert.That(worn.worn, Is.EqualTo(Color.red),
                    "round " + round + ": and it wears the colour of the row it is an instance of");
            }
        }

        [Test]
        public void TheTypeADefNames_IsRememberedAfterTheFirstBody_NotLookedUpAgainForEveryRow()
        {
            // The cache is a STATIC and lives as long as the domain, so a sibling test may
            // already have filled it. Emptied here rather than assumed empty.
            Forget();
            ServiceDef def = Tinting(nameof(TestTintView));
            Assert.That(Remembered(nameof(TestTintView)), Is.False, "emptied for this test");

            Build(def, 0);

            Assert.That(Remembered(nameof(TestTintView)), Is.True,
                "the name is remembered, so the 60th row of a level costs a dictionary lookup and "
                + "not a walk of every assembly in the AppDomain");
        }

        [Test]
        public void ADefNamingAPartThatDoesNotExist_IsRememberedAsNothing_AndBuildsAnyway()
        {
            const string missing = "NoSuchTintViewAnywhere";
            ServiceDef def = Tinting(missing);

            GameObject first = Build(def, 0);
            Assert.That(first, Is.Not.Null, "a name that matches nothing does not stop the body");
            Assert.That(Remembered(missing), Is.True,
                "and the MISS is remembered too — otherwise a def with a typo pays the walk on every row");

            GameObject second = Build(def, 1);
            Assert.That(second, Is.Not.Null, "and the second body is built the same way");
        }

        /// <summary>Has the factory remembered this name, hit or miss?</summary>
        private static bool Remembered(string name) => Cache().ContainsKey(name);

        /// <summary>Empty what the factory remembers, so a test can watch it fill.</summary>
        private static void Forget() => Cache().Clear();

        private static Dictionary<string, Type> Cache()
        {
            FieldInfo field = typeof(ServiceBodyFactory).GetField("s_TypesByName",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "ServiceBodyFactory remembers the types it has looked up");
            var byName = (Dictionary<string, Type>)field.GetValue(null);
            Assert.That(byName, Is.Not.Null);
            return byName;
        }

        /// <summary>A def whose bodies wear the colour of the row they are an instance of, through
        /// a part named — but not present on — the prefab.</summary>
        private ServiceDef Tinting(string part)
        {
            var registry = ScriptableObject.CreateInstance<ItemRegistry>();
            registry.entries.Add(new ItemDef { id = "item.red", name = "red", tint = Color.red });
            m_Junk.Add(registry);

            var prefab = new GameObject("Pickup") { hideFlags = HideFlags.HideAndDontSave };
            prefab.AddComponent<WorldObjectBehaviour>();
            m_Junk.Add(prefab);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "pickup";
            def.serviceName = "pickup";
            def.body.prefab = prefab;
            def.registry = registry;
            def.body.tintFromDefinition = true;
            def.body.tintPart = part;
            m_Junk.Add(def);
            return def;
        }

        private GameObject Build(ServiceDef def, int round)
        {
            GameObject made = ServiceBodyFactory.Build(def, new LevelObjectDef
            {
                id = "place." + round,
                entry = new LevelObjectEntryRef { entryName = "red" }
            }, null, Vector3.zero, Quaternion.identity);
            if (made != null)
                m_Junk.Add(made);
            return made;
        }
    }

    /// <summary>
    /// THE GRAPH WINDOW BOTH EDITOR TOOLS LOOK FOR. <c>GraphWindowPanelGuard</c> and
    /// <c>ChoiceDropdownSync</c> find their windows by asking for this type — internal to Graph
    /// Toolkit, so both name it as a string and resolve it once through <see cref="TypeCache"/>.
    /// Asking for the TYPE rather than for every EditorWindow in the process is what took the two
    /// of them from 263 ms to 27 ms of a comparable capture of an idle editor.
    ///
    /// The whole arrangement rests on that name still being the window's name. If Graph Toolkit
    /// renames it, both tools quietly find nothing and stop working — a refreshed dropdown never
    /// appears and a blank graph window is never restored, with no error anywhere. This test is
    /// the thing that says so instead.
    /// </summary>
    [TestFixture]
    public sealed class GraphWindowTypeTests
    {
        private const string k_GraphWindowTypeName =
            "Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp";

        [Test]
        public void TheGraphWindowType_IsStillCalledWhatTheEditorToolsAskFor()
        {
            Type found = null;
            foreach (Type candidate in TypeCache.GetTypesDerivedFrom<EditorWindow>())
            {
                if (candidate.FullName != k_GraphWindowTypeName)
                    continue;
                found = candidate;
                break;
            }
            Assert.That(found, Is.Not.Null,
                "GraphWindowPanelGuard and ChoiceDropdownSync both look for '" + k_GraphWindowTypeName
                + "'. If Graph Toolkit has renamed it they now find nothing, silently: a refreshed "
                + "dropdown never appears on a pin and a graph window that opens with every panel "
                + "hidden is never put right. Update the constant in both.");
        }
    }
}
