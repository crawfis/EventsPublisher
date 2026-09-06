using System;
using System.Reflection;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum SessionTestEvents
    {
        [EventDelivery(EventDelivery.Sticky)] Level,
        [EventDelivery(EventDelivery.Replay)] Journal,
        Edge,
    }

    /// <summary>
    /// What survives from one play session to the next when the domain is not reloaded, and what
    /// does not.
    /// </summary>
    /// <remarks>
    /// <para>Two hooks bracket a session. Unity calls <see cref="EventsRegistry.BeginPlaySession"/> at
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> on every play entry; the editor
    /// calls <see cref="EventsRegistry.EndPlaySession"/> once play mode has finished tearing down.
    /// With domain reload on both run against empty statics. With it off — Unity 6.6's default for
    /// new projects — the statics carry over, and these two are what separate one session from the
    /// next.</para>
    /// <para>The rule they implement: <em>runtime state</em> belongs to a session and is dropped at
    /// its end — subscriptions, retained values, pushed frames, typed wrappers. <em>Declarations</em>
    /// derive from code and are kept at both ends — registrations, delivery policies, payload types,
    /// prefix claims, the domain listing, interned ids. A declaration can only change through a
    /// recompile, which always reloads the domain, and the static initializers that made it will not
    /// run again without one. The play-entry hook resets the diagnostics and nothing else: ordering
    /// within <c>SubsystemRegistration</c> is undefined, so a drop there could discard a subscription a
    /// consumer's own entry point had just made.</para>
    /// <para>2.4.0 through 2.6.0 wiped the declarations at play entry and, from 2.6.0, the
    /// registrations at play exit.</para>
    /// </remarks>
    public class PlaySessionResetTests : EventsTestBase
    {
        private static EventsPublisher Publisher => (EventsPublisher)EventsPublisher.Instance;

        // ---- the hooks themselves ----

        [Test]
        public void BeginPlaySession_IsTheSubsystemRegistrationHook()
        {
            MethodInfo hook = typeof(EventsRegistry).GetMethod(
                nameof(EventsRegistry.BeginPlaySession), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var attribute = hook.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();

            Assert.IsNotNull(attribute, "the session start must run on every play entry");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attribute.loadType,
                "it must precede the BeforeSceneLoad sweep and every Awake");
        }

        [Test]
        public void TheFullReset_IsNotARuntimeHook()
        {
            // ResetStaticState wipes declarations too. It exists for test isolation; wired to play entry
            // it would silently revert every policy a static initializer declared, which is the bug.
            MethodInfo fullReset = typeof(EventsRegistry).GetMethod(
                nameof(EventsRegistry.ResetStaticState), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNull(fullReset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>());
        }

        // ---- play entry: diagnostics reset, everything else untouched ----

        [Test]
        public void BeginPlaySession_ResetsTheDiagnostics()
        {
            // The audit reads one boot sequence, not an editor session's accumulated noise.
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Edge, null, null);

            EventsRegistry.BeginPlaySession();
            EventsFor<SessionTestEvents>.Subscribe(SessionTestEvents.Edge, Recorder("x"));

            Assert.AreEqual(0, EventsDiagnostics.GetLateDeliveries().Count);
        }

        [Test]
        public void BeginPlaySession_KeepsSubscriptionsAndRetainedValues()
        {
            // Ordering among SubsystemRegistration entry points is undefined. A consumer's own entry
            // point may subscribe before this one runs, and that subscription must survive. Runtime
            // state is dropped at the end of the previous session instead.
            Bus.RegisterEvent("Session/Early", EventDelivery.Sticky);
            Bus.SubscribeToEvent("Session/Early", Recorder("early"));
            Bus.PublishEvent("Session/Early", null, "v");
            Log.Clear();

            EventsRegistry.BeginPlaySession();

            Assert.IsTrue(Bus.TryGetLast("Session/Early", out _, out _));
            Bus.PublishEvent("Session/Early", null, "w");
            Assert.AreEqual("early:w", string.Join(",", Log));
        }

        [Test]
        public void Registrations_Survive_SoStrictModeStaysQuiet()
        {
            Bus.RegisterEvent("Session/Registered");

            EventsRegistry.BeginPlaySession();
            Bus.PublishEvent("Session/Registered", "probe", null);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void PolicyDeclaredImperatively_Survives()
        {
            // The documented home for RegisterEvent(name, policy) is a static initializer, which runs
            // once per domain. Dropping the policy here would revert the event to Transient on the second
            // play, silently, with nothing left to re-declare it.
            Bus.RegisterEvent("Session/Sticky", EventDelivery.Sticky);

            EventsRegistry.BeginPlaySession();
            Bus.PublishEvent("Session/Sticky", null, "v");
            Bus.SubscribeToEvent("Session/Sticky", Recorder("late"));

            Assert.AreEqual("late:v", string.Join(",", Log));
        }

        [Test]
        public void PolicyDeclaredByAttribute_Survives()
        {
            EventsFor<SessionTestEvents>.EnsureRegistered();

            EventsRegistry.BeginPlaySession();
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Level, null, "v");
            EventsFor<SessionTestEvents>.Subscribe(SessionTestEvents.Level, Recorder("late"));

            Assert.AreEqual("late:v", string.Join(",", Log));
        }

        [Test]
        public void PayloadDeclaration_Survives_SoStrictModeStillChecksIt()
        {
            // Declared in a static readonly field, per the README. Forgetting it on the second play turns
            // the payload check off for that event with no message anywhere.
            EventId<string> typed = EventId<string>.Of("Session/Typed");
            typed.Register();

            EventsRegistry.BeginPlaySession();

            LogAssert.Expect(LogType.Error, new Regex("Session/Typed.*declared to carry System.String"));
            Bus.PublishEvent("Session/Typed", "probe", 12345);
        }

        [Test]
        public void PrefixClaims_Survive_SoACollisionIsStillReported()
        {
            EventsFor<CollidingA.SameNameEvents>.EnsureRegistered();

            EventsRegistry.BeginPlaySession();

            LogAssert.Expect(LogType.Error, new Regex("both project onto the event name prefix 'SameNameEvents/'"));
            EventsFor<CollidingB.SameNameEvents>.EnsureRegistered();
        }

        [Test]
        public void RegisteredEnumTypes_Survive()
        {
            // The domain listing describes the code, not the session.
            EventsFor<SessionTestEvents>.EnsureRegistered();

            EventsRegistry.BeginPlaySession();

            CollectionAssert.Contains(EventsRegistry.RegisteredEnumTypes, typeof(SessionTestEvents));
        }

        [Test]
        public void BeginPlaySession_IsIdempotent()
        {
            Bus.RegisterEvent("Session/Twice", EventDelivery.Sticky);

            EventsRegistry.BeginPlaySession();
            EventsRegistry.BeginPlaySession();
            Bus.PublishEvent("Session/Twice", null, "v");
            Bus.SubscribeToEvent("Session/Twice", Recorder("late"));

            Assert.AreEqual("late:v", string.Join(",", Log));
        }

        // ---- play end: runtime state dropped, declarations and diagnostics kept ----

        [Test]
        public void EndPlaySession_DropsSubscriptions()
        {
            // A subscriber that outlived its session targets a destroyed object, or duplicates the
            // subscription its replacement is about to make. Neither is ever wanted.
            Bus.SubscribeToEvent("Session/A", Recorder("stale"));
            Bus.SubscribeToAllEvents(Recorder("stale-all"));

            EventsRegistry.EndPlaySession();
            Bus.PublishEvent("Session/A", null, "v");

            Assert.AreEqual("", string.Join(",", Log));
        }

        [Test]
        public void EndPlaySession_DropsTheStickyValue()
        {
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Level, null, "old");

            EventsRegistry.EndPlaySession();

            Assert.IsFalse(EventsFor<SessionTestEvents>.TryGetLast(SessionTestEvents.Level, out _, out _));
            EventsFor<SessionTestEvents>.Subscribe(SessionTestEvents.Level, Recorder("late"));
            Assert.AreEqual("", string.Join(",", Log),
                "the next session's subscriber must not be handed this session's value");
        }

        [Test]
        public void EndPlaySession_DropsTheReplayJournal()
        {
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Journal, null, "old");

            EventsRegistry.EndPlaySession();
            EventsFor<SessionTestEvents>.Subscribe(SessionTestEvents.Journal, Recorder("late"));

            Assert.AreEqual("", string.Join(",", Log));
        }

        [Test]
        public void EndPlaySession_PopsPushedFrames()
        {
            // A frame pushed for a scene and never popped, because play mode ended first, would
            // otherwise stay on the stack — one more per session — still holding that scene's values.
            Bus.Push();
            Bus.Push();

            EventsRegistry.EndPlaySession();

            Assert.AreEqual(1, Publisher.FrameCount);
        }

        [Test]
        public void EndPlaySession_ForgetsTypedWrappersTogetherWithTheirSubscriptions()
        {
            // The wrapper table mirrors the publisher's subscriptions. Dropping one without the other
            // leaves a handler that re-subscribes next session subscribed twice, or unable to unsubscribe.
            EventId<string> typed = EventId<string>.Of("Session/TypedSub");
            typed.Register();
            Action<string, object, string> handler = (e, s, d) => Log.Add("typed:" + d);
            typed.Subscribe(handler);

            EventsRegistry.EndPlaySession();
            typed.Subscribe(handler);
            typed.Publish(null, "v");
            Assert.AreEqual("typed:v", string.Join(",", Log), "delivered once, not once per session");

            Log.Clear();
            typed.Unsubscribe(handler);
            typed.Publish(null, "w");
            Assert.AreEqual("", string.Join(",", Log), "one unsubscribe must remove the only subscription");
        }

        [Test]
        public void EndPlaySession_KeepsRegistrationsAndPolicies()
        {
            // A registration made from a static initializer cannot be made again without a domain
            // reload, so dropping it here would have StrictMode report the event on the next play.
            Bus.RegisterEvent("Session/EndKeep", EventDelivery.Sticky);

            EventsRegistry.EndPlaySession();
            Bus.PublishEvent("Session/EndKeep", "probe", "v");
            Bus.SubscribeToEvent("Session/EndKeep", Recorder("late"));

            Assert.AreEqual("late:v", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EndPlaySession_KeepsTheDiagnostics()
        {
            // The upgrade audit reads them after play mode has ended; that is its whole workflow.
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Edge, null, null);
            EventsFor<SessionTestEvents>.Subscribe(SessionTestEvents.Edge, Recorder("late"));

            EventsRegistry.EndPlaySession();

            Assert.AreEqual(1, EventsDiagnostics.GetLateDeliveries().Count);
        }

        [Test]
        public void EndPlaySession_IsIdempotent()
        {
            Bus.RegisterEvent("Session/EndTwice", EventDelivery.Sticky);
            Bus.Push();

            EventsRegistry.EndPlaySession();
            EventsRegistry.EndPlaySession();
            Bus.PublishEvent("Session/EndTwice", null, "v");
            Bus.SubscribeToEvent("Session/EndTwice", Recorder("late"));

            Assert.AreEqual("late:v", string.Join(",", Log));
            Assert.AreEqual(1, Publisher.FrameCount);
        }

        // ---- the sweep after an explicit Clear ----

        [Test]
        public void AnnotatedFamily_IsReRegisteredByTheSweepAfterAClear()
        {
            // The editor's "Clear Now" drops registrations. The BeforeSceneLoad sweep must put an
            // annotated family's back even though its facade is cached, or the first publish of the
            // next session is reported as unregistered.
            EventsFor<SessionTestEvents>.EnsureRegistered();
            Bus.Clear();

            EventsRegistry.RegisterAnnotatedEventEnums(OwnAssembly);
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Edge, "probe", null);

            LogAssert.NoUnexpectedReceived();
        }
    }
}
