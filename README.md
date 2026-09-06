# EventsPublisher
Event system for Unity

## Publishing and subscribing

`EventsFor<T>` is the entry point, one closed type per enum family. It is static and
lazily initialized, so it needs no GameObject, no `[DefaultExecutionOrder]`, and works
from a scene loaded in any order:

```csharp
using TempleRunBus = CrawfisSoftware.Events.EventsFor<TempleRunEvents>;

private void Awake()
{
    TempleRunBus.Subscribe(TempleRunEvents.PlayerDied, OnPlayerDied);
}

private void OnDestroy()
{
    TempleRunBus.Unsubscribe(TempleRunEvents.PlayerDied, OnPlayerDied);
}

private void Die()
{
    TempleRunBus.Publish(TempleRunEvents.PlayerDied, this, _finalDistance);
}
```

`EventsPublisherEnumsSingleton<T>` is deprecated as of 2.6.0 and will be removed in 3.0.
It still works and forwards to the same facade, so existing scene objects keep running
unchanged — but a subclass now compiles with an obsolete warning. **Window > Events >
Upgrade Audit** lists the subclasses to replace and what removing them touches in your
scenes.

## Delivery policy

Under additive scene loading a subscriber's `Awake` may run after an event has already
been published. Declare a policy so the publisher retains the event rather than making
every subscriber mirror it into a `bool`:

```csharp
[EventEnum]
public enum TempleRunEvents
{
    PlayerFailingAtTurn = 12,                              // edge  -> Transient (default)
    [EventDelivery(EventDelivery.Sticky)] PlayerDied = 5,  // level -> retained
    [EventDelivery(EventDelivery.Replay)] SegmentCreated,  // accumulation -> journalled
}
```

| Policy | A subscriber arriving later gets |
|---|---|
| `Transient` | nothing |
| `Sticky` | the last `(sender, data)`, immediately on subscribe |
| `Replay` | every `(sender, data)`, in order, immediately on subscribe |

The judgement per event is **edge or level**. An edge is a transition and is only
meaningful in sequence, so replaying it is wrong. A level is a state and is
self-describing — received once, late, with no history, it still tells the truth.

`TryGetLast` reads the retained value without subscribing, for a one-shot read, a
non-`MonoBehaviour`, or an editor tool.

`[EventEnum]` registers a family before any scene loads. It only matters when an event
name reaches the publisher as a raw string — from a serialized Inspector field — without
the facade ever being touched; anything published or subscribed through `EventsFor<T>`
is registered by that first touch.

A `Replay` journal grows for as long as its publisher frame lives. Scope it by pushing a
publisher frame when the owning scene loads and popping it on unload.

## Typed payloads

`data` is an `object`, so a handler that casts it wrongly fails at runtime, inside the
handler, with nothing naming what the event was supposed to carry. Declare the payload
type and the compiler checks it instead:

```csharp
[EventEnum]
public enum TempleRunEvents
{
    [EventPayload(typeof(PlayerFailedData))]
    [EventDelivery(EventDelivery.Sticky)]
    PlayerFailed,
}

// Resolve once. Everything downstream of this line is compiler-checked.
private static readonly EventId<PlayerFailedData> Failed =
    EventsFor<TempleRunEvents>.Id<PlayerFailedData>(TempleRunEvents.PlayerFailed);

private void OnEnable()  => Failed.Subscribe(OnPlayerFailed);
private void OnDisable() => Failed.Unsubscribe(OnPlayerFailed);

// No cast, and a wrong parameter type will not compile.
private void OnPlayerFailed(string eventName, object sender, PlayerFailedData data) { }

private void Fail() => Failed.Publish(this, new PlayerFailedData(...));
```

The type argument is written once, in the `static readonly` field, and checked against
the attribute at startup. Two call sites declaring different types for the same event is
the one mistake the compiler cannot catch, so it is reported the moment the second one
is minted.

Adoption is per event and both directions keep working: a typed publish reaches existing
untyped subscribers, an untyped publish reaches typed handlers, and `SubscribeToAllEvents`
still receives `object`, so the global logger and event history do not change.

An untyped publish carrying the wrong payload is reported at the publisher — naming the
event, the expected type, the actual type, and the sender — and typed handlers are
skipped rather than handed a value they cannot use. An event with no `[EventPayload]` is
unchecked, exactly as before.

Use `EventId<TData>.Of("Some/Name")` for a name no enum can annotate. `typeof(object)`
declares an intentionally untyped payload.

## Authoring event names in the Inspector

Scene data has no compile step, so a plain `[SerializeField] string` event name has no check
of any kind. Two ways to get a dropdown of real events instead:

```csharp
[EventName] [SerializeField] private string _eventToFire;   // existing fields
[SerializeField] private EventRef _eventToFire;             // new fields
```

Prefer `[EventName]` when converting an existing field. Changing a field's type changes its
serialized shape, so Unity cannot carry the old value across and every name already baked into
a `.unity` or `.prefab` asset would be lost. The attribute leaves the field a string, so
existing values keep working and only the authoring experience changes.

`EventRef` also has publisher overloads, so an authored call site never unwraps the string:

```csharp
EventsPublisher.Instance.PublishEvent(_eventToFire, this, null);
```

The dropdown lists every member of every enum marked `[EventEnum]`, grouped by family. A value
that matches no known event is shown as `Missing/<value>` rather than silently cleared, so a
name whose enum is not yet marked — or that has since been renamed — is surfaced instead of
being replaced the moment the Inspector paints it.

## Name collisions

Event names are projected from the enum's simple type name, so two enums both called
`Events` in different namespaces would share every event. The publisher reports this at
registration. Fix it with an explicit prefix on one of them:

```csharp
[EventEnum(Prefix = "GuiEvents")]
public enum Events { ... }
```

Only that enum's names change. Set it when the enum is introduced — changing it later
renames every one of its events and breaks any name already serialized.

## Listing the event domains

**CrawfisSoftware > Events > List Domains** logs one line per registered enum family: the
prefix it publishes under, the enum's full name, how many members it declares, and how
many of those declare `[EventPayload]`, `Sticky` or `Replay`.

```
GameFlowEvents/  —  MyGame.Flow.GameFlowEvents  —  7 member(s), 2 with [EventPayload], 3 Sticky, 0 Replay
```

Nothing sweeps in edit mode — the sweep runs before the first scene loads — so the menu
runs that same sweep before reporting. In play mode it also lists the families that
registered lazily on first touch, which no scan for `[EventEnum]` can see.

The same list is available in code as `EventsRegistry.RegisteredEnumTypes`, with
`EventsRegistry.GetPrefix(type)` for the prefix each one projects onto. It reports what
the registry holds rather than what the project contains, and every read hands back the
same collection rather than building a snapshot.

## Diagnostics

`EventsPublisher.StrictMode` — on by default in the editor and development builds —
reports publishing an event name no frame has registered. Without it, a misspelled name
reaches no subscriber while still notifying "all events" subscribers, so the logger
prints it and the system looks healthy. It also reports a publish whose payload is not
what the event declared it carries, naming the sender.

**CrawfisSoftware > Events** also has *Log Events* (logs every publish while in play
mode), *List Current Subscribers*, and *Clear Now*. Subscriptions and retained values are
dropped automatically once play mode has finished tearing down, and again on the next
play entry; registrations and declarations stay (see the next section). Inspect a
subscription leak while still in play mode; afterwards there is nothing to list.

## Entering Play mode without domain reload

Unity 6.6 creates new projects with domain reload off when entering Play mode, and
recommends that setting everywhere. Statics then survive from one play session to the
next, so the package resets itself at `SubsystemRegistration` on every play entry, and
the editor does the same once play mode has finished tearing down:

| Dropped at each play entry | Kept |
|---|---|
| subscriptions, including "all events" subscribers | event registrations |
| Sticky values and Replay journals | delivery policies, whether from an attribute or `RegisterEvent(name, policy)` |
| publisher frames pushed above the root | declared payload types, prefix claims, the domain listing |
| typed-handler wrappers, `EventsDiagnostics` counts | interned `EventId`s |

Runtime state belongs to a session. Declarations derive from code, and code changes
only through a recompile, which always reloads the domain — so a policy or payload type
declared from a static initializer stays declared even though that initializer runs
once. With domain reload on, the reset runs against empty state and changes nothing.

The end-of-play drop is the same minus the diagnostics, which the upgrade audit reads
after play mode ends. `EventsPublisher.StrictMode` and `EventsDiagnostics.Enabled` are
ordinary settings and are never reset. *Clear Now* is the stronger operation: it drops
registrations too, and an annotated family is registered again by the next play entry.

That covers the package's statics. `Documentation~/fast-play-mode-prompt.md` is a general
prompt, not specific to this package, for the singletons and static state in a Unity project
itself — a `MonoBehaviour` with a static `Instance`, a static blackboard class, a `GameState`
of static bools, static events — covering what breaks without a domain reload, what to
replace each shape with, and the two-play check this package was verified with.

## Upgrading an existing project

**Window > Events > Upgrade Audit** reflects over the project and reads its assets, then
reports which upgrade steps it has and has not taken — so it describes the project in
front of it rather than a generic one. Findings are graded: an *Action* is discovered
from evidence, a *Suggestion* is a guess or a judgement it cannot make for you.

Enter play mode and run the boot sequence before refreshing. `EventsDiagnostics` records
every event that was subscribed to *after* it had already been published — the delivery
that was missed — so the edge-or-level decision is read off a run rather than guessed.
It is on in the editor and development builds, off in release.

`Documentation~/UPGRADING.md` is the step-by-step guide, and
`Documentation~/upgrade-prompt.md` is a project-agnostic prompt for doing it with an AI
assistant. Nothing in the upgrade is required — the package is source-compatible, and
every step is opt-in.

## Running the tests

The tests live in `Tests/Editor` and are EditMode tests. Unity only builds a package's
tests when the consuming project opts in, so add the package to `testables` in the
project's `Packages/manifest.json`:

```json
{
  "testables": [ "com.crawfissoftware.eventspublisher" ]
}
```

They then appear under **Window > General > Test Runner > EditMode**.

## Design notes

`Documentation~/adr/0001-event-identity-and-delivery.md` records why events are keyed on
strings, what that costs, and the staged plan for making the string a projection rather
than the identity.
