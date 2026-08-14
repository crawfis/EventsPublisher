using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// How the publisher treats an event for subscribers that arrive after it was published.
    /// </summary>
    /// <remarks>
    /// The per-event judgement is whether the event is an <em>edge</em> or a <em>level</em>. An edge is
    /// a transition — "the menu just closed" — and is only meaningful in sequence, so replaying it is
    /// wrong. A level is a state — "the menu is closed" — and is self-describing: received once, late,
    /// with no history, it still tells the whole truth. Only levels are safe to retain.
    /// </remarks>
    public enum EventDelivery
    {
        /// <summary>
        /// Fire and forget. A subscriber that arrives later does not see it. The default, and correct
        /// for edges: requests, transitions, one-shot notifications, per-frame gameplay events.
        /// </summary>
        Transient = 0,

        /// <summary>
        /// The last published <c>(sender, data)</c> is retained and delivered to a subscriber
        /// immediately on subscribe. Correct for levels — lifecycle and state events whose current
        /// value a late subscriber needs.
        /// </summary>
        Sticky = 1,

        /// <summary>
        /// Every published <c>(sender, data)</c> is retained in order and delivered to a subscriber
        /// immediately on subscribe. For accumulations a late subscriber must rebuild.
        /// </summary>
        /// <remarks>The journal grows for as long as its publisher frame lives, so this is the policy
        /// that needs scoping: push a publisher frame when the owning scene loads and pop it on unload,
        /// and the journal is discarded with the scene. Growth past
        /// <see cref="EventsRegistry.JournalWarningThreshold"/> entries is reported rather than
        /// silently truncated.</remarks>
        Replay = 2,
    }

    /// <summary>
    /// Declares the <see cref="EventDelivery"/> policy for a single enum member.
    /// </summary>
    /// <example>
    /// <code>
    /// [EventEnum]
    /// public enum TempleRunEvents
    /// {
    ///     PlayerFailingAtTurn = 12,                          // edge → Transient (the default)
    ///     [EventDelivery(EventDelivery.Sticky)] PlayerDied = 5,   // level → retained
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class EventDeliveryAttribute : Attribute
    {
        /// <summary>The declared delivery policy.</summary>
        public EventDelivery Delivery { get; }

        /// <summary>Declares the delivery policy for the annotated enum member.</summary>
        public EventDeliveryAttribute(EventDelivery delivery)
        {
            Delivery = delivery;
        }
    }
}
