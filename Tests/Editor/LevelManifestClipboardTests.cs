using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Copy / Paste / Duplicate of Level Manifest rows, on throwaway manifests: a paste is a copy with a fresh id,
    /// offset in its own manifest and on the same pose anywhere else, everything else kept; several rows at once, one
    /// undo step; a one-per-level kind the target holds is refused inline; Duplicate leaves the clipboard alone.
    /// </summary>
    [TestFixture]
    public sealed class LevelManifestClipboardTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private LevelObjectKindRegistry m_Kinds;
        private LevelObjectRegistry m_Hall;
        private LevelContent m_Level;
        private LevelManifestWindow m_Window;
        private string m_Clipboard;

        [SetUp]
        public void SetUp()
        {
            m_Clipboard = EditorGUIUtility.systemCopyBuffer;
            m_Kinds = Make<LevelObjectKindRegistry>();
            foreach (string kind in new[] { "looks", "door" })
                m_Kinds.entries.Add(new LevelObjectKindDef { id = "kind." + kind, name = kind });
            m_Kinds.entries.Add(new LevelObjectKindDef { id = "kind.player", name = "player", onePerLevel = true });

            m_Hall = Make<LevelObjectRegistry>();
            m_Hall.plane = LevelGroundPlane.XZ;
            LevelObjectDef bed = Row(m_Hall, "hall.looks.pink-bed", "looks", new Vector2(2f, 3f));
            bed.group = "beds";
            bed.facing = 90f;
            bed.entry.entryId = "look.pink";
            bed.entry.entryName = "pink";
            bed.preset = new StateTreeEntryRef<LevelObjectPresetDef> { entryId = "preset.bed", entryName = "Bed" };
            bed.attributes.values.Add(new PlacementAttribute { attribute = "worth", value = 4f });
            bed.parameters.values.Add(new GraphTaskParameterOverride { id = "p.radius", name = "radius", enabled = true, floatValue = 2.5f });
            bed.tags.Add(new LevelObjectTagRef { tag = "soft" });
            Row(m_Hall, "hall.door.1", "door", new Vector2(-1f, 0f));
            Row(m_Hall, "hall.player.1", "player", new Vector2(0f, 0f));

            m_Level = Make<LevelContent>();
            m_Level.name = "ClipboardTest" + System.Guid.NewGuid().ToString("N");
            m_Level.objects = m_Hall;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Window != null)
                Object.DestroyImmediate(m_Window);
            foreach (string kind in new[] { "looks", "door", "player" })
                EditorPrefs.DeleteKey(LevelManifestWindow.FoldKey(m_Level.name, kind));
            foreach (Object junk in m_Junk)
            {
                if (junk is LevelObjectRegistry manifest)
                    Undo.ClearUndo(manifest);
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
            EditorGUIUtility.systemCopyBuffer = m_Clipboard;
        }

        [Test]
        public void CopyPaste_InTheSameManifest_IsAFreshId_OffsetALittle_EverythingElseKept()
        {
            m_Window = Open(m_Level, "hall");
            LevelObjectDef bed = m_Hall.entries[0];
            string before = EditorJsonUtility.ToJson(bed);
            m_Window.Pick(0, EventModifiers.None);
            Assert.AreEqual(1, m_Window.CopyPicked());

            List<LevelObjectDef> pasted = m_Window.Paste();

            Assert.AreEqual(1, pasted.Count);
            LevelObjectDef copy = pasted[0];
            Assert.AreEqual("hall.looks.pink-bed.1", copy.id);
            Assert.AreEqual(copy.id, copy.name, "a name that was the id follows the id");
            Assert.AreEqual(bed.position + LevelManifestClipboard.Offset, copy.position);
            Assert.AreEqual(Normalised(bed), Normalised(copy), "kind, preset, entry, options, tags, height, facing, group kept");
            Assert.AreEqual(before, EditorJsonUtility.ToJson(bed), "the original is untouched — what targets it still does");
            Assert.AreNotSame(bed.tags, copy.tags, "a copy, not a reference");
            Assert.AreNotSame(bed.attributes, copy.attributes);
            CollectionAssert.AreEqual(new[] { m_Hall.entries.IndexOf(copy) }, m_Window.picked.ToArray(), "the paste is picked");

            Assert.AreEqual("hall.looks.pink-bed.2", m_Window.Paste()[0].id);
            Assert.AreEqual(bed.position + LevelManifestClipboard.Offset * 3f, m_Window.Paste()[0].position,
                "another paste does not land on the last");
        }

        [Test]
        public void Paste_IntoAnotherLevel_KeepsThePose_UnderThatLevelsName()
        {
            m_Window = Open(m_Level, "hall");
            m_Window.Pick(0, EventModifiers.None);
            m_Window.CopyPicked();

            LevelObjectRegistry dorm = Make<LevelObjectRegistry>();
            dorm.plane = LevelGroundPlane.XZ;
            LevelContent other = Make<LevelContent>();
            other.name = m_Level.name + "Dorm";
            other.objects = dorm;
            m_Window.Edit(other, m_Kinds, "dorm");
            List<LevelObjectDef> pasted = m_Window.Paste();

            Assert.AreEqual(1, dorm.entries.Count);
            Assert.AreEqual("dorm.looks.pink-bed", pasted[0].id);
            Assert.AreEqual(m_Hall.entries[0].position, pasted[0].position, "the exact pose");
            Assert.AreEqual(m_Hall.entries[0].facing, pasted[0].facing);
            Assert.AreEqual(Normalised(m_Hall.entries[0]), Normalised(pasted[0]));
            EditorPrefs.DeleteKey(LevelManifestWindow.FoldKey(other.name, "looks"));
        }

        [Test]
        public void Paste_IntoAVisit_KeepsThePose_AndNeverShadowsTheLevelsRow()
        {
            LevelObjectRegistry visit = Make<LevelObjectRegistry>();
            visit.plane = LevelGroundPlane.XZ;
            visit.visitOf = m_Hall;
            LevelManifestClipboard.Copy(m_Hall, "hall", new[] { m_Hall.entries[0] });

            List<LevelObjectDef> pasted = LevelManifestClipboard.Paste(visit, m_Kinds, "hall",
                LevelManifestClipboard.Read(), new List<string>());

            Assert.AreEqual(1, visit.entries.Count);
            Assert.AreEqual("hall.looks.pink-bed.1", pasted[0].id, "a visit's row must not shadow the level's");
            Assert.AreEqual(m_Hall.entries[0].position, pasted[0].position);
            Assert.AreEqual(Normalised(m_Hall.entries[0]), Normalised(pasted[0]));
        }

        [Test]
        public void SeveralRows_PickedAndPasted_AreOneUndoStep()
        {
            m_Window = Open(m_Level, "hall");
            m_Window.Pick(0, EventModifiers.None);
            m_Window.Pick(1, EventModifiers.Command);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, m_Window.picked.ToArray());
            Assert.AreEqual(2, m_Window.CopyPicked());
            int before = m_Hall.entries.Count;

            List<LevelObjectDef> pasted = m_Window.Paste();

            Assert.AreEqual(2, pasted.Count);
            Assert.AreEqual(before + 2, m_Hall.entries.Count);
            CollectionAssert.AreEquivalent(new[] { "hall.looks.pink-bed.1", "hall.door.2" }, pasted.Select(row => row.id).ToArray());
            CollectionAssert.AreEquivalent(pasted.Select(row => m_Hall.entries.IndexOf(row)).ToArray(), m_Window.picked.ToArray());
            Undo.PerformUndo();
            Assert.AreEqual(before, m_Hall.entries.Count, "the whole paste is one undo step");
        }

        [Test]
        public void ShiftClick_PicksTheRange_TheListShows()
        {
            m_Window = Open(m_Level, "hall");
            m_Window.Pick(0, EventModifiers.None);
            m_Window.Pick(2, EventModifiers.Shift);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, m_Window.picked.ToArray());
            m_Window.Pick(1, EventModifiers.Control);
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, m_Window.picked.ToArray(), "Ctrl/Cmd takes one out again");
        }

        [Test]
        public void AOnePerLevelKind_TheTargetHolds_IsRefused_InlineNote()
        {
            m_Window = Open(m_Level, "hall");
            m_Window.Pick(1, EventModifiers.None);
            m_Window.Pick(2, EventModifiers.Command);
            m_Window.CopyPicked();
            int before = m_Hall.entries.Count;

            List<LevelObjectDef> pasted = m_Window.Paste();

            Assert.AreEqual(1, pasted.Count, "the door is pasted, the start is not");
            Assert.AreEqual("door", pasted[0].kind.entryName);
            Assert.AreEqual(before + 1, m_Hall.entries.Count);
            StringAssert.Contains("player", m_Window.note);
            Assert.AreEqual(1, m_Window.rootVisualElement.Query(className: LevelManifestWindow.NoteClass).ToList().Count,
                "said in the window, no dialog");

            LevelObjectRegistry visit = Make<LevelObjectRegistry>();
            visit.visitOf = m_Hall;
            var refused = new List<string>();
            LevelManifestClipboard.Copy(m_Hall, "hall", new[] { m_Hall.entries[2] });
            Assert.AreEqual(0, LevelManifestClipboard.Paste(visit, m_Kinds, "hall", LevelManifestClipboard.Read(), refused).Count,
                "the level's start stands on its visit too");
            Assert.AreEqual(1, refused.Count);
        }

        [Test]
        public void Duplicate_IsCopyAndPasteInPlace_LeavingTheClipboardAlone()
        {
            m_Window = Open(m_Level, "hall");
            EditorGUIUtility.systemCopyBuffer = "typed text";
            m_Window.Pick(1, EventModifiers.None);

            List<LevelObjectDef> made = m_Window.Duplicate();

            Assert.AreEqual(1, made.Count);
            Assert.AreEqual("hall.door.2", made[0].id);
            Assert.AreEqual(m_Hall.entries[1].position + LevelManifestClipboard.Offset, made[0].position);
            Assert.AreEqual("typed text", EditorGUIUtility.systemCopyBuffer);
            Undo.PerformUndo();
            Assert.AreEqual(3, m_Hall.entries.Count, "one undo step");
        }

        [Test]
        public void Fresh_NumbersAfterTheStem_AsThePaletteDoes()
        {
            var taken = new HashSet<string> { "hall.looks.pink-bed", "hall.looks.pink-bed.1", "hall.door.1", "hall.door.3" };
            Assert.AreEqual("hall.looks.pink-bed.2", LevelManifestClipboard.Fresh("hall.looks.pink-bed", "hall.looks.pink-bed", taken));
            Assert.AreEqual("hall.door.2", LevelManifestClipboard.Fresh("hall.door.3", "hall.door.3", taken));
            Assert.AreEqual("dorm.door.3", LevelManifestClipboard.Fresh("dorm.door.3", "hall.door.3", taken));
            Assert.AreEqual("dorm.door.3", LevelManifestClipboard.Reprefix("hall.door.3", "hall", "dorm"));
            Assert.AreEqual("lamp", LevelManifestClipboard.Reprefix("lamp", "hall", "dorm"));
        }

        /// <summary>The row's data less what a paste changes — id, name and position.</summary>
        private static string Normalised(LevelObjectDef row)
        {
            var copy = new LevelObjectDef();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(row), copy);
            copy.id = "";
            copy.name = "";
            copy.position = Vector2.zero;
            return EditorJsonUtility.ToJson(copy);
        }

        private LevelManifestWindow Open(LevelContent level, string key)
        {
            var window = ScriptableObject.CreateInstance<LevelManifestWindow>();
            window.Edit(level, m_Kinds, key);
            return window;
        }

        private static LevelObjectDef Row(LevelObjectRegistry manifest, string id, string kind, Vector2 position)
        {
            var row = new LevelObjectDef { id = id, name = id, position = position };
            row.kind.entryId = "kind." + kind;
            row.kind.entryName = kind;
            manifest.entries.Add(row);
            return row;
        }

        private T Make<T>() where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            m_Junk.Add(made);
            return made;
        }
    }
}
