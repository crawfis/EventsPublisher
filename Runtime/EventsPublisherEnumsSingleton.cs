using System;

using UnityEngine;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Scene-object entry point to the events publisher for a single enum family.
    /// </summary>
    /// <remarks>
    /// <para>Prefer <see cref="EventsFor{T}"/> for new code. This type requires a GameObject in a
    /// scene and assigns <see cref="Instance"/> in <c>Awake</c>, so consumers must run later — which is
    /// what <c>[DefaultExecutionOrder(-10000)]</c> on concrete subclasses is for. That attribute orders
    /// <c>Awake</c> calls within a scene load batch, and an additively-loaded scene is a separate
    /// batch, so a scene loaded before the one hosting this singleton will
    /// <c>NullReferenceException</c> on <see cref="Instance"/>. <see cref="EventsFor{T}"/> is static
    /// and lazily initialized, so that race cannot occur.</para>
    /// <para>Every member here now forwards to <see cref="EventsFor{T}"/>, so the two paths share one
    /// facade and cannot disagree. Existing scene objects keep working unchanged during migration.</para>
    /// </remarks>
    public class EventsPublisherEnumsSingleton<T> : MonoBehaviour where T : Enum
    {
        public static EventsPublisherEnumsSingleton<T> Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy the duplicate that just awoke, not the instance already in use.
                Destroy(this);
                return;
            }
            Instance = this;
            EventsFor<T>.EnsureRegistered();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Publishes an event to all registered subscribers.
        /// </summary>
        /// <param name="eventEnum">The event identifier of type <typeparamref name="T"/> that specifies the event to be published.</param>
        /// <param name="sender">The source of the event. This parameter can be used to identify the origin of the event.</param>
        /// <param name="data">The data associated with the event. This can be any object containing information relevant to the event.</param>
        public void PublishEvent(T eventEnum, object sender, object data)
        {
            EventsFor<T>.Publish(eventEnum, sender, data);
        }

        /// <summary>
        /// Subscribes to the specified event and associates it with a callback to handle the event.
        /// </summary>
        /// <remarks>This method allows you to register a handler for a specific event. Ensure that the
        /// callback is thread-safe if the event may be triggered from multiple threads.</remarks>
        /// <param name="eventEnum">The event to subscribe to, represented as an enumeration value of type <typeparamref name="T"/>.</param>
        /// <param name="callback">The callback to invoke when the event is triggered. The callback receives three parameters: <list
        /// type="bullet"> <item><description>A <see cref="string"/> representing the event name.</description></item>
        /// <item><description>An <see cref="object"/> representing the event's primary data.</description></item>
        /// <item><description>An <see cref="object"/> representing additional context or metadata for the
        /// event.</description></item> </list></param>
        public void SubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            EventsFor<T>.Subscribe(eventEnum, callback);
        }

        /// <summary>
        /// Unsubscribes the specified callback from the event associated with the given event enumeration value.
        /// </summary>
        /// <remarks>If the specified callback is not currently subscribed to the event, this method has
        /// no effect.</remarks>
        /// <param name="eventEnum">The event enumeration value representing the event to unsubscribe from.</param>
        /// <param name="callback">The callback to be removed from the event's subscription list. This callback will no longer be invoked when
        /// the event is triggered.</param>
        public void UnsubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            EventsFor<T>.Unsubscribe(eventEnum, callback);
        }

        /// <summary>
        /// Retrieves the most recently retained value for <paramref name="eventEnum"/>, without
        /// subscribing to it. See <see cref="EventsFor{T}.TryGetLast"/>.
        /// </summary>
        public bool TryGetLast(T eventEnum, out object sender, out object data)
        {
            return EventsFor<T>.TryGetLast(eventEnum, out sender, out data);
        }

        /// <summary>
        /// Gets the published event name for <paramref name="eventEnum"/>.
        /// </summary>
        public string GetEventName(T eventEnum)
        {
            return EventsFor<T>.GetEventName(eventEnum);
        }

        /// <summary>
        /// Recovers the enum value for a published event name, without allocating.
        /// </summary>
        /// <remarks>Prefer this over slicing the name on '/' and calling <see cref="Enum.Parse{T}(string)"/>
        /// inside an "all events" handler, which allocates a string per published event.</remarks>
        public bool TryGetEnum(string eventName, out T eventEnum)
        {
            return EventsFor<T>.TryGetEnum(eventName, out eventEnum);
        }
        //private static void RegisterKnownEvents()
        //{
        //    foreach (T eventEnum in Enum.GetValues(typeof(T)))
        //    {
        //        string eventName = eventEnum.ToString();
        //        EventsPublisher.Instance.RegisterEvent(eventName);
        //    }
        //}

        /// <summary>
        /// Subscribes <paramref name="callback"/> to every member of <typeparamref name="T"/>.
        /// </summary>
        public void SubscribeToAllEnumEvents(Action<string, object, object> callback)
        {
            EventsFor<T>.SubscribeToAll(callback);
        }

        /// <summary>
        /// Unsubscribes <paramref name="callback"/> from every member of <typeparamref name="T"/>.
        /// </summary>
        public void UnsubscribeToAllEnumEvents(Action<string, object, object> callback)
        {
            EventsFor<T>.UnsubscribeFromAll(callback);
        }
    }
}