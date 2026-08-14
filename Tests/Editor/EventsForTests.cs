using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum SweptTestEvents { One, Two }

    public enum LazyTestEvents { Alpha, Beta }

    namespace CollidingA { public enum SameNameEvents { Alpha } }
    namespace CollidingB { public enum SameNameEvents { Alpha } }

    /// <summary>
    /// The static facade: registration timing, family scoping, cross-family collisions, and reset.
    /// </summary>
    public class EventsForTests : EventsTestBase
    {
        [Test]
        public void FirstTouch_RegistersTheWholeFamily_WithNoSceneObject()
        {
            // No Awake, no [DefaultExecutionOrder], no GameObject. Any publish or subscribe goes through
            // EventsFor<T>, so registration always precedes first use.
            EventsFor<LazyTestEvents>.Subscribe(LazyTestEvents.Alpha, Recorder("late"));
            EventsFor<LazyTestEvents>.Publish(LazyTestEvents.Alpha, null, "v");

            Assert.AreEqual("late:v", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProjectedName_IsTypeNameSlashMemberName()
        {
            Assert.AreEqual("LazyTestEvents/Alpha", EventsFor<LazyTestEvents>.GetEventName(LazyTestEvents.Alpha));
        }

        [Test]
        public void TryGetEnum_RoundTripsWithoutParsing()
        {
            string name = EventsFor<LazyTestEvents>.GetEventName(LazyTestEvents.Beta);

            Assert.IsTrue(EventsFor<LazyTestEvents>.TryGetEnum(name, out LazyTestEvents value));
            Assert.AreEqual(LazyTestEvents.Beta, value);
        }

        [Test]
        public void TryGetEnum_RejectsForeignAndNullNames()
        {
            Assert.IsFalse(EventsFor<LazyTestEvents>.TryGetEnum("SomeOther/Beta", out _));
            Assert.IsFalse(EventsFor<LazyTestEvents>.TryGetEnum(null, out _));
        }

        [Test]
        public void RawStringPublish_IsFlaggedBeforeTheSweepAndCleanAfter()
        {
            LogAssert.Expect(LogType.Error, new Regex("SweptTestEvents/One.*is not registered"));
            Bus.PublishEvent("SweptTestEvents/One", "probe", null);

            // [EventEnum] covers the one case lazy initialization cannot: a name reaching the publisher
            // as a raw string, from a serialized Inspector field, without the facade ever being touched.
            EventsRegistry.RegisterAnnotatedEventEnums();

            Bus.PublishEvent("SweptTestEvents/One", "probe", null);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SubscribeToAll_IsScopedToItsOwnFamily()
        {
            EventsFor<LazyTestEvents>.SubscribeToAll(Recorder("lazy"));

            EventsFor<LazyTestEvents>.Publish(LazyTestEvents.Beta, null, "mine");
            EventsFor<SweptTestEvents>.Publish(SweptTestEvents.Two, null, "theirs");

            Assert.AreEqual("lazy:mine", string.Join(",", Log));
        }

        [Test]
        public void UnsubscribeFromAll_RemovesEveryMember()
        {
            System.Action<string, object, object> callback = Recorder("lazy");
            EventsFor<LazyTestEvents>.SubscribeToAll(callback);
            EventsFor<LazyTestEvents>.UnsubscribeFromAll(callback);

            EventsFor<LazyTestEvents>.Publish(LazyTestEvents.Beta, null, "mine");

            Assert.AreEqual(0, Log.Count);
        }

        [Test]
        public void TwoEnumTypesWithTheSameSimpleName_AreReportedAsColliding()
        {
            // Names project from Type.Name, not Type.FullName, so these share every event.
            // This check lived on the generic type until it was noticed that a static field inside a
            // generic exists per constructed type — so each closed type saw only its own prefix and the
            // cross-type comparison could never fire.
            LogAssert.Expect(LogType.Error, new Regex("both project onto the event name prefix 'SameNameEvents/'"));

            EventsFor<CollidingA.SameNameEvents>.EnsureRegistered();
            EventsFor<CollidingB.SameNameEvents>.EnsureRegistered();
        }

        [Test]
        public void SameEnumTypeReRegistering_IsNotACollision()
        {
            // Must stay idempotent: statics survive a domain reload when the project disables it.
            EventsFor<LazyTestEvents>.EnsureRegistered();
            EventsFor<LazyTestEvents>.EnsureRegistered();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Reset_DropsCachedFacadesAndRebuildsOnNextUse()
        {
            EventsFor<LazyTestEvents>.EnsureRegistered();

            EventsRegistry.ResetStaticState();

            EventsFor<LazyTestEvents>.Subscribe(LazyTestEvents.Alpha, Recorder("after"));
            EventsFor<LazyTestEvents>.Publish(LazyTestEvents.Alpha, null, "v");

            Assert.AreEqual("after:v", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
