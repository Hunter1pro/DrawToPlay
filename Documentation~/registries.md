# Registries and entry refs

A **registry** is a catalog of rows. An **entry ref** is a typed pointer at one row. Together
they are how everything in this toolset names content without hardcoding a string.

## The shape

`StateTreeRegistryAsset` is the abstract base; every catalog derives from it —
`ItemRegistry`, `AbilityRegistry`, `AttributeRegistry`, `CraftRecipeRegistry`, `UiRegistry`,
`LevelRegistry`, `LevelObjectRegistry`, `LevelObjectKindRegistry`, `ZoneRegistry`,
`ObjectiveRegistry`, `CutsceneRegistry`, `WorldTagRegistry`, `EquipmentSlotRegistry`,
`EffectRegistry`, `CueRegistry`, `ContractRegistry`.

Every row carries at least:

```csharp
public string entryId   = "";   // stable, machine-facing:  "item.rope"
public string entryName = "";   // the human name:          "Rope"
```

`entryId` is the identity and must not change once content references it. `entryName` is for
display and may change freely. When you see a pair like `entryId = "item.rope"` and
`entryName = "Rope"` in generated content, the first is the wire and the second is the label.

## Entry refs

`StateTreeEntryRef<TEntry>` is a serialized, typed reference to a row of a particular registry:

```csharp
public StateTreeEntryRef<UiDef>   screen;     // a UI row
public StateTreeEntryRef<ItemDef> requires;   // an item row
```

In the inspector this draws as a picker — ⛃ — offering exactly the rows of the right registry,
not a free text field. That is the point: a def that names a row can only name a row that
exists, and renaming content does not silently break a reference.

A ref resolves to `entry`, and exposes `entryName` through it. `entryNamePrefix` supports
grouped naming where a family of rows shares a stem.

## How a def names a row

A request row on a `ServiceDef` carries `namesRowOf` — the registry whose rows this request's
*value* names. That is what lets the validator say "this request names a row of a catalog
nobody declares", and what lets the editor offer a picker for the value instead of a string.

The chain is: **def declares a catalog** (`declares` / `registry`) → **request names rows of
it** (`namesRowOf`) → **the caller passes an entry id** → the service looks the row up.

## Creating and editing

Registries are ScriptableObjects. The `RegistryCreator` inspector offers to make its own rows,
so a new catalog is not a blank asset you have to hand-fill.

Generated content — anything a project's demo builder writes — should be created through the
same registries, never hand-edited as YAML. If a row is wrong, fix the generator and regenerate;
a hand-edit is overwritten the next time anyone runs the builder.

## Rules that keep catalogs usable

- **`entryId` is forever.** Treat it like a database key. If a rename is genuinely needed, it is
  a migration, not an edit.
- **One catalog, one owner.** A registry is declared by exactly one def. Two subsystems both
  claiming to manage the same catalog is the drift the Subsystem APIs window reports.
- **A picker, not a string.** If you are typing an id into a `string` field, there is a
  `StateTreeEntryRef<T>` that should have been there instead.
