using NUnit.Framework;

using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    public enum DiagnosticTestEvents
    {
        Transient,

        [EventDelivery(EventDelivery.Sticky)]
        Retained,
    }

    /// <summary>
    /// The late-delivery recorder that turns the edge-or-level decision into a measurement.
    /// </summary>
    /// <remarks>Its usefulness rests entirely on being quiet. An event reported here is one a project
    /// will spend real time reasoning about, so the tests are mostly about what must <em>not</em> be
    /// reported.</remarks>
    public class EventsDiagnosticsTests : EventsTestBase
    {
        private static readonly string TransientName =
            EventsFor<DiagnosticTestEvents>.GetEventName(DiagnosticTestEvents.Transient);

        [Test]
        public void SubscribingAfterAPublish_IsRecordedAsLate()
        {
            EventsFor<DiagnosticTestEvents>.Publish(DiagnosticTestEvents.Transient, null, "v");
            EventsFor<DiagnosticTestEvents>.Subscribe(DiagnosticTestEvents.Transient, Recorder("late"));

            var late = EventsDiagnostics.GetLateDeliveries();
            Assert.AreEqual(1, late.Count);
            Assert.AreEqual(TransientName, late[0].Name);
            Assert.AreEqual(1, late[0].LateSubscribes);
            Assert.AreEqual(1, late[0].Publishes);
            Assert.AreEqual("", string.Join(",", Log), "a transient event delivers nothing to a late subscriber");
        }

        [Test]
        public void SubscribingBeforeAnyPublish_IsNotLate()
        {
            // The check the whole diagnostic depends on. Counting every subscribe would list every event
            // in the project, and a report that flags everything is one nobody can act on.
            EventsFor<DiagnosticTestEvents>.Subscribe(DiagnosticTestEvents.Transient, Recorder("early"));
            EventsFor<DiagnosticTestEvents>.Publish(DiagnosticTestEvents.Transient, null, "v");

            Assert.AreEqual(0, EventsDiagnostics.GetLateDeliveries().Count);
            Assert.AreEqual("early:v", string.Join(",", Log));
        }

        [Test]
        public void ASubscriberArrivingLateToAStickyEvent_IsNotLate()
        {
            // It was delivered the retained value, so nothing was missed. Reporting it would tell a
            // project to make Sticky what is already Sticky.
            EventsFor<DiagnosticTestEvents>.Publish(DiagnosticTestEvents.Retained, null, "v");
            EventsFor<DiagnosticTestEvents>.Subscribe(DiagnosticTestEvents.Retained, Recorder("late"));

            Assert.AreEqual(0, EventsDiagnostics.GetLateDeliveries().Count);
            Assert.AreEqual("late:v", string.Join(",", Log));
        }

        [Test]
        public void APublishIsCountedOncePerPublish_NotOncePerPublisherFrame()
        {
            EventsPublisher.Instance.Push();
            EventsFor<DiagnosticTestEvents>.Publish(DiagnosticTestEvents.Transient, null, "v");
            EventsFor<DiagnosticTestEvents>.Subscribe(DiagnosticTestEvents.Transient, Recorder("late"));

            var late = EventsDiagnostics.GetLateDeliveries();
            Assert.AreEqual(1, late.Count);
            Assert.AreEqual(1, late[0].Publishes, "the stack dispatches to every frame; that is one publish");

            EventsPublisher.Instance.Pop();
        }

        [Test]
        public void Disabled_RecordsNothing()
        {
            EventsDiagnostics.Enabled = false;

            EventsFor<DiagnosticTestEvents>.Publish(DiagnosticTestEvents.Transient, null, "v");
            EventsFor<DiagnosticTestEvents>.Subscribe(DiagnosticTestEvents.Transient, Recorder("late"));

            Assert.AreEqual(0, EventsDiagnostics.GetAll().Count);
        }

        [Test]
        public void Reset_DiscardsWhatWasRecorded()
        {
            EventsFor<DiagnosticTestEvents>.Publish(DiagnosticTestEvents.Transient, null, "v");
            EventsFor<DiagnosticTestEvents>.Subscribe(DiagnosticTestEvents.Transient, Recorder("late"));
            Assert.AreEqual(1, EventsDiagnostics.GetLateDeliveries().Count);

            EventsDiagnostics.Reset();

            Assert.AreEqual(0, EventsDiagnostics.GetAll().Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MostAffectedEventIsReportedFirst()
        {
            EventsPublisher.Instance.RegisterEvent("Diag/Once");
            EventsPublisher.Instance.PublishEvent("Diag/Once", null, null);
            EventsPublisher.Instance.SubscribeToEvent("Diag/Once", Recorder("a"));

            EventsPublisher.Instance.RegisterEvent("Diag/Twice");
            EventsPublisher.Instance.PublishEvent("Diag/Twice", null, null);
            EventsPublisher.Instance.SubscribeToEvent("Diag/Twice", Recorder("b"));
            EventsPublisher.Instance.SubscribeToEvent("Diag/Twice", Recorder("c"));

            var late = EventsDiagnostics.GetLateDeliveries();
            Assert.AreEqual(2, late.Count);
            Assert.AreEqual("Diag/Twice", late[0].Name, "the worst offender must lead the report");
        }
    }
}
