# Changelog

## 0.5.7 - 2026-09-02
- Auto-update: on load and on regaining focus (right after a pull), the editor asks the
  remote where the pinned ref stands (`git ls-remote`, background, 60s cooldown); a moved
  ref runs the same one-click update. Off-switch: Tools/Draw To Play/Auto-Update on Focus.
  Never during play mode; an unreachable remote does nothing.

## 0.5.6 - 2026-09-02
- Tools/Draw To Play/Update Draw To Play: one click re-fetches the pinned git tag (re-Add
  of the manifest's own URL - the official UPM way; the lock rewrites itself). For the
  designers: no terminal, no lockfile surgery.

## 0.5.5 - 2026-09-02
- Level Manifest: a MISSING kind catalog is cached like a found one - a level whose
  registry does not wire `LevelRegistry.kinds` no longer re-scans the project per row per
  repaint (ghosts fell back to stand-ins AND the scene lagged; now it only says "nothing
  to add" once, and notices when the catalog appears via projectChanged).

## 0.5.4 - 2026-09-02
- Level Manifest window no longer drags the scene down: dragging a handle re-used the
  bound fields instead of rebuilding the whole panel per frame, the scene repaints only
  when a drawn property (position/facing/kind/name) actually changes, the ghost style is
  read from EditorPrefs once instead of per row per repaint, and off-screen ghosts are
  frustum-culled before their meshes are drawn.

## 0.5.3 - 2026-09-02
- A tree SEES the keys of the registries it lists: the picker offers them and the runtime
  resolves wired ids through them - one declaration (on the registry) serves the rows that
  gate on a key and the tree tasks that write it.

## 0.5.2 - 2026-09-02
- Registries declare KEYS the way trees do (`StateTreeRegistryAsset.keys`), and a
  StateTreeKeyField on a registry row resolves its picker from them - the registry's own
  and its dependsOn's (`StateTreeOffers.KeysFor`). No more dead picker on rows that live
  in assets.

## 0.5.1 - 2026-09-02
- The row params are typed, never plain text: fact and gate keys are StateTreeKeyFields,
  the fact value is a picked placement id, the gate value is a number (the board's flag
  domain), the completion request is a declared key plus a row picked through dependsOn.

## 0.5.0 - 2026-09-02
- Row gates (`gateKey/gateValue`): unset = the row is PENDING (current but inert - the
  ledger waits for the answer); equal = it runs; different = it is passed over silently
  (no completion, no announcement). How a choice forks a linear stack.
- `completeRequestKey/Value`: a row's completion writes a declared request on the root
  board - a completion film plays with no film state in any tree.


## 0.4.0 - 2026-09-02
- Fact-completed rows: `ObjectiveDef.factKey/factValue` - a row completes when the scope
  board says so. The flow writes facts; the ledger hears them; no completing task in any
  tree.
- Every completion is ANNOUNCED (`objective.completed`, payload = row name) - a film or a
  flow keys off the serial with `AnnouncementCondition`, once per completion, nothing to
  consume.
- `RunZoneTask` releases only on completion: pre-empted, the ask - and the watching -
  stands, so a film over the zone loses nothing.

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
