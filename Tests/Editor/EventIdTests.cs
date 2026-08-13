using NUnit.Framework;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum IdTestEvents { Alpha, Beta }

    /// <summary>
    /// The interned identity: the string becomes a projection off it, and both the id-keyed and
    /// name-keyed paths must reach the same event.
    /// </summary>
    public class EventIdTests : EventsTestBase
    {
        [Test]
        public void SameName_InternsToTheSameId()
        {
            Assert.AreEqual(EventId.Of("A/One"), EventId.Of("A/One"));
        }

        [Test]
        public void DifferentNames_InternToDifferentIds()
        {
            Assert.AreNotEqual(EventId.Of("A/One"), EventId.Of("A/Two"));
        }

        [Test]
        public void NameIsAProjectionOffTheId()
        {
            Assert.AreEqual("A/One", EventId.Of("A/One").Name);
        }

        [Test]
        public void DefaultId_IsInvalidRatherThanAliasingTheFirstEvent()
        {
            // Handles are one-based precisely so an unassigned field cannot silently mean "the first
            // event that happened to be registered".
            var unset = default(EventId);

            Assert.IsFalse(unset.IsValid);
            Assert.IsNull(unset.Name);
            Assert.AreNotEqual(EventId.Of("A/One"), unset);
        }

        [Test]
        public void NullAndEmptyNames_InternToTheInvalidId()
        {
            Assert.IsFalse(EventId.Of(null).IsValid);
            Assert.IsFalse(EventId.Of(string.Empty).IsValid);
        }

        [Test]
        public void PublishingThroughAnUnsetId_IsANoOp()
        {
            EventsPublisher.StrictMode = false;
            var idBus = (IEventIdPublisher)Bus;

            Assert.DoesNotThrow(() =>
            {
                idBus.SubscribeToEvent(default, Recorder("x"));
                idBus.PublishEvent(default, null, null);
                idBus.UnsubscribeToEvent(default, Recorder("x"));
            });
            Assert.IsFalse(idBus.TryGetLast(default, out _, out _));
        }

        [Test]
        public void IdPathAndNamePath_ReachTheSameEvent()
        {
            // The whole refactor rests on this: the two entry points must key on the same thing, or a
            // subscriber registered one way silently misses a publish made the other way.
            var idBus = (IEventIdPublisher)Bus;
            EventId id = EventId.Of("Shared/Event");

            Bus.SubscribeToEvent("Shared/Event", Recorder("byName"));
            idBus.SubscribeToEvent(id, Recorder("byId"));

            idBus.PublishEvent(id, null, "viaId");
            Bus.PublishEvent("Shared/Event", null, "viaName");

            Assert.AreEqual("byName:viaId,byId:viaId,byName:viaName,byId:viaName", string.Join(",", Log));
        }

        [Test]
        public void FacadeIdMatchesTheProjectedName()
        {
            Assert.AreEqual(
                EventId.Of(EventsFor<IdTestEvents>.GetEventName(IdTestEvents.Alpha)),
                EventsFor<IdTestEvents>.GetEventId(IdTestEvents.Alpha));
        }

        [Test]
        public void IdsSurviveTheStaticReset()
        {
            // The intern table deliberately outlives ResetStaticState. Recycling handles would silently
            // repoint an EventId a caller still holds at a different event.
            EventId before = EventId.Of("Durable/Event");

            EventsRegistry.ResetStaticState();

            Assert.AreEqual(before, EventId.Of("Durable/Event"));
            Assert.AreEqual("Durable/Event", before.Name);
        }

        [Test]
        public void RetainedValuesAreReachableThroughEitherPath()
        {
            var idBus = (IEventIdPublisher)Bus;
            EventId id = EventId.Of("Retained/Event");
            Bus.RegisterEvent("Retained/Event", EventDelivery.Sticky);

            idBus.PublishEvent(id, null, "value");

            Assert.IsTrue(Bus.TryGetLast("Retained/Event", out _, out object byName));
            Assert.IsTrue(idBus.TryGetLast(id, out _, out object byId));
            Assert.AreEqual("value", byName);
            Assert.AreEqual("value", byId);
        }
    }
}
