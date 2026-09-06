using UnityEditor;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// Menu entry points for inspecting the publisher, and the end-of-session drop that runs once play
    /// mode has finished tearing down.
    /// </summary>
    /// <remarks>
    /// <para>With <em>Enter Play Mode Options</em> set to skip the domain reload — Unity 6.6's default
    /// for new projects — statics survive between play sessions, so <see cref="EventsPublisher"/> would
    /// otherwise carry the previous session's subscriptions, delegates bound to destroyed objects, and
    /// its retained values into the next one. <see cref="EventsRegistry.EndPlaySession"/> drops those
    /// and keeps every declaration; see it for what falls on which side and why.</para>
    /// <para>It runs at <see cref="PlayModeStateChange.EnteredEditMode"/> rather than
    /// <see cref="PlayModeStateChange.ExitingPlayMode"/>, which is the point teardown has finished: an
    /// <c>OnDestroy</c> that unsubscribes, or that publishes a shutdown event, still sees the
    /// subscribers it expects. The cost is that <b>List Current Subscribers</b> reports nothing once
    /// play mode has ended — inspect a subscription leak while still in play mode.</para>
    /// <para>History: until 2.6.0 the drop sat behind a "Clear Events on Exiting Play Mode" toggle that
    /// never ran — the hook lived in an ordinary method named after the class it was copied from, and
    /// the toggle's menu item had a validator but no action. 2.6.0 made it unconditional, as a full
    /// <see cref="EventsPublisher.Clear"/>, which also discarded the registrations a static initializer
    /// cannot make again without a domain reload; 2.6.1 keeps those, and unwinds frames pushed above
    /// the root rather than leaving them on the stack empty.</para>
    /// </remarks>
    [InitializeOnLoad]
    public class ClearEventsMenu : EditorWindow
    {
        private const string CLEAR_NOW_MENU_LOCATION = "CrawfisSoftware/Events/Clear Now";
        private const string LIST_SUBSCRIBERS_MENU_LOCATION = "CrawfisSoftware/Events/List Current Subscribers";

        static ClearEventsMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem(CLEAR_NOW_MENU_LOCATION)]
        private static void Clear()
        {
            EventsPublisher.Instance.Clear();
        }

        [MenuItem(LIST_SUBSCRIBERS_MENU_LOCATION)]
        private static void ListSubscribers()
        {
            UnityEngine.Debug.Log("Listing all current subscribers ...");
            EventsPublisher publisher = (EventsPublisher)(EventsPublisher.Instance);
            int count = 0;
            foreach ((string eventName, string targetName) subscriberData in publisher.GetSubscribers())
            {
                UnityEngine.Debug.Log($"{subscriberData.targetName} is subscribed to {subscriberData.eventName}.");
                count++;
            }
            UnityEngine.Debug.Log($"   ....  {count} subscribers listed");
        }

        internal static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EventsRegistry.EndPlaySession();
            }
        }
    }
}
