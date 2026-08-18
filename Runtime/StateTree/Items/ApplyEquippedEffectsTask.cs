using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT THE WEAPON ADDS (M29) — the beat that lets the thing in your hand change what a
    /// swing does, without the swing ever learning which weapon it is.
    ///
    /// The ability keeps its own damage: a strike is a strike whether you are holding a hammer
    /// or nothing at all. This reads the item equipped in a SLOT and applies that row's own
    /// effects — its <see cref="ItemDef.hitEffects"/> to whoever was struck, its
    /// <see cref="ItemDef.wielderEffects"/> to the one who swung — so a dagger that poisons and
    /// an axe that gives fury are two ROWS, not two abilities.
    ///
    /// Nothing here knows what any particular weapon does, which is the point: adding a sword
    /// that sets things alight is an effect row and an item row, and the strike is untouched.
    ///
    /// A MISS APPLIES NOTHING AND SUCCEEDS, exactly as the ability's own effect beat does: the
    /// swing happened, it found nobody, and the ability should finish and go on cooldown.
    /// </summary>
    [StateTreeCategory("Tasks/Items", "Apply the equipped weapon's own effects on a hit")]
    public sealed class ApplyEquippedEffectsTask : StateTreeTaskAsset
    {
        [Tooltip("Which slot holds the weapon whose effects these are.")]
        public StateTreeEntryRef<EquipmentSlotDef> slot = new StateTreeEntryRef<EquipmentSlotDef>();

        [Tooltip("The key holding whoever was struck. Absent or null = a miss.")]
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField targetKey = new StateTreeKeyField("struck");

        [Tooltip("Apply the row's hitEffects to the victim.")]
        public bool applyToTarget = true;

        [Tooltip("Apply the row's wielderEffects to whoever swung.")]
        public bool applyToWielder = true;

        [InjectOwner] private AbilityHost m_Owner;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Owner == null || context == null)
                return StateTreeStatus.Success;

            InventoryService bag = StateTreeContextHost.FindService<InventoryService>(
                m_Owner.gameObject);
            if (bag == null)
                return StateTreeStatus.Success;   // an actor with no bag simply has no weapon

            // THE CARRIER SCOPE holds the equipment, not the actor: a bag belongs to a player,
            // and the same lookup the equip verb uses is the one that must answer here.
            StateTreeContextHost carrier = StateTreeContextHost.Resolve(m_Owner.gameObject,
                StateTreeContextKind.Player);
            if (carrier == null || carrier.Context == null)
                return StateTreeStatus.Success;

            string slotId = slot.entryId;
            if (string.IsNullOrEmpty(slotId))
                return StateTreeStatus.Success;

            string wornName = bag.EquippedIn(slotId);
            ItemDef weapon = string.IsNullOrEmpty(wornName) ? null : bag.Row(wornName);
            if (weapon == null)
                return StateTreeStatus.Success;   // bare hands: the ability's own damage, alone

            if (applyToTarget)
            {
                GameObject victim = Body(context, targetKey);
                var struck = victim != null ? victim.GetComponent<AbilityHost>() : null;
                if (struck != null)
                    Apply(weapon.hitEffects, struck);
            }

            if (applyToWielder)
                Apply(weapon.wielderEffects, m_Owner);

            return StateTreeStatus.Success;
        }

        private void Apply(System.Collections.Generic.List<StateTreeEntryRef<EffectDef>> rows,
            AbilityHost onto)
        {
            if (rows == null || onto == null)
                return;
            AbilityService service = m_Owner.service;
            for (int i = 0; i < rows.Count; i++)
            {
                string rowName = rows[i] != null ? rows[i].entryName : "";
                EffectDef effect = service != null && !string.IsNullOrEmpty(rowName)
                    ? service.FindEffect(rowName)
                    : null;
                if (effect == null)
                {
                    if (!string.IsNullOrEmpty(rowName))
                        Debug.LogWarning("[Weapon] no effect row named '" + rowName
                            + "' for the equipped item.", m_Owner);
                    continue;
                }
                // THE SOURCE IS THE WIELDER, always — a cue that flashes on "the caster" must
                // flash on the arm that swung, even when the effect lands on somebody else.
                onto.ApplyEffect(effect, m_Owner.gameObject);
            }
        }

        private static GameObject Body(StateTreeContext context, StateTreeKeyField key)
        {
            string name = key;
            if (context == null || string.IsNullOrEmpty(name)
                || !context.blackboard.TryGetValue(name, out object held))
                return null;
            return held as GameObject ?? (held as Component)?.gameObject;
        }
    }
}
