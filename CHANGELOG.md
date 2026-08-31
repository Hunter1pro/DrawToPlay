# Changelog

## 0.3.2 - 2026-09-01
- `StateTreeContextKind.Character`: a non-player actor's host kind, so `Resolve(Player)` -
  what the objective watchers and the zone orchestrator ask - stays unique when a level
  holds more than one actor.

## 0.3.1 - 2026-09-01
- `CompleteRow(name)` / the `objective-complete` verb with a name also advances a RELEASED
  zone whose cursor is that row - a film state that pre-empted the zone can still say "that
  step is done", and re-asking resumes past it.
- An interrupt whose target resolves to the state already running is skipped instead of
  re-entered every tick - a film state can consume the fact that called it without racing
  its own condition.

## 0.3.0 — 2026-08-31
- `RunZoneTask`: a tree state runs a zone's objective stack — the ordered `ZoneAsset` list
  is the sequence, so a tree carries one state per zone instead of one per objective, and a
  side quest is an ancestor interrupt that releases the ask and resumes the cursor on
  re-entry. While an ask stands the distance orchestrator stands down; no volume needed.
- `objective-complete` action on `ObjectiveService`: a flow completes the current row (named
  as a guard) through a declared request — the step no watcher can see.

## 0.2.0 — 2026-08-31
- Interrupts are heard along the whole active chain: a `checkWhileRunning` transition on
  any ancestor of the current state is evaluated every tick — current state first, then up
  the parents — so "a film pre-empts everything" is one row on the root instead of one per
  leaf. A pre-empted task still exits `Cancelled`, never completed.
- Entry resolution descends through a node whose only transitions are interrupts:
  listening from the chain does not make an organizational node a resident state.

## 0.1.2 — 2026-08-27
- Constructor injection for subsystems: after `(scope, def)`, further constructor parameters
  are resolved from the scope at install. A missing required one fails the install, naming
  the parameter. `[InjectService]` on a service stays for late and cross-scope collaborators.

## 0.1.1 — 2026-08-27
- `ManifestSpawner` holds the level's OWN host the way it holds spawned bodies: a level host
  with `autoStart` off on the spawner's object starts on frame two, after its rows are
  citizens. Consumers no longer need a spawner subclass for a level tree.
- `[PlacementId]`: a manifest row id, picked from the manifests the asset declares — the
  `[WorldTag]` rule for placements. `StateTreeOffers.PlacementsFor` is the offer.

## 0.1.0 — 2026-08-24
First cut as a package, extracted from the PhysicsExamples2D sandbox with its history
(M0–M43). Promoted from the examples: `InputService`, `LevelBootstrap` (press play on a
level and the session comes in behind you), `ManifestSpawner` (rows → bodies, frame-two
tree start; derive for progress/gained), `LevelAuthoring` (placement + subsystems
authoring helpers), `GraphAuthoring` (task-graph authoring by picking). All output
folders now route through `DrawToPlayFolders` (a host project sets `project`,
`projectGenerated`, and registers its task assemblies).
