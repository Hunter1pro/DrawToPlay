using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Renaming a key that something already calls.
    ///
    /// A request key and an announcement key are contracts held BY NAME: the def declares the
    /// word, and tasks, reactions and components elsewhere type the same word. So the inspector
    /// locks a key the moment anything names it — and a lock is only honest if there is a way
    /// through it, which is this: rename the def's row and every caller in one step.
    ///
    /// What is pinned here is the rewrite itself, because a rename that silently changed nothing
    /// looks exactly like one that worked.
    /// </summary>
    [TestFixture]
    public sealed class ServiceKeyRenameTests
    {
        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void EveryMentionOfTheKeyMoves_AndNothingElseDoes()
        {
            var caller = Make<RequestTask>("Asks the bag to open");
            caller.key = "ui.bag.toggle";
            caller.value = "1";
            caller.valueKey = new StateTreeKeyField("ui.bag.toggle");   // the same word, elsewhere

            var bystander = Make<RequestTask>("Asks for something else");
            bystander.key = "cutscene.play";

            int changed = ServiceKeyRename.Rewrite(caller, "ui.bag.toggle", "ui.bag.open");
            Assert.That(changed, Is.EqualTo(2),
                "it rewrites by VALUE, which is the same rule the usage index found it by");
            Assert.That(caller.key, Is.EqualTo("ui.bag.open"));
            Assert.That(caller.valueKey.text, Is.EqualTo("ui.bag.open"));
            Assert.That(caller.value, Is.EqualTo("1"), "a value that was not the key stays");

            Assert.That(ServiceKeyRename.Rewrite(bystander, "ui.bag.toggle", "ui.bag.open"),
                Is.EqualTo(0));
            Assert.That(bystander.key, Is.EqualTo("cutscene.play"));
        }

        [Test]
        public void ARenameToNothing_OrToItself_IsNotARename()
        {
            var caller = Make<RequestTask>("Asks");
            caller.key = "craft.begin";

            Assert.That(ServiceKeyRename.Rewrite(caller, "craft.begin", "craft.begin"),
                Is.EqualTo(0), "renaming a key to itself must not dirty a single asset");
            Assert.That(ServiceKeyRename.Rewrite(null, "craft.begin", "craft.start"),
                Is.EqualTo(0));
            Assert.That(ServiceKeyRename.Rewrite(caller, "", "craft.start"), Is.EqualTo(0));
            Assert.That(caller.key, Is.EqualTo("craft.begin"));
        }

        [Test]
        public void AKeyDeclaredInCode_IsFoundAndCannotBeRenamedFromTheInspector()
        {
            // The case that started this: an announcement whose only namer is a const looked
            // unused, when a constant is the STRONGEST kind of namer — the source of the name.
            Assert.That(ServiceKeyCode.Owners(ItemUseResult.Key),
                Does.Contain(nameof(ItemUseResult) + "." + nameof(ItemUseResult.Key)));
            Assert.That(ServiceKeyCode.Owners(CraftKeys.Begin),
                Does.Contain(nameof(CraftKeys) + "." + nameof(CraftKeys.Begin)));
            Assert.That(ServiceKeyCode.Owners("nothing.declares.this"), Is.Empty);
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            m_Junk.Add(asset);
            return asset;
        }
    }
}
