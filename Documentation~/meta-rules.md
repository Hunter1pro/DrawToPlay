# The project's meta-rules

*Written 2026-08-22, after M39.2b, from the user's account of how HeavenlyTreasures (Unity) is
built and why it stays buildable. These are not patterns — a pattern is a shape you reach for.
These are the constraints that decide which shapes are allowed; the shapes fall out. Each rule
says what it forbids, what to do instead, and how it is checked, because a rule nobody can
check is a preference. Rule 5 was added after M40.3, when the capabilities that M39–M40 had
been reaching for one at a time turned out to be the thing holding the layer together.*

Why they exist, in the user's words: an event is not a line, it is a **process** — a second
execution path with its own timing, which is where double execution and ordering bugs come
from. Two systems depending on each other by events plus three subscribers is a web that grows
exponentially, and nobody can *see* a flow through it. The 1 200-line root was the good
version: you could read how one flow touched four systems. A mission, a user, a character that
is "global" and listened to becomes the same web; a scope that is dropped and rebuilt is not.
The rules below are what keep that property as the project grows, for a human or an AI adding
the next feature by following them.

---

## 1. Call and return

**A subsystem does its work by calling the services it needs, in order, in one method, and
returns a result.** No subsystem raises a C# event for another subsystem or a screen to hear.
A screen is held by the subsystem that showed it (`StateTreeService.Spawned<T>()`) and *told*
what to draw; its buttons call the subsystem's verbs and show what they return. A collaborator
the subsystem cannot assume (a save in a session that may have none) is asked for at first need
and remembered (`m_X ??= FindService<T>(scope.gameObject)`), because `[InjectService]` is loud
on purpose.

*The model:* `InventoryService.Changed(item)` — redraw the screen, tell the quest line the
count, knock on the save. Three lines, one place, the order is the text.

*What it forbids:* `public event` on a service; `+=` to another service's member; a bridge
component that finds two services and forwards between them.

*Not covered:* the executor's own signals (runner/host tree events, `AbilityHost`,
`AttributeComponent.changed`) — machinery the editor and cues ride, not domain. Announcements
on the board (`Announce`, `When Announced`) — they are rule 4's declared channel, visible on
the map, consumed by drawn flows.

*Check:* a test counts `public event` on `StateTreeService` subclasses and example services
and fails on a new one; code review asks "who else hears this?" of any `+=`.

## 2. Waiting is a drawn flow

**Anything that has to wait on another subsystem is a state tree or a task graph.** `Ask` is
the call — it posts the request and stays *Running until the service has served it*; `Asked
Result` is the return, reading the answer contract the service announced; a reaction graph on a
request row is the continuation. The beat is lit on the canvas while it waits (M38.4), so a
call nobody answers is *seen* stuck, not silently skipped. There is no UniTask in this stack;
`Running` is the await.

*What it forbids:* a hand-written FSM, a coroutine or a callback chain that spans two
subsystems; a request key written over while a previous request sits unserved (the mailbox
has one slot — M39.3).

*Difficult types:* a result crosses as a contract object announced on its key and flattened
field by field (`craft.last.line`, `craft.last.made`) — a dictionary or a simple return, which
is all a graph needs.

*Check:* `RequestTask`/`Ask` returns Running while its key holds a request (tested); the
validator flags a key no def serves; a C# class that holds "phase" state for a cross-subsystem
sequence is a finding in review.

## 3. Lifetime is the scope

**A thing that can change — user, level, character, mission — is a scope that is destroyed and
rebuilt, never a value other systems watch.** `Root / Level / Player` hosts own their services
and `Dispose` them with the scope. The born thing asks for what it needs at birth (the level's
objectives ask the save for their snapshot; a new body asks the bag what it wears); nothing
that outlives it watches for it to be replaced.

*What it forbids:* "re-find and re-subscribe when X is replaced" code (`m_*Wired` flags, a
`Tick` that compares the current X to the last one, `Body()`-style change detection); a
session-long service that holds state belonging to a shorter life; a "current user changed"
event.

*Check:* grep for `Wired`, `ReferenceEquals(..., m_Last`, `!= m_Previous` in services; a
service of scope S holding a reference to a thing of shorter scope without asking at the
thing's birth is a finding in review.

## 4. Declared is what crosses

**What one subsystem offers another is a row on its def — a request (with its answer
contract), an announcement, a setting, a spawn. Everything inside a subsystem is plain C#.**
A screen's own buttons, a service's own bookkeeping, a panel's refresh are not rows. A row
comes back the day a flow asks for it, not the day it might.

*What it forbids:* `internalOnly` rows (a def admitting nobody else should call it); reaction
rows carrying a subsystem's conversation with its own screen; a graph node or tree task whose
only job is to call one verb on one service (the `Ask` node exists).

*Check:* `BagSeamTests.TheDefs_CarryOnlyWhatAFlowWires` pins the generated defs;
`NodeWrapperDrift` and `DeclaredApiValidator` keep the declared surface honest; the Subsystem
APIs window is the readout.

## 5. Capabilities are interfaces, and the scope provides them

**What a subsystem offers to *code* — as opposed to a flow (rule 4) — is a small interface in
the runtime layer, named for what it does, with the few members a consumer actually calls.**
`IBag` (count, add, remove), `IAutosave` (knock), `IWornView` (show what is worn), `IBindsBody`
(here is your body). A consumer asks for the capability, never the class; the scope provides an
instance under every capability it implements (the installer does this from the class's
interfaces, `Provide(typeof(IBag), bag)` does it by hand); the runtime defines capabilities and
the game implements or consumes them — in both directions — so the infrastructure layer
(defs, services, subsystems, hosts) never references a game class, and a game class never has
to be the thing another one names.

*Why it is a rule and not taste:* it is what let the bag (runtime) tell a hand (game) what it
wears, the quest line (runtime) knock on a save (game), and a scope (runtime) bind a body to
whichever services want one — without a bridge, an event or an assembly cycle. Each capability
is one interface and one provide; each replaces a watcher.

*What it forbids:* `[InjectService]`/`FindService<T>` on a concrete class where an interface of
the members used would do; a runtime type that must know a game type (the compiler already
refuses); an interface with members nobody calls ("in case").

*Check:* the assembly boundary (runtime cannot reference examples); a review question for every
new injection — "which capability is this, and who else could provide it?"; the installer's
provide-under-all-interfaces test (`ServiceSettingsTests.TheSwap_…`).

---

## How they compose

- A **feature** is: a def (rule 4) whose class does its work by calls (rule 1), shows its own
  screen and holds it (rule 1), lives in the scope its facts live in (rule 3), offers what code
  needs as a capability (rule 5), and whose cross-subsystem sequences are drawn (rule 2).
- **Adding to a feature** means adding a line to a method (rule 1), a row to a def (rule 4), or
  a node to a flow (rule 2) — never a subscriber.
- **Removing a feature** means dropping its scope (rule 3); nothing else holds a wire to it.

What these rules cost, stated plainly: a subsystem *names* the services it affects (the bag
knows the quest line and the save exist). That is coupling made visible at the source end,
where it can be read, instead of spread over consumers where it cannot. Measured on M39.2 →
39.2b: same lines, the files a reader opens to learn every effect of one write went from 4 to
1, and subscription-lifetime code (the M26 and M35.2 bug class) from 28 guards to 10.
