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

`EventsPublisherEnumsSingleton<T>` still works and forwards to the same facade, so
existing scene objects keep running unchanged.

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

The dropdown lists every member of every enum marked `[EventEnum]`, grouped by family. A value
that matches no known event is shown as `Missing/<value>` rather than silently cleared, so a
name whose enum is not yet marked — or that has since been renamed — is surfaced instead of
being replaced the moment the Inspector paints it.

## Diagnostics

`EventsPublisher.StrictMode` — on by default in the editor and development builds —
reports publishing an event name no frame has registered. Without it, a misspelled name
reaches no subscriber while still notifying "all events" subscribers, so the logger
prints it and the system looks healthy.

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
