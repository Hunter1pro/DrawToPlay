using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ASK THE OBJECT ITSELF (M30.4) — call a request on whatever this state is about, through
    /// the def that built it.
    ///
    /// <see cref="RequestTask"/> asks a SUBSYSTEM: one inventory, one craft rulebook, addressed
    /// by name on the root board. This asks a THING: the door in front of you, the tree you are
    /// chopping, the object a beat was handed — and the def on top of that object is what says
    /// which requests it has. Nothing here knows what a door is.
    ///
    /// A DERIVED REQUEST IS SERVED HERE, and that is what stops the derived surface being
    /// decoration: "health.add -5" reaches the body's attributes and moves them, and a request
    /// the def does not declare — or one it declares read-only — is REFUSED, by the same rule the
    /// inspector drew. An authored request is written onto the root board exactly as the generic
    /// caller writes it, because a def's own verbs are still served by its service.
    /// </summary>
    [StateTreeCategory("Tasks/Services", "Call a request on the object this state is about")]
    public sealed class ObjectRequestTask : StateTreeTaskAsset
    {
        [Tooltip("The key holding the object to ask. Empty asks the owner — the body this tree "
            + "is running on.")]
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField target = new StateTreeKeyField();

        [Tooltip("The request — one the object's def declares, or a derived one like "
            + "'health.add'.")]
        public string request = "";

        [Tooltip("The value: a number for a derived request, a row name for a typed one.")]
        public string value = "1";

        [Tooltip("Optional: a blackboard key holding the value — wins over the field.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField valueKey = new StateTreeKeyField();

        [Tooltip("Where an 'ask' puts its answer, on this tree's own board.")]
        [StateTreeKey(StateTreeKeyKind.Float)]
        public StateTreeKeyField into = new StateTreeKeyField();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(request))
                return StateTreeStatus.Failure;

            GameObject body = Body(context) ?? context.owner;
            ServiceDef def = ServiceBodyBinding.Of(body);
            if (def == null)
            {
                Debug.LogWarning("[ObjectRequest] '" + body.name + "' was not built from a def, "
                    + "so it has no requests to call.", body);
                return StateTreeStatus.Failure;
            }

            ServiceRequest row = def.RequestFor(request);
            if (row == null)
            {
                // NAMING THE DEF IS THE DIAGNOSIS: "no request 'open.set'" is a typo hunt,
                // "'tree' has no request 'open.set'" is an answer.
                Debug.LogWarning("[ObjectRequest] '" + def.serviceName + "' has no request '"
                    + request + "' — it may be undeclared, or declared read-only.", body);
                return StateTreeStatus.Failure;
            }

            string resolved = Resolved(context);
            if (!def.IsDerived(request))
            {
                // AN AUTHORED VERB IS STILL THE SUBSYSTEM'S: written where its service watches.
                StateTreeContextHost root = StateTreeContextHost.Resolve(context.owner,
                    StateTreeContextKind.Root);
                if (root == null || root.Context == null)
                    return StateTreeStatus.Failure;
                root.Context.blackboard[request] = resolved ?? "";
                return StateTreeStatus.Success;
            }

            return Serve(context, body, def, resolved);
        }

        /// <summary>The derived half, on the body's own attributes — the guard the generated
        /// rows would otherwise be missing.</summary>
        private StateTreeStatus Serve(StateTreeContext context, GameObject body, ServiceDef def,
            string resolved)
        {
            if (!ServiceDef.SplitDerived(request, out string name, out string verb))
                return StateTreeStatus.Failure;

            var attributes = body.GetComponentInParent<AttributeComponent>();
            if (attributes == null || !attributes.Has(name))
            {
                // THE DEF SAID IT HAS THIS AND THE BODY DOES NOT — worth saying out loud,
                // because it is a def and a prefab that have drifted apart.
                Debug.LogWarning("[ObjectRequest] '" + body.name + "' has no '" + name
                    + "' to " + verb + ", though '" + def.serviceName + "' declares it.", body);
                return StateTreeStatus.Failure;
            }

            if (verb == ServiceDef.AskVerb)
            {
                string answerKey = into;
                if (string.IsNullOrEmpty(answerKey))
                    return StateTreeStatus.Failure;   // an ask with nowhere to put it is a no-op
                context.blackboard[answerKey] = attributes.Value(name);
                return StateTreeStatus.Success;
            }

            if (!float.TryParse(resolved, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float amount))
            {
                Debug.LogWarning("[ObjectRequest] '" + resolved + "' is not a number, so '"
                    + request + "' cannot be served.", body);
                return StateTreeStatus.Failure;
            }

            if (verb == ServiceDef.SetVerb)
            {
                attributes.SetCurrent(name, amount);
                return StateTreeStatus.Success;
            }

            // SIGNED, and the two directions are two methods on purpose: giving back is capped
            // by the pool and spending is not, because overkill is information.
            if (amount >= 0f)
                attributes.Restore(name, amount);
            else
                attributes.Consume(name, -amount);
            return StateTreeStatus.Success;
        }

        private string Resolved(StateTreeContext context)
        {
            string dynamicKey = valueKey;
            if (!string.IsNullOrEmpty(dynamicKey)
                && context.blackboard.TryGetValue(dynamicKey, out object held))
            {
                if (held is string text && !string.IsNullOrEmpty(text))
                    return text;
                if (held is float number)
                    return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return value;
        }

        private GameObject Body(StateTreeContext context)
        {
            string key = target;
            if (string.IsNullOrEmpty(key)
                || !context.blackboard.TryGetValue(key, out object held))
                return null;
            return held as GameObject ?? (held as Component)?.gameObject;
        }
    }
}
