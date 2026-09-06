# Singletons and static state under fast Play mode

Drop this into an AI coding assistant working in **any** Unity project that still has
singletons — a `MonoBehaviour` with a static `Instance`, a static blackboard class, a
`GameState` full of static bools, static events — and that will run, or already runs, with
Unity 6.6's default of entering Play mode without a domain reload. It is not about any
particular package. It says what breaks, how to find every static that will break, what to
replace each shape with, and how to prove the result with two plays.

The facts behind it, measured in Unity 6000.4 through 6000.6: with domain reload off, static
fields, static events and static singletons keep the values they had when the previous play
session ended; static constructors and static field initializers run once per domain and do
not run again; `[RuntimeInitializeOnLoadMethod]` and the scene lifecycle still run every
session; a destroyed Unity object still has a live C# reference, so it passes `is null` and
`?.` and answers `ToString()` with `"null"`; and a recompile still reloads the domain, so all
of this appears only on the *second* play without a code change. Projects created before 6.6
carry the old setting and still reload fully until it is flipped.

---

```
Make this project's singletons and static state correct when Play mode is entered without
a domain reload, and move away from singletons where a better shape exists.

Context. Unity 6.6 creates new projects with Edit > Project Settings > Editor > Enter Play
Mode Settings set to "Reload Scene only", and recommends that setting for existing projects;
the CoreCLR runtime it is moving to has no domain reload at all. Under it, every static
field, static event and static singleton keeps the value it had when the previous play
session ended. Static constructors and static field initializers run once per domain and do
not run again. [RuntimeInitializeOnLoadMethod] and the scene lifecycle (Awake, OnEnable,
OnDestroy) still run every session. A destroyed Unity object still has a live C# reference:
it passes `is null`, `?.` and ReferenceEquals, and its ToString() returns "null". A
recompile still reloads the domain, so all of this shows up only on the SECOND play without
a code change, which is why it survives review.

## The rule

Classify every static as one of two things, and treat them oppositely.

 - A DECLARATION derives from code and is the same in every session: an enum-built lookup
   table, an interned id, a config constant, a registration made from a static initializer.
   Keep it. Never reset it from a hook — the initializer that made it will not run again,
   so a reset turns it into a silent absence on the second play.
 - RUNTIME STATE describes one session: a reference to a scene object or component
   (`Instance`, `CurrentAvatar`, `CurrentCamera`), a bool mirror (`IsGameStarted`,
   `IsInitialized`, `HasSignedIn`), a score or counter, a cache of loaded objects, a
   coroutine host, a static event's subscriber list, a bootstrapper's "started" flag.
   Reset it every session, by the class that owns it, or it lies on the second play.

## How to find the work

Enumerate first, classify second, and show me the list before changing anything.

 1. Search every non-test assembly for: `static` fields and auto-properties that are not
    `const` and not `static readonly` with a pure initializer; `static event`; any
    MonoBehaviour or ScriptableObject with a static `Instance`; `DontDestroyOnLoad`;
    `[RuntimeInitializeOnLoadMethod]`; `[InitializeOnEnterPlayMode]`; static constructors
    that subscribe to or register anything; Resources.Load or Addressables results stored
    in statics.
 2. For each hit, say which class it is and, for runtime state, what the second play does
    with it: a handler on a destroyed object throws MissingReferenceException; a bool
    already true skips a boot step; a subscriber list grows by one per session so handlers
    fire twice; last session's camera or avatar is used as if it were current.
 3. Group runtime state by owner. One reset per owner — not a scattering, and not a global
    "reset everything" hook.

## The singleton shapes, and what to do with each

 A. A MonoBehaviour singleton with a static Instance. Minimum: assign in Awake, and in
    OnDestroy write `if (Instance == this) Instance = null;` using Unity's `==`, never
    `is null`. Pick one duplicate policy and keep it everywhere — destroy the newcomer (the
    usual choice) or replace the survivor — rather than mixing both across singletons. An
    editor-only [InitializeOnEnterPlayMode] that nulls Instance is a fine belt-and-braces,
    but OnDestroy is the one that also covers a scene unload mid-session. Non-static
    instance fields need nothing: the object dies with its scene and a fresh one has fresh
    fields. Do not put per-session state in statics on a MonoBehaviour just because the
    class is a singleton; keep it on the instance.
 B. A static class blackboard: static properties holding the current avatar, path, camera,
    prescription, coroutine host. All of it is runtime state, and nothing resets it. Give
    the class one Reset() that returns every member to its default, and call it from a
    [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] on that class. On Unity 6.6 the
    same thing is [AutoStaticsCleanup] (Unity.Scripting.LifecycleManagement) on the class,
    with [NoAutoStaticsCleanup] on any member that is a declaration. Then make the readers
    tolerate "not set yet" — a blackboard read before its writer has run is the same bug as
    a stale one.
 C. A GameState of static bool mirrors (IsGameStarted, IsGameOver, IsPaused...). Resetting
    them in the owner's Awake works only while the owner lives in the first scene and awakes
    before every reader; an additively-loaded scene breaks that. Reset them at
    SubsystemRegistration instead, and prefer replacing the bools with one state enum that
    is published as a retained event, so readers subscribe rather than poll.
 D. Static events and static subscriber lists. Instances subscribe in OnEnable and
    unsubscribe in OnDisable, never only in Awake. A static system that subscribes from a
    static constructor subscribes once and then holds a stale delegate; move the subscribe
    into a [RuntimeInitializeOnLoadMethod], make it idempotent (`X -= H; X += H;`), or on
    Unity 6.5+ pair [OnEnteringPlayMode] with [OnExitingPlayMode]. [AutoStaticsCleanup] on a
    static event clears its subscriber list on each transition.
 E. A lazily created singleton (`Instance ??= new GameObject(...).AddComponent<T>()`, often
    with DontDestroyOnLoad). The static keeps last session's destroyed object, so `??=`
    sees non-null and hands back a corpse. Check with Unity's `==`
    (`if (Instance == null) create`), and null it in OnDestroy as in A.
 F. A ScriptableObject singleton loaded from Resources or Addressables and used to hold
    runtime values. An asset's fields modified during Play mode keep the modified values for
    the rest of the editor session, domain reload or not. Runtime values do not belong on an
    asset: move them to an object created at play start, or reset the asset from a
    bootstrapper's OnEnable and OnDisable.
 G. A bootstrapper with a `static bool _started` guard. The guard is what makes the second
    play skip startup. Reset it at SubsystemRegistration, or key it off something that is
    per-session, such as a scene handle.
 H. Static handles to things loaded during play — Addressables handles, instantiated
    prefabs, RenderTextures. Release them at the end of the session or reload them at the
    start; a handle from the last session points at something already released.

## Moving away from singletons

Where a singleton exists only so that "anyone can reach it", replace it in this order of
preference, and tell me which you chose and why:

 1. Pass it. If two or three components use it, give them a serialized reference or a
    constructor argument. No static at all.
 2. Scope it to a session object. A bootstrapper creates one context object at play start —
    the first scene's Awake, or a [RuntimeInitializeOnLoadMethod] — that owns every piece of
    runtime state the singletons held, and disposes it at the end. The former singletons
    become fields on it. Statics reduce to one: the context, nulled on dispose.
 3. Publish the state as a retained event. Readers that only need "the current value"
    subscribe and receive the latest value on subscribe; nothing polls a static. The value
    lives on the bus for the session and is dropped with it.
 4. Keep the singleton, made lifecycle-correct per A through H. Acceptable for a
    cross-cutting service — time, audio — with no per-session state beyond Instance.

Where you reset with [RuntimeInitializeOnLoadMethod(SubsystemRegistration)]: ordering among
entry points in that phase is undefined, so reset only what THIS class owns — never a shared
registry, and never something another hook in the same phase may have just created. A reset
that belongs at the END of a session is editor-only by nature, since a player never has a
second session; it goes in an editor assembly, on EditorApplication.playModeStateChanged at
EnteredEditMode, after teardown has finished.

If the project uses EventsPublisher 2.6.1 or later, the bus already drops its own
subscriptions, retained values and pushed frames at the end of each session and keeps its
declarations. Do not add resets for it.

## How to verify

Set Enter Play Mode Settings to "Reload Scene only", then, without recompiling in between:

 1. Enter play mode, run the boot sequence and one full loop of play, exit.
 2. Enter play mode again and run the same sequence.

The second run must be indistinguishable from the first. Look specifically for: a static
counter you add in a [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] logging 2 — this
proves statics persisted, so the test is testing the right thing; any
MissingReferenceException; any log line appearing twice that appeared once before; any
system reporting itself ready, started or configured before its initialization ran; any
value from the first run visible at the start of the second — score, avatar, selected
level. Tell me what you ran and what you saw, run by run. Do not report the project as safe
without the second run.

## What not to do

 - Do not move a declaration out of a static initializer "to be safe". Declarations belong
   there.
 - Do not add a global "reset everything" hook. Resets belong to owners.
 - Do not switch the project setting back to "Reload Domain and Scene" as the fix. It hides
   the problem, and it will not exist under CoreCLR.
 - Do not check a Unity object for null with `is null`, `?.` or ReferenceEquals.
 - Do not report a static as fixed without saying which of the two classes it is and what
   the second-run check showed.
```
