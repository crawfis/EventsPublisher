# Upgrading a project to EventsPublisher 2.4

Nothing here is required. The package is source-compatible: a project that upgrades and
changes nothing keeps working exactly as before, with better diagnostics. Every step
below is opt-in and independently shippable.

> **Skip 2.4.0 and upgrade to 2.4.1.** 2.4.0 accidentally changed when a publish made
> from inside a handler was delivered, so the paragraph above was false for that one
> release — see the [erratum](#erratum-240-reordered-re-entrant-publishes) at the end of
> this file for who it affects and how it shows up. 2.4.1 restores the 2.3.x behaviour
> and pins it with tests.

The design reasoning is in `adr/0001-event-identity-and-delivery.md`. This file is the
operational version — what to do, in what order, and which parts a tool can decide for
you.

## Start with the audit

**Window > Events > Upgrade Audit**, then **Refresh**.

It reflects over the project and reads its assets, so it reports what *this* project
contains rather than what a generic guide assumes. Findings are marked:

- **Action** — a concrete step, discovered from evidence, safe to just do.
- **Suggestion** — a guess, or a judgement the tool cannot make for you.
- **Ok** — already done.

The distinction is load-bearing for step 3 in particular. A field is reported as an
Action only when an actual asset holds a known event name in it; a field merely *named*
like an event name is a Suggestion, and those do produce false positives — a `[TextArea]`
field called `_events` that holds a log dump will be listed. Read them, do not batch-apply
them.

Before the last step, enter play mode and run the boot sequence, then refresh again. The
delivery-policy evidence comes from what actually happened during a run.

**Copy report** puts the findings on the clipboard, which is how you hand them to an
assistant along with `upgrade-prompt.md`.

## The steps

### 1. Enable the tests

Add the package to `testables` in `Packages/manifest.json`:

```json
{ "testables": [ "com.crawfissoftware.eventspublisher" ] }
```

Unity only builds a package's tests when the consuming project opts in. 109 EditMode
tests then appear under **Window > General > Test Runner**. Running them once in your
project is a genuine check of the project's setup, not a formality.

### 2. Mark the event enums — `[EventEnum]`

Registers a family before any scene loads and populates the Inspector dropdowns.

Watch for a prefix-collision error: two enums with the same simple name in different
namespaces project onto the same event names and would silently share every event. Fix
it with `[EventEnum(Prefix = "...")]` on **one** of them. Do not rename the enum type,
and do not switch the projection to `Type.FullName` — that renames every event in the
project to fix a local problem, breaking every name baked into a scene or prefab.

### 3. Mark Inspector event names — `[EventName]`

Scene data has no compile step, so a `[SerializeField] string` event name has no check of
any kind. `[EventName]` turns it into a dropdown.

**Add the attribute; do not change the field's type.** Converting a `string` field to
`EventRef` changes its serialized shape, so Unity cannot carry the value across and every
name already saved in a `.unity` or `.prefab` is lost. Use `EventRef` for new fields only.

The audit finds these two ways. Fields whose value in an actual asset is a known event
name are certain — that catches a field called `_trigger` that no naming heuristic would
find. Fields merely *named* like event names are guesses, and marked as such.

After marking, open each affected component. A value showing as `Missing/<name>` means
that name is in no `[EventEnum]` enum — investigate rather than re-picking, because the
publisher is keyed on exactly that string.

### 4. Retire the execution-order workaround — `EventsFor<T>`

`EventsPublisherEnumsSingleton<T>` needs a GameObject and assigns `Instance` in `Awake`,
which is what any `[DefaultExecutionOrder(-10000)]` on its subclasses is for. That
attribute orders `Awake` **within one scene load batch**, and an additively-loaded scene
is a separate batch — so a scene loaded before the one hosting the singleton
`NullReferenceException`s on `Instance`. The attribute never protected additive loading.

`EventsFor<T>` is static and lazily initialized, so the race cannot occur:

```csharp
using GameFlowBus = CrawfisSoftware.Events.EventsFor<GameFlowEvents>;

GameFlowBus.Publish(GameFlowEvents.GameStarting, this, null);
```

Then delete the singleton subclasses and the execution-order attributes. The old
singleton still works and forwards to the same facade, so the call sites can move file by
file. As of 2.6.0 it is `[Obsolete]`, so each remaining subclass declaration reports itself
as a compiler warning; removal is planned for 3.0.

The deletion itself is not code-only, which is easy to miss. A subclass is a
`MonoBehaviour`, so removing it orphans every instance authored into a scene and leaves a
missing-script entry where a working singleton used to be. Find them by the subclass's
script GUID and remove them; where the component is alone on its GameObject, remove the
GameObject too and unlink its transform from the scene roots or its parent's `m_Children`.
Verify nothing still references what you removed — this is the one part of the upgrade that
edits `.unity` assets.

### 5. Remove per-publish `Enum.Parse`

An "all events" handler that slices the name on `/` and calls `Enum.Parse` allocates a
string and does a reflection lookup on **every published event**. Replace with
`EventsFor<T>.TryGetEnum(eventName, out var value)`, which allocates nothing.

### 6. Replace `bool` mirrors with delivery policy

This is the largest win and the only step needing real judgement.

The symptom: static `bool`s named like `IsInitialized` / `HasSignedIn`, set from event
handlers, polled in `Awake` by anything that might have loaded too late to hear the
event. Those mirrors drift — one that is declared and reset but never set is common — and
they cannot carry the event's payload.

**Let the audit tell you which events need this.** Enter play mode, run the boot
sequence, refresh: every event whose subscribers arrived after it was published is
listed, most affected first. That is the measured version of the question, and it
routinely disagrees with the guess.

Then decide, per event, **edge or level**:

- **Edge** — a transition (*"just failed at a turn"*), only meaningful in sequence. Leave
  it `Transient`. Replaying an edge is actively wrong: a late subscriber would act on a
  transition that has since been superseded.
- **Level** — a state (*"gameplay is ready"*), self-describing on its own. Mark it
  `[EventDelivery(EventDelivery.Sticky)]`.

An event that already carries a payload is usually a level — needing data to be useful is
the tell.

Where a level is currently expressed as **two opposing edges** — `Ready`/`NotReady`,
`SignedIn`/`SignedOut`, `Paused`/`Resumed` — sticky alone is unsafe, because a late
subscriber gets whichever half fired last regardless of what happened after. Model the
level directly instead, carrying the value:

| Edge pair | Level event | Payload |
|---|---|---|
| `GameplayReady` / `GameplayNotReady` | `GameplayReadyChanged` | `bool` |
| `PlayerSignedIn` / `PlayerSignedOut` | `AuthStateChanged` | `AuthState` |
| `Paused` / `Resumed` | `PauseStateChanged` | `bool` |

Prefer an enum over `bool` wherever "not yet" is meaningful — a never-published sticky
event does not invoke the subscriber at all, which `bool` cannot express.

The edges are **not** deleted. Animation, SFX and analytics want the moment, not the
state. Keep them `Transient` alongside the new level.

Check whether an event is the **source** of an auto-chain or bridge mapping before marking
it `Sticky`. A retained event is redelivered to anything subscribing late through
`SubscribeToAllEvents`, so the chain re-fires from that point. That is often the fix — a
bridge in an additively-loaded scene that was silently missing the event now receives it —
but the same mechanism can re-run a command or re-trigger a scene load. The maps are worth
reading before the decision, not after.

Then delete each `bool` and its `Awake`-time poll. A subscriber to a `Sticky` event is
delivered the retained value immediately on subscribe, **with the payload**. For code
that needs the value without subscribing, use `TryGetLast`.

### 7. Type the payloads — `[EventPayload]`

`data` is an `object`, so a handler that casts it wrongly fails inside the handler, where
the publisher catches it and logs an exception naming neither the expected type nor who
published it.

```csharp
[EventEnum]
public enum TempleRunEvents
{
    [EventPayload(typeof(PlayerFailedData))]
    [EventDelivery(EventDelivery.Sticky)]
    PlayerFailed,
}

private static readonly EventId<PlayerFailedData> Failed =
    EventsFor<TempleRunEvents>.Id<PlayerFailedData>(TempleRunEvents.PlayerFailed);

private void OnEnable()  => Failed.Subscribe(OnPlayerFailed);
private void OnDisable() => Failed.Unsubscribe(OnPlayerFailed);
private void Fail()      => Failed.Publish(this, new PlayerFailedData(...));

// No cast. A wrong parameter type will not compile.
private void OnPlayerFailed(string eventName, object sender, PlayerFailedData data) { }
```

Resolve the id **once**, into a `static readonly` field. Everything downstream of that
line is compiler-checked; the line itself is checked at startup against the attribute.
Two call sites declaring different types for the same event is the one mistake no
compiler can catch, so it is reported when the second one is minted.

Start with events that **already carry data** — those have a real type to declare and the
most casts to delete. Leave an event unannotated if it has no payload or a genuinely
variable one; no declaration means no checking, which is the intended default.

Do not try to type Inspector-authored components from step 3. They hold their event name
as data, not as a literal, so there is no type argument to write. `StrictMode` covers them
instead: a publish whose payload does not match the declaration is reported at the
publisher, naming the sender. **That report is the main reason to declare payload types
in a project that still has raw-string publishes** — which most do, and will keep doing.

## Things that stay true throughout

- **Never rename a published event.** Names are serialized into scenes and prefabs;
  renaming silently breaks every baked reference. All of the above is additive.
- **Keep `EventsPublisher.StrictMode` on while migrating.** It reports an unregistered
  name and a mismatched payload — the two mistakes steps 2, 3 and 7 can introduce.
- **Erasure holds in both directions.** Typed publishes reach untyped subscribers,
  untyped publishes reach typed handlers, and `SubscribeToAllEvents` still receives
  `object` — so the global logger, event history and editor menus need no change at any
  point. Migrate one event, one file, one step at a time.
- **A `Replay` journal grows for the life of its publisher frame.** If you adopt
  `Replay` for an accumulation, scope it: push a publisher frame when the owning scene
  loads and pop it on unload.
- **`EventId` needs nothing from you.** The publisher keys on an interned handle
  internally and handler signatures did not change. It only becomes visible if you choose
  to hold one.

## Turning the diagnostic off

`EventsDiagnostics.Enabled` defaults to on in the editor and development builds, off in
release. Set it to `false` to disable recording entirely; it then costs one bool test per
publish. `EventsDiagnostics.Reset()` clears what has been recorded, and is called
automatically at the start of each play session so a report describes one run.

## Erratum: 2.4.0 reordered re-entrant publishes

2.4.0 shipped a behavioural change while documenting it as no change, so upgrades were
not audited for it. A publish made from **inside** an event handler no longer completed
before the publishing statement returned; it was queued and delivered after everything
already in flight — including after the rest of the handler that published it. Top-level
publishes were unaffected, which is why every test that existed at the time stayed green.
2.4.1 restores the 2.3.x contract, now pinned by `NestedPublishOrderingTests`:
`PublishEvent` returns only after its event has been delivered, at any nesting depth.

If a project ran on 2.4.0, the thing to look back for is a silent reorder, not an error.
Affected code publishes from inside a handler and relies on the effects of that publish
afterwards:

- a handler that publishes X and then reads state X's subscribers computed;
- a handler that publishes two events in sequence, where the second event's subscribers
  consume what the first event's chain produced;
- auto-chain or bridge components that republish in response to events — the chained
  event landed behind anything published later in the same cascade.

It shows up as ordering casualties with nothing in the failure naming the event system: a
guard like `if (cache.Count > 0)` or `if (_ready)` silently skipping because the event
that satisfies it arrived late; a `NullReferenceException` in a handler whose input an
earlier event should have set; state lagging one event behind what was just published.
Moving to 2.4.1 removes the cause. To audit a run that stays on 2.4.0, search handlers
for `PublishEvent` (and facade `Publish`) calls that are followed by more code in the
same handler, or that are one of several publishes in one handler — those are the sites
whose timing changed.
