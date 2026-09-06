# Fast Enter Play Mode prompt

Drop this into an AI coding assistant working in a project that consumes EventsPublisher
2.6.1 or later and is moving off `EventsPublisherEnumsSingleton<T>` — or off any
`MonoBehaviour` singleton — while Unity 6.6 makes entering Play mode without a domain reload
the default. It complements `upgrade-prompt.md`: that one migrates the event calls; this one
makes the project's **own** static state correct when a play session starts with the
previous session's statics still in memory.

Two facts to know before pasting it. First, existing projects created before 6.6 still carry
the old setting and fully reload — measured in 6000.4, 6000.5 and 6000.6 — so nothing below
shows up until the setting is flipped or a new project is created. Second, a recompile still
reloads the domain, so the bugs only appear on the *second* play without a code change, which
is exactly why they survive review.

What the package already does, so the assistant does not re-implement it: at the end of every
play session it drops subscriptions, retained Sticky and Replay values, and publisher frames
pushed above the root; at every play entry it resets its diagnostics; it keeps registrations,
delivery policies, payload types and prefix claims, so a declaration made from a static
initializer stays declared. See *Entering Play mode without domain reload* in the README.

---

```
Make this project correct when Play mode is entered without a domain reload.

Context. Unity 6.6 creates new projects with Edit > Project Settings > Editor > Enter Play
Mode Settings set to "Reload Scene only", and recommends that setting for existing projects.
Under it, every static field, static event and static singleton in this project keeps the
value it had when the previous play session ended. Static constructors and static field
initializers run once per domain and do not run again. [RuntimeInitializeOnLoadMethod] and
the scene lifecycle (Awake, OnEnable, OnDestroy) still run every session. A recompile still
reloads the domain, so this only shows up on the SECOND play without a code change.

EventsPublisher 2.6.1+ handles its own half: at the end of each play session it drops
subscriptions, retained Sticky/Replay values and publisher frames pushed above the root, and
at each play entry it resets its diagnostics; it keeps registrations, delivery policies,
payload types and prefix claims. Do not add code that duplicates that. This prompt is about
the project's OWN statics.

## The rule to apply to every static

Classify each static as one of two things, and treat them oppositely:

 - A DECLARATION derives from code: an event name, an EventId<T> resolved once, a policy
   declared with RegisterEvent(name, policy), a lookup table built from an enum, a config
   constant. It is correct to keep across sessions, and WRONG to reset from a hook, because
   the static initializer that made it will not run again. Leave these alone. A `static
   readonly` field whose initializer only reads code is a declaration.
 - RUNTIME STATE describes one play session: a reference to a scene object or component
   (`Instance`), a bool mirror (`IsInitialized`, `HasSignedIn`, `IsGameOver`), a score, a
   cache of loaded objects, a static event's subscriber list, a "started" flag in a
   bootstrapper. It must be reset every session or it lies on the second play.

## How to find the work

Do not guess. Enumerate, then classify, then show me the list before changing anything.

 1. Search every non-test assembly for `static` fields and properties that are not `const`
    and not `static readonly` with a pure initializer; for `static event`; for
    MonoBehaviour and ScriptableObject types with a static `Instance`; for
    `DontDestroyOnLoad`; for `[RuntimeInitializeOnLoadMethod]`; and for static constructors
    that subscribe to anything or register anything.
 2. For each hit, say which class it is (declaration / runtime state) and what goes wrong on
    the second play if it is runtime state: a stale reference to a destroyed object, a bool
    already true at boot so the boot step is skipped, a subscriber list that grows by one
    per session so handlers fire twice, a value from the last run presented as current.
 3. Group the runtime state by owner and propose one reset per owner, not a scattering.

## How to fix each kind

 - MonoBehaviour singleton `Instance`: set it in Awake, and clear it in OnDestroy with
   `if (Instance == this) Instance = null;`. Never test it with `is null` or
   ReferenceEquals — a destroyed Unity object is not C#-null; only Unity's overloaded `==`
   knows. Better: stop being a singleton. If the type only publishes or subscribes to
   events, replace it with EventsFor<T> per upgrade-prompt.md step 4. If it holds real
   state, make that state a Sticky level event that late subscribers receive on subscribe,
   so nothing needs a static to poll.
 - Static bool mirrors of events: replace with a Sticky level event (upgrade-prompt.md
   step 6). The package retains the value within a session and drops it at the end of the
   session, which is exactly the lifetime the bool was trying to have. If a mirror must
   stay, reset it in a [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] on the class
   that owns it.
 - Static events and static subscriber lists in the project's own code: instances subscribe
   in OnEnable and unsubscribe in OnDisable, never only in Awake. A static system that
   subscribes from a static constructor subscribes once and then holds a stale delegate;
   move the subscribe into a [RuntimeInitializeOnLoadMethod] and make it idempotent
   (`X -= Handler; X += Handler;`), or on Unity 6.5+ pair [OnEnteringPlayMode] with
   [OnExitingPlayMode]. On Unity 6.6, [AutoStaticsCleanup]
   (Unity.Scripting.LifecycleManagement) re-evaluates a field's initializer on each play
   transition and calls Clear() on a `static readonly` collection; use it on runtime state,
   and [NoAutoStaticsCleanup] to mark a declaration explicitly where the field would
   otherwise look like state.
 - Bootstrappers with a `static bool _started` guard: the guard is what makes the second
   play skip startup. Reset it at SubsystemRegistration, or key it off something that is
   per-session, such as the scene handle.
 - Publisher frames: keep Push on scene load paired with Pop on unload. The package unwinds
   leaked frames at the end of a session, but a leak within a session still hides events
   from the frame underneath.
 - Static references to things loaded during play (Addressables handles, instantiated
   prefabs, RenderTextures): release them at the end of the session or reload them at the
   start; a handle from the last session points at something already released.

Where you reset with [RuntimeInitializeOnLoadMethod(SubsystemRegistration)]: ordering among
entry points in the same phase is undefined, so reset only what THIS class owns. Never clear
a shared registry, and never drop something another hook in the same phase may have just
created. A reset that belongs at the END of a session is editor-only by nature — the
player never has a second session — so it goes in an editor assembly, on
EditorApplication.playModeStateChanged at EnteredEditMode, after teardown has finished.

## How to verify

Set Enter Play Mode Settings to "Reload Scene only", then, without recompiling in between:

 1. Enter play mode, run the boot sequence, exit.
 2. Enter play mode again and run the same sequence.

The second run must be indistinguishable from the first. Look specifically for: a static
counter you add in a [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] logging 2 — this proves
statics persisted, so the test is testing the right thing; any StrictMode "is not
registered" error that did not appear on the first run; any MissingReferenceException from a
handler; any log line appearing twice that appeared once before; any system that reports
itself ready before its initialization ran; Window > Events > Upgrade Audit showing the same
late-delivery list as after the first run. Tell me what you ran and what you saw, run by
run. Do not report the project as safe without the second run.

## What not to do

 - Do not move a declaration out of a static initializer "to be safe". Declarations belong
   there; the package keeps them.
 - Do not add a global "reset everything" hook. Resets belong to owners.
 - Do not switch the project setting back to "Reload Domain and Scene" as the fix. It hides
   the problem, and Unity's CoreCLR runtime will not have a domain reload at all.
 - Do not turn off StrictMode to quiet the second run.
 - Do not report a static as fixed without saying which of the two classes it is and what
   the second-run check showed.
```
