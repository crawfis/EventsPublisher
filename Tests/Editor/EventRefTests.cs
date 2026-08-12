using CrawfisSoftware.Events.Editor;

using NUnit.Framework;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum PrefixedTestEvents { Alpha }

    namespace Aliased
    {
        [EventEnum(Prefix = "AliasedEvents")]
        public enum PrefixedTestEvents { Alpha }
    }

    /// <summary>
    /// The prefix override, and the <see cref="EventRef"/> overloads that keep authored call sites from
    /// unwrapping the underlying string.
    /// </summary>
    public class EventRefTests : EventsTestBase
    {
        [Test]
        public void PrefixOverride_ResolvesACollisionWithoutRenamingTheType()
        {
            // Switching the whole scheme to Type.FullName would close this too, but would rename every
            // event and break every name already baked into a scene or prefab. An override touches only
            // the enum that collides.
            Assert.AreEqual("PrefixedTestEvents/Alpha",
                EventsFor<PrefixedTestEvents>.GetEventName(PrefixedTestEvents.Alpha));
            Assert.AreEqual("AliasedEvents/Alpha",
                EventsFor<Aliased.PrefixedTestEvents>.GetEventName(Aliased.PrefixedTestEvents.Alpha));
        }

        [Test]
        public void PrefixOverride_MeansNoCollisionIsReported()
        {
            EventsFor<PrefixedTestEvents>.EnsureRegistered();
            EventsFor<Aliased.PrefixedTestEvents>.EnsureRegistered();

            // Same simple type name, different namespaces — but no longer the same prefix.
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CatalogHonoursThePrefixOverride()
        {
            string[] names = EventNameCatalog.BuildNames(new[] { typeof(Aliased.PrefixedTestEvents) });

            Assert.AreEqual(1, names.Length);
            Assert.AreEqual("AliasedEvents/Alpha", names[0]);
        }

        [Test]
        public void EventRef_ProjectsTheSameNameAsTheFacade()
        {
            EventRef reference = EventRef.For(PrefixedTestEvents.Alpha);

            Assert.AreEqual(EventsFor<PrefixedTestEvents>.GetEventName(PrefixedTestEvents.Alpha), reference.Name);
            Assert.IsFalse(reference.IsEmpty);
        }

        [Test]
        public void EventRef_RoundTripsBackToItsEnum()
        {
            EventRef reference = EventRef.For(PrefixedTestEvents.Alpha);

            Assert.IsTrue(reference.TryGetEnum(out PrefixedTestEvents value));
            Assert.AreEqual(PrefixedTestEvents.Alpha, value);
        }

        [Test]
        public void EventRefOverloads_PublishAndSubscribe()
        {
            EventRef reference = EventRef.For(PrefixedTestEvents.Alpha);

            Bus.SubscribeToEvent(reference, Recorder("ref"));
            Bus.PublishEvent(reference, null, "payload");

            Assert.AreEqual("ref:payload", string.Join(",", Log));
        }

        [Test]
        public void EventRefOverloads_IgnoreAnUnsetReference()
        {
            EventsPublisher.StrictMode = false;
            var unset = default(EventRef);

            // A component unsubscribing an optional event in OnDestroy would otherwise need its own
            // guard, and forgetting it is exactly how a null name reached Dictionary.ContainsKey.
            Assert.IsTrue(unset.IsEmpty);
            Assert.DoesNotThrow(() =>
            {
                Bus.SubscribeToEvent(unset, Recorder("x"));
                Bus.PublishEvent(unset, null, null);
                Bus.UnsubscribeToEvent(unset, Recorder("x"));
            });
            Assert.IsFalse(Bus.TryGetLast(unset, out _, out _));
        }

        [Test]
        public void EventRefOverloads_CarryDeliveryPolicy()
        {
            EventRef reference = EventRef.For(PrefixedTestEvents.Alpha);
            Bus.RegisterEvent(reference, EventDelivery.Sticky);

            Bus.PublishEvent(reference, null, "retained");
            Bus.SubscribeToEvent(reference, Recorder("late"));

            Assert.AreEqual("late:retained", string.Join(",", Log));
            Assert.IsTrue(Bus.TryGetLast(reference, out _, out object data));
            Assert.AreEqual("retained", data);
        }
    }
}
