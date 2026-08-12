using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Static, enum-typed entry point to the events publisher. One closed type per enum family:
    /// <c>EventsFor&lt;GameFlowEvents&gt;.Publish(GameFlowEvents.GameStarting, this, null)</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> <see cref="EventsPublisherEnumsSingleton{T}"/> requires a
    /// GameObject in a scene, and that requirement is itself a source of ordering bugs under additive
    /// scene loading. Its <c>Instance</c> is assigned in <c>Awake</c>, so consumers must run later —
    /// which is what <c>[DefaultExecutionOrder(-10000)]</c> on the concrete subclasses is for. That
    /// attribute orders <c>Awake</c> calls <em>within a scene load batch</em>, and an additively-loaded
    /// scene is a separate batch, so a scene loaded before the one hosting the singleton will
    /// <c>NullReferenceException</c> on <c>Instance</c>.</para>
    /// <para>Nothing about mapping an enum onto event names needs a scene object. Initialization here
    /// is lazy and static, so the race cannot occur: any publish or subscribe goes through this type,
    /// which registers every member of <typeparamref name="T"/> on first touch. Registration therefore
    /// always precedes the first use, with no execution-order attribute and no scene setup.</para>
    /// <para>The one case lazy initialization does not cover is an event name reaching the publisher as
    /// a raw string without this facade ever being touched. Mark the enum with
    /// <see cref="EventEnumAttribute"/> to have it registered before any scene loads.</para>
    /// </remarks>
    /// <typeparam name="T">The enum type identifying this family of events.</typeparam>
    public static class EventsFor<T> where T : Enum
    {
        private static EventsPublisherEnums<T> _facade;

        private static EventsPublisherEnums<T> Facade
        {
            get
            {
                if (_facade == null)
                {
                    // Assigned before registering so that anything reached during registration which
                    // calls back into this type sees the facade rather than recursing.
                    _facade = new EventsPublisherEnums<T>(EventsPublisher.Instance);
                    EventsRegistry.AddResetHandler(Reset);
                    _facade.RegisterKnownEvents();
                }
                return _facade;
            }
        }

        /// <summary>
        /// Registers every member of <typeparamref name="T"/> if that has not happened yet.
        /// </summary>
        /// <remarks>Rarely needed directly — publishing or subscribing does this implicitly. Call it to
        /// register the family ahead of a raw-string publish, or mark the enum with
        /// <see cref="EventEnumAttribute"/> to have it called automatically before the first scene loads.</remarks>
        public static void EnsureRegistered()
        {
            _ = Facade;
        }

        private static void Reset()
        {
            _facade = null;
        }

        /// <summary>
        /// Publishes <paramref name="eventEnum"/> to its subscribers.
        /// </summary>
        public static void Publish(T eventEnum, object sender, object data)
        {
            Facade.PublishEvent(eventEnum, sender, data);
        }

        /// <summary>
        /// Subscribes <paramref name="callback"/> to a single event.
        /// </summary>
        public static void Subscribe(T eventEnum, Action<string, object, object> callback)
        {
            Facade.SubscribeToEvent(eventEnum, callback);
        }

        /// <summary>
        /// Unsubscribes <paramref name="callback"/> from a single event.
        /// </summary>
        public static void Unsubscribe(T eventEnum, Action<string, object, object> callback)
        {
            Facade.UnsubscribeToEvent(eventEnum, callback);
        }

        /// <summary>
        /// Subscribes <paramref name="callback"/> to every member of <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>This is scoped to this enum family. For a genuinely global observer — a logger or
        /// event recorder — use <see cref="IEventsPublisher{T}.SubscribeToAllEvents"/> on
        /// <see cref="EventsPublisher.Instance"/> instead, which sees every event from every family.</remarks>
        public static void SubscribeToAll(Action<string, object, object> callback)
        {
            foreach (T eventEnum in Enum.GetValues(typeof(T)))
                Facade.SubscribeToEvent(eventEnum, callback);
        }

        /// <summary>
        /// Unsubscribes <paramref name="callback"/> from every member of <typeparamref name="T"/>.
        /// </summary>
        public static void UnsubscribeFromAll(Action<string, object, object> callback)
        {
            foreach (T eventEnum in Enum.GetValues(typeof(T)))
                Facade.UnsubscribeToEvent(eventEnum, callback);
        }

        /// <summary>
        /// Gets the published event name for <paramref name="eventEnum"/>.
        /// </summary>
        public static string GetEventName(T eventEnum)
        {
            return Facade.GetEventName(eventEnum);
        }

        /// <summary>
        /// Recovers the enum value for a published event name, without allocating.
        /// </summary>
        /// <remarks>Use this in an "all events" handler instead of slicing the name on '/' and calling
        /// <see cref="Enum.Parse(Type, string)"/>, which allocates a string and does a reflection-backed
        /// lookup on every published event.</remarks>
        public static bool TryGetEnum(string eventName, out T eventEnum)
        {
            return Facade.TryGetEnum(eventName, out eventEnum);
        }
    }
}
