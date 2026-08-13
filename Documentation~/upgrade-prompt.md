# Upgrade prompt

Drop this into an AI coding assistant working in a project that consumes
EventsPublisher. It is deliberately project-agnostic: it tells the assistant how to
*discover* the project's call sites rather than naming them, so the same text works in
every consuming project.

Paste the **Copy report** output from **Window > Events > Upgrade Audit** underneath it
when you have one. Without a report the assistant has to rediscover by hand what the
audit already knows.

---

```
Upgrade this project to EventsPublisher 2.4.

Read Documentation~/UPGRADING.md in the package first — it is the operational guide, and
Documentation~/adr/0001-event-identity-and-delivery.md has the reasoning behind each
step, including the places the design's own earlier claims turned out to be wrong.

Nothing in this upgrade is required. The package is source-compatible, so a project that
changes nothing keeps working. Every step is opt-in and independently shippable. Do not
treat a step as mandatory because it is listed.

## How to find the work

Do not guess at this project's structure. Run Window > Events > Upgrade Audit and work
its findings. If an audit report is pasted below, start from it. If not, ask me to run
the audit and paste the report before doing anything in steps 3 or 6 — those two depend
on evidence the audit gathers and are guesswork without it.

Findings are graded, and the grades mean something:
 - Action     — discovered from evidence; safe to just do.
 - Suggestion — a guess or a judgement the tool cannot make. Bring it to me, do not
                silently act on it.
 - Ok         — already done; skip it.

## Order of work

Steps 1-5 are mechanical. Do them without asking, one commit each.

 1. Add the package to "testables" in Packages/manifest.json. Then ask me to run the
    EditMode tests and confirm they pass before you continue — they were verified on a
    stub harness, not in a real editor.
 2. Add [EventEnum] to every event enum the audit lists as unmarked. If a prefix
    collision is reported, fix it with [EventEnum(Prefix = "...")] on ONE of the
    colliding enums. Never rename an enum type and never switch to Type.FullName.
 3. Add [EventName] to the serialized string fields the audit lists. Add the ATTRIBUTE
    only — do not change any field's type to EventRef. Changing the type changes the
    serialized shape, so Unity cannot carry the value across and every event name already
    baked into a .unity or .prefab is lost. EventRef is for new fields.
      Audit entries annotated "(holds an event name in an asset)" are certain. The rest
    are name-based guesses — list them for me rather than assuming.
 4. Replace EventsPublisherEnumsSingleton<T> subclasses with the static EventsFor<T>,
    then delete the subclasses and their [DefaultExecutionOrder] attributes. That
    attribute only orders Awake within one scene load batch, so it never protected
    additively-loaded scenes anyway. The old singleton forwards to the same facade, so
    this can be done file by file.
 5. Replace any per-publish Enum.Parse in "all events" handlers with
    EventsFor<T>.TryGetEnum(name, out var value).

Steps 6 and 7 need judgement. Propose, then wait for me.

 6. Delivery policy. Ask me to enter play mode, run the boot sequence, and refresh the
    audit. The report then lists every event whose subscribers arrived after it was
    published — these are the Sticky candidates, measured rather than guessed.
      For each, decide edge or level and TELL ME YOUR REASONING before changing
    anything. An edge is a transition, only meaningful in sequence — leave it Transient,
    because replaying an edge is actively wrong. A level is a state, self-describing on
    its own — mark it [EventDelivery(EventDelivery.Sticky)]. An event that already
    carries a payload is usually a level.
      Where a level is currently two opposing edges (Ready/NotReady, SignedIn/SignedOut,
    Paused/Resumed), sticky alone is unsafe — a late subscriber gets whichever half fired
    last. Propose a single level event carrying the value instead, and keep the edges as
    Transient for animation, SFX and analytics. Prefer an enum over bool wherever "not
    yet" is a meaningful third state.
      Only after a level event is in place and subscribed to, delete the corresponding
    static bool mirror and its Awake-time poll.
 7. Payload types. Add [EventPayload(typeof(X))] to events that carry data, and convert
    their call sites to a static readonly EventId<X> resolved once via
    EventsFor<T>.Id<X>(member). Start with events that ALREADY carry a payload — they
    have a real type to declare and the most casts to delete. Leave events with no
    payload, or a genuinely variable one, unannotated; no declaration means no checking,
    which is the intended default.
      Do not try to type the Inspector-authored components from step 3. They hold their
    event name as data, so there is no type argument to write. StrictMode reports their
    payload mismatches at the publisher instead.

## Rules that hold throughout

 - NEVER rename an event that has already been published. Names are serialized into
   scenes and prefabs; renaming silently breaks every baked reference. Everything here
   is additive.
 - Keep EventsPublisher.StrictMode on for the whole migration. It reports both an
   unregistered name and a mismatched payload, which is how mistakes in steps 2, 3 and 7
   surface.
 - Erasure holds in both directions, so nothing needs to move in lockstep: typed
   publishes reach untyped subscribers, untyped publishes reach typed handlers, and
   SubscribeToAllEvents still receives object. Loggers, event history and editor menus
   need no change at any point.
 - You do not need to do anything for EventId. The publisher keys on an interned handle
   internally and handler signatures did not change.
 - After each step, ask me to enter play mode and confirm the boot sequence still
   completes, watching the console for StrictMode errors. Do not batch several steps and
   verify once.

## What not to do

 - Do not auto-rewrite scenes or prefabs. Every step here is a code or attribute change.
 - Do not convert string fields to EventRef (see step 3).
 - Do not mark an event Sticky because its name sounds like a state. Use the runtime
   evidence, and when the evidence is absent say so rather than guessing.
 - Do not add [EventPayload] to an event whose payload actually varies by publisher.
   Declaring typeof(object) is the honest way to say "anything goes".
 - Do not report a step as done without saying what you verified.
```
