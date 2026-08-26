# Objectives, zones, cutscenes and saving

Three subsystems that share one shape: they are all *things that happen to the player over
time*, and none of them is allowed to be an event.

## Objectives

`ObjectiveDef` rows in an `ObjectiveRegistry`, with an `ObjectiveKind`. `ObjectiveService`
tracks them; `ActivateObjectiveTask` is how a tree turns one on; `ObjectiveBanner` and
`ObjectiveWidgetView` show them; `OffscreenIcon` points at one that is off-screen.

`ILookAt` is the capability for something an objective can point the camera or an indicator at.

## Zones

`ZoneDef` rows in a `ZoneRegistry`, with `ZoneAsset` for the authored shape and
`ObjectiveZoneBehaviour` for the runtime volume. A zone is how "be somewhere" becomes an
objective condition without the objective knowing about colliders.

A zone is a **placement** like anything else in a level — see [levels.md](levels.md) — so zones
are authored as rows, not dragged in.

## Cutscenes

`CutsceneDef` rows in a `CutsceneRegistry`. `CutsceneService` runs one; `DirectTask` is the task
that directs it; `CutsceneRole` binds an actor to a part; `CutsceneResult` is what comes back;
`CutsceneKeys` holds the request keys.

A cutscene is the clearest case of **waiting is a drawn flow**. The tree asks the cutscene
service to play, and the *flow* waits — `Ask Subsystem` → `Asked Result`. The service does not
call anything back when it finishes; it returns, and the flow continues on the far side. That is
what makes "skip", "interrupt" and "what if the player dies mid-scene" ordinary cases instead of
special ones: a `Cancelled` tears the waiting task down like any other.

`IWritesOff` is the capability for something a cutscene can take control of and hand back.

## Saving

There is no save *system* in the sense of a component that walks the scene. Instead:

- **`IAutosave`** is a capability a service offers. A service that has state worth keeping
  implements it and hands over its own `SaveState`.
- Each subsystem owns its own `SaveState` shape — `InventoryService.SaveState`,
  `ObjectiveService.SaveState`. Nothing else knows the shape.
- **`ILevelProgressStore`** is the level-scoped equivalent: what a level remembers between
  visits.

The consequence worth internalising: **adding state to a subsystem means adding it to that
subsystem's `SaveState`, and nowhere else.** There is no central list of things to save that you
can forget to update — but there is also nothing that will save your new field for you.

Because *lifetime is the scope*, what gets saved and what gets rebuilt is decided by which scope
a service lives on. Root-scope state survives travel; Level-scope state is rebuilt from the
manifest and the progress store.

## The pattern shared by all three

1. Content is **rows in a registry**, authored with pickers.
2. A **service** on the right scope owns the runtime state.
3. A **task or graph** is how behaviour reaches it, and how it waits.
4. A **view** is injected at spawn and told what to draw.
5. Persistence is the service's own `SaveState`, offered through `IAutosave`.

If you are adding a fourth subsystem of this shape, that list is the checklist — and
`Tools/Draw To Play/Subsystems` will generate most of it (see [subsystems.md](subsystems.md)).
