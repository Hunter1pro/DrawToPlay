using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Level object presets on throwaway assets: a linked row runs its preset's core with its own settings, and
    /// one it never touched follows the preset; an edit to the core reaches every row; "a new preset from this"
    /// forks and switches without changing what runs; a row without a preset is untouched; the palette shows
    /// presets under their kind; a new object's name keeps its level and kind fixed; the window hosts it all.
    /// </summary>
    [TestFixture]
    public sealed class LevelPresetTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private LevelObjectKindRegistry m_Kinds;
        private LevelObjectPresetRegistry m_Presets;
        private LevelObjectPresetDef m_Door;
        private StateTreeAsset m_Tree;
        private LevelObjectRegistry m_Manifest;
        private LevelContent m_Level;
        private LevelManifestWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            m_Kinds = Make<LevelObjectKindRegistry>();
            foreach (string kind in new[] { "door", "npc" })
                m_Kinds.entries.Add(new LevelObjectKindDef { id = "kind." + kind, name = kind });

            m_Tree = Make<StateTreeAsset>();
            m_Tree.name = "DoorTree";
            m_Tree.parameters.Add(new GraphTaskParameter { name = "reach", id = "reach", kind = GraphTaskParameterKind.Float, floatValue = 1f });
            m_Tree.parameters.Add(new GraphTaskParameter { name = "auto", id = "auto", kind = GraphTaskParameterKind.Bool });
            m_Tree.parameters.Add(new GraphTaskParameter { name = "speed", id = "speed", kind = GraphTaskParameterKind.Float, floatValue = 4f });

            m_Presets = Make<LevelObjectPresetRegistry>();
            m_Door = new LevelObjectPresetDef { id = "preset.door", name = "Door", tree = m_Tree };
            m_Door.kind.entryId = "kind.door";
            m_Door.kind.entryName = "door";
            m_Door.parameters.values.Add(Value("reach", 2.5f));
            m_Door.parameters.values.Add(Value("speed", 3f));
            m_Door.settings.AddRange(new[] { "reach", "auto" });
            m_Door.tags.Add(new LevelObjectTagRef { tag = "outline" });
            m_Door.facts.Add(new LevelObjectPresetFact { key = new StateTreeKeyField("door.opened"), meaning = "Opened" });
            m_Door.steps.Add("open");
            m_Presets.entries.Add(m_Door);

            m_Manifest = Make<LevelObjectRegistry>();
            m_Manifest.plane = LevelGroundPlane.XZ;
            m_Manifest.presets = m_Presets;

            m_Level = Make<LevelContent>();
            m_Level.name = "PresetTest" + System.Guid.NewGuid().ToString("N");
            m_Level.objects = m_Manifest;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string kind in new[] { "door", "npc", LevelManifestEdits.NoKind })
                EditorPrefs.DeleteKey(LevelManifestWindow.FoldKey(m_Level.name, kind));
            if (m_Window != null)
                Object.DestroyImmediate(m_Window);
            Undo.ClearUndo(m_Manifest);
            Undo.ClearUndo(m_Presets);
            foreach (Object junk in m_Junk)
            {
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
        }

        // ---- the core and the settings ------------------------------------------------------

        [Test]
        public void ALinkedRow_RunsThePresetsCore_TheRowItselfUntouched()
        {
            LevelObjectDef row = Linked("hall.door.1");

            LevelObjectDef runs = m_Manifest.Resolve(row);

            Assert.AreSame(m_Door, m_Manifest.PresetOf(row));
            Assert.AreNotSame(row, runs);
            Assert.AreEqual("door", runs.kind.entryName);
            Assert.AreSame(m_Tree, runs.tree);
            CollectionAssert.AreEqual(new[] { "outline" }, runs.tags.Select(t => t.tag).ToArray());
            Assert.AreEqual(2.5f, ValueOf(runs, "reach"));
            Assert.AreEqual(3f, ValueOf(runs, "speed"));
            Assert.AreEqual(row.id, runs.id);
            Assert.AreEqual(row.position, runs.position);
            Assert.IsNull(row.tree, "the row is never written");
            Assert.AreEqual(0, row.tags.Count);

            var tags = new List<string>();
            m_Level.CollectTags(tags);
            CollectionAssert.AreEquivalent(new[] { "door", "outline" }, tags, "what the level holds is what runs");
        }

        [Test]
        public void ASettingTheObjectSet_IsItsOwn_AnUntouchedOneFollowsThePreset_ACoreValueIsNeverItsOwn()
        {
            LevelObjectDef row = Linked("hall.door.1");
            GraphTaskParameter reach = m_Tree.parameters[0];
            GraphTaskParameter auto = m_Tree.parameters[1];

            LevelPresetEdits.SetSetting(m_Manifest, row, reach, "", 5f, "", "");
            row.parameters.values.Add(Value("speed", 9f));

            LevelObjectDef runs = m_Manifest.Resolve(row);
            Assert.AreEqual(5f, ValueOf(runs, "reach"), "its own value");
            Assert.AreEqual(3f, ValueOf(runs, "speed"), "speed is the core's, not a setting: the row cannot change it");
            Assert.IsNull(runs.parameters.values.Find(v => v.id == "auto"), "untouched, unset in the core: the tree's default");

            Assert.AreEqual(5f, LevelPresetEdits.Setting(m_Door, row, reach, out GraphTaskParameterOverride own).floatValue);
            Assert.IsNotNull(own);
            Assert.AreEqual(0f, LevelPresetEdits.Setting(m_Door, row, auto, out own).floatValue);
            Assert.IsNull(own);

            LevelPresetEdits.ResetSetting(m_Manifest, row, reach);
            Assert.AreEqual(2.5f, ValueOf(m_Manifest.Resolve(row), "reach"), "reset follows the preset again");
        }

        [Test]
        public void AnEditToTheCore_ReachesEveryObjectMadeFromIt()
        {
            LevelObjectDef first = Linked("hall.door.1");
            LevelObjectDef second = Linked("hall.door.2");
            LevelPresetEdits.SetSetting(m_Manifest, second, m_Tree.parameters[0], "", 7f, "", "");

            m_Door.parameters.values.Find(v => v.id == "speed").floatValue = 6f;
            m_Door.parameters.values.Find(v => v.id == "reach").floatValue = 1.5f;
            m_Door.tags.Add(new LevelObjectTagRef { tag = "stuck" });

            foreach (LevelObjectDef row in new[] { first, second })
            {
                LevelObjectDef runs = m_Manifest.Resolve(row);
                Assert.AreEqual(6f, ValueOf(runs, "speed"));
                CollectionAssert.Contains(runs.tags.Select(t => t.tag).ToArray(), "stuck");
            }
            Assert.AreEqual(1.5f, ValueOf(m_Manifest.Resolve(first), "reach"), "untouched: follows the new default");
            Assert.AreEqual(7f, ValueOf(m_Manifest.Resolve(second), "reach"), "set: keeps its own");
        }

        [Test]
        public void ARowWithoutAPreset_RunsAsItIs()
        {
            var row = new LevelObjectDef { id = "hall.npc.1", name = "hall.npc.1", tree = m_Tree };
            row.kind.entryName = "npc";
            row.parameters.values.Add(Value("speed", 9f));
            m_Manifest.entries.Add(row);

            Assert.IsNull(m_Manifest.PresetOf(row));
            Assert.AreSame(row, m_Manifest.Resolve(row), "the very row, as before presets");
            m_Manifest.presets = null;
            Assert.AreSame(row, m_Manifest.Resolve(row));
            Assert.AreSame(Linked("hall.door.1"), m_Manifest.Resolve(m_Manifest.entries[1]),
                "no catalogue: a row naming a preset runs as itself");
        }

        // ---- a new preset from an object ----------------------------------------------------

        [Test]
        public void ANewPresetFromThis_CopiesTheCoreWithItsSettingsAsDefaults_SwitchesIt_RunsTheSame_OneUndo()
        {
            LevelObjectDef row = Linked("hall.door.1");
            LevelPresetEdits.SetSetting(m_Manifest, row, m_Tree.parameters[0], "", 5f, "", "");
            LevelObjectDef before = m_Manifest.Resolve(row);

            LevelObjectPresetDef made = LevelPresetEdits.Fork(m_Manifest, row, "Heavy door");

            Assert.AreEqual(2, m_Presets.entries.Count);
            Assert.AreEqual("Heavy door", made.name);
            Assert.AreNotEqual(m_Door.id, made.id);
            Assert.AreSame(made, m_Manifest.PresetOf(row), "the object is switched to it");
            Assert.AreEqual(0, row.parameters.values.Count, "its values are the new preset's defaults now");
            Assert.AreEqual(5f, ValueOf(made.parameters, "reach"));
            Assert.AreEqual(3f, ValueOf(made.parameters, "speed"));
            CollectionAssert.AreEqual(m_Door.settings, made.settings);
            CollectionAssert.AreEqual(m_Door.steps, made.steps);
            Assert.AreEqual("door.opened", made.primaryFact.key.text);
            Assert.AreEqual(2.5f, ValueOf(m_Door.parameters, "reach"), "the old preset is not touched");

            LevelObjectDef after = m_Manifest.Resolve(row);
            Assert.AreSame(before.tree, after.tree);
            Assert.AreEqual(ValueOf(before, "reach"), ValueOf(after, "reach"));
            Assert.AreEqual(ValueOf(before, "speed"), ValueOf(after, "speed"));
            CollectionAssert.AreEqual(before.tags.Select(t => t.tag).ToArray(), after.tags.Select(t => t.tag).ToArray());

            Assert.AreEqual("Door 2", LevelPresetEdits.Fork(m_Manifest, Linked("hall.door.2"), "Door").name,
                "a taken name gets a number");

            Undo.PerformUndo();
            Assert.AreEqual(2, m_Presets.entries.Count, "each fork is one undo step");
            Undo.PerformUndo();
            Assert.AreEqual(1, m_Presets.entries.Count);
            Assert.AreEqual(m_Door.id, m_Manifest.PresetOf(m_Manifest.entries[0]).id, "the object is back on its preset");
        }

        [Test]
        public void ANewPresetFromARowWithoutOne_TakesItsKindTreeArgumentsAndTags_RunsTheSame()
        {
            var row = new LevelObjectDef { id = "hall.door.9", name = "hall.door.9", tree = m_Tree };
            row.kind.entryId = "kind.door";
            row.kind.entryName = "door";
            row.parameters.values.Add(Value("speed", 8f));
            row.tags.Add(new LevelObjectTagRef { tag = "outline" });
            m_Manifest.entries.Add(row);

            LevelObjectPresetDef made = LevelPresetEdits.Fork(m_Manifest, row, "Plain door");

            Assert.AreEqual("door", made.kind.entryName);
            Assert.AreSame(m_Tree, made.tree);
            Assert.IsNull(row.tree);
            LevelObjectDef runs = m_Manifest.Resolve(row);
            Assert.AreSame(m_Tree, runs.tree);
            Assert.AreEqual(8f, ValueOf(runs, "speed"));
            CollectionAssert.AreEqual(new[] { "outline" }, runs.tags.Select(t => t.tag).ToArray());
        }

        // ---- the palette and the name ------------------------------------------------------

        [Test]
        public void ThePalette_ShowsEachPresetUnderItsKind_AndPlacesARowMadeFromIt()
        {
            m_Window = Open();

            CollectionAssert.AreEqual(new[] { "door", "npc" }, m_Window.palette.Offered());
            CollectionAssert.AreEqual(new[] { "door/Door" }, m_Window.palette.OfferedPresets());
            string[] order = m_Window.palette.Query<VisualElement>().ToList()
                .Where(e => e.ClassListContains(LevelKindPalette.TileClass) || e.ClassListContains(LevelKindPalette.PresetTileClass))
                .Select(e => e.ClassListContains(LevelKindPalette.TileClass) ? "kind" : "preset").ToArray();
            CollectionAssert.AreEqual(new[] { "kind", "preset", "kind" }, order, "the preset sits right under its kind");

            LevelObjectDef row = m_Window.PlaceAtCentre("door", m_Door.id);

            Assert.AreEqual("preset.door", row.preset.entryId);
            Assert.AreSame(m_Door, m_Manifest.PresetOf(row));
            Assert.AreEqual("door", row.kind.entryName);
            Assert.AreEqual("hall.door.1", row.name);
        }

        [Test]
        public void ANewObjectsName_KeepsLevelAndKindFixed_OnlyTheLastPartTyped()
        {
            Assert.AreEqual("hall.door.", LevelPresetEdits.NamePrefix("hall", "door"));
            Assert.AreEqual("front", LevelPresetEdits.Tail("hall.door.front", "hall.door."));
            Assert.AreEqual("hall.door.front", LevelPresetEdits.Renamed("hall.door.3", "hall.door.", " front "));
            Assert.AreEqual("hall.door.3", LevelPresetEdits.Renamed("hall.door.3", "hall.door.", "  "),
                "nothing typed keeps the fresh number");

            m_Window = Open();
            LevelObjectDef row = m_Window.PlaceAtCentre("door");
            TextField name = m_Window.rootVisualElement.Q<TextField>(className: LevelManifestWindow.NameClass);
            Assert.IsNotNull(name, "a placed row is named by its last part");
            Assert.AreEqual("1", name.value);
            Assert.IsTrue(name.Query<Label>().ToList().Any(label => label.text == "hall.door."), "the prefix shows, fixed");

            m_Window.RenameSelected("front");

            Assert.AreEqual("hall.door.front", row.name);
            Assert.AreEqual("hall.door.1", row.id, "the id a step names never moves");
        }

        [Test]
        public void TheWindow_ShowsALinkedRowsCoreReadOnly_ItsSettings_AndItsActions()
        {
            m_Window = Open();
            m_Window.PlaceAtCentre("door", m_Door.id);

            VisualElement root = m_Window.rootVisualElement;
            List<string> paths = root.Query<PropertyField>().ToList().Select(field => field.bindingPath).ToList();
            string at = "entries.Array.data[" + m_Window.selected + "].";
            foreach (string field in new[] { "name", "group", "preset", "entry", "attributes", "position", "facing" })
                CollectionAssert.Contains(paths, at + field, field + " is still reached by a PropertyField");
            foreach (string field in new[] { "kind", "tree", "parameters", "tags" })
                CollectionAssert.DoesNotContain(paths, at + field, field + " is the preset's: not editable on the object");

            VisualElement preset = root.Q(className: LevelManifestWindow.PresetClass);
            Assert.IsNotNull(preset);
            List<string> labels = preset.Query<Label>().ToList().Select(label => label.text).ToList();
            Assert.IsTrue(labels.Contains("Reach") && labels.Contains("Auto"), "one field per setting: " + string.Join(" | ", labels));
            TextField core = preset.Query<TextField>().ToList().FirstOrDefault(field => field.label == "speed");
            Assert.IsNotNull(core, "a core value shows");
            bool locked = false;
            for (VisualElement up = core; up != null; up = up.parent)
                locked |= !up.enabledSelf;
            Assert.IsTrue(locked, "and is never edited on the object");

            List<string> buttons = root.Query<Button>().ToList().Select(button => button.text).ToList();
            CollectionAssert.Contains(buttons, "Use in mission…");
            CollectionAssert.Contains(buttons, "New preset from this…");
            Assert.IsNotNull(root.Q(className: LevelManifestWindow.ForkClass));
        }

        // ---- helpers ----------------------------------------------------------------------

        private LevelManifestWindow Open()
        {
            var window = ScriptableObject.CreateInstance<LevelManifestWindow>();
            window.Edit(m_Level, m_Kinds, "hall");
            return window;
        }

        private LevelObjectDef Linked(string id)
        {
            var row = new LevelObjectDef { id = id, name = id, position = new Vector2(1f, 2f) };
            row.kind.entryId = "kind.door";
            row.kind.entryName = "door";
            row.preset.entryId = m_Door.id;
            row.preset.entryName = m_Door.name;
            m_Manifest.entries.Add(row);
            return row;
        }

        private static GraphTaskParameterOverride Value(string id, float value)
        {
            return new GraphTaskParameterOverride { id = id, name = id, enabled = true, floatValue = value, stringValue = "" };
        }

        private static float ValueOf(LevelObjectDef row, string id) => ValueOf(row.parameters, id);

        private static float ValueOf(GraphTaskParameterSet set, string id)
        {
            GraphTaskParameterOverride found = set.values.LastOrDefault(v => v != null && v.enabled && v.id == id);
            Assert.IsNotNull(found, id + " is set");
            return found.floatValue;
        }

        private T Make<T>() where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            m_Junk.Add(made);
            return made;
        }
    }
}
