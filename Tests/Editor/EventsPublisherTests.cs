using System;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    /// <summary>
    /// Dispatch behaviour of the publisher itself: ordering, isolation, and the strict-mode diagnostic.
    /// </summary>
    public class EventsPublisherTests : EventsTestBase
    {
        [Test]
        public void NestedPublish_RunsCurrentEventsHandlersFirst()
        {
            Bus.RegisterEvent("A");
            Bus.RegisterEvent("B");
            Bus.SubscribeToEvent("A", (e, s, d) => { Log.Add("A1"); Bus.PublishEvent("B", null, null); });
            Bus.SubscribeToEvent("A", (e, s, d) => Log.Add("A2"));
            Bus.SubscribeToEvent("B", (e, s, d) => Log.Add("B1"));

            Bus.PublishEvent("A", null, null);

            // A callback that publishes must not pre-empt the remaining callbacks for the current event.
            Assert.AreEqual("A1,A2,B1", string.Join(",", Log));
        }

        [Test]
        public void NestedPublish_TwoLevelsDeep_DeliversLevelByLevel()
        {
            Bus.RegisterEvent("A");
            Bus.RegisterEvent("B");
            Bus.RegisterEvent("C");
            Bus.SubscribeToEvent("A", (e, s, d) => { Log.Add("A1"); Bus.PublishEvent("B", null, null); });
            Bus.SubscribeToEvent("A", (e, s, d) => Log.Add("A2"));
            Bus.SubscribeToEvent("B", (e, s, d) => { Log.Add("B1"); Bus.PublishEvent("C", null, null); });
            Bus.SubscribeToEvent("C", (e, s, d) => Log.Add("C1"));

            Bus.PublishEvent("A", null, null);

            // The FIFO sequence, identical under the re-entrant completion drain: A2 is queued ahead
            // of B1, and C1 is enqueued mid-chain by B1. When control returns to each publisher is a
            // separate contract, pinned by NestedPublishOrderingTests.
            Assert.AreEqual("A1,A2,B1,C1", string.Join(",", Log));
        }

        [Test]
        public void ThrowingHandler_DoesNotStopLaterHandlers()
        {
            LogAssert.Expect(LogType.Error, new Regex("Exception publishing C"));
            Bus.RegisterEvent("C");
            Bus.SubscribeToEvent("C", (e, s, d) => throw new InvalidOperationException("boom"));
            Bus.SubscribeToEvent("C", Recorder("after"));

            Bus.PublishEvent("C", null, null);

            Assert.AreEqual(1, Log.Count, "the handler after the throwing one should still run");
        }

        [Test]
        public void ThrowingHandler_DoesNotPoisonTheNextPublish()
        {
            LogAssert.Expect(LogType.Error, new Regex("Exception publishing C"));
            LogAssert.Expect(LogType.Error, new Regex("Exception publishing C"));
            Bus.RegisterEvent("C");
            Bus.SubscribeToEvent("C", (e, s, d) => throw new InvalidOperationException("boom"));
            Bus.SubscribeToEvent("C", Recorder("after"));

            Bus.PublishEvent("C", null, null);
            Log.Clear();
            Bus.PublishEvent("C", null, null);

            // A stranded queue entry would deliver twice here.
            Assert.AreEqual(1, Log.Count, "the second publish should deliver exactly once");
        }

        [Test]
        public void WrongPayloadCast_IsContainedInTheHandler()
        {
            LogAssert.Expect(LogType.Error, new Regex("Exception publishing D"));
            Bus.RegisterEvent("D");
            Bus.SubscribeToEvent("D", (e, s, d) => Log.Add("int=" + (int)d));

            // Publishing the wrong payload type must not take the publisher down. Making this a compile
            // error rather than a swallowed runtime one is Stage 3 of the identity work.
            Assert.DoesNotThrow(() => Bus.PublishEvent("D", null, "not-an-int"));
        }

        [Test]
        public void StrictMode_ReportsAnUnregisteredPublish()
        {
            // Without this the publish silently reaches no targeted subscriber while still notifying
            // "all events" subscribers, so the logger prints it and the system looks healthy.
            LogAssert.Expect(LogType.Error, new Regex("GameFlowEvents/Typoo.*is not registered"));

            Bus.PublishEvent("GameFlowEvents/Typoo", "probe", null);
        }

        [Test]
        public void StrictMode_DoesNotReportAnEventRegisteredOnALowerFrame()
        {
            Bus.RegisterEvent("OnLowerFrame");
            Bus.Push();
            try
            {
                // RegisterEvent reaches only the top frame while PublishEvent visits every frame, so a
                // per-frame check would report a false positive for every frame below the registration.
                Bus.PublishEvent("OnLowerFrame", "probe", null);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Bus.Pop();
            }
        }

        [Test]
        public void NullAndEmptyEventNames_AreRejectedRatherThanThrowing()
        {
            EventsPublisher.StrictMode = false;

            // FireEventAfterSceneLoads.OnDestroy unsubscribes its reset event unconditionally, even when
            // the field is null. Dictionary.ContainsKey(null) throws ArgumentNullException.
            Assert.DoesNotThrow(() =>
            {
                Bus.UnsubscribeToEvent(null, Recorder("x"));
                Bus.SubscribeToEvent(null, Recorder("x"));
                Bus.PublishEvent(null, null, null);
                Bus.RegisterEvent(string.Empty);
            });
        }

        [Test]
        public void AllEventsSubscriber_StillSeesEveryEvent()
        {
            // The global logger, EventHistory and DebugEventFileLogger all depend on this channel.
            Bus.SubscribeToAllEvents(Recorder("all"));
            Bus.RegisterEvent("E");

            Bus.PublishEvent("E", null, "payload");

            Assert.AreEqual("all:payload", string.Join(",", Log));
        }
    }
}
