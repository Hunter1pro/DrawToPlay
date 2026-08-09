using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// <c>await screen.Process()</c> as an atom, for the KEY-WATCHING seam (screens that show
    /// while a Screen-kind key equals their id, the M16/M17 doctrine) — the twin of
    /// <see cref="ShowScreenTask"/>, which serves the UIService address book.
    ///
    /// OnEnter RAISES the screen (writes <see cref="screenId"/> into <see cref="screenKey"/> on
    /// the scope) and clears any stale answer; it stays Running while the human looks; the
    /// screen ANSWERS by writing a button id into <see cref="answerKey"/> (every
    /// TriggerButton does), which completes the task with Success and the id as the
    /// <see cref="button"/> output — routable by transitions, readable at the call site.
    /// OnExit lowers the screen on EVERY path, Cancelled included, so an interrupt anywhere
    /// above tears the UI down correctly for free — the imperative
    /// spawn → await → dispose block, as one node.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/UI/Await Screen", fileName = "AwaitScreen")]
    [StateTreeCategory("Tasks/UI",
        "Raise a key-watched screen and wait for its answer; the button id is the return")]
    public sealed class AwaitScreenTask : StateTreeTaskAsset
    {
        /// <summary>The scope the screen lives on — a wire-field the framework binds per
        /// activation (both worlds), so nothing here resolves anything.</summary>
        public StateTreeHostRef scope = new StateTreeHostRef();

        /// <summary>The key the views watch ("which screen is up") — a String-valued slot
        /// whose VALUE is a screen address.</summary>
        [StateTreeKey(StateTreeKeyKind.String, any: true)]
        public StateTreeKeyField screenKey = new StateTreeKeyField();

        /// <summary>The screen to raise — a Screen-kind ADDRESS (declare the ids on the
        /// tree, the ShowScreenTask precedent), written into the key and matched by one
        /// view's own id. Wireable, parameter-bindable on a canvas — never a shallow
        /// string.</summary>
        [StateTreeKey(StateTreeKeyKind.Screen)]
        public StateTreeKeyField screenId = new StateTreeKeyField();

        /// <summary>Where the screen answers: a button press writes its id here.</summary>
        [StateTreeKey(StateTreeKeyKind.String, any: true)]
        public StateTreeKeyField answerKey = new StateTreeKeyField("ui:answer");

        /// <summary>Which button answered — the screen's return value.</summary>
        [TaskOutput("Id of the button that answered the screen")]
        public string button = "";

        public override void OnEnter(StateTreeContext context)
        {
            button = "";
            if (scope.host == null)
                return;
            var blackboard = scope.host.Context.blackboard;
            blackboard.Remove((string)answerKey);
            blackboard[(string)screenKey] = (string)screenId;
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (scope.host == null)
                return StateTreeStatus.Failure;

            var blackboard = scope.host.Context.blackboard;
            if (!blackboard.TryGetValue((string)answerKey, out object held)
                || !(held is string answer) || string.IsNullOrEmpty(answer))
                return StateTreeStatus.Running;

            button = answer;
            blackboard.Remove((string)answerKey);
            return StateTreeStatus.Success;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (scope.host == null)
                return;
            // Lower only OUR screen: another state may already have raised the next one.
            var blackboard = scope.host.Context.blackboard;
            if (blackboard.TryGetValue((string)screenKey, out object held)
                && held is string current
                && string.Equals(current, (string)screenId, System.StringComparison.Ordinal))
                blackboard.Remove((string)screenKey);
        }
    }
}
