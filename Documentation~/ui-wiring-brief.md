# UI Wiring: Views, Services, and Who Finds Whom

**Status:** design brief, two variants for decision · **Scope:** every `UiViewBehaviour` and every future UI feature in Draw-to-Play · **Reference ground truth:** this repo's `OutpostNpc` / `WorldObjectBehaviour` / `UiViewBehaviour`, and the HeavenlyTreasures Unity project (`Assets/Scripts/PlayerContext/*`, `Assets/Scripts/GameEntry/Views/*`)

---

## 0. Why this brief exists

The toolset has a wiring doctrine everywhere except the screen. Services connect
themselves (`StateTreeServiceBehaviour`), citizens register themselves
(`WorldObjectBehaviour`), components that need a service inject it
(`[InjectService]`), and the standing rule since M0 is *injected fields are valid
from the first Update — nobody polls*. Then the UI arrived, and the newest view
(`InventoryWidgetView`) does the one thing the rest of the architecture forbids:
a per-frame `Update()` loop that calls `FindService` until it stops returning
null. The deleted `OutpostHudView` bag did the same. It works, it is even
defensible frame-by-frame — and it is the first crack of a known failure mode,
because the poll is only the visible half of the real problem: **the view owns
its own wiring.**

A view that owns its wiring grows. Every cross-cutting ask — lock movement while
the bag is open, tutorial highlights a slot, a quest flashes an item, a tree
wants the panel closed before a cutscene — has exactly one place to land: inside
the widget, as one more reference the widget resolves and one more event it
juggles. That is the last-step-of-the-architecture trap: the layers below stay
clean and all the integration debt piles up in the leaf that draws pixels. Most
Unity projects and most of Unreal die exactly there (UMG widgets that are
secretly the game's brain); even Epic's UEFN-era "swap the UI under the hood"
push only works when a widget is a *subscriber to a service with data*, not a
finder of things.

This brief states the wiring laws the existing code already proves, then lays
out two variants for UI features and when each is the right size.

---

## 1. The laws, from code that is already right

These are not proposals. They are extracted from working code and apply to both
variants below.

**Law 1 — a component that needs a service INJECTS it; nobody polls.**
`OutpostNpc` (`Scripts/Outpost/OutpostNpc.cs`) is the canonical form:

```csharp
[InjectService] private OutpostDialogService m_Dialog;

public OutpostDialogDef row
{
    get
    {
        if (m_Dialog == null)
        {
            StateTreeServiceInjector.Inject(this, gameObject);   // once, at use
            if (m_Dialog == null)
                return null;                                     // graceful "not yet"
        }
        return m_Dialog.Row(dialog.entryName);
    }
}
```

Injection at the **point of use**, once, with a graceful null path — never an
`Update()` that spins "for a few frames until non-null". The injector's contract
(quiet pass at Start, loud pass one frame later) already solves scene-load
ordering; a poll loop re-solves it worse, silently, and forever.

**Law 2 — lifecycle lives in the base, consumers never write it.**
`WorldObjectBehaviour` owns register/retry/unregister internally (OnEnable
attempt → Start retry → idempotent `RegisterToWorld()` → OnDisable unregister).
`OutpostNpc` adds `EnsureTag("npc")` and calls `base.OnEnable()` — that is its
entire lifecycle cost. Whatever wiring pattern UI adopts, it must live once, in
a base or a service, not re-typed per widget.

**Law 3 — the view base stays thin.**
`UiViewBehaviour` is `Bind(arguments)` plus two arg helpers. That is correct and
must survive this brief: the base is a *receiving* surface. The moment a view
base grows a service locator, every widget inherits the disease.

**Law 4 — spawn-time is bind-time.**
`UiService` instantiates every view prefab. The session tree's Setup state shows
UI in the tree's first state — which by the injector contract runs when services
are already valid. So the one component that can *always* hand a view its
dependencies without polling is the thing that spawned it. HT never violates
this: its views are constructed by setup tasks that pass everything in.

---

## 2. What HeavenlyTreasures actually does (the evidence)

The HT Unity project has an inventory with slots, radial and swipe-bar skins,
weapon/vfx/sound reactions on slot change — and **no view ever finds anything**:

- **Views are plain classes, driven from outside.**
  `InventoryJoystickUISystem` / `InventorySwipeUISystem`
  (`GameEntry/Views/`) are not MonoBehaviours. They take a `Config` in the
  constructor, build their elements into a container they are handed, and expose
  verbs: `Show(bool)`, `SwitchToSlot(int)`, `SetCurrentHole(int)`,
  `UpdateJoystickState(...)`. Zero resolution, zero subscriptions to domain
  services, zero Update loops of their own.

- **A runtime mediator holds the cross-references.**
  `DashInventoryRuntime` (`PlayerContext/`) is the hub: it holds the view refs,
  publishes the UI-facing events (`SlotChanged`, `InventoryPanelOpenChanged`,
  `DashApproved`), and owns an `AddCleanup(Action)` list so every subscription
  registered through it is torn down in one `Dispose`.

- **Setup tasks do the wiring, once, with cleanup registered.**
  `SetupBottomInventorySwipeBarTask` constructs the view, bridges
  view-gesture → `runtime.NotifySlotChanged(...)`, subscribes
  `runtime.SlotChanged += SyncToSlot`, and immediately
  `runtime.AddCleanup(() => runtime.SlotChanged -= SyncToSlot)`. The wiring is a
  *step in a tree*, not a property of a widget.

- **Policy subscribes to the mediator, not the view.**
  `SlotActionMapTask` maps slot changes to weapon swap + ring VFX + pickup
  sound by listening to the runtime. The radial skin and the swipe bar both feed
  the same `SlotChanged`; policy neither knows nor cares which skin fired it —
  which is precisely why HT could swap skins under the hood.

The cleanest exhibit of the whole idea is the **resource trio**:

- `ResourceProvider` (`ResourseEntry/Systems/`) — the domain atom: data +
  save, plain methods that *return results* (`AddResources` hands back the
  updated item). No events, no UI knowledge.
- `ResourceUISystem` — the dumb UI system: **imperative, awaitable verbs**
  (`await AddResources(resourceData)` plays the counter animation and returns
  when it is done). It is told; it never finds or listens.
- `ResourceCollectTask` (`PlayerContext/Tasks/`) — **the flow, on top**. One
  readable sequence: remove from world data → per-view `JumpIn` animation
  toward the character → collect sound → remove the level object → `await
  ResourceUISystem.AddResources(...)` → save. One action, four-plus UI calls,
  each result feeding the next step, in ONE file you can read top to bottom.
  `PlayerContextServices.Current` is the reference hub the flow reaches
  through.

Call the hub a god-object if you like — the name misses what it buys. It holds
the view and system refs **on purpose**: that is what lets a flow be written as
a plain visible sequence — one action in, N UI calls out, a sub-UI's awaited
result returned into the same flow — instead of being smeared across N
subscriptions in N files. The god-ness is the feature. The only real fence: it
holds *references and forwards*; domain rules stay in providers, drawing stays
in systems, and the flow itself lives one layer up.

---

## 3. Variant A — the view wires itself (current state, repaired)

The widget keeps its direct relationship with the domain service, but the poll
loop dies and the wiring shrinks to the lawful form.

**Shape:**
- `UiService` **injects every `UiViewBehaviour` it spawns** at spawn time
  (`StateTreeServiceInjector.Inject(view, view.gameObject)` right after
  instantiate + `Bind`) — Law 4. Views declare `[InjectService]` fields like
  every other component in the project.
- A view that can outlive a service target (level swaps) re-injects at the
  point of use, `OutpostNpc`-style — never in Update.
- Subscription/unsubscription stays in the view (`OnEnable`/`OnDisable` against
  the injected reference), and the view still calls domain verbs directly
  (`m_Inventory.Use(...)` on button press).

**What it costs / what it buys:**

| | |
|---|---|
| **Buys** | Fewest moving parts. A read-only widget is one file. The prefab is self-contained: drop the row in the UI registry and it works. No new concepts. |
| **Costs** | The widget is still the integration point, and the flow is **invisible**: it exists only as the union of subscriptions scattered across files. Every cross-widget interaction ("when using an item, also pulse the HUD") either grows the widget or spawns a third-party glue service that exists *only for this* — and the glue accretes. Following one action means walking an event → handler → event call stack across N files; every hop threads parameters one more step; every threaded step is a place for a hidden bug. This is the UMG/UE-widget disease, and it is not simplifiable from inside — the style itself has no place where the flow could be read. |

**Right size for:** read-only, single-service widgets with no choreography —
`ObjectiveWidgetView`, the HUD bars. These should stay Variant A forever; a
mediator for a label that reads one service is ceremony.

---

## 4. Variant B — visible flow on top (the HT pattern, with a state tree as the flow layer)

Three layers, each already native to this architecture. Nothing subscribes to
anything sideways; the flow is a thing you can *read*.

**Layer 1 — the domain atom** (`InventoryService`, exists): plain verbs that
return results (`Use` → bool, `Equip` → bool). No UI knowledge. Unchanged.

**Layer 2 — the UI system:** `InventoryWidgetView` shrinks to HT's
`ResourceUISystem` shape — **imperative, told, dumb**:

- Verbs in: `Open()/Close()`, `Redraw(stacks, slotLines)`, `Flash(itemName)` —
  and where a call is an animation, it reports completion so the flow can await
  it (the `ResourceUISystem.AddResources` contract).
- One output edge: user presses don't call the domain and don't fan out — a
  press becomes a **request** (`ui.bag.use = "ration"` on the blackboard, the
  exact `LevelService.GotoKey` shape travel already proved). The skin's entire
  job is pixels in, requests out.
- No `FindService`, no subscriptions, no Update. Swapping the skin for a radial
  one changes a prefab reference and nothing else — the UEFN point, actually
  held.

**Layer 3 — THE FLOW IS A STATE TREE.** This is where the adaptation beats the
original. In HT the flow-creator on top is root code or an orchestrating
service — readable, but still C# someone has to open. Here the architecture
already says *orchestration lives in trees*: tasks tick in list order, a task's
result feeds the next, transitions read the blackboard. So the inventory flow
is a tree (a state of the session tree, or a `ui`-kind tree beside it):

```
[use-item flow]                        ← entered when ui.bag.use is set
  ├─ UseItemTask        (item from key)     domain verb, Success/Failure IS the branch
  ├─ FlashItemTask      (bag: the cell)     UI call
  ├─ PulseHealthTask    (hud)               UI call — "something on top of the inventory"
  └─ ClearKeyTask       (ui.bag.use)        consume the request (the travel rule)
```

One action, four calls, visible **in the dashboard** — not in a C# file, in the
same editor every other flow in the project is authored in. A confirmation
popup is a `ShowUiTask` with `holdWhileShown` whose result state branches the
flow — "some UI returns a result, the result feeds the next one" is literally
what M22 semantics already do. No call stack to trace, because there is no
stack: there is a list.

**The mediator that remains** is small and honest: a `UiService`-adjacent hub
(or `UiService` itself) that holds the spawned view references so tasks can
reach them (`PlayerContextServices.Current`'s job), injected per Law 4. It
holds refs and forwards. It owns no flows — flows are trees — and no rules —
rules are the domain's.

### 4b. Refinement (review of the first landing): the ServiceDef owns the flows

The first implementation put the five bag states INSIDE the session tree,
wired by the builder. Review caught what that costs, and each cost is the same
mistake — **the subsystem has no declared home**:

- The inventory `ServiceDef` sits nearly empty ({name, scope, registry} — the
  treeKind/nesting machinery the ability service proves is dormant) while the
  flows that ARE the subsystem's behavior live in someone else's tree.
- As siblings of setup/travel/playing they pollute the session: the M22
  implicit-flow hint literally reads "bag-use flows to bag-wear (its next
  sibling)" — accidental structure that means nothing.
- The request keys are typed strings in task fields — free-typed params, not
  wired declarations.
- Extending means editing the BUILDER in three places (key, state, interrupt
  on 'playing'), and no other scene or game can reuse the flows at all.

The fix keeps every layer of variant B and moves the flow layer to where the
subsystem is declared:

- **`ServiceDef.flows`** — a `StateTreeAsset` the service RUNS on its own
  scope for as long as it lives. The def finally is the subsystem's root
  point: registry = the vocabulary, flows = the behavior, scope = where.
- **`StateTreeServiceBehaviour` runs it in the base** (the AbilityHost shape:
  its own `StateTreeExecutor`, started at first Update when services are
  valid, public `Tick` so EditMode tests own the clock, Cancelled teardown on
  disable). Owner = the service's connected host, so the flow tree reads the
  SAME blackboard the skins' requests land on. Every subsystem gets this for
  free — objectives, a shop, the dialog panel — zero per-subsystem runner code.
- **The flow tree declares its requests as tree KEYS** (`tree.keys`, the
  struck-key pattern): `ui.bag.use` et al become id-wired declarations with
  descriptions — the subsystem's API, visible in the dashboard header, and
  every task field wires to the declaration (locked ⚿ param) instead of
  holding a string that happens to match.
- **Shape of the tree:** an `idle` hub (Never complete) with one id-wired
  interrupt per request, each flow state transitioning explicitly back to
  idle. No sibling chain, no implicit-flow noise — the hub is the only entry
  and the only exit.
- **The session tree goes back to setup/travel/playing.** Integration with
  other systems is now a sentence: anything that can write a blackboard key —
  a tutorial state, a dialog graph node, another service — triggers a bag flow
  by writing a DECLARED request; the def's tree serves it no matter who wrote
  it. Mounting the subsystem in another scene is mounting the service with its
  def. Nothing edits anyone else's tree.

(HT's `serviceUI` async-API idea maps onto this without changing the shape: a
UI verb that takes time is a task that holds Running until the skin reports
done — per-verb adoption, later, as needed.)

### 4c. Typed flows: the grammar (the ability system's shape, applied to UI)

The §4b landing has the right root but the wrong FORM, and review named it:
every flow state is an anonymous pile of 3–5 generic tasks, two of which
(the interrupt's condition, the consume) are pure boilerplate repeated five
times; the bag atoms (RedrawBagTask, FlashItemTask, ToggleBagTask,
PulseHudTask) are one-feature one-offs that build no OTHER UI; and the def —
the subsystem root — still declares none of it, while its rules machinery
(treeKind, nestingRules, kindSeeds) sits unused one more time.

The nearest working example is the ability system (HT's original, our M23
port): states have TYPES, the def declares the grammar, the editor creates by
rule, and C# orchestrates BY KIND — an 'effect' state is not four tasks, it is
an effect. The same transformation, applied to flows:

**1. The def declares the request API — `ServiceDef.requests`.**
A list of rows `{key, stateId, description}` on the def itself: the key that
triggers, the flow state that serves, the sentence that explains. This is the
"subsystem root" made real — the def's inspector now shows scope + registry +
flows + THE API, and "service api is known to C#" follows: the base service
exposes `Request(key, value)` that validates against the declared rows (an
unknown request is a loud finding, not a silent nothing), so C# callers go
through the service instead of raw blackboard strings. Skins keep the
view-side `Request` (they never hold the service); bridges switch to the typed
call.

**2. C# orchestrates by definition — the boilerplate is DERIVED, not authored.**
The base flows runner reads `def.requests` and does what the five hand-built
interrupts and five Clear tasks did:
- entry: at the hub, a present request key transitions to its declared state
  (a small public seam on the executor — an externally requested transition,
  the same thing an interrupt does);
- consume: leaving a request state clears its key (the runner listens to
  `activeNodeChanged` — the executor already raises it).
The authored tree shrinks to what MEANS something: a hub plus request states
whose task lists are only the verb and the reactions. `HasBlackboardKeyCondition`
and `SetBlackboardTask.Clear` disappear from the flows entirely.

**3. States are typed — the rules machinery finally runs for flows.**
The flows tree's kind ("flows") is claimed by the def (`treeKind` — the
existing `ServiceClaiming` editor path, zero new plumbing), states carry
`roleKind = "request"`, nesting rules say root → request, and kindSeeds give a
new request state its subsystem's canonical opening task (the inventory seeds
`RedrawBagTask`). Add State in the dashboard now creates a REQUEST, not an
empty box. A FlowRules validator (the AbilityRules shape) checks the def's
rows: every declared stateId exists, is request-kind, keys are unique.

### 4d. Typed wires: payloads and row-named values

§4c left one conceded weakness — values are strings checked at runtime — and one
unmet want: a state should be able to hand RICHER contract data forward without
paying a blackboard key per field or rewiring downstream tasks. Both close the
same way: type the declarations, validate the wires, leave the runtime
dictionary open (dynamic keys like the spine's `item:<name>` depend on that).

- **Row-named values.** `StateTreeKeyDeclaration` and `ServiceRequest` gain
  `namesRowOf` (a registry): "a string" becomes "an item of M21Items". The
  typed `Request()` refuses a value that names no row — loudly, at the door —
  and the def's inspector reads the API as sentences: *ui.bag.use — row of
  M21Items — Use one of the named item.* Tools can offer row pickers wherever
  a typed key is written (follow-up: the SetBlackboard/graph drawers).
- **Contract payloads.** `TaskOutputValue` gains an object slot; the executor's
  one landing site prefers it and checks it against the key's declared
  `payloadTypeName` (once per key, loudly). A task publishes a whole result
  class — `UseItemTask` publishes `ItemUseResult {item: ItemDef, itemName,
  used}` — and the transition routes it under ONE declared Object key
  (`ui.bag.last-use`). The contract grows by growing the class: no new keys,
  no rewiring of readers that ignore the new field. The scalar slots stay
  filled (the item's name in the string slot) as the degraded view for readers
  that only speak Float/String.
- **Agreement checked.** FlowRules verifies that a request row and the flow
  tree's same-named declaration type the value with the SAME registry — two
  authorities disagreeing is a picker offering rows a validator then refuses.
- **Convention:** typed request values name ROWS (names, not ids) — the bag's
  take-off sends the slot row's name; the domain keeps speaking ids and the
  task resolves at the boundary.

### 4e. Typed wire AUTHORING: the route row stops being three text fields

Review of §4d's landing (screenshot evidence): the payload contract is real at
runtime, but the ROUTE ROW in the inspector is still `[task ▾] [result]
[ui.bag.last-use]` — an output name typed by hand into a key typed by hand.
The contract exists; the author never sees it. Unity's own runtime data
binding (data sources + `PropertyPath` against typed C# classes) is the prior
art for the fix: the wire should reference the C# contract, and the editor
should offer only what fits.

- **Tasks declare their outputs statically.** `[TaskOutput]` fields already
  carry name + type via reflection — the editor can enumerate them. For
  `IStateTreeOutputSource` tasks (whose outputs are runtime-built), a
  class-level `[TaskOutputContract("result", typeof(ItemUseResult), "…")]`
  attribute declares the same statically. Every task's output surface is now
  knowable without running it.
- **The route row becomes two pickers.** Output: a dropdown of the selected
  task's declared outputs, labeled with their types — `result : ItemUseResult`.
  Target: the declared-key picker (the ⚿ machinery), offering ONLY compatible
  keys — payload-type match for object contracts, kind match for scalars —
  and wiring by `keyId` so renames survive (`TransitionOutputRoute` gains the
  id beside its text, the StateTreeKeyField pattern; the StartTree resolve
  pass covers routes). Free text remains the legal fallback, as everywhere.
- **The contract reaches the skin, bound the Unity way.**
  `UiViewBehaviour.Call` gains an optional object payload; `UiCallTask` passes
  the blackboard OBJECT through when its argument key holds one. The bag
  demonstrates the linked pattern end to end: an `announce` verb hands the
  `ItemUseResult` to a line whose Label is bound with runtime `DataBinding` —
  `dataSourcePath = new PropertyPath(nameof(ItemUseResult.itemName))` — the
  tree routes the typed object, the skin binds C# properties, no manual
  `label.text` plumbing in the middle.
- **Honesty:** `PropertyPath` is itself a string under the hood — `nameof`
  keeps it refactor-safe, and full compile-time wiring would mean generated
  binding code, which is not worth its weight here. The win is that every
  choice the author makes is a PICK from a typed offer — sourced from the
  three things the tree already knows: task output CONTRACTS, registry ROWS
  (`namesRowOf`), and DECLARED keys/params (the ⚿ wire) — and a mismatch is
  unauthorable.

**Shared state vs contract — the scoping rule.** Contracts are for
INFRASTRUCTURE wires: subsystem APIs, results crossing subsystem boundaries,
anything another system will build against. A general gameplay state keeps
using plain blackboard strings when that is all it needs — a patrol counter,
an ad-hoc flag between two sibling states — and owes nobody a declaration.
The line is audience: state shared between YOUR OWN states is shared state;
state another subsystem consumes is a contract, and gets declared, typed, and
picked.

### 4f. Next iteration: the API surface as tooling (the HT ui-flows picture, visual)

With requests, contracts, and announcements all DECLARED, the remaining
boilerplate is the finding and the reacting: an author who wants "when a
potion is drunk, my tutorial reacts" still has to know the def exists, open
it, read the key, and hand-build the interrupt + consume in their own tree.
The §4f surface resolves exactly that — states created OVER existing APIs:

- **The API browser.** One place (grown from the def inspector — the good
  starting point — plus a Subsystem APIs window) listing every declared
  subsystem: its REQUESTS (typed, described), and its ANNOUNCEMENTS — the
  declared Object keys with payload contracts (`ui.bag.last-use :
  ItemUseResult`). The wire map already draws connections; this lists the
  connectable.
- **One-click caller.** From a request row: *Add call to selected state* — a
  SetBlackboardTask writing the key, its value a ROW PICK when the request is
  `namesRowOf`-typed. The "known registries wire" completed at the write site.
- **One-click reaction.** From an announcement: *Create reaction state in
  open tree* — a new state with the id-wired interrupt on the key, a consume
  at the end, and a stub in the middle for what the reaction does. The
  biggest boilerplate — find the API, wire the entry, remember the consume —
  generated from a pick, leaving the author only the meaning.

### 4g. The degenerate flow: def-only subsystems (no state tree at all)

Review of the finished inventory found the deeper cut. Every one of the bag's
five "flows" is the same degenerate shape — domain verb, UI beats, consume,
all in one frame, nothing waited for, nothing branched on. A state machine
running that is ceremony: the states aren't states, they are ROWS wearing
state costumes. The flow tree earns its keep where flow EXISTS — AI, player
states, a cutscene that holds — and §4c–§4f stay exactly as built for those.
For a subsystem like the inventory, the ServiceDef alone is the root point:
API, actions, screen, connections — typed rows, no tree.

**The def says everything:**

- **What it answers to** — `requests`, as today, each row now carrying its
  HANDLING: an `action` (the domain verb the service interprets — "use",
  "wear", "takeoff") and typed `reactions` (rows of {ui row, verb, argument
  source}) — the beat list that was a task list, as data on the def. The
  base service serves these DIRECTLY: pending key → domain hook →
  reactions → consume. `stateId` stays for flow-backed subsystems; an
  empty one means def-served.
- **What it spawns** — `spawns`: the UI rows this subsystem owns. The
  service shows them itself at first Update; the session tree stops
  mentioning the bag at all. Mounting the service with its def IS the whole
  subsystem, screen included.
- **What it announces** — `announcements` on the def ({key, payload type,
  description}), no longer parasitic on a flow tree's key list. The domain
  hook returns the payload (ItemUseResult) and the service lands it.
- **What its screen can do** — the visual surface in the def inspector is
  the WIDGET's, not states': spawned views declare their verbs
  (`[UiVerbContract("flash", "item name")]`, the TaskOutputContract twin)
  and the inspector lists them beside the view's public fields — "know the
  fields of the UI" as a read, not a guess.

**What the service keeps in C#:** the domain hook (`OnRequest(action, value,
out payload)`) and the read-model build (the redraw data — stacks and slot
lines — handed to the skin as a payload of the "redraw" verb). Rules stay
code; the mapping stays rows.

**The rule of §4g:** if every handler of a subsystem is single-frame —
verb plus beats — it is def-only, no tree. The moment one handler needs to
WAIT (a confirmation popup, an animation that gates, a multi-step exchange),
that handler graduates to a flow state and `stateId` points at it — both
paths live on the same def, per row.

**4. UI atoms go generic — one verb task builds every next UI.**
`UiViewBehaviour` gains a virtual verb surface — `Call(verb, argument)` — and
ONE `UiCallTask {ui row, verb, argument-or-key}` replaces FlashItemTask,
ToggleBagTask, and PulseHudTask: the bag answers "toggle"/"flash", the HUD
answers "pulse", the next skin answers whatever it declares — no new task
types per feature, which is what "these tasks don't help build other UI"
demands. The one atom that stays subsystem-specific is the data-assembling
redraw (RedrawBagTask): building the read model IS domain knowledge, and a
generic task cannot know it. Declared tree keys stay for what they already do
well — the ⚿ id-wire on task fields inside the tree.

**What it costs / what it buys:**

| | |
|---|---|
| **Buys** | The flow is data: visible, authorable, diffable, in the dashboard. The widget stops accreting — it cannot grow wiring because it has no wiring surface. Skins swap. No hidden subscription chains, no parameter threading through hops — the blackboard is the parameter, named once. Adding a fifth UI reaction to an action is inserting a task in a list, not opening four files. |
| **Costs** | UI verbs must exist as task atoms (`FlashItemTask`, `OpenBagTask`, …) — a few small classes up front. The press→request→tree hop is one indirection more than calling the service in the click handler; for a widget with one button and no choreography that indirection buys nothing — see Variant A's right-size list. |

---

## 5. Recommendation

Hybrid, with the law applied everywhere:

1. **Both variants, by feature weight.** Read-only widgets stay Variant A.
   Anything whose actions should ripple — where one press means several UI
   reactions, or another feature will ever want to command this one — gets the
   Variant B stack. The inventory crossed that line the day it grew Use/Equip
   buttons on cells.
2. **The wiring law is unconditional** (either variant): views never poll and
   never resolve — `UiService` injects what it spawns; late needs re-inject at
   the point of use, `OutpostNpc`-style. `InventoryWidgetView.Update()` is
   deleted in both futures.
3. **Adopt in two steps:**
   - **Step 1 (law repair, small):** `UiService` injects views at spawn;
     `InventoryWidgetView` loses its Update poll. Exit: no `UiViewBehaviour`
     contains a wiring Update; suite green.
   - **Step 2 (the decision, this brief):** the widget shrinks to a dumb
     system — verbs in (`Redraw`/`Flash`/`Open`/`Close`), one request edge out
     (blackboard keys, the GotoKey shape); the UI task atoms land
     (`FlashItemTask`, `OpenBagTask`, `ClearKey` reuse); the use/equip flows
     move into a tree beside the session tree's Setup state. Exit: the widget
     contains zero `FindService`/`Resolve`/domain-type references and zero
     subscriptions; the use-item flow is READABLE IN THE DASHBOARD as a task
     list; a tutorial-style tree state can open the bag and flash an item
     without touching widget code; suite green; live proof (press Use → domain
     verb + bag flash + HUD pulse from ONE visible flow; reload unchanged).

   - **Step 3 (the §4b refinement):** `ServiceDef.flows` + the base-class
     runner land; `M21InventoryFlows` becomes its own asset with declared,
     id-wired request keys and the idle-hub shape; the session tree drops the
     five states and five interrupts. Exit: the session tree is
     setup/travel/playing again; the flow tree's request keys are declarations
     (task fields locked); the bag works with ZERO session-tree involvement;
     a `StateTreeServiceTests` case proves any service with `def.flows` serves
     a request key headless; suite green; live proof unchanged from step 2.
   - **Step 4 (the §4c grammar):** `ServiceDef.requests` rows + typed
     `Request()` on the base service; derived entry/consume in the runner (the
     executor's external-transition seam + `activeNodeChanged` consume);
     request states typed `roleKind="request"` under def-claimed rules and
     seeds; `UiCallTask` + `UiViewBehaviour.Call` replace the per-feature
     beat tasks; FlowRules validation. Exit: the flows tree contains no
     HasBlackboardKey conditions and no Clear tasks; every flow state is
     request-kind with only meaningful tasks; the def's inspector lists the
     API; an unknown `Request()` is a loud finding; suite green; live proof
     unchanged.

Step 1 is worth doing even if Variant B is rejected. Step 2 is where the two
variants genuinely fork — and it is cheap *now*, while the inventory is the only
interactive panel, and expensive later, after the dialog panel, a shop, or a
second bag skin each re-invent their own hidden wiring.
