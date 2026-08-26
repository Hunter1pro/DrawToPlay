# Tags

A tag is the most-used wire in the toolset: it is how one system asks a question about an
entity it knows nothing else about. *Is this thing flammable? Is it a citizen? Can it swim?*

The rule that makes tags safe is that the **vocabulary is declared**. A tag is a row in
`WorldTagRegistry`, not a string someone typed.

## Declaring

Tags live in a `WorldTagRegistry` as `WorldTagDef` rows, each with a group, an id, and a
description of what carrying it *means*. The description is not decoration — a tag whose meaning
is not written down gets used two ways within a month.

```
Group "State" · id "burning" · "Takes damage over time and ignites what it touches."
```

## Using one in code

`[WorldTag]` on a string field turns it into a picker over the declared vocabulary:

```csharp
[WorldTag] public string requiredTag;
```

The inspector then offers the declared tags, grouped, instead of a free text field. A typo
becomes impossible rather than becoming a bug that shows up in one level.

`HasTagCondition` is the condition form for state trees; `HasWorldTagConditionValueNode` is the
graph node. `LevelObjectTagRef` is how a placement carries tags.

## Who holds tags

Tags sit on the **world** side, managed by `WorldService`. An entity registered with the world
carries its tags there; asking is a call to the world service, not a component lookup on the
GameObject. That is what lets a tag be asked about something that has no component at all —
a placement, a zone, a level fact.

`IWatchesCitizens` is the capability for a service that needs to know when the world's
population changes.

## What tags are and are not

**Are:** a declared, project-wide vocabulary for questions about entities.

**Are not:** a way to smuggle state between systems. A tag added and removed every few frames
to signal something is an event in disguise, and events between systems are the thing
this design does not have. If one subsystem needs to tell another something, it asks or it
draws a flow — see [subsystems.md](subsystems.md).

**Are not:** a replacement for a capability interface. If code needs to *do* something to a
thing, it names the capability (`IBag`, `IWornView`). Tags answer questions; interfaces offer
verbs.

## Seeing them

Tags are visible per entity rather than only per registry — you can look at a thing and see what
it carries, which is what makes an unfamiliar level readable. That inspection is the reason the
vocabulary is declared in the first place: a registry of tags nobody can see the effect of would
just be a longer way of writing a string.
