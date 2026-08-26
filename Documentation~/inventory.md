# Items, the bag, equipment and crafting

Four connected pieces: a catalog of items, a service that holds them, slots that wear them, and
a bench that turns some into others.

## Items

`ItemDef` rows live in an `ItemRegistry` — a plain serializable class in the registry's list,
**not one asset per item**. Each row carries `entryId` (what typed references store) and
`entryName` (the runtime string that inventory keys and click routing use), plus an `ItemKind`:

```
Weapon · Consumable · Trinket
```

## The bag

`InventoryService` is the implementation; **`IBag` is what everything else names**:

```csharp
ItemDef Row(string itemName);
int     Add(string itemName, int count = 1);
bool    Remove(string itemName, int count = 1);
int     Count(string itemName);
bool    Has(string itemName, int count = 1);
```

That is the entire surface, and its smallness is the design. Consumers take an `IBag`, never an
`InventoryService` — which is what lets a chest, a corpse or a shop stock be a bag too without
anything that reads bags learning about them.

The bag lives on the **Player** scope. It is created with the actor and dies with the actor;
nothing looks for "the" bag globally.

`SaveState` on the service is what persists; `IAutosave` is the capability that says when.

## Equipment

`EquipmentSlotDef` rows in an `EquipmentSlotRegistry` declare what can be worn where.
`IWornView` is the capability a view implements to show worn items — the view is *told* what to
draw when it is spawned, it does not poll the bag.

`ApplyEquippedEffectsTask` is the task that turns worn items into active effects, going through
the ability/effect system (`EffectDef`, `EffectRegistry`) rather than mutating stats directly.
That indirection is what makes "this sword is +2" revertible, saveable, and visible in one
place.

## Crafting

`CraftRecipeDef` rows in a `CraftRecipeRegistry`. A recipe has a `group` (the station that
offers it), a list of `CraftCostLine` costs, and a `CraftResult`:

```
id      "recipe.rope"
group   "Bench"
costs   2 × fibre
result  entryId "item.rope", entryName "Rope"
```

`CraftService` reads the recipes, asks the bag whether the costs are payable, spends them and
adds the result. `CraftOffer` is what a station presents; `CraftKeys` holds the request keys.

The whole exchange is **call and return**: the bench asks the bag, gets a yes or no, and acts on
the next line. No event says "crafting finished".

## UI

`InventoryWidgetView`, `BagCellModel`, `BagSlotModel` and `CraftPanelView` are the views.
`UiService` injects them at spawn — a view never polls the bag, and a press is a *request* on
the showing scope rather than a direct call into the service. See the package's
`Documentation~/ui-wiring-brief.md` for the full pattern.

## The rules that keep this small

- **Name the capability.** `IBag`, not `InventoryService`.
- **Counts are the bag's business.** A consumer asks `Has(...)`; it does not read a list and
  count it itself.
- **Items are rows, not assets.** Adding an item is adding a row, not creating a file.
- **No events.** "The bag changed" is not broadcast. Whatever needs to know, asks — and
  whatever needs to wait, draws a flow.
