# ADR 0001 — Event Identity and Delivery Timing

**Status:** Proposed
**Date:** 2026-08-12
**Applies to:** `com.crawfissoftware.eventspublisher` 2.3.1 and consumers
(`EventsPublishingTesting`, `RunnerUGSTemplate`)

---

## Context

The publisher keys every event on a `string`. Enum-based facades
(`EventsPublisherEnums<T>`) project an enum onto `"EnumTypeName/MemberName"`, and
that string becomes the identity used by the dictionary, the subscribers, and the
global logger.

Two separate problems follow from this design. They are usually discussed
together, but they have different causes and different fixes.

1. **Identity** — the string is the identity, so nothing checks that a call site
   reproduced it correctly.
2. **Timing** — under additive scene loading, a subscriber's `Awake` may run after
   an event has already been published, so the subscriber never sees it.

The second problem is currently worked around by mirroring events into static
`bool` state and polling that state at `Awake`.

### Why the string exists

The string exists to serve the *observation* side: `SubscribeToAllEvents` feeds
`EventLoggingMenu`, `EventHistory`, and `DebugEventFileLogger`, all of which need a
readable name for an event whose concrete type they do not know.

That requirement is real, but it only constrains the **read** side. The
**publication** side never needed to be stringly-typed. The current design erases
to a string at publish time, which discards the type information before anything
can validate it.

---

## Evidence from the codebase

Observed in `RunnerUGSTemplate` at `dd7537f`:

- **Missed events are handled by hand.** `UGS_State` declares eight static
  `bool`s under the comment *"Keep track of potentially missed events in scenes
  that load after UGS initialization"*. `PlayerAuthenticationManager.cs:50` reads
  `if (UGS_State.IsCheckForExistingSession)` with the comment
  *"// Missed the event being published."*
- **The mirror drifts.** `UGS_State.IsGameReady` is declared and reset but never
  assigned `true`. Any subscriber polling it is silently dead.
- **The mirror loses the payload.** A `bool` records *that* `RemoteConfigUpdated`
  fired, not *what* the config was. Late subscribers need a second state holder to
  recover the data.
- **Every handler needs two code paths.** A subscription for the future, plus an
  `Awake`-time poll for the past. The poll is easy to forget and impossible to
  check.
- **Identity round-trips through the string.**
  `GameFlowAutoEventFlow.AutoFireGameFlowEventFromGameFlowEvent` slices the string
  on `'/'` and calls `Enum.Parse<GameFlowEvents>` — a per-publish allocation plus a
  reflection-backed lookup, to recover an enum the publisher already had.
- **Event names are authored in the Inspector.**
  `[SerializeField] private string _eventName` appears in `TimedEvent` (defaulting
  to `"ERROR"`), `FireEventAfterSceneLoads`, `CloseSceneOnEvent`,
  `FireEventWhenSceneCloses`, `LoadSceneAfterGameControlEvent`, and the
  `Test_AutoFire*` scripts. These bypass the enum facade entirely and have no
  compile-time check of any kind.
- **Names are baked into scene assets.** `GameFlowEvents/GameplayReady`,
  `UGS_EventsEnum/RemoteConfigUpdated` and eight others appear as literals inside
  `.unity` and `.prefab` files.
- **Ordering is load-bearing and invisible.** `EventsPublisherGameFlow`,
  `EventsPublisherUGS`, `EventsPublisherTempleRun` and
  `EventsPublisherUserInitiated` each carry `[DefaultExecutionOrder(-10000)]` so
  their `Awake` sets `Instance` before subscribers run. `DefaultExecutionOrder`
  orders `Awake` calls *within a scene load batch*; an additively-loaded scene is a
  separate batch. A scene loaded before the one hosting the publisher singleton
  will `NullReferenceException` on `Instance`.

---

## Decision 1 — Identity: the string becomes a projection

**The string should be a projection (`.Name`), not the identity.**

Identity becomes a token the compiler owns. `.Name` is derived, and exists only for
display, serialization, and the erased observer channel. Erase at the observation
boundary, not at the publication boundary.

This preserves `SubscribeToAllEvents` and every tool built on it, unchanged.

### Staged path

**Stage 0 — make the hazard loud.** Non-breaking. *(Implemented; see Consequences.)*

- Strict-mode diagnostic when publishing an unregistered name, under
  `UNITY_EDITOR || DEVELOPMENT_BUILD`. Converts silent no-ops into visible errors.
- Detect two enum types projecting to the same `"TypeName/"` prefix and report it.
- Replace `Delegate.DynamicInvoke` with a direct cast invoke.
- Repair the exception handler and the callback-queue drain.
- Null/empty guards on event names.
- Allocation-free `TryGetEnum` so consumers stop calling `Enum.Parse`.

**Stage 1 — close the write side.** Demote the public `string` overloads to
`internal`, or mark `[Obsolete]` first for package consumers. Authored code goes
through an enum facade. The typo class largely disappears with no new types.

Blocked on the Inspector-authored strings above: those call sites need a typed
Inspector control before the string API can be closed. See Open Questions.

**Stage 2 — `EventId` as identity.**

```csharp
public readonly struct EventId : IEquatable<EventId>
{
    private readonly int _id;                    // interned; dictionary key
    public string Name => EventNames.Get(_id);   // projection — observers only
}
```

`EventsPublisherEnums<T>` becomes the interner rather than a string factory, so the
enum authoring surface is unchanged. The internal dictionary keys on `int`: no
string hashing, no collisions, no allocation.

**Stage 3 — type the payload.** `EventId<TData>`, with
`Publish<TData>(EventId<TData> id, object sender, TData data)`. A wrong payload
becomes a compile error rather than an `InvalidCastException` inside a swallowed
callback. Erased subscribers still receive `object`, so the logger, `EventHistory`,
and the editor menus do not change.

`object data` is the larger hazard of the two — a typo'd key fails loudly once
Stage 0 lands, whereas a wrong payload cast fails inside a handler.

### Rejected for now

- **Switching the enum prefix to `Type.FullName`.** This closes the residual
  collision hole (two enums with the same simple name in different namespaces), but
  the projected names are serialized into `.unity` and `.prefab` assets. Changing
  the scheme would silently break every baked reference — the exact rename hazard
  this ADR argues against. Stage 0 ships a collision *detector* instead. Revisit
  alongside a scene-asset migration.
- **Message-type-as-identity** (`readonly record struct GameStarted(int Score)`,
  MediatR/MessagePipe shape). The idiomatic C# endpoint: the CLR type is the
  identity, namespaces make collisions impossible, rename refactoring is safe, and
  the logger prints the type name. Rejected because it costs the enum authoring
  ergonomics and the `Enum.GetValues` iteration that `EventMenu` and
  `SubscribeToAllEnumEvents` depend on. Reasonable for new domains.
- **`ScriptableObject` event channels.** Unity-native and Inspector-friendly, which
  would fix the `[SerializeField] string` call sites specifically, but it does not
  reach pure-C# code and adds asset-management overhead.

---

## Decision 2 — Timing: the bus retains, not the subscriber

**Move replay into the bus so the subscriber has one code path.**

Give each event a declared *delivery policy*:

| Policy | Behavior | Fits |
|---|---|---|
| `Transient` (default) | Fire and forget. Current behavior. | Input, ticks, per-frame gameplay |
| `Sticky` | Bus caches the last `(sender, data)`; a later subscriber receives it immediately on subscribe. | Lifecycle/state: `UnityServicesInitialized`, `PlayerAuthenticated`, `RemoteConfigUpdated`, `GameplayReady` |
| `Replay` | Bus caches the ordered list and replays all of it. | Accumulations (`TrackSegmentCreated`), `EventHistory` |

With `Sticky`, most of `UGS_State` and `GameState` evaporates. Instead of a
subscription *plus* an `Awake`-time poll of a hand-maintained `bool`, a subscriber
just subscribes — and if the event already fired, it is invoked immediately **with
the original payload**. One code path, no mirror to drift, no lost data.

### Declaring the policy

On the enum member, read once at facade construction where `EventsPublisherEnums<T>`
already reflects over `Enum.GetValues`. Zero per-publish cost:

```csharp
public enum UGS_EventsEnum
{
    [EventDelivery(Sticky)] UnityServicesInitialized,
    [EventDelivery(Sticky)] PlayerAuthenticated,
    [EventDelivery(Sticky)] RemoteConfigUpdated,
    PlayerSigningIn,   // transient
}
```

### Edge vs. level — the part that decides whether this works

Sticky replay is correct for **level-triggered** state and wrong for
**edge-triggered** transitions.

The current enums are overwhelmingly edge-based — `MainMenuShowRequested` →
`MainMenuShowing` → `MainMenuShown` → `MainMenuHiding` → `MainMenuHidden`. Replaying
`MainMenuShown` to a late subscriber is meaningless; the menu may since have closed.
This is precisely why the `bool` mirror emerged: the mirror is a hand-rolled *level*
reconstructed from a pair of *edges*.

So the per-event judgement is: **is this an edge or a level?**

- Edges (`Xxxing` / `Xxxed` pairs, requests, one-shot notifications) → `Transient`.
- Levels (`GameplayReady`, `IsGameConfigured`, `RemoteConfigUpdated`,
  `DifficultyChanged`) → `Sticky`.

Where a level is currently expressed as two opposing edges — `PlayerSignedIn` /
`PlayerSignedOut`, `MainMenuShown` / `MainMenuHidden` — sticky alone is unsafe: a
late subscriber after sign-out would still receive a stale "signed in". Two options,
in order of preference:

1. **Model the level directly**, carrying the value:
   `AuthStateChanged(bool signedIn)`, `MainMenuVisibilityChanged(bool visible)`.
   Sticky is then unambiguously correct, and the `bool` mirror is fully replaced.
2. **Declare an invalidation pair** — `PlayerSignedOut` clears the sticky value of
   `PlayerSignedIn`. Preserves the existing enums but keeps the two-edges-one-level
   coupling that caused the drift.

Option 1 is the real fix. Option 2 is the migration aid.

### Reset scope

A stale sticky value replaying into a fresh play session is a regression relative to
today. The `IStackEventsPublisher` push/pop stack is the natural scope: a sticky
value lives in the publisher frame it was published into, and `Pop()` discards it.
`Clear()` must drop sticky state too, and `ClearEventsMenu`'s existing
"clear on exiting play mode" toggle already covers the editor loop.

### Replay re-entrancy

A sticky replay fires during `SubscribeToEvent`, i.e. typically inside `Awake` —
before `Start`, and possibly before other scene objects exist. This matches the
timing of today's `Awake`-time poll, so it is consistent with existing behavior, but
it must be documented: **a handler for a sticky event must tolerate being invoked
during subscription.** Deferring replay to end-of-frame is the alternative; it is
safer but changes ordering relative to the current poll and can itself be missed.

### Independently: make the enum facades static

`EventsPublisherEnumsSingleton<T> : MonoBehaviour` requires a GameObject in a scene,
and that requirement is itself the source of the `[DefaultExecutionOrder(-10000)]`
fragility described above. Nothing in `EventsPublisherEnums<T>` needs a scene
object.

A static, lazily-initialized `EventsFor<T>` removes the `Awake` race entirely, needs
no execution-order attribute, and makes `EventsPublisherGameFlow`,
`EventsPublisherUGS`, `EventsRegistration` and `GuiEventsPublisher` unnecessary.

This is the single highest-value fix for the timing problem and is **independent of
sticky events** — worth doing either way. It requires care around Unity's domain
reload settings (`[RuntimeInitializeOnLoadMethod]` / `[InitializeOnEnterPlayMode]`)
when *Enter Play Mode Options* has domain reload disabled.

---

## Consequences

### Implemented in this change (Stage 0)

Verified by compiling the package against Unity stubs and exercising old and new
builds side by side. Claims below distinguish *fixed live defect* from *hardening*.

`EventsPublisherInternal`:

- **`DynamicInvoke` → direct cast invoke.** Removes a reflection dispatch and an
  `object[]` allocation per callback per publish, and removes the
  `TargetInvocationException` wrapping. This is the substantive win.
- **Exception handler rewritten** to log the exception directly rather than
  `e.InnerException.Message/StackTrace/Source`.
  *Correction to an earlier draft of this ADR:* the old `InnerException` dereference
  was **not** a live defect. `DynamicInvoke` wraps a throwing handler in
  `TargetInvocationException`, so `InnerException` is non-null on the ordinary path;
  a side-by-side probe confirmed the old code neither threw from the `catch` nor
  left stale queue entries. The null-`InnerException` path required `DynamicInvoke`
  itself to fail before invoking, which the typed API makes unreachable — every
  queued callback is an `Action<string, object, object>` invoked with matching
  arity. The rewrite is correctness-by-construction once the wrapping is gone, plus
  a fuller log line; it is not a bug fix.
- **Nested publishes enqueue and return** rather than starting a second drain of the
  shared queue, with `try/finally` clearing the flag and the queue. Nested ordering
  was verified **identical to the previous behavior** (`A1,A2,B1,B2` and
  `A1,A2,B1,B2,C1` for a two-level nest), so this is not a behavioral change — it
  makes the intent stated in the existing comment explicit rather than incidental,
  and guarantees a callback that escapes cannot strand entries for a later publish.
- **Null/empty event names are rejected** instead of reaching
  `Dictionary.ContainsKey(null)`, which throws `ArgumentNullException` (confirmed
  against the old build). Reachable via `FireEventAfterSceneLoads.OnDestroy`, which
  unsubscribes `_resetOnEvent` unconditionally even when it is `null`. Note this is
  latent for scene-authored components — Unity deserializes a null string field as
  `""`, which the old code already tolerated — but live for components added at
  runtime via `AddComponent`, where the `= null` initializer stands.

`EventsPublisher`:

- **Publishing an unregistered event name reports an error** in editor and
  development builds, via `EventsPublisher.StrictMode`. Previously it skipped all
  targeted callbacks while still notifying `allSubscribers` — so the logger printed
  the event and the system looked healthy while no handler had run.
  The check is deliberately at the **stack** level, not inside
  `EventsPublisherInternal`: `RegisterEvent` registers only with the top frame while
  `PublishEvent` visits every frame, so a per-frame check would report a false
  positive on every lower frame once anything is pushed.

`EventsPublisherEnums<T>` / `EventsPublisherEnumsSingleton<T>`:

- Reports an error when two enum types project to the same `"TypeName/"` prefix.
  Idempotent across Unity domain reloads: the same type re-claiming its own prefix
  is not a collision.
- Adds allocation-free `TryGetEnum(string, out T)` and `GetEventName(T)` so
  consumers can stop slicing strings and calling `Enum.Parse` per publish.
  `GameFlowAutoEventFlow`, `UGSAutoEventFlow` and `TempleRunAutoEventFlow` are the
  intended first callers.

No public API was removed or changed; all additions are additive, so 2.3.1
consumers continue to compile.

### Not addressed

Stages 1–3 and Decision 2 are design only. No delivery policy is implemented.

### Unrelated defects noticed

Not fixed here; they are not identity or timing problems.

- `EventsPublisherEnumsSingleton.Awake` destroys the *existing* `Instance` rather
  than the duplicate (`Destroy(Instance)` where `Destroy(this)` is meant).
  `GameState.Awake` and `UGS_State.Awake` have the same inversion via
  `DestroyImmediate(Instance)`.
- `EventsPublisherInternal.GetSubscribers` uses `.Skip(1)` on the invocation list,
  assuming `NullCallback` is always first.

---

## Open questions

1. **Delivery policy declaration** — attribute on the enum member, or an explicit
   `RegisterEvent(name, policy)` overload? The attribute keeps the policy next to
   the event; the overload allows runtime decisions and works for the
   Inspector-authored string call sites.
2. **Sticky replay timing** — immediate (consistent with today's `Awake` poll) or
   end-of-frame (safer, but changes ordering)?
3. **Edge→level migration** — model levels directly (Option 1) or ship invalidation
   pairs (Option 2) to preserve the existing enums?
4. **Inspector call sites** — these are what block Stage 1. A typed drop-down
   (a `[EventName]` property drawer populated from `GetRegisteredEvents`, or
   `ScriptableObject` channels) would fix them, but the existing baked strings in
   `.unity`/`.prefab` need a migration path.
5. **Static facade rollout** — does `RunnerUGSTemplate` run with domain reload
   disabled? That determines how much static-state reset plumbing `EventsFor<T>`
   needs.
