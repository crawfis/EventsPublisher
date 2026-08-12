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
    }
}
