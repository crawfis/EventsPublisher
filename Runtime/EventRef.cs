using System;

using UnityEngine;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// A serializable reference to a published event, authored in the Inspector as a dropdown rather
    /// than typed as a free-text string.
    /// </summary>
    /// <remarks>
    /// <para>Scene data has no compile step, so a <c>[SerializeField] string</c> event name has no check
    /// of any kind — a typo survives to runtime and the event silently reaches nobody. This carries the
    /// projected name but restricts authoring to names that actually exist.</para>
    /// <para>The stored value is the projected name (<c>"TypeName/MemberName"</c>) rather than the type
    /// and member separately, so it is the same string the publisher keys on and the same string already
    /// baked into existing scenes and prefabs.</para>
    /// <para>The dropdown is populated from enums marked <see cref="EventEnumAttribute"/>. A value that
    /// no longer matches any known event is shown as missing rather than silently cleared.</para>
    /// </remarks>
    [Serializable]
    public struct EventRef : IEquatable<EventRef>
    {
        [SerializeField] private string _eventName;

        /// <summary>Creates a reference to an already-projected event name.</summary>
        public EventRef(string eventName)
        {
            _eventName = eventName;
        }

        /// <summary>The projected event name, or <see langword="null"/>/empty when unset.</summary>
        public string Name => _eventName;

        /// <summary>True when no event has been chosen.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(_eventName);

        /// <summary>Creates a reference from an enum member, using the same projection the publisher uses.</summary>
        public static EventRef For<T>(T eventEnum) where T : Enum
        {
            return new EventRef(EventsFor<T>.GetEventName(eventEnum));
        }

        /// <summary>
        /// Recovers the enum value this reference points at, when it belongs to <typeparamref name="T"/>.
        /// </summary>
        public bool TryGetEnum<T>(out T eventEnum) where T : Enum
        {
            return EventsFor<T>.TryGetEnum(_eventName, out eventEnum);
        }

        public bool Equals(EventRef other) => string.Equals(_eventName, other._eventName, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is EventRef other && Equals(other);
        public override int GetHashCode() => _eventName == null ? 0 : _eventName.GetHashCode();
        public override string ToString() => _eventName ?? string.Empty;
    }

    /// <summary>
    /// Turns a <see cref="string"/> field holding an event name into the same Inspector dropdown that
    /// <see cref="EventRef"/> uses, without changing how the field is serialized.
    /// </summary>
    /// <remarks>
    /// <para>Prefer this over converting an existing <c>[SerializeField] string</c> to
    /// <see cref="EventRef"/>. Changing a field's type changes its serialized shape, so Unity cannot
    /// carry the old value across and every name already baked into a <c>.unity</c> or <c>.prefab</c>
    /// asset would be lost and have to be re-picked by hand. Adding this attribute leaves the field a
    /// string, so existing values keep working and only the authoring experience changes.</para>
    /// <para>Use <see cref="EventRef"/> for new fields, where there is nothing to migrate.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [EventName] [SerializeField] private string _eventToFire;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class EventNameAttribute : PropertyAttribute
    {
    }
}
