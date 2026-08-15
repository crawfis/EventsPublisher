using NUnit.Framework;

namespace CrawfisSoftware.Events.Tests
{
    /// <summary>
    /// The re-entrant publish contract: <c>PublishEvent</c> returns only after its event has been
    /// delivered to every subscriber, however deep the publish is nested.
    /// </summary>
    /// <remarks>
    /// <para>Auto-chained architectures publish from inside handlers constantly, and producers build
    /// state across consecutive publishes: publish one event whose subscribers derive state, then
    /// publish a second whose subscribers consume it. That pattern is only sound if a nested publish
    /// has completed by the time the publishing statement returns, which is what this fixture pins.</para>
    /// <para>The pre-2.4.0 drain gave exactly that guarantee, as an emergent property of draining the
    /// shared FIFO queue inline: whoever called <c>PublishEvent</c> got the queue drained to empty
    /// before it returned. 2.4.0 replaced the inline drain with enqueue-and-return under an
    /// <c>_isDraining</c> guard and recorded the ordering as unchanged — but the probes used to verify
    /// that (a single chain of nested publishes, observed only as a final sequence) produce identical
    /// sequences under both contracts. A handler that publishes twice, or does anything after its
    /// publish statement, can tell them apart; these tests do.</para>
    /// </remarks>
    public class NestedPublishOrderingTests : EventsTestBase
    {
        [Test]
        public void TopLevelPublish_IsDeliveredBeforeItReturns()
        {
            Bus.SubscribeToEvent("A", Recorder("A1"));

            Bus.PublishEvent("A", null, null);
            Log.Add("returned");

            Assert.AreEqual("A1:-,returned", string.Join(",", Log));
        }

        [Test]
        public void NestedPublish_IsDeliveredBeforeItReturns()
        {
            Bus.SubscribeToEvent("B", Recorder("B1"));
            Bus.SubscribeToEvent("Start", (e, s, d) =>
            {
                Bus.PublishEvent("B", null, null);
                // Anything the publisher does after this statement — cache a result, publish a
                // dependent event, tear something down — assumes B's subscribers have already run.
                Log.Add("after-B");
            });

            Bus.PublishEvent("Start", null, null);

            Assert.AreEqual("B1:-,after-B", string.Join(",", Log),
                "a publish made from inside a handler must complete before the publishing statement returns");
        }

        [Test]
        public void HandlerPublishingTwoEvents_DeliversTheFirstEventsChainBeforeTheSecondEvent()
        {
            // The auto-chain shape that broke a consumer on 2.4.0: A's handler responds by publishing
            // B (segment created -> geometry ready, cached by a third component), and C's subscribers
            // consume what B's subscribers cached (active track changing -> pop the cache). Deliver C
            // before B and the cache is silently empty at the moment it is read.
            Bus.SubscribeToEvent("A", (e, s, d) => Bus.PublishEvent("B", null, null));
            Bus.SubscribeToEvent("B", Recorder("B"));
            Bus.SubscribeToEvent("C", Recorder("C"));
            Bus.SubscribeToEvent("Start", (e, s, d) =>
            {
                Bus.PublishEvent("A", null, null);
                Bus.PublishEvent("C", null, null);
            });

            Bus.PublishEvent("Start", null, null);

            Assert.AreEqual("B:-,C:-", string.Join(",", Log),
                "everything the publish of A caused must be delivered before the later publish of C");
        }

        [Test]
        public void NestedPublish_FlushesTheCurrentEventsRemainingHandlersBeforeReturning()
        {
            // Forced by two invariants held together: the current event's remaining handlers run
            // before the nested event's handlers (the shared FIFO queue), and the nested publish
            // completes before returning (this fixture). A2 therefore runs inside A1's publish call,
            // ahead of B1 — it cannot run any later without one of the two invariants breaking.
            Bus.SubscribeToEvent("A", (e, s, d) =>
            {
                Log.Add("A1");
                Bus.PublishEvent("B", null, null);
                Log.Add("A1-after-B");
            });
            Bus.SubscribeToEvent("A", (e, s, d) => Log.Add("A2"));
            Bus.SubscribeToEvent("B", (e, s, d) => Log.Add("B1"));

            Bus.PublishEvent("A", null, null);

            Assert.AreEqual("A1,A2,B1,A1-after-B", string.Join(",", Log));
        }

        [Test]
        public void NestedPublish_BehavesTheSameOnEveryFrameOfTheStack()
        {
            // The 2.4.0 deferral was per-frame state, so a nested publish was deferred on the frame
            // whose drain was active while being delivered inline on every other frame — one publish,
            // two orderings. The contract must not depend on which frame a subscriber sits on.
            Bus.SubscribeToEvent("B", Recorder("B-lower"));
            Bus.Push();
            try
            {
                Bus.SubscribeToEvent("A", (e, s, d) =>
                {
                    Bus.PublishEvent("B", null, "x");
                    Log.Add("A-done");
                });
                Bus.SubscribeToEvent("B", Recorder("B-top"));

                Bus.PublishEvent("A", null, null);
            }
            finally
            {
                Bus.Pop();
            }

            Assert.AreEqual("B-top:x,B-lower:x,A-done", string.Join(",", Log));
        }
    }
}
