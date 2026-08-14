using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Declares the payload type an enum member carries, so that a typed identity can be resolved for
    /// it and a publish of the wrong payload is reported.
    /// </summary>
    /// <remarks>
    /// <para>This is the single declaration of an event's payload. It sits next to the member, the way
    /// <see cref="EventDeliveryAttribute"/> does, and is read once at facade construction rather than
    /// per publish.</para>
    /// <para>Declaring it is what lets the publisher check an <em>untyped</em> publish — a raw string,
    /// or an Inspector-authored <see cref="EventRef"/> — against what the event is supposed to carry.
    /// Without a declaration the publisher has nothing to compare against and the payload stays
    /// unchecked, which is the pre-Stage-3 behaviour and remains the default.</para>
    /// <para>An event with no payload is left unannotated. Annotating one as <c>typeof(void)</c> is not
    /// supported; use <c>typeof(object)</c> if a payload is genuinely untyped, which declares "anything
    /// goes" explicitly rather than by omission.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [EventEnum]
    /// public enum TempleRunEvents
    /// {
    ///     [EventPayload(typeof(PlayerFailedData))]
    ///     [EventDelivery(EventDelivery.Sticky)]
    ///     PlayerFailed = 5,
    ///
    ///     PlayerJumped = 6,   // no payload
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class EventPayloadAttribute : Attribute
    {
        /// <summary>The type of the <c>data</c> argument this event carries.</summary>
        public Type PayloadType { get; }

        /// <summary>Declares the payload type for the annotated enum member.</summary>
        public EventPayloadAttribute(Type payloadType)
        {
            PayloadType = payloadType;
        }
    }
}
