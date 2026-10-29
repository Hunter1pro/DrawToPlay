using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>One field of every reference kind, for the drawers.</summary>
    internal sealed class PickStubTask : StateTreeTaskAsset
    {
        public StateTreeKeyField key = new StateTreeKeyField("not.declared.anywhere");

        [PlacementId] public string near = "p.gone";

        [WorldTag] public string tag = "nobody-has-this-tag";

        [RowName(typeof(LevelObjectDef))] public string level = "no-such-row";

        [KeyName] public string named = "";

        [ScopeId] public string scopeId = "";

        [SubTreeState] public string state = "";

        [HumanText] public string line = "Hello";

        public StateTreeEntryRef<LevelObjectDef> row = new StateTreeEntryRef<LevelObjectDef>();
    }

    /// <summary>A row with a key, for a parameter derived from a picked row.</summary>
    [Serializable]
    internal sealed class PickStubRow : StateTreeRegistryEntry
    {
        public StateTreeKeyField answer = new StateTreeKeyField();
    }

    internal sealed class PickStubRegistry : StateTreeRegistry<PickStubRow>
    {
    }

    /// <summary>A template's task: a row, a key, a free string (the shape the rule forbids).</summary>
    internal sealed class PickStubStepTask : StateTreeTaskAsset
    {
        public StateTreeEntryRef<PickStubRow> choice = new StateTreeEntryRef<PickStubRow>();

        public StateTreeKeyField key = new StateTreeKeyField();

        public string loose = "";

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime) => StateTreeStatus.Running;
    }

    internal sealed class PickStubContext : StateTreeTaskAsset
    {
        [RowName(typeof(PickStubRow))] public string level = "";
    }

    /// <summary>
    /// "A parameter is a reference to something — never a string": the guard over every task and
    /// condition, the drawers that make a reference a pick (not a text box), and the mission
    /// inspector's derived parameters.
    /// </summary>
    [TestFixture]
    public sealed class StateTreePickTests
    {
        private const string k_Folder = "Assets/__PickTests";

        /// <summary>The free strings the package still has, each waiting for a catalog that does not
        /// exist yet. The list may only shrink: a new free string fails the guard, a fixed one fails
        /// <see cref="TheDebts_AreStillDebts"/> until it is struck off.</summary>
        private static readonly Dictionary<string, string> k_Debts = new Dictionary<string, string>
        {
            { "AfloatTask.visualChild", "a child of the actor's prefab, by name — no prefab is known to the task" },
            { "AfloatTask.hiddenChild", "the same" },
            { "BroadsideTargetTask.rangeAttribute", "a placement attribute name — attributes have no catalog yet" },
            { "BroadsideTargetTask.arcAttribute", "the same" },
            { "FireCueTask.cueName", "an animation cue — cues have no catalog yet" },
            { "UiCallTask.verb", "a verb of the view's vocabulary — views do not declare verbs yet" },
            { "UiCallTask.argument", "the verb's argument — follows the verb" },
            { "ObjectRequestTask.request", "a request the target's def declares — the def is only known at run time" },
        };

        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object junk in m_Junk)
            {
                if (junk != null && !AssetDatabase.Contains(junk))
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
            AssetDatabase.DeleteAsset(k_Folder);
        }

        // ---- the guard ----------------------------------------------------------------------

        private static IEnumerable<Type> PackageAtoms()
        {
            foreach (Type root in new[] { typeof(StateTreeTaskAsset), typeof(StateTreeConditionAsset) })
            {
                foreach (Type type in TypeCache.GetTypesDerivedFrom(root))
                {
                    string assembly = type.Assembly.GetName().Name;
                    if (!type.IsAbstract && assembly == "PowerOfFire.DrawToPlay")
                        yield return type;
                }
            }
        }

        [Test]
        public void EveryTaskAndCondition_HoldsNoFreeTextReference()
        {
            var free = new List<string>();
            foreach (Type type in PackageAtoms())
            {
                foreach (FieldInfo field in StateTreeStringRule.FreeFields(type))
                {
                    string name = type.Name + "." + field.Name;
                    if (!k_Debts.ContainsKey(name))
                        free.Add(name);
                }
            }
            CollectionAssert.IsEmpty(free, "A string a task or condition holds names something, so it is picked: give "
                + "it a picker attribute ([PlacementId], [WorldTag], [RowName], [KeyName], [ScopeId], …), make it a "
                + "StateTreeKeyField or a StateTreeEntryRef, or — only for words a person reads — mark it [HumanText].");
        }

        [Test]
        public void TheDebts_AreStillDebts()
        {
            var atoms = PackageAtoms().ToDictionary(t => t.Name);
            foreach (string debt in k_Debts.Keys)
            {
                string[] parts = debt.Split('.');
                Assert.IsTrue(atoms.TryGetValue(parts[0], out Type type), debt + ": the type is gone — strike the debt");
                Assert.IsTrue(StateTreeStringRule.FreeFields(type).Any(f => f.Name == parts[1]),
                    debt + " has a picker now — strike it off the debts");
            }
        }

        [Test]
        public void TheRule_SaysWhatEachStringIs()
        {
            FieldInfo F(string name) => typeof(PickStubTask).GetField(name);
            Assert.AreEqual(StateTreeStringKind.Picked, StateTreeStringRule.Classify(F("near")));
            Assert.AreEqual(StateTreeStringKind.Picked, StateTreeStringRule.Classify(F("tag")));
            Assert.AreEqual(StateTreeStringKind.Picked, StateTreeStringRule.Classify(F("level")));
            Assert.AreEqual(StateTreeStringKind.Picked, StateTreeStringRule.Classify(F("named")));
            Assert.AreEqual(StateTreeStringKind.Picked, StateTreeStringRule.Classify(F("scopeId")));
            Assert.AreEqual(StateTreeStringKind.HumanText, StateTreeStringRule.Classify(F("line")));
            Assert.AreEqual(StateTreeStringKind.NotString, StateTreeStringRule.Classify(F("key")));
            Assert.AreEqual(StateTreeStringKind.Free, StateTreeStringRule.Classify(typeof(PickStubStepTask).GetField("loose")));
        }

        // ---- the drawers --------------------------------------------------------------------

        [TestCase("key", typeof(StateTreeKeyField))]
        [TestCase("near", typeof(PlacementIdAttribute))]
        [TestCase("tag", typeof(WorldTagAttribute))]
        [TestCase("level", typeof(RowNameAttribute))]
        [TestCase("named", typeof(KeyNameAttribute))]
        [TestCase("scopeId", typeof(ScopeIdAttribute))]
        [TestCase("state", typeof(SubTreeStateAttribute))]
        public void EachReferenceKind_DrawsAPicker_NotATextBox(string fieldName, Type drawnFor)
        {
            var task = Junk(ScriptableObject.CreateInstance<PickStubTask>());
            var serialized = new SerializedObject(task);
            SerializedProperty property = serialized.FindProperty(fieldName);
            FieldInfo field = typeof(PickStubTask).GetField(fieldName);

            VisualElement drawn = Draw(drawnFor, field, property);

            Assert.IsNotNull(drawn.Q<StateTreePickField>(), fieldName + " is drawn as a pick");
            Assert.IsNull(drawn.Q<TextField>(), fieldName + " has no text box");
        }

        [Test]
        public void AValueThatNamesNothing_IsShownAsAnError_NotAsAGoodPick()
        {
            var task = Junk(ScriptableObject.CreateInstance<PickStubTask>());
            var serialized = new SerializedObject(task);
            foreach (string name in new[] { "near", "level", "key" })
            {
                FieldInfo field = typeof(PickStubTask).GetField(name);
                Type drawnFor = name == "key" ? typeof(StateTreeKeyField) : field.GetCustomAttributes(true).OfType<UnityEngine.PropertyAttribute>().First().GetType();
                VisualElement drawn = Draw(drawnFor, field, serialized.FindProperty(name));
                Assert.IsTrue(drawn.Q<StateTreePickField>().missing, name + " names nothing and says so");
            }
        }

        [Test]
        public void AKeyCatalogConstant_IsOffered_AndPickedByName()
        {
            IReadOnlyList<StateTreeKeyOffer> code = StateTreeKeyOffers.CodeKeys();
            Assert.IsTrue(code.Any(k => k.name == PickStubKeys.Opened), "the constant is offered");
            Assert.IsFalse(code.Any(k => k.name == PickStubKeys.Request), "a constant declared for something else is not");

            var task = Junk(ScriptableObject.CreateInstance<PickStubTask>());
            task.key = new StateTreeKeyField(PickStubKeys.Opened);
            var serialized = new SerializedObject(task);
            VisualElement drawn = Draw(typeof(StateTreeKeyField), typeof(PickStubTask).GetField("key"), serialized.FindProperty("key"));
            Assert.IsFalse(drawn.Q<StateTreePickField>().missing, "a catalog key is a good pick");
        }

        [Test]
        public void DeclaringAKey_PutsItOnTheTree_Once()
        {
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            StateTreeKeyDeclaration first = StateTreeKeyOffers.Declare(tree, "door.opened", StateTreeKeyKind.String, "t");
            StateTreeKeyDeclaration again = StateTreeKeyOffers.Declare(tree, "door.opened", StateTreeKeyKind.String, "t");
            Assert.AreSame(first, again);
            Assert.AreEqual(1, tree.keys.Count);
            Assert.IsTrue(StateTreeKeyOffers.TryFind(StateTreeKeyOffers.For(tree, tree), "door.opened", out StateTreeKeyOffer offer));
            Assert.AreEqual(first.id, offer.id);
        }

        // ---- mission mode: derived and picked parameters ----------------------------------

        [Test]
        public void ADerivedParameter_FollowsItsRow_OrItsContext_AndIsWrittenIdempotently()
        {
            Fixture fixture = Build();
            RunSubTreeTask run = (RunSubTreeTask)fixture.step.tasks[0];

            StateTreeSchemaRules.SetParameter(run, fixture.template.parameters[0], "yes-no", 0f, "row.yes-no", "t");
            Assert.Greater(StateTreeSchemaRules.ApplyDerived(fixture.host, fixture.schema), 0);

            List<StateTreeStepParameter> parameters = StateTreeSchemaRules.Parameters(fixture.kind, fixture.step);
            Assert.AreEqual("room.answered", parameters.Single(p => p.declaration.name == "talk.answer").stringValue,
                "the answer key is the picked row's");
            Assert.AreEqual("hall", parameters.Single(p => p.declaration.name == "talk.level").stringValue,
                "the level is the enclosing group's");
            Assert.IsTrue(parameters.Single(p => p.declaration.name == "talk.answer").isDerived);
            Assert.AreEqual(0, StateTreeSchemaRules.ApplyDerived(fixture.host, fixture.schema), "a second pass changes nothing");

            fixture.rows.entries[0].answer = new StateTreeKeyField("room.answered.again");
            StateTreeSchemaRules.ApplyDerived(fixture.host, fixture.schema);
            Assert.AreEqual("room.answered.again", StateTreeSchemaRules.Parameters(fixture.kind, fixture.step)
                .Single(p => p.declaration.name == "talk.answer").stringValue, "the row changed, the step follows");
        }

        [Test]
        public void TheMissionInspector_PicksAKey_ShowsADerivedValueReadOnly_AndRefusesAFreeString()
        {
            Fixture fixture = Build();
            StateTreeSchemaRules.SetParameter((RunSubTreeTask)fixture.step.tasks[0], fixture.template.parameters[0],
                "yes-no", 0f, "row.yes-no", "t");

            var root = new ScrollView();
            var inspector = new StateTreeMissionInspector(root, null, null, null, null);
            inspector.Rebuild(fixture.host, fixture.schema, fixture.step, StateTreeSchemaRules.Validate(fixture.host, fixture.schema));

            List<string> textBoxes = root.Query<TextField>().ToList().Select(t => t.label).ToList();
            CollectionAssert.AreEquivalent(new[] { "Name", "State" }, textBoxes,
                "only the node's own name is typed — every parameter is a pick");
            List<string> picks = root.Query<StateTreePickField>().ToList()
                .Select(p => p.Q<Label>().text).ToList();
            CollectionAssert.Contains(picks, "Choice");
            CollectionAssert.Contains(picks, "Fact");
            CollectionAssert.Contains(picks, "Loose", "a free string is shown, not typed");
            Assert.IsNotNull(root.Query<HelpBox>().ToList().FirstOrDefault(h => h.text.Contains("free-text")),
                "and the inspector says what is wrong with it");
            List<string> labels = root.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.IsTrue(labels.Contains("room.answered"), "the derived answer is shown: " + string.Join(" | ", labels));
            Assert.IsTrue(labels.Any(l => l.Contains("from the Choice 'yes-no'")), "and where it comes from");
        }

        // ---- helpers ------------------------------------------------------------------------

        private sealed class Fixture
        {
            public StateTreeAsset host;
            public StateTreeAsset template;
            public StateTreeSchema schema;
            public StateTreeSchemaKind kind;
            public StateTreeNodeAsset step;
            public PickStubRegistry rows;
        }

        private Fixture Build()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__PickTests");
            var fixture = new Fixture();
            fixture.rows = ScriptableObject.CreateInstance<PickStubRegistry>();
            fixture.rows.entries.Add(new PickStubRow { id = "row.yes-no", name = "yes-no", answer = new StateTreeKeyField("room.answered") });
            fixture.rows.entries.Add(new PickStubRow { id = "row.hall", name = "hall" });
            AssetDatabase.CreateAsset(fixture.rows, k_Folder + "/Rows.asset");

            var template = ScriptableObject.CreateInstance<StateTreeAsset>();
            template.treeKind = "task";
            template.registries.Add(fixture.rows);
            template.parameters.Add(new GraphTaskParameter { name = "talk.choice", id = "choice", kind = GraphTaskParameterKind.String });
            template.parameters.Add(new GraphTaskParameter
            {
                name = "talk.answer", id = "answer", kind = GraphTaskParameterKind.String,
                derived = new GraphTaskParameterDerivation
                    { source = GraphTaskParameterDerivation.Source.Row, parameterId = "choice", member = "answer" }
            });
            template.parameters.Add(new GraphTaskParameter
            {
                name = "talk.level", id = "level", kind = GraphTaskParameterKind.String,
                derived = new GraphTaskParameterDerivation { source = GraphTaskParameterDerivation.Source.Context, member = "level" }
            });
            template.parameters.Add(new GraphTaskParameter { name = "talk.fact", id = "fact", kind = GraphTaskParameterKind.String });
            template.parameters.Add(new GraphTaskParameter { name = "talk.loose", id = "loose", kind = GraphTaskParameterKind.String });
            AssetDatabase.CreateAsset(template, k_Folder + "/Talk.asset");
            var state = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            state.nodeId = "ask";
            AssetDatabase.AddObjectToAsset(state, template);
            var task = ScriptableObject.CreateInstance<PickStubStepTask>();
            AssetDatabase.AddObjectToAsset(task, template);
            state.tasks.Add(task);
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "choice", parameterId = "choice" });
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "key", parameterId = "answer" });
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "key", parameterId = "fact" });
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "loose", parameterId = "loose" });
            template.root = state;
            fixture.template = template;

            fixture.schema = Junk(ScriptableObject.CreateInstance<StateTreeSchema>());
            fixture.schema.treeKind = "pick-test";
            fixture.schema.topKinds.Add("level");
            fixture.schema.kinds.Add(new StateTreeSchemaKind
            {
                kind = "level", children = { StateTreeSchema.StepToken }, contextTask = typeof(PickStubContext).FullName
            });
            fixture.kind = new StateTreeSchemaKind { kind = "talk", label = "Talk", step = true, template = template };
            fixture.schema.kinds.Add(fixture.kind);

            var host = ScriptableObject.CreateInstance<StateTreeAsset>();
            host.treeKind = "pick-test";
            AssetDatabase.CreateAsset(host, k_Folder + "/Host.asset");
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            AssetDatabase.AddObjectToAsset(root, host);
            host.root = root;
            fixture.host = host;
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(host, fixture.schema, root, "level");
            ((PickStubContext)level.tasks[0]).level = "hall";
            fixture.step = StateTreeSchemaRules.AddNode(host, fixture.schema, level, "talk");
            return fixture;
        }

        /// <summary>The drawer Unity would pick for the field, built the way a PropertyField builds it.</summary>
        private static VisualElement Draw(Type drawnFor, FieldInfo field, SerializedProperty property)
        {
            Type drawerType = TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>().First(t =>
                t.GetCustomAttributes<CustomPropertyDrawer>(false).Any(a =>
                    (Type)typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(a) == drawnFor));
            var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType, true);
            typeof(PropertyDrawer).GetField("m_FieldInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(drawer, field);
            object attribute = field.GetCustomAttributes(true).FirstOrDefault(a => a.GetType() == drawnFor);
            if (attribute != null)
                typeof(PropertyDrawer).GetField("m_Attribute", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(drawer, attribute);
            return drawer.CreatePropertyGUI(property);
        }

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }
    }

    [KeyCatalog]
    internal static class PickStubKeys
    {
        public const string Opened = "pick.test.opened";

        [System.ComponentModel.Description("declared for something else")]
        public const string Request = "pick.test.request";
    }
}
