# Subsystems: the creation flow, and call and return

A subsystem is a service plus everything declared around it. This document is about **making
one** and about **how two of them talk**.

## Call and return — the only way subsystems talk

One rule, and most of the design follows from it:

> A subsystem **calls** the services it needs, in order, in one method, and **returns** a
> result. Anything that has to *wait* is a drawn flow.

There are no events between systems. No service subscribes to another. No service calls back.
When a bench needs the bag, it asks the bag and uses the answer on the next line.

When the wait is real — a screen the user has to dismiss, a cutscene that has to finish — the
waiting is drawn as a state tree or a task graph: `Ask Subsystem` issues the request,
`Asked Result` is the return on the far side. The flow is the thing that waits; the service
just answers.

The payoff is that you can read one method and know what happens. The cost is that "fire and
forget" is not available, and that is deliberate.

## Requests

A request row on the def is the unit of asking:

| part | meaning |
|---|---|
| `key` | unique project-wide; `ServiceKeyCode` knows the existing ones |
| `action` | the verb, matching a `public const string …Action` on the class |
| `namesRowOf` | which catalog the value names a row of, when it names one |
| `valueHint` / `emptyMeans` | what the value means, and what an empty one means |
| `reactionGraph` | answer this request with a drawn graph instead of a `switch` case |

The standard verbs are `ask`, `set`, `add` (`ServiceDef.AskVerb` and friends).

**Announcements** are the other direction: a service *says* something happened, and whatever
displays it picks it up. An announcement is not an event subscription — nothing is registered,
and no control flow crosses.

## Making a subsystem

`Tools/Draw To Play/Subsystems` is both the project's table of contents and the creation flow.

**The table of contents** lists every `ServiceDef` grouped by scope, with its class (resolved or
missing), its request/announcement/setting counts, and which scenes install it — read from
binary scenes through `AssetDatabase.GetDependencies`. Kinds are listed separately. This is the
engineer's view of the project, and it is useful whether or not you ever create from it.

**The sketch.** `SubsystemSketch` is the form: name, scope kind, the catalog it manages (an
existing registry, a new one of a chosen kind, or none), an optional capability interface, and
row lists for requests, announcements, spawns, settings, attributes and contracts. Every typed
reference on it is a picker offering what the project already declares.

`SubsystemSketchValidator` asks at author time what the runtime would ask later: is this key
already served elsewhere, is this action a valid word, is this class name taken, does this row
come from a catalog nobody declares.

**Generation writes, once:**

- the `ServiceDef` asset with every list filled and `serviceTypeName` set;
- the class — one `[ServiceActionContract]` and `public const string` per action, one
  `[ServiceSetting]` field per setting, the `(scope, def)` constructor, and an `OnRequest`
  switch whose every case is a loud `Debug.LogError("not implemented: …")`;
- the capability interface, when named, with the class implementing it;
- a test fixture that builds the service from its def on a throwaway scope and asserts the
  declared surface;
- an installer row on the chosen scope.

**The def may be regenerated at any time. The class is written once** — the flow never
overwrites a file an engineer has edited. What it does after that is report drift.

A generated `OnRequest` case logs an error rather than returning silently, so an unimplemented
action announces itself the first time it is asked instead of looking like a working no-op.

## Installing

`StateTreeServiceInstaller` builds the declared services for a scope. `StateTreeServiceInjector`
fills `[InjectService]` fields on tasks from that scope. Install-time overrides of
`[ServiceSetting]` values live on the installer row — the highest-precedence layer.

Nothing self-injects. A component that reaches for a service by scanning the scene has broken
*lifetime is the scope*: it will find the previous level's service the moment levels change.

## Contracts

`ContractDef` and `[StateTreeContract]` / `[StateTreeImplements]` let a subsystem declare a
promise other code can name — the same idea as a capability interface, expressed as data so
that a def can point at it. `ContractRegistry` is the catalog.

## Where the boundary sits

- **C# task** — computation, a loop, a query, real math.
- **Graph node** — wiring, a branch, an ask, a wait.
- **Service method** — a decision the subsystem owns and can answer synchronously.
- **State tree** — a mode, and anything that pre-empts another thing.

A step that is one line of C# in a graph node should have been a method on the service. A
service method that awaits should have been a drawn flow.
