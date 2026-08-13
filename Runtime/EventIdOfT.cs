using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// An <see cref="EventId"/> that also carries the type of the payload the event delivers, so that
    /// publishing and subscribing through it are checked by the compiler.
    /// </summary>
    /// <remarks>
    /// <para><b>What this does and does not guarantee.</b> Once a call site holds an
    /// <see cref="EventId{TData}"/>, publishing the wrong payload and writing a handler with the wrong
    /// parameter type are both compile errors — there is no cast to get wrong. That covers every call
    /// site except one: the place the type argument is first written, in <see cref="Of"/> or
    /// <see cref="EventsFor{T}.Id{TData}"/>. That single point is checked at runtime against the
    /// <see cref="EventPayloadAttribute"/> declaration, so getting it wrong is a startup error naming
    /// both types rather than an <see cref="InvalidCastException"/> inside a handler much later.</para>
    /// <para>Declare the type once, in a <c>static readonly</c> field, and use that field everywhere:
    /// the runtime-checked point is then hit once at type initialization, and everything downstream of
    /// it is the compiler's problem.</para>
    /// <para>Erasure is preserved. Subscribers registered through the untyped API still receive
    /// <see cref="object"/>, so the global logger, <c>EventHistory</c> and the editor menus see every
    /// typed event without changing.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// private static readonly EventId&lt;PlayerFailedData&gt; Failed =
    ///     EventsFor&lt;TempleRunEvents&gt;.Id&lt;PlayerFailedData&gt;(TempleRunEvents.PlayerFailed);
    ///
    /// void OnEnable()  =&gt; Failed.Subscribe(OnPlayerFailed);
    /// void OnDisable() =&gt; Failed.Unsubscribe(OnPlayerFailed);
    ///
    /// // data is PlayerFailedData. No cast, and a wrong parameter type will not compile.
    /// private void OnPlayerFailed(string eventName, object sender, PlayerFailedData data) { }
    /// </code>
    /// </example>
    /// <typeparam name="TData">The type of the <c>data</c> argument this event carries.</typeparam>
    public readonly struct EventId<TData> : IEquatable<EventId<TData>>
    {
        private readonly EventId _id;

        internal EventId(EventId id)
        {
            _id = id;
        }

        /// <summary>The erased identity, for the untyped API and for the observer channel.</summary>
        public EventId Untyped => _id;

        /// <summary>True when this refers to an interned event name.</summary>
        public bool IsValid => _id.IsValid;

        /// <summary>The projected event name. Display and erased observers only — never the key.</summary>
        public string Name => _id.Name;

        /// <summary>
        /// Resolves an event name to a typed identity, declaring <typeparamref name="TData"/> as its
        /// payload type.
        /// </summary>
        /// <remarks>The escape hatch for names no enum can annotate. For an enum family, prefer
        /// <see cref="EventsFor{T}.Id{TData}"/>, which reads the type from
        /// <see cref="EventPayloadAttribute"/> and so keeps the declaration next to the event.
        /// Either way, the first declaration wins and a differing one is reported.</remarks>
        public static EventId<TData> Of(string eventName)
        {
            EventId id = EventsRegistry.Intern(eventName);
            EventsRegistry.DeclarePayload(id, typeof(TData));
            return new EventId<TData>(id);
        }

        /// <summary>Widens to the erased identity, so a typed id works anywhere an untyped one does.</summary>
        public static implicit operator EventId(EventId<TData> eventId) => eventId._id;

        public bool Equals(EventId<TData> other) => _id.Equals(other._id);
        public override bool Equals(object obj) => obj is EventId<TData> other && Equals(other);
        public override int GetHashCode() => _id.GetHashCode();
        public override string ToString() => _id.ToString();

        public static bool operator ==(EventId<TData> left, EventId<TData> right) => left._id == right._id;
        public static bool operator !=(EventId<TData> left, EventId<TData> right) => left._id != right._id;
    }
}
