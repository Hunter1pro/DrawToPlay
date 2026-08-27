# Defs and services

A **service** is a plain C# class. A **def** is the asset that declares its whole surface and
makes it. Nothing about a service is discovered at runtime by scanning — everything it manages,
answers, says, shows, has, promises and is tuned by is written down on the def.

The def sits **on top of** the class. The class holds behaviour and the constants; the def holds
data and wiring; the installer builds one from the other.

## `ServiceDef`, field by field

| field | what it declares |
|---|---|
| `serviceName` | the human name |
| `serviceTypeName` | the class it makes (`serviceType` resolves it) |
| `scope` | `Root`, `Level` or `Player` — its lifetime |
| `registry` / `declares` | the catalog it manages, and every catalog it declares |
| `requests` | what it can be **asked** — key, action, value hint, `namesRowOf` |
| `spawns` | the UI rows it **shows** (`StateTreeEntryRef<UiDef>`) |
| `attributes` | what it **has** |
| `settings` | how it is **tuned** (`ServiceSettingSet`) |
| `body` | the `ServiceBody` it builds, when it is a kind rather than a subsystem |
| `nestingRules`, `kindSeeds` | how it composes and what it seeds |

`reactionGraph` on a request lets a request be answered by a drawn graph rather than a `switch`.

## Subsystems and kinds are both defs

This distinction catches everyone once. A project's defs split in two:

- A **subsystem** names a class and is *installed* on a scope. The bag, the bench, the level
  service, the director. This is the number you decide a project in.
- A **kind** names **no class** and builds a **body**. It is spawned by *placement*, not
  installed — the thing a level drops into the world.

A def with no class is not a broken subsystem; it is a kind. `Tools/Draw To Play/Subsystems`
lists them under separate headings for exactly this reason.

## Writing the class

```csharp
[StateTreeService]
public sealed class BenchService
{
    public const string CraftAction = "craft";        // one const per action

    [ServiceSetting(3f, "How long one craft takes.")]
    private float m_CraftSeconds;                      // one field per knob

    public BenchService(StateTreeContext scope, ServiceDef def) { … }   // the constructor

    public object OnRequest(string action, object value) => action switch
    {
        CraftAction => Craft(value),
        _ => null,
    };
}
```

- `[ServiceActionContract]` marks an action as part of the declared contract.
- `[ServiceSetting(default, "description")]` declares a knob. It appears on the def's inspector
  and can be overridden at install.
- The constructor takes `(scope, def)`. A service is **made**, not found. Any further
  parameter is a subsystem handed from the scope at install — `(scope, def, WorldService
  world)` — which is why an installer's list is in dependency order; a required collaborator
  that is not installed yet fails the install out loud.

**No `public event` on a service.** This is the first meta-rule and the one most often reached
for. A subsystem that needs to know something *asks*; a subsystem that needs to wait *draws a
flow*. If you find yourself adding an event so another system can react, the reaction belongs in
a state tree or a graph.

## Settings and which value wins

`ServiceSettingSource` is `Code`, `Def`, `Install` — and that is also the precedence order,
lowest to highest:

1. **Code** — the `[ServiceSetting]` default, always present, so the service runs with nothing
   authored.
2. **Def** — the value on the def asset; the project's answer.
3. **Install** — the override on this particular installer row; this scene's answer.

An unset value at a higher layer does not blank the lower one; it simply does not participate.

## Capabilities are interfaces

What a subsystem offers to *code* is a small runtime interface — `IBag`, `IAutosave`,
`IWornView`, `IBindsBody`, `IScreenJuice`, `ILevelProgressStore`, `IWatchesCitizens`.

Consumers name the **capability**, never the class. The scope provides under every capability a
service implements. The runtime *defines* the interface; the game implements or consumes it —
never the reverse. This is what lets a project swap the bag implementation without touching
anything that uses a bag.

## Declared is what crosses

A def's rows are what the subsystem offers to the outside. Everything inside the subsystem is
plain C# and is nobody's business. There is no `internalOnly` row — if it is on the def, it
crosses; if it should not cross, it is a private field.

## Drift

Three things describe one subsystem — the sketch, the def, the class — and they will disagree.
The `Subsystem APIs` window lists the disagreements: an action the class declares that the def
does not serve, a setting on the def the class no longer has, a request whose `namesRowOf`
names an undeclared catalog, a capability nobody consumes.

Check it after renaming anything. The generated test fixture also fails the day the class and
the def stop agreeing, which is usually sooner than anyone notices by reading.
