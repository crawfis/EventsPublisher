using System.Collections.Generic;

using NUnit.Framework;

namespace CrawfisSoftware.Events.Tests
{
    /// <summary>
    /// Shared setup for the events tests.
    /// </summary>
    /// <remarks>
    /// <see cref="EventsPublisher"/> is a process-wide singleton and <see cref="EventsRegistry"/> holds
    /// static state, so tests would otherwise leak subscriptions, retained values and declared policies
    /// into each other. Every fixture resets both before and after each test.
    /// </remarks>
    public abstract class EventsTestBase
    {
        /// <summary>Collects handler invocations as <c>"tag:data"</c> so order can be asserted.</summary>
        protected List<string> Log { get; private set; }

        protected static IStackEventsPublisher<string> Bus => EventsPublisher.Instance;

        /// <summary>A callback that records its invocation, for asserting delivery and ordering.</summary>
        protected System.Action<string, object, object> Recorder(string tag)
        {
            return (eventName, sender, data) => Log.Add(tag + ":" + (data ?? "-"));
        }

        [SetUp]
        public virtual void SetUp()
        {
            ResetEverything();
            Log = new List<string>();
            EventsPublisher.StrictMode = true;
        }

        [TearDown]
        public virtual void TearDown()
        {
            ResetEverything();
        }

        private static void ResetEverything()
        {
            EventsPublisher.Instance.Clear();
            // Drops declared policies, claimed prefixes, and every cached EventsFor<T> facade.
            EventsRegistry.ResetStaticState();
        }
    }
}
