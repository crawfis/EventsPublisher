# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.5.1] - 2026-08-29

### Fixed

- This package's own test fixtures registered as event domains in any project that listed it
  in `testables`. Nine enums in `CrawfisSoftware.EventsPublisher.Tests` are marked
  `[EventEnum]` — the sweep tests need them to be — and the `BeforeSceneLoad` sweep walked
  every assembly in the AppDomain without asking where a family came from. On entering play
  mode they claimed prefixes in the project's own event namespace, and they surfaced in all
  three places `[EventEnum]` is discovered: **List Domains**, the Inspector event-name
  dropdowns, and the **Upgrade Audit** window. A consumer with three real domains saw twelve,
  and could point a serialized event field at `SweptTestEvents/One` — a name nothing
  publishes, failing silently at runtime with no error to follow.

  The sweep and both editor discovery sites now skip test assemblies, identified by a
  reference to `nunit.framework`. The rule covers a *consuming* project's test assemblies
  too, so a fixture marked `[EventEnum]` in your own EditMode tests no longer leaks into your
  domain listing. Nothing changes for a project that does not compile test assemblies, and
  builds were never affected — test assemblies are editor-only.

  Removing the package from `testables` was the only workaround, at the cost of not being
  able to run its tests; that is no longer necessary.

### Added

- `EventsRegistry.RegisterAnnotatedEventEnums(IEnumerable<Assembly>)` — the sweep over an
  explicit set of assemblies. The parameterless entry point is now the filtering boundary and
  delegates to it, which is what keeps the exclusion testable: the tests that assert a marked
  family registers hand the sweep their own assembly, precisely the one the entry point drops.
  Internal, like the sweep it splits.
- `EventsRegistry.IsTestAssembly(Assembly)` — the single definition of that rule, shared by
  the runtime sweep and the two editor tools rather than each testing for it their own way.
  Internal.
- 3 EditMode tests pinning the exclusion end to end — the sweep skips a marked fixture, the
  domain listing reports no fixture, and the predicate tells the suite from the package —
  bringing the suite to 125.

## [2.5.0] - 2026-08-27

Additive: nothing was removed or changed, so a 2.4.x consumer that upgrades and changes nothing
keeps working.

### Added

- `EventsRegistry.RegisteredEnumTypes` — every enum family the registry currently holds, as a
  read-only collection. The registry could already report an event's policy, its payload type, and
  the prefix a given enum projects onto, but not *which* enums had registered, so anything wanting
  to describe a project's event domains had to run a reflection sweep of its own and hope it agreed
  with the runtime. Every read hands back the same collection rather than building a snapshot. Both
  sides of a prefix collision are listed: both families registered, and seeing them share a prefix
  is the point.
- **CrawfisSoftware > Events > List Domains** — one console line per registered domain, giving the
  prefix, the enum's full name, how many members it declares, and how many of those declare
  `[EventPayload]`, `Sticky` or `Replay`. Nothing sweeps in edit mode — the `BeforeSceneLoad` sweep
  runs before the first scene loads — so the menu runs that same sweep before reporting rather than
  walking the assemblies a second, drifting way. In play mode it additionally lists the families that
  registered lazily, which no attribute-based scan can see.
- 13 EditMode tests covering the enumeration and the domain summary, bringing the suite to 122.

## [2.4.1] - 2026-08-15

### Fixed

- 2.4.0 changed when a publish made from *inside* a handler was delivered, while
  documenting the change as no change. The nested publish was queued behind everything
  already in flight instead of completing before the publishing statement returned, so a
  handler that published event A and then acted on its results — or published A and then
  C, with C's subscribers consuming what A's chain produced — ran against state that did
  not exist yet. The deferral was also per-frame, so the same publish was deferred on the
  frame whose drain was active and delivered inline on every other frame of the stack.
  The 2.3.x contract is restored and now pinned by tests: `PublishEvent` returns only
  after its event has been delivered, at any nesting depth. What the 2.4.0 rework
  actually set out to do is kept — no reflection dispatch, and a callback that escapes
  cannot strand queue entries for a later publish — and sticky replay triggered by a
  `Subscribe` made mid-drain is untouched: it stays deferred, as decided when replay
  became immediate. If you shipped on 2.4.0, see the erratum in
  `Documentation~/UPGRADING.md` for how the reorder shows up.
- The drain's error log could itself throw — it formats the handler's target and the
  exception, either of which can have a throwing `ToString()` — which aborted the drain
  and silently discarded every callback still queued. The logging is now contained, and
  the queued callbacks are delivered.

### Added

- `NestedPublishOrderingTests` — five EditMode tests pinning the re-entrant publish
  contract with control-flow assertions rather than final-sequence ones, bringing the
  suite to 109. Four of the five fail on 2.4.0, which is how the regression was
  demonstrated; all five pass on 2.3.1 and on this release.

## [2.4.0] - 2026-08-14

Event identity and delivery timing. Everything here is additive: no public API was removed
or changed, so a 2.3.1 consumer that upgrades and changes nothing keeps working, with
better diagnostics. Every step of adoption is opt-in and independently shippable — see
`Documentation~/UPGRADING.md`, and `Documentation~/adr/0001-event-identity-and-delivery.md`
for the reasoning, including the places the design's own earlier claims turned out to be
wrong.

### Added

- `EventsFor<T>` — a static, lazily-initialized entry point, one closed type per enum
  family. Needs no GameObject and no `[DefaultExecutionOrder]`, so the `Awake` race that
  `EventsPublisherEnumsSingleton<T>` subclasses worked around cannot occur. The singleton
  still works and now forwards to the same facade, so the two paths cannot disagree.
- Delivery policy: `EventDelivery.Transient` / `Sticky` / `Replay`, declared per enum
  member with `[EventDelivery(...)]` and swept at `BeforeSceneLoad`. A subscriber to a
  `Sticky` event is delivered the retained `(sender, data)` immediately on subscribe, which
  removes the need to mirror events into static `bool`s and poll them at `Awake`.
  `RegisterEvent(name, policy)` is the escape hatch for names no enum can annotate.
- `TryGetLast` — reads a retained value without subscribing, for a one-shot read, a
  non-`MonoBehaviour`, or an editor tool.
- `[EventEnum]` — registers a family before any scene loads, which matters when an event
  name reaches the publisher as a raw string without the facade being touched.
  `[EventEnum(Prefix = "...")]` resolves two enums with the same simple name projecting
  onto the same event names, without renaming the type.
- `EventRef` and `[EventName]` — an Inspector dropdown of real events instead of a
  free-text string. Prefer `[EventName]` on an existing field: it leaves the field a
  `string`, so names already baked into a `.unity` or `.prefab` survive. A value matching
  no known event shows as `Missing/<value>` rather than being silently cleared.
- `EventId` — an interned handle that is the publisher's internal identity. Handler
  signatures are unchanged, so this needs nothing from consumers.
- `EventId<TData>` with `[EventPayload(typeof(X))]` — a typed identity resolved once via
  `EventsFor<T>.Id<X>(member)`. Publishing the wrong payload and writing a handler with the
  wrong parameter type both become compile errors. Erasure holds in both directions: typed
  publishes reach untyped subscribers, untyped publishes reach typed handlers, and
  `SubscribeToAllEvents` still receives `object`.
- `EventsPublisher.StrictMode` — reports publishing an unregistered name, and a publish
  whose payload does not match the event's declaration, naming the sender. On by default in
  the editor and development builds.
- `EventsDiagnostics` — records every event subscribed to after it had already been
  published, so the edge-or-level decision is read off a run rather than guessed.
- **Window > Events > Upgrade Audit** — reflects over the consuming project and reads its
  assets, reporting which upgrade steps it has and has not taken.
- `TryGetEnum(string, out T)` and `GetEventName(T)` — allocation-free, so "all events"
  handlers can stop slicing the name on `/` and calling `Enum.Parse` per published event.
- 104 EditMode tests in `Tests/Editor`. Consumers must add the package to `testables` in
  `Packages/manifest.json` for Unity to build them.

### Changed

- Callback dispatch uses a direct cast invoke instead of `Delegate.DynamicInvoke`,
  removing a reflection dispatch and an `object[]` allocation per callback per publish.
- A nested publish enqueues and returns rather than starting a second drain of the shared
  queue. Ordering is unchanged; the intent is now explicit rather than incidental.
  *Correction: the second sentence was wrong. This deferred a nested publish's delivery
  until after the publishing statement returned — and only on the frame whose drain was
  active. Reversed in 2.4.1.*
- Retention is top-frame only. Every publisher frame is still dispatched to, but only the
  frame that is top at publish time retains the value, so `Push`/`Pop` is a real scope.

### Fixed

- The prefix collision detector shipped in 2.3.x could never fire: its registry was a
  static field inside a generic type, so each constructed type saw only its own prefix —
  precisely the cross-type comparison the check exists to make. It now lives on the
  non-generic `EventsRegistry`.
- Null and empty event names are rejected instead of reaching `Dictionary.ContainsKey(null)`,
  which throws `ArgumentNullException`.
- `EventsPublisherEnumsSingleton.Awake` destroyed the existing `Instance` rather than the
  duplicate that had just awoken.
- `EventsUpgradeAudit` referenced `UnityEngine.DefaultExecutionOrderAttribute`; Unity's
  class is named `DefaultExecutionOrder` with no suffix, so the editor assembly failed to
  compile and took every other assembly in the project with it.
- `package.json` declared a sample at `Samples~/Basic Usage` that does not exist in the
  repository. Harmless for an immutable git package, but an embedded copy is scanned
  eagerly and throws `DirectoryNotFoundException` on every scan.

## [2.3.1] - 2026-02-24

- `SubscribeToAll` for enums goes through the enum publisher, so the underlying event name
  matches what the rest of the facade publishes.

## [2.3.0] - 2026-02-24

- Enum events publish as `"EnumTypeName/MemberName"` rather than the bare member name,
  avoiding most collisions between two enums sharing a member name.

## [2.2.0] - 2025-11-19

- Menu items to clear events now and on exiting play mode; subscribe to all registered
  events for an enum; list current subscribers.

## [2.1.0] - 2025-08-17

- Initial package layout: assembly definitions, editor tooling, event subscriber logging.

[2.5.0]: https://github.com/crawfis/EventsPublisher/releases/tag/v2.5.0
[2.4.1]: https://github.com/crawfis/EventsPublisher/releases/tag/v2.4.1
[2.4.0]: https://github.com/crawfis/EventsPublisher/releases/tag/v2.4.0
[2.3.1]: https://github.com/crawfis/EventsPublisher/releases/tag/v2.3.1
