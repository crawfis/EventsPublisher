using System;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    /// <summary>A reference-typed payload, so null is a legal value for it.</summary>
    public class PayloadData
    {
        public int Value;
        public override string ToString() => "P" + Value;
    }

    public enum PayloadTestEvents
    {
        [EventPayload(typeof(PayloadData))] Detailed,

        // A value-typed payload: null is not a legal value here, which is a case worth its own tests.
        [EventPayload(typeof(int))] Counted,

        // Deliberately unannotated: the payload stays unchecked, as it was before Stage 3.
        Loose,

        [EventPayload(typeof(int))]
        [EventDelivery(EventDelivery.Sticky)]
        Score,

        [EventPayload(typeof(PayloadData))]
        [EventDelivery(EventDelivery.Sticky)]
        Level,
    }

    /// <summary>
    /// Typed payloads: what the compiler covers, what the runtime has to check instead, and that
    /// erasure still holds for the observer channel.
    /// </summary>
    public class EventPayloadTests : EventsTestBase
    {
        private static EventId<PayloadData> Detailed => EventsFor<PayloadTestEvents>.Id<PayloadData>(PayloadTestEvents.Detailed);
        private static EventId<int> Counted => EventsFor<PayloadTestEvents>.Id<int>(PayloadTestEvents.Counted);
        private static EventId<PayloadData> Level => EventsFor<PayloadTestEvents>.Id<PayloadData>(PayloadTestEvents.Level);
        private static EventId<int> Score => EventsFor<PayloadTestEvents>.Id<int>(PayloadTestEvents.Score);

        // ---- delivery ----

        [Test]
        public void TypedHandler_ReceivesThePayloadWithNoCast()
        {
            Detailed.Subscribe(OnDetailed);
            Detailed.Publish(null, new PayloadData { Value = 7 });

            Assert.AreEqual("detailed:7", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TypedPublish_ReachesAnUntypedSubscriberOfTheSameEvent()
        {
            // The typed API must be a view onto the same event, not a parallel channel. If it were
            // separate, every existing subscriber in a migrating project would silently stop hearing it.
            Bus.SubscribeToEvent(EventsFor<PayloadTestEvents>.GetEventName(PayloadTestEvents.Detailed), Recorder("erased"));
            Detailed.Publish(null, new PayloadData { Value = 3 });

            Assert.AreEqual("erased:P3", string.Join(",", Log));
        }

        [Test]
        public void AllEventsSubscriber_StillSeesATypedPublish()
        {
            // The global logger and EventHistory subscribe this way. Stage 3 must not cost them anything.
            Bus.SubscribeToAllEvents(Recorder("all"));
            Detailed.Publish(null, new PayloadData { Value = 1 });

            Assert.AreEqual("all:P1", string.Join(",", Log));
        }

        [Test]
        public void UntypedPublish_IsDeliveredToATypedHandler()
        {
            // The other direction: a raw-string publisher that has not migrated must still reach a
            // handler that has.
            Detailed.Subscribe(OnDetailed);
            Bus.PublishEvent(Detailed.Name, null, new PayloadData { Value = 9 });

            Assert.AreEqual("detailed:9", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- unsubscribe: the wrapper table ----

        [Test]
        public void Unsubscribe_RemovesTheHandlerRatherThanLeakingIt()
        {
            // A typed handler is wrapped in an erased one before it reaches the publisher. If the
            // wrapper were rebuilt per call, this unsubscribe would hand the publisher a delegate that
            // matches nothing and the handler would stay subscribed forever.
            Detailed.Subscribe(OnDetailed);
            Detailed.Unsubscribe(OnDetailed);
            Detailed.Publish(null, new PayloadData { Value = 4 });

            Assert.AreEqual("", string.Join(",", Log));
        }

        [Test]
        public void SubscribingTwice_FiresTwiceAndNeedsTwoUnsubscribes()
        {
            // Matching what the untyped += and -= already do, so the wrapper cannot quietly change the
            // semantics of a double subscription.
            Detailed.Subscribe(OnDetailed);
            Detailed.Subscribe(OnDetailed);
            Detailed.Publish(null, new PayloadData { Value = 1 });
            Assert.AreEqual("detailed:1,detailed:1", string.Join(",", Log));

            Log.Clear();
            Detailed.Unsubscribe(OnDetailed);
            Detailed.Publish(null, new PayloadData { Value = 2 });
            Assert.AreEqual("detailed:2", string.Join(",", Log), "one unsubscribe must remove only one");

            Log.Clear();
            Detailed.Unsubscribe(OnDetailed);
            Detailed.Publish(null, new PayloadData { Value = 3 });
            Assert.AreEqual("", string.Join(",", Log));
        }

        [Test]
        public void UnsubscribingSomethingNeverSubscribed_IsANoOp()
        {
            Assert.DoesNotThrow(() => Detailed.Unsubscribe(OnDetailed));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Reset_DropsTheWrapperTable()
        {
            Detailed.Subscribe(OnDetailed);
            EventsRegistry.ResetStaticState();

            // The publisher's subscriptions are deliberately not cleared by a reset, so a leaked wrapper
            // entry would survive here and a later Unsubscribe would remove a handler belonging to the
            // previous play session.
            Detailed.Unsubscribe(OnDetailed);
            Detailed.Publish(null, new PayloadData { Value = 5 });

            Assert.AreEqual("detailed:5", string.Join(",", Log),
                "reset must forget the wrapper, leaving the old subscription for Clear() to deal with");
        }

        // ---- retained values ----

        [Test]
        public void TypedHandler_SubscribingLate_GetsTheRetainedPayload()
        {
            // Sticky replay runs through the same wrapper as a live publish, so the payload has to
            // survive the round trip through object and back.
            Level.Publish(null, new PayloadData { Value = 2 });
            Level.Subscribe(OnLevel);

            Assert.AreEqual("level:2", string.Join(",", Log));
        }

        [Test]
        public void TypedTryGetLast_ReturnsTheRetainedPayload()
        {
            Level.Publish(null, new PayloadData { Value = 8 });

            Assert.IsTrue(Level.TryGetLast(out object sender, out PayloadData data));
            Assert.AreEqual(8, data.Value);
        }

        [Test]
        public void TypedTryGetLast_ReportsAndFailsWhenTheRetainedValueIsTheWrongType()
        {
            EventsPublisher.StrictMode = false;   // isolate the read-side report from the publish-side one
            LogAssert.Expect(LogType.Error, new Regex("reading the retained value.*PayloadTestEvents/Level"));

            Bus.PublishEvent(Level.Name, null, "not-a-payload");

            Assert.IsFalse(Level.TryGetLast(out object sender, out PayloadData data));
            Assert.IsNull(data);
        }

        [Test]
        public void TypedTryGetLast_IsFalseWhenNothingWasRetained()
        {
            Assert.IsFalse(Level.TryGetLast(out object sender, out PayloadData data));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- mismatches the compiler cannot catch ----

        [Test]
        public void TypeArgumentDisagreeingWithTheAttribute_IsReported()
        {
            // The one runtime-checked point in the design: where the type argument is written. Getting
            // it wrong here is a loud error naming both types, not a cast failure inside a handler later.
            LogAssert.Expect(LogType.Error, new Regex("PayloadTestEvents/Detailed.*already declared to carry"));

            EventsFor<PayloadTestEvents>.Id<string>(PayloadTestEvents.Detailed);
        }

        [Test]
        public void TwoCallSitesDisagreeingAboutAnUnannotatedEvent_IsReported()
        {
            LogAssert.Expect(LogType.Error, new Regex("Ad/Hoc.*already declared to carry System.Int32"));

            EventId<int>.Of("Ad/Hoc");
            EventId<string>.Of("Ad/Hoc");
        }

        [Test]
        public void RestatingTheSamePayloadType_IsNotAConflict()
        {
            EventsFor<PayloadTestEvents>.Id<PayloadData>(PayloadTestEvents.Detailed);
            EventsFor<PayloadTestEvents>.Id<PayloadData>(PayloadTestEvents.Detailed);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UntypedPublishOfTheWrongPayload_IsReportedAtThePublisher()
        {
            // Named at the publisher, with the sender, which is the half of the story the handler-side
            // report cannot tell.
            LogAssert.Expect(LogType.Error, new Regex("published by probe with a payload of System.String"));

            EventsFor<PayloadTestEvents>.EnsureRegistered();
            Bus.PublishEvent(Detailed.Name, "probe", "wrong");
        }

        [Test]
        public void UntypedPublishOfTheWrongPayload_SkipsTheHandlerRatherThanThrowing()
        {
            EventsPublisher.StrictMode = false;
            LogAssert.Expect(LogType.Error, new Regex("delivering to a handler of.*PayloadTestEvents/Detailed"));

            Detailed.Subscribe(OnDetailed);
            Assert.DoesNotThrow(() => Bus.PublishEvent(Detailed.Name, null, "wrong"));

            Assert.AreEqual("", string.Join(",", Log), "the handler must not run with a payload it cannot use");
        }

        [Test]
        public void UnannotatedEvent_StaysUnchecked()
        {
            // The default has to remain "no declaration, no checking", or Stage 3 would start reporting
            // every event in a project that has not declared anything.
            EventsFor<PayloadTestEvents>.Publish(PayloadTestEvents.Loose, "probe", "anything at all");
            EventsFor<PayloadTestEvents>.Publish(PayloadTestEvents.Loose, "probe", 42);

            LogAssert.NoUnexpectedReceived();
        }

        // ---- null payloads ----

        [Test]
        public void NullPayload_ReachesAReferenceTypedHandler()
        {
            Detailed.Subscribe(OnDetailed);
            Detailed.Publish(null, null);

            Assert.AreEqual("detailed:null", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NullPayload_IsReportedForAValueTypedEvent()
        {
            // Null cannot be an int, so a handler expecting one would otherwise be handed default(int)
            // and treat a missing payload as a real zero.
            LogAssert.Expect(LogType.Error, new Regex("payload of null, but it is declared to carry System.Int32"));

            EventsFor<PayloadTestEvents>.EnsureRegistered();
            Bus.PublishEvent(Counted.Name, "probe", null);
        }

        [Test]
        public void NullPayload_IsNotDeliveredAsZeroToAValueTypedHandler()
        {
            // The typed API cannot publish null to an EventId<int> — that does not compile — so this is
            // reachable only from an untyped publish. Handing the handler default(int) would be the worst
            // outcome available: a missing payload silently read as a real score of zero.
            EventsPublisher.StrictMode = false;
            LogAssert.Expect(LogType.Error, new Regex("delivering to a handler of.*PayloadTestEvents/Score"));

            Score.Subscribe(OnScore);
            Bus.PublishEvent(Score.Name, null, null);

            Assert.AreEqual("", string.Join(",", Log));
        }

        [Test]
        public void TypedTryGetLast_DoesNotTurnANullRetainedValueIntoZero()
        {
            EventsPublisher.StrictMode = false;
            LogAssert.Expect(LogType.Error, new Regex("reading the retained value.*PayloadTestEvents/Score"));

            EventsFor<PayloadTestEvents>.EnsureRegistered();
            Bus.PublishEvent(Score.Name, null, null);

            Assert.IsFalse(Score.TryGetLast(out object sender, out int data));
            Assert.AreEqual(0, data);
        }

        [Test]
        public void ObjectPayloadDeclaration_MeansAnythingGoes()
        {
            EventId<object>.Of("Any/Thing").Register();
            Bus.PublishEvent("Any/Thing", "probe", 1);
            Bus.PublishEvent("Any/Thing", "probe", "one");

            LogAssert.NoUnexpectedReceived();
        }

        // ---- identity ----

        [Test]
        public void TypedIdentity_WidensToTheUntypedOne()
        {
            EventId erased = Detailed;

            Assert.AreEqual(EventsFor<PayloadTestEvents>.GetEventId(PayloadTestEvents.Detailed), erased);
            Assert.AreEqual(Detailed.Name, erased.Name);
        }

        [Test]
        public void DefaultTypedIdentity_IsInvalidAndInert()
        {
            EventId<PayloadData> unset = default(EventId<PayloadData>);

            Assert.IsFalse(unset.IsValid);
            Assert.DoesNotThrow(() => unset.Publish(null, new PayloadData()));
            Assert.DoesNotThrow(() => unset.Subscribe(OnDetailed));
            Assert.IsFalse(unset.TryGetLast(out object sender, out PayloadData data));
        }

        private void OnDetailed(string eventName, object sender, PayloadData data)
        {
            Log.Add("detailed:" + (data == null ? "null" : data.Value.ToString()));
        }

        private void OnLevel(string eventName, object sender, PayloadData data)
        {
            Log.Add("level:" + (data == null ? "null" : data.Value.ToString()));
        }

        private void OnScore(string eventName, object sender, int data)
        {
            Log.Add("score:" + data);
        }
    }
}
