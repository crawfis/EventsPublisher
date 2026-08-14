# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[2.4.0]: https://github.com/crawfis/EventsPublisher/releases/tag/v2.4.0
[2.3.1]: https://github.com/crawfis/EventsPublisher/releases/tag/v2.3.1
