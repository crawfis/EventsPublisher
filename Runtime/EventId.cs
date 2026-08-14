using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// An interned handle for an event name: the identity the publisher keys on, with the string
    /// available as a projection off it.
    /// </summary>
    /// <remarks>
    /// <para>This is the shape the whole design has been moving toward — the string becomes
    /// <see cref="Name"/>, derived and used for display and for the erased observer channel, while the
    /// thing the dictionaries key on is a value the compiler hands you.</para>
    /// <para>Resolving a name to an <see cref="EventId"/> costs one dictionary lookup, and every publish
    /// or subscribe through that id afterwards costs none. <see cref="EventsFor{T}"/> resolves each
    /// member of an enum family once at construction, so the enum path never hashes a string again.</para>
    /// <para>A default <see cref="EventId"/> is deliberately not a valid handle, so a field nobody
    /// assigned cannot silently alias the first registered event.</para>
    /// </remarks>
    public readonly struct EventId : IEquatable<EventId>
    {
        // One-based, so default(EventId) is 0 and therefore invalid rather than aliasing index 0.
        private readonly int _handle;

        internal EventId(int handle)
        {
            _handle = handle;
        }

        internal int Handle => _handle;

        /// <summary>True when this refers to an interned event name.</summary>
        public bool IsValid => _handle != 0;

        /// <summary>
        /// The projected event name. Display, serialization and erased observers only — never the key.
        /// </summary>
        public string Name => EventsRegistry.GetInternedName(this);

        /// <summary>Resolves an event name to its handle, interning it on first use.</summary>
        public static EventId Of(string eventName) => EventsRegistry.Intern(eventName);

        public bool Equals(EventId other) => _handle == other._handle;
        public override bool Equals(object obj) => obj is EventId other && Equals(other);
        public override int GetHashCode() => _handle;
        public override string ToString() => Name ?? "<unset>";

        public static bool operator ==(EventId left, EventId right) => left._handle == right._handle;
        public static bool operator !=(EventId left, EventId right) => left._handle != right._handle;
    }

    /// <summary>
    /// The <see cref="EventId"/> entry points on a publisher, kept off <see cref="IEventsPublisher{T}"/>
    /// so that adding them breaks no existing implementer.
    /// </summary>
    /// <remarks>Callers that hold a resolved id use these to skip the name lookup entirely. Everything
    /// keeps working without them — the string-keyed API resolves the id itself.</remarks>
    public interface IEventIdPublisher
    {
        /// <summary>Publishes a pre-resolved event.</summary>
        void PublishEvent(EventId eventId, object sender, object data);

        /// <summary>Subscribes to a pre-resolved event, receiving any retained value immediately.</summary>
        void SubscribeToEvent(EventId eventId, Action<string, object, object> callback);

        /// <summary>Unsubscribes from a pre-resolved event.</summary>
        void UnsubscribeToEvent(EventId eventId, Action<string, object, object> callback);

        /// <summary>Registers a pre-resolved event.</summary>
        void RegisterEvent(EventId eventId);

        /// <summary>Reads the retained value for a pre-resolved event, without subscribing.</summary>
        bool TryGetLast(EventId eventId, out object sender, out object data);
    }
}
