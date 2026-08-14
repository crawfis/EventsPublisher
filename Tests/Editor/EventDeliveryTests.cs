using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    /// <summary>Mirrors the shape of TempleRunEvents: an edge, a level, and an accumulation.</summary>
    [EventEnum]
    public enum DeliveryTestEvents
    {
        PlayerFailingAtTurn = 12,
        [EventDelivery(EventDelivery.Sticky)] PlayerDied = 5,
        [EventDelivery(EventDelivery.Replay)] SegmentCreated = 40,
    }

    /// <summary>
    /// Retention and replay: the three delivery policies, declaration rules, and scoping.
    /// </summary>
    public class EventDeliveryTests : EventsTestBase
    {
        [Test]
        public void Transient_RetainsNothing()
        {
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.PlayerFailingAtTurn, null, "turn1");

            EventsFor<DeliveryTestEvents>.Subscribe(DeliveryTestEvents.PlayerFailingAtTurn, Recorder("late"));

            // Replaying an edge would re-trigger whatever the transition drives.
            Assert.AreEqual(0, Log.Count);
            Assert.IsFalse(EventsFor<DeliveryTestEvents>.TryGetLast(DeliveryTestEvents.PlayerFailingAtTurn, out _, out _));
        }

        [Test]
        public void Sticky_DeliversTheLastValueDuringSubscribe()
        {
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.PlayerDied, null, 100);
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.PlayerDied, null, 250);

            EventsFor<DeliveryTestEvents>.Subscribe(DeliveryTestEvents.PlayerDied, Recorder("late"));

            // Immediate, not deferred: the subscriber knows the current state by the time Subscribe
            // returns, so it does not spend the rest of the frame wrong about the world.
            Assert.AreEqual("late:250", string.Join(",", Log), "only the most recent value, delivered inline");
        }

        [Test]
        public void Sticky_CarriesThePayloadThatABoolMirrorCannot()
        {
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.PlayerDied, null, 250);

            Assert.IsTrue(EventsFor<DeliveryTestEvents>.TryGetLast(DeliveryTestEvents.PlayerDied, out _, out object data));
            Assert.AreEqual(250, data);
        }

        [Test]
        public void Replay_DeliversTheWholeJournalInOrder()
        {
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.SegmentCreated, null, "s1");
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.SegmentCreated, null, "s2");
            EventsFor<DeliveryTestEvents>.Publish(DeliveryTestEvents.SegmentCreated, null, "s3");

            EventsFor<DeliveryTestEvents>.Subscribe(DeliveryTestEvents.SegmentCreated, Recorder("late"));

            Assert.AreEqual("late:s1,late:s2,late:s3", string.Join(",", Log));
        }

        [Test]
        public void SubscribeFromInsideAHandler_EnqueuesTheReplayRatherThanNesting()
        {
            // The implementation risk called out when replay moved from end-of-frame to immediate.
            Bus.RegisterEvent("Trigger");
            Bus.RegisterEvent("StickyX", EventDelivery.Sticky);
            Bus.PublishEvent("StickyX", null, "X");

            Bus.SubscribeToEvent("Trigger", (e, s, d) =>
            {
                Log.Add("H1-start");
                Bus.SubscribeToEvent("StickyX", Recorder("replayed"));
                Log.Add("H1-end");
            });
            Bus.SubscribeToEvent("Trigger", (e, s, d) => Log.Add("H2"));

            Bus.PublishEvent("Trigger", null, null);

            Assert.AreEqual("H1-start,H1-end,H2,replayed:X", string.Join(",", Log),
                "the replay must run after the in-flight drain, not inside H1");
        }

        [Test]
        public void RedeclaringTheSamePolicy_IsNotAConflict()
        {
            Bus.RegisterEvent("Conflicted", EventDelivery.Sticky);
            Bus.RegisterEvent("Conflicted", EventDelivery.Sticky);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ConflictingDeclaration_IsReportedAndTheFirstIsKept()
        {
            LogAssert.Expect(LogType.Error, new Regex("already declared as Sticky.*redeclared as Replay"));

            Bus.RegisterEvent("Conflicted", EventDelivery.Sticky);
            Bus.RegisterEvent("Conflicted", EventDelivery.Replay);

            Assert.AreEqual(EventDelivery.Sticky, EventsRegistry.GetPolicy("Conflicted"));
        }

        [Test]
        public void SubscribingBeforeDeclaring_DoesNotPinTheEventToTransient()
        {
            // SubscribeToEvent registers the name on the fly. If that recorded a Transient default, this
            // later declaration would be silently ignored and the event would never retain.
            Bus.SubscribeToEvent("LateDeclared", (e, s, d) => { });
            Bus.RegisterEvent("LateDeclared", EventDelivery.Sticky);

            Assert.AreEqual(EventDelivery.Sticky, EventsRegistry.GetPolicy("LateDeclared"));

            Bus.PublishEvent("LateDeclared", null, "v");
            Bus.SubscribeToEvent("LateDeclared", Recorder("late"));

            Assert.AreEqual("late:v", string.Join(",", Log));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DeclaringAfterAPublish_DoesNotRetroactivelyRetain()
        {
            // Documents the ordering that remains: a policy is sensitive to publishes, not subscribes.
            // The attribute sweep and lazy facade init exist so this cannot happen for enum families.
            Bus.RegisterEvent("Late");
            Bus.PublishEvent("Late", null, "missed");

            Bus.RegisterEvent("Late", EventDelivery.Sticky);
            Bus.SubscribeToEvent("Late", Recorder("late"));

            Assert.AreEqual(0, Log.Count, "the value published under the old policy was never retained");
        }

        [Test]
        public void Pop_DiscardsOnlyTheFrameThatRetained()
        {
            Bus.RegisterEvent("Scoped", EventDelivery.Sticky);
            Bus.PublishEvent("Scoped", null, "outer");

            Bus.Push();
            Bus.PublishEvent("Scoped", null, "inner");
            Bus.SubscribeToEvent("Scoped", Recorder("top"));
            Assert.AreEqual("top:inner", string.Join(",", Log), "the top frame replays its own value");
            Bus.Pop();

            // Every frame is dispatched to, but only the frame that is top at publish time retains.
            // Retaining on dispatch would leave a copy below, and Pop would discard nothing — which
            // would make Push/Pop useless as a scope for Replay journals.
            Assert.IsTrue(Bus.TryGetLast("Scoped", out _, out object data));
            Assert.AreEqual("outer", data);
        }

        [Test]
        public void Clear_DropsRetainedValues()
        {
            Bus.RegisterEvent("Scoped", EventDelivery.Sticky);
            Bus.PublishEvent("Scoped", null, "v");

            Bus.Clear();

            Assert.IsFalse(Bus.TryGetLast("Scoped", out _, out _));
        }

        [Test]
        public void LowerFrameSubscribers_StillHearEventsPublishedWhileAFrameIsPushed()
        {
            Bus.RegisterEvent("Heard");
            Bus.SubscribeToEvent("Heard", Recorder("lower"));

            Bus.Push();
            try
            {
                Bus.PublishEvent("Heard", null, "v");
            }
            finally
            {
                Bus.Pop();
            }

            Assert.AreEqual("lower:v", string.Join(",", Log), "scoping retention must not change dispatch");
        }
    }
}
