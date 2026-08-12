using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Marks an enum whose members name published events, so that they are registered before any
    /// scene loads.
    /// </summary>
    /// <remarks>
    /// <para>Registration normally happens the first time <see cref="EventsFor{T}"/> is touched, which
    /// is inherently early enough for anything published or subscribed through that facade. This
    /// attribute covers the remaining case: an event name reaching the publisher as a raw string —
    /// from a serialized Inspector field, for example — without the facade for its enum ever having
    /// been used. Without it, such a publish is unregistered and
    /// <see cref="EventsPublisher.StrictMode"/> reports it.</para>
    /// <para>Annotated enums are swept at
    /// <see cref="UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad"/>, which runs after
    /// assemblies load and before the first scene's <c>Awake</c> — and therefore before every
    /// additively-loaded scene's <c>Awake</c>.</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
    public sealed class EventEnumAttribute : Attribute
    {
        /// <summary>
        /// Overrides the event-name prefix, which otherwise defaults to the enum's simple type name.
        /// </summary>
        /// <remarks>
        /// <para>Event names are projected from <see cref="Type.Name"/>, not <see cref="Type.FullName"/>,
        /// so two enums with the same simple name in different namespaces would silently share every
        /// event. Setting an explicit prefix on one of them resolves that without renaming the type and
        /// without disturbing any name already baked into a scene or prefab.</para>
        /// <para>Changing this on an enum already in use renames every one of its events, which breaks
        /// any name that has been serialized. Choose it when the enum is introduced.</para>
        /// </remarks>
        public string Prefix { get; set; }
    }
}
