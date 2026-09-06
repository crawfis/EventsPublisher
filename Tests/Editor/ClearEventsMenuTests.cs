using System.Linq;

using CrawfisSoftware.Events.Editor;

using NUnit.Framework;

using UnityEditor;

namespace CrawfisSoftware.Events.Tests
{
    /// <summary>
    /// The clear the editor performs when play mode has finished tearing down.
    /// </summary>
    /// <remarks>2.6.0 made it unconditional after finding that the toggle guarding it had never been
    /// wired. It cleared everything, registrations included, which a static initializer cannot put back
    /// without a domain reload. It now drops the session's runtime state and keeps the declarations,
    /// the same rule the play-entry reset follows.</remarks>
    public class ClearEventsMenuTests : EventsTestBase
    {
        [Test]
        public void TheHook_IsInstalledByAStaticConstructor()
        {
            // [InitializeOnLoad] runs the static constructor and nothing else. Until 2.6.0 the hook sat
            // in an ordinary method named EventLoggingMenu, which nothing called.
            Assert.IsNotNull(typeof(ClearEventsMenu).TypeInitializer);
        }

        [Test]
        public void EnteredEditMode_DropsSubscriptionsAndRetainedValues()
        {
            Bus.RegisterEvent("Menu/Sticky", EventDelivery.Sticky);
            Bus.SubscribeToEvent("Menu/Sticky", Recorder("stale"));
            Bus.PublishEvent("Menu/Sticky", null, "old");
            Log.Clear();

            ClearEventsMenu.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            Assert.IsFalse(Bus.TryGetLast("Menu/Sticky", out _, out _));
            Bus.PublishEvent("Menu/Sticky", null, "new");
            Assert.AreEqual("", string.Join(",", Log));
        }

        [Test]
        public void EnteredEditMode_KeepsRegistrations()
        {
            Bus.RegisterEvent("Menu/Registered");

            ClearEventsMenu.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            CollectionAssert.Contains(Bus.GetRegisteredEvents().ToList(), "Menu/Registered");
        }

        [Test]
        public void EnteredEditMode_KeepsTheDiagnostics()
        {
            // The upgrade audit is refreshed after play mode ends; clearing here would blank it.
            EventsFor<SessionTestEvents>.Publish(SessionTestEvents.Edge, null, null);
            EventsFor<SessionTestEvents>.Subscribe(SessionTestEvents.Edge, Recorder("late"));

            ClearEventsMenu.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            Assert.AreEqual(1, EventsDiagnostics.GetLateDeliveries().Count);
        }

        [Test]
        public void ExitingPlayMode_LeavesEverythingInPlace()
        {
            // Teardown runs between ExitingPlayMode and EnteredEditMode. An OnDestroy that publishes a
            // shutdown event, or unsubscribes, must still find the subscribers it expects.
            Bus.SubscribeToEvent("Menu/Shutdown", Recorder("teardown"));

            ClearEventsMenu.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            Bus.PublishEvent("Menu/Shutdown", null, "bye");

            Assert.AreEqual("teardown:bye", string.Join(",", Log));
        }
    }
}
