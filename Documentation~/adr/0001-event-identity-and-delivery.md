# ADR 0001 — Event Identity and Delivery Timing

**Status:** Decision 1 accepted, Stage 0 implemented. Decision 2 implemented
(`EventsFor<T>`, `Transient`/`Sticky`/`Replay`, immediate replay, `TryGetLast`).
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

**Stage 1 — REVISED: the write side cannot be closed, and does not need to be.**

As originally written this stage demoted the public `string` overloads to `internal`
so that authored code had to go through an enum facade. That is not implementable,
and attempting it revealed the goal was mis-stated.

A component whose event is chosen in the Inspector holds that name as **data, not as
a literal**, and must still hand a string to the publisher at runtime.
`FireEventAfterSceneLoads`, `CloseSceneOnEvent`, `FireEventWhenSceneCloses`,
`LoadSceneAfterGameControlEvent`, `TimedEvent` and the `Test_AutoFire*` scripts all do
exactly this — and they still do it *after* migrating to `EventRef`, because the
reference resolves to a string at the call. Closing the API would break every one of
them permanently, not just until they migrate. The editor's `EventMenu`, which
publishes a name picked from the registered list, is in the same position.

The hazard was never the string API. It was **a name being typed by hand with nothing
to check it**. That is closed at the authoring step instead:

- `EventsFor<T>` — compile-checked, for names known in code.
- `EventRef` / `[EventName]` — a dropdown of real events, for names authored in the
  Inspector.
- `StrictMode` — reports a publish of a name nothing registered, at runtime.
- `EventRef` overloads on `IEventsPublisher<string>` (extension methods, so no
  implementer changes) — so an authored call site never unwraps the string itself.

With those in place the remaining raw-string surface is used only for names that are
genuinely dynamic, which is what it is for. It stays public and is not marked
`[Obsolete]`: marking it would flag `EventsPublisherEnums<T>`'s own legitimate use of
it as the implementation substrate, and every dynamic call site, for no gain.

Removing implicit registration from `SubscribeToEvent` — the one piece of the original
Stage 1 still worth doing — is deferred until the Inspector call sites have moved to
`EventRef`, since it would break subscribing to a name that is not pre-registered.

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

- **Switching the enum prefix to `Type.FullName`.** REJECTED, now permanently. It
  closes the residual collision hole (two enums with the same simple name in different
  namespaces), but renames *every* event in the project to do it, breaking every name
  serialized into a `.unity` or `.prefab` asset — the exact rename hazard this ADR
  argues against, applied globally to fix a local problem.
  `[EventEnum(Prefix = "...")]` supersedes it: an explicit prefix on the one enum that
  collides, leaving every other name untouched. The Stage 0 collision *detector* is
  what surfaces the need, and now has a fix to point at rather than "rename the type".
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

### Declaring the policy — DECIDED: hybrid, registered before any scene loads

Policy is declared through `RegisterEvent(name, policy)`, with an attribute sweep as
the zero-timing default for enum families.

The imperative form alone has three races, all silent:

1. **Publish before register.** Nothing has declared `UnityServicesInitialized` as
   `Sticky` yet, so the bus has no reason to retain it. The first publish — the one
   that matters most during boot — is dropped. Precisely the case sticky exists for.
2. **Subscribe before register.** `SubscribeToEvent` already calls `RegisterEvent`
   internally, and `RegisterEvent` no-ops when the key exists. An early subscriber
   therefore registers the event as `Transient`, and the later
   `RegisterEvent(name, Sticky)` **silently does nothing**.
3. **The publisher stack.** `RegisterEvent` goes to `Peek()`; `PublishEvent` visits
   every frame. A `Push()` creates a frame that knows no policies.

The resolution is not attribute-versus-imperative — it is getting registration off
the `MonoBehaviour` lifecycle entirely.
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` runs
after assemblies load and before the first scene's `Awake`, and therefore before
every additively-loaded scene's `Awake` by definition. Races 1 and 2 disappear,
because no user code can publish or subscribe before it.

That constrains where the declaration can live: if registration must precede all user
code, the policy must be knowable without running user code. An attribute is readable
at type load and satisfies this for free; an imperative
`Register(name, policy)` also satisfies it **provided the call site is a static
initializer rather than an `Awake`**.

Therefore:

- **Enum families** — attribute on the member, swept at `BeforeSceneLoad`.
  Zero timing, zero per-publish cost.
- **`RegisterEvent(name, policy)`** — retained as the escape hatch for
  runtime-computed names and any remaining Inspector-authored strings.
- **Conflict rule** — first declaration wins; a differing later declaration is an
  error, so an implicit registration cannot silently downgrade a declared policy.
- **The policy registry is stack-global**, while the sticky *value cache* stays
  per-frame so `Pop()` discards it. These are two different structures.
- **Implicit registration is removed from `SubscribeToEvent`.** Once registration is
  guaranteed at load, subscribing to an unregistered name is a typo rather than a
  request to create one. This is the same change Stage 1 wants.

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

Where a level is currently expressed as two opposing edges — `GameplayReady` /
`GameplayNotReady`, `PlayerSignedIn` / `PlayerSignedOut` — sticky alone is unsafe: a
late subscriber after sign-out would still receive a stale "signed in".

**DECIDED: model the level directly** (option 1 below), rather than declaring
invalidation pairs (option 2), which would preserve the existing enums but keep the
two-edges-one-level coupling that caused the drift in the first place.

#### What "model the level directly" means

Make one event carry the state value, instead of two events announcing transitions
into it:

```csharp
// Edge pair — needs history to interpret
GameplayReady
GameplayNotReady

// Level — self-describing
GameplayReadyChanged        // data: bool
```

The property that matters: **a level event is self-describing and idempotent.**
Received once, late, with no history, it tells the whole truth — which is exactly
what makes sticky replay safe. Replaying an *edge* is actively wrong: a late
subscriber would get `GameplayReady` even though `GameplayNotReady` fired afterwards.

| Current edge pair | Level event | Payload |
|---|---|---|
| `GameplayReady` / `GameplayNotReady` | `GameplayReadyChanged` | `bool` |
| `PlayerSignedIn` / `PlayerSignedOut` | `AuthStateChanged` | `AuthState` enum |
| `MainMenuShowing` / `MainMenuHidden` | `MainMenuVisibilityChanged` | `bool` |
| `Paused` / `Resumed` | `PauseStateChanged` | `bool` |
| `GameStarted` / `GameEnding` | `GameSessionStateChanged` | `{ NotStarted, Running, Ended }` |

Several events are **already levels**: `RemoteConfigUpdated` carries the config,
`DifficultyChanged` carries the difficulty, `GameConfigApplied` carries the config.
These work with sticky unchanged. The tell is that they already carry a payload — an
event that needs a payload to be useful is usually a level.

Prefer an enum over `bool` wherever "not yet" is meaningful, since a never-published
sticky event simply does not invoke the subscriber at all.

#### The edges are not deleted

This is additive, not a rewrite. Edges remain useful — `MainMenuHiding` is a real cue
to start a fade, and animation, SFX and analytics all want the moment rather than the
state. Both are kept, with different policies:

- **Edges → `Transient`.** Anything reacting to the transition.
- **Levels → `Sticky`.** Anything needing current state: late subscribers, UI
  rebuilding itself, guards.

Migration is therefore incremental: add the level event alongside the existing edges,
publish it wherever the edges are published, move the `bool` pollers onto it, delete
the `bool`.

### Reset scope

A stale sticky value replaying into a fresh play session is a regression relative to
today. The `IStackEventsPublisher` push/pop stack is the natural scope: a retained
value lives in the publisher frame that was top when it was published, and `Pop()`
discards it. `Clear()` drops retained state too, and `ClearEventsMenu`'s existing
"clear on exiting play mode" toggle already covers the editor loop.

#### Correction: retention is top-frame only, not per-frame

An earlier draft said a retained value "lives in the frame it was published into"
without noticing that `EventsPublisher.PublishEvent` dispatches to **every** frame in
the stack. Retaining wherever the event was dispatched would therefore have written
the value into *all* frames — so `Pop()` would discard nothing, because a copy would
still be sitting in the frames underneath. The scoping story this ADR relies on for
`Replay` would not have worked at all.

Caught by a test asserting that a value published before a `Push()` survives the
matching `Pop()`; it returned the inner frame's value instead.

Dispatch and retention are now separated: every frame is still dispatched to, so
subscribers underneath continue to hear the event, but only the frame that is top at
publish time retains it. `TryGetLast` on the stack searches top-down and returns the
first frame holding a value. `Push`/`Pop` is consequently a real scope, which is what
makes the `Replay` scoping guidance actionable.

### Replay timing — DECIDED: immediate, on subscribe

Sticky replay fires **inline during `Subscribe`**, routed through the existing
callback queue.

#### Reversal of an earlier decision

This ADR previously recorded end-of-frame replay, paired with a mandatory
`TryGetLast` pull so that `Awake`/`Start` readers were not regressed. That decision
was mine, it was presented as the "safer" option, and the reasoning was weaker than
the presentation implied. It is reversed here.

The tell is structural: **end-of-frame creates the gap that `TryGetLast` then
patches.** A choice that requires a second mechanism to compensate for it is not
carrying its weight. Weighing the two honestly:

- **End-of-frame buys:** the handler does not run mid-`Awake`.
- **End-of-frame costs:** between `Awake` and end of frame, the subscriber has
  subscribed but has not been told the current state. It is wrong about the world for
  the rest of the frame, with no recourse — except to call `TryGetLast`
  synchronously, in `Awake`.

That last point dissolves the original argument. If reading the value synchronously
during `Awake` were genuinely hazardous, `TryGetLast` in `Awake` would be equally
hazardous. It is not, so the objection to immediate replay does not hold.

#### The remaining objection is under the subscriber's control

"The handler might run before the subscriber is fully constructed" is real, but
immediate replay fires *exactly when `Subscribe` is called* — a moment the subscriber
already chooses. Subscribing at the end of `Awake`, in `OnEnable`, or in `Start` is
sufficient.

That is local, visible discipline in one file, in exchange for the invisible global
constraint (`[DefaultExecutionOrder]`, scene load order) that this work exists to
remove. Trading a global invisible constraint for a local visible one is the right
direction.

#### Accepted costs

1. **Re-entrancy into the drain.** `Subscribe` called from inside a handler — during
   an active publish drain — must not invoke a callback mid-drain. The replay is
   routed through the same `_callbackQueue`: if a drain is in progress it enqueues and
   runs in order, otherwise it invokes immediately. The `_isDraining` guard added in
   Stage 0 is the hook. **This is the part that needs care in implementation.**
2. **Structural work in a handler.** A handler that loads or unloads scenes during the
   `Awake` of a still-loading scene is dicey in Unity. Such a subscriber defers itself
   (`StartCoroutine`, or a flag acted on in `Update`) — and would face the same
   problem via `TryGetLast`.
3. **Staggered delivery.** Subscribers see a replayed event at different times
   depending on when each subscribes. Inherent to replay in any form, end-of-frame
   included.

#### `TryGetLast` is retained, but demoted

```csharp
bool TryGetLast(string eventName, out object sender, out object data);
```

It is no longer required as compensation. It remains useful for code that wants the
value **without subscribing at all** — a one-shot read, a non-`MonoBehaviour`, an
editor tool. It still returns the payload, which the `bool` mirror never could.

#### Rejected: a per-subscription override

`Subscribe(e, cb, Immediate | Deferred)` was considered and rejected. One default,
and a subscriber needing deferral does it itself in a line. Adding the knob before
anything needs it moves the decision to every call site.

### Implemented: delivery policy

Shipped. `EventDelivery` (`Transient`/`Sticky`/`Replay`) with
`[EventDelivery(...)]` on enum members, read once at facade construction; the
publisher then does a dictionary lookup per publish rather than any reflection.

- **Policy registry is stack-global**, on `EventsRegistry`; retained values are
  per-frame. `RegisterEvent(name, delivery)` on `IEventsPublisher<T>` is the escape
  hatch for names no enum can annotate.
- **First declaration wins**, a differing second one is reported. Registering a name
  *without* a policy — as `SubscribeToEvent` does implicitly — records nothing, so an
  early subscriber cannot pin an event to `Transient` and silently turn a later
  `[EventDelivery(Sticky)]` into a no-op. This resolves race 2 structurally, so the
  planned implicit/explicit upgrade mechanism was not needed and no longer appears.
- **Replay is immediate**, routed through the shared callback queue. A `Subscribe`
  made from inside a handler during an active drain enqueues and runs in order.
- **`TryGetLast`** is enum-keyed on the facades, string-keyed on the publisher.
- **Journal growth past `EventsRegistry.JournalWarningThreshold`** is reported, never
  truncated.

Removing implicit registration from `SubscribeToEvent`, listed under the policy
decision, is **deferred to Stage 1**. Doing it now would break the Inspector-authored
subscribe call sites in `RunnerUGSTemplate`, which cannot move to a typed control
until `EventRef` exists. It is not needed for policy correctness given the point
above.

### Implemented: `EventsFor<T>`

Shipped. `EventsFor<T>` is a static, lazily-initialized facade; `EventsPublisherEnums<T>`
remains the implementation and `EventsPublisherEnumsSingleton<T>` now forwards to
`EventsFor<T>`, so the two paths share one facade and cannot disagree. Existing scene
objects keep working unchanged.

Lazy static initialization is *inherently* race-free for the facade's own events: any
publish or subscribe goes through `EventsFor<T>`, which registers every member of `T`
on first touch, so registration always precedes first use. No execution-order
attribute and no scene object are involved.

The one case it does not cover is an event name reaching the publisher as a raw
string without the facade ever being touched — the Inspector-authored call sites.
`[EventEnum]` marks an enum for registration at `BeforeSceneLoad`, which closes that
gap until those call sites move to `EventRef`.

Two supporting pieces:

- **`EventsRegistry`** — non-generic, because a static field declared inside a generic
  type exists once per *constructed* type. It owns the cross-family prefix registry
  and the `SubsystemRegistration` reset.
- **Static reset** — `SubsystemRegistration` runs before `BeforeSceneLoad` and drops
  cached facades and prefix claims. A no-op when domain reload is enabled (the current
  setting), correct when it is not. It deliberately does **not** clear
  `EventsPublisher` itself; dropping live subscriptions stays the opt-in editor toggle.

#### Defect found and fixed in the Stage 0 collision detector

The detector shipped in Stage 0 **never fired**. `_claimedPrefixes` was a static field
inside the generic `EventsPublisherEnums<T>`, so `EventsPublisherEnums<A>` and
`EventsPublisherEnums<B>` each had their own dictionary containing only their own
prefix — exactly the cross-type comparison the check exists to make was the one it
could not see. Confirmed by constructing facades for two same-named enums in different
namespaces and observing no error. The registry now lives on the non-generic
`EventsRegistry` and the cross-family case is covered by a test.

### Rationale: why the enum facades are static

`EventsPublisherEnumsSingleton<T> : MonoBehaviour` requires a GameObject in a scene,
and that requirement is itself the source of the `[DefaultExecutionOrder(-10000)]`
fragility described above. Nothing in `EventsPublisherEnums<T>` needs a scene
object.

A static, lazily-initialized `EventsFor<T>` removes the `Awake` race entirely, needs
no execution-order attribute, and makes `EventsPublisherGameFlow`,
`EventsPublisherUGS`, `EventsRegistration` and `GuiEventsPublisher` unnecessary.

This was the single highest-value fix for the timing problem and is **independent of
sticky events**. See *Implemented: `EventsFor<T>`* above.

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

### Tests

`Tests/Editor` holds 31 EditMode tests across three fixtures, covering dispatch ordering
and isolation, the static facade and its registration timing, and all three delivery
policies. `Runtime/AssemblyInfo.cs` grants the test assembly access to internals so each
test can reset `EventsRegistry`'s static state — a public reset would be a footgun in
game code.

The suite was mutation-checked rather than merely observed green: retaining on every
frame instead of the top one, firing sticky replay inline instead of through the queue,
and dropping the null-name guard each fail exactly the test written for that behaviour.

Consumers must add the package to `testables` in `Packages/manifest.json` for Unity to
build them.

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

## Decisions taken

1. **Delivery policy declaration** — hybrid. `RegisterEvent(name, policy)` is the
   mechanism; an attribute sweep at `BeforeSceneLoad` is the zero-timing default for
   enum families. First declaration wins, conflicts are an error, implicit
   registration is removed from `SubscribeToEvent`. See *Declaring the policy*.
2. **Sticky replay timing** — end of frame, paired with a synchronous
   `TryGetLast` pull so `Awake`/`Start` code is not regressed.
   **Superseded** — replay is immediate, on subscribe, routed through the existing
   callback queue; `TryGetLast` is retained but demoted from required to convenience.
   See *Replay timing* for the reversal and its reasoning.
3. **Edge→level migration** — model levels directly; do not ship invalidation pairs.
   Edges are retained as `Transient` alongside the new `Sticky` levels. See
   *Edge vs. level*.
4. **Domain reload** — `RunnerUGSTemplate/ProjectSettings/EditorSettings.asset` has
   `m_EnterPlayModeOptionsEnabled: 1` with `m_EnterPlayModeOptions: 0`, i.e. the
   fast-enter feature is on but neither domain nor scene reload is actually disabled.
   Statics therefore still reset on entering play mode today, so a static registry is
   currently safe. The project is one checkbox away from statics persisting, so the
   registry resets explicitly rather than relying on that.
5. **Inspector call sites** — adopt a serializable `EventRef` (enum type name +
   member name) with a two-dropdown property drawer, rather than a concrete
   per-family subclass of each generic component. Keeps one component per behavior,
   avoids the eight-components × four-families cross product, and removes typos at
   the authoring step, which is the only step that exists for scene data. This
   unblocks Stage 1 and reopens `Type.FullName` (see *Rejected for now*).
6. **`Replay` policy** — in scope. All three policies ship: `Transient`, `Sticky`,
   `Replay`.
7. **Sticky reset granularity** — no `Invalidate(name)`. `Pop()` and `Clear()` are
   the only reset mechanisms. Retained values are expected to stay valid across
   scene boundaries, and the publisher stack provides the scoping where they should
   not. See *Sticky reset granularity* below.

## Sticky reset granularity — DECIDED: no `Invalidate`

`Pop()` and `Clear()` are the only reset mechanisms. A per-event `Invalidate(name)`
was considered and rejected. The analysis below is retained because it explains what
the policy does *not* cover, and because two of its conclusions were wrong for this
codebase in instructive ways.

### Why it was rejected

- **Retained values are expected to stay valid.** Most sticky state is cached
  deliberately and remains correct across scene boundaries. Discarding it is the
  unusual case, not the default.
- **The stack already provides the scoping.** Where a value genuinely should not
  outlive its scope, a publisher frame is the right unit, and `Pop()` discards it.
- **The motivating example was mis-modelled.** Sign-out was used below as a case
  where a level "becomes unknown". It is not: it is a value change, correctly
  expressed as `AuthStateChanged(SignedOut)`. It is also read at exactly one point in
  the flow — after the game ends and before the main menu is shown — so a stale
  `RemoteConfigUpdated` between sign-out and re-authentication is never observed.
  Re-authentication republishes it.

### Retracted: auto-drop on a destroyed sender

An earlier draft recommended that replay silently drop a retained value whose
`sender` is a `UnityEngine.Object` comparing equal to null. **This is withdrawn — it
would be actively harmful here.** A destroyed sender says nothing about whether the
payload is still valid: `RemoteConfigUpdated` published by a manager that has since
been destroyed still carries a perfectly good config. Silently dropping it would
break precisely the "cached and can continue" case that is the norm.

If a diagnostic is wanted at all, it should report rather than discard.

### Where the retention concern actually lives

The concern does not vanish; it moves, and it lands on `Replay` rather than `Sticky`:

- The events that would be `Sticky` carry configs, enums and bools — `LevelConfig`,
  `DifficultyConfig`, `bool`. These are assets or value types and survive scene
  unload cleanly. This is why the leak argument below does not apply to them.
- The events that would be `Replay` are the accumulating gameplay ones —
  `TrackSegmentCreated`, `SplineSegmentCreated`. A full journal of those holds a
  reference to **every segment ever created**, and unlike a sticky value it grows
  without bound for the whole session rather than holding a single stale entry.

`Replay` is therefore the policy that needs scoping, and the publisher stack is the
mechanism: a gameplay scene that pushes a frame on load and pops it on unload
discards its journal with the level. Implementation should not offer `Replay` without
that scoping story.

### The gap this leaves

A sticky event has three possible states, and publishing only reaches two of them:

| State | Late subscriber sees |
|---|---|
| Never published | not invoked |
| Has a retained value | the value, immediately on subscribe |
| **Had a value, now untrue** | **the stale value** |

The third state is the gap. The distinction is *value changed* versus *value became
unknown*:

- `DifficultyChanged(Hard)` → `DifficultyChanged(Easy)` is a **change**. Publishing
  handles it; no invalidation needed.
- `RemoteConfigUpdated(config)` → the player signs out and the session's config is no
  longer valid. There is no new config to publish. Publishing
  `RemoteConfigUpdated(null)` asserts *"here is the new config, it is null"*, which is
  a different and weaker claim than *"there is no current answer"* — and it forces
  every subscriber to null-check.

The contracts differ at the subscriber: a value that was never published does not
invoke the subscriber at all, whereas `Publish(null)` invokes it with null.

**Accepted consequence.** Without `Invalidate`, an event whose answer becomes
"no answer" has only two ways to express it: leave the stale value retained, or
publish an explicit unknown. The first is acceptable here because such values are
either never read while stale, or are republished before they are. The second is
preferred where a subscriber genuinely must distinguish — and it is not an
invalidation at all but a state value, per the existing guidance to prefer an enum
over `bool` wherever "not yet" is meaningful:

```csharp
AuthStateChanged   // { Unknown, SignedOut, SigningIn, SignedIn }
```

That covers the case `Invalidate` was proposed for, without a second mechanism.
