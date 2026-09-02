using UnityEditor;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// Menu entry points for inspecting the publisher, and the clear that runs when play mode ends.
    /// </summary>
    /// <remarks>
    /// <para>The clear used to sit behind a "Clear Events on Exiting Play Mode" toggle that defaulted
    /// off, and that never actually ran: the method wired to
    /// <see cref="EditorApplication.playModeStateChanged"/> carried the name of the class it was copied
    /// from, so it was an ordinary private method rather than the static constructor
    /// <c>[InitializeOnLoad]</c> calls, and the toggle's menu item had a validator but no action, so it
    /// could not be switched on either.</para>
    /// <para>It is now unconditional. With <em>Enter Play Mode Options</em> set to skip the domain
    /// reload, statics survive between play sessions, so <see cref="EventsPublisher"/> would otherwise
    /// carry the previous session's subscriptions — delegates bound to destroyed objects — into the
    /// next one. <c>EventsRegistry.ResetStaticState</c> already drops the rest of the package's static
    /// state at <c>SubsystemRegistration</c>; this is the publisher's half of the same job, and doing
    /// it unconditionally keeps it correct whatever that project setting says.</para>
    /// <para>It runs at <see cref="PlayModeStateChange.EnteredEditMode"/> rather than
    /// <see cref="PlayModeStateChange.ExitingPlayMode"/>, which is the point teardown has finished: an
    /// <c>OnDestroy</c> that unsubscribes, or that publishes a shutdown event, still sees the
    /// subscribers it expects. The cost is that <b>List Current Subscribers</b> reports nothing once
    /// play mode has ended — inspect a subscription leak while still in play mode.</para>
    /// <para>This clears subscriptions, registered names and retained values on every frame of the
    /// publisher stack. It does not unwind the stack itself: a frame pushed and never popped survives,
    /// empty, which is harmless but is not what the code that pushed it intended.</para>
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

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EventsPublisher.Instance.Clear();
            }
        }
    }
}
