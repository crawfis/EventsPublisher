using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Records events that were published before something subscribed to them, so that the
    /// <see cref="EventDelivery.Sticky"/> candidates in a project can be read off observed behaviour
    /// rather than guessed at.
    /// </summary>
    /// <remarks>
    /// <para>This exists for the migration. Deciding edge-versus-level for every event in a project is
    /// the expensive part of adopting delivery policies, and most of it is guesswork until something
    /// says which events actually had late subscribers. A late subscriber to a
    /// <see cref="EventDelivery.Transient"/> event is precisely the symptom that the hand-maintained
    /// <c>bool</c> mirrors exist to work around.</para>
    /// <para><b>It is a heuristic, not a proof.</b> It reports that a subscribe happened after a
    /// publish, which is evidence of a missed delivery but not of one that mattered — an event may be
    /// genuinely transient and its late subscriber genuinely uninterested in the earlier occurrence. A
    /// resubscribe after an unsubscribe counts as late, too. Read the output as a list of events worth
    /// examining, ranked by how often it happened.</para>
    /// <para>Off outside the editor and development builds, and the recording is skipped entirely when
    /// <see cref="Enabled"/> is false, so it costs a bool test per publish in a shipped game.</para>
    /// </remarks>
    public static class EventsDiagnostics
    {
        /// <summary>
        /// Whether late deliveries are recorded. Defaults to on in the editor and development builds.
        /// </summary>
        public static bool Enabled { get; set; } =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>What was observed for one event.</summary>
        public readonly struct Observation
        {
            /// <summary>The event this describes.</summary>
            public readonly EventId EventId;

            /// <summary>How many times it was published.</summary>
            public readonly int Publishes;

            /// <summary>
            /// How many subscribes arrived after it had already been published and were delivered
            /// nothing, because its policy retains nothing.
            /// </summary>
            public readonly int LateSubscribes;

            internal Observation(EventId eventId, int publishes, int lateSubscribes)
            {
                EventId = eventId;
                Publishes = publishes;
                LateSubscribes = lateSubscribes;
            }

            /// <summary>The event name, for display.</summary>
            public string Name => EventId.Name;
        }

        private sealed class Counts
        {
            public int Publishes;
            public int LateSubscribes;
        }

        private static readonly Dictionary<EventId, Counts> _counts = new Dictionary<EventId, Counts>();

        /// <summary>Notes a publish. Called once per publish, not once per publisher frame.</summary>
        internal static void NotePublish(EventId eventId)
        {
            if (!Enabled || !eventId.IsValid) return;
            Get(eventId).Publishes++;
        }

        /// <summary>
        /// Notes a subscribe to an event that retains nothing, which is a missed delivery only if that
        /// event has already been published.
        /// </summary>
        internal static void NoteTransientSubscribe(EventId eventId)
        {
            if (!Enabled || !eventId.IsValid) return;
            if (!_counts.TryGetValue(eventId, out Counts counts) || counts.Publishes == 0) return;
            counts.LateSubscribes++;
        }

        private static Counts Get(EventId eventId)
        {
            if (!_counts.TryGetValue(eventId, out Counts counts))
            {
                counts = new Counts();
                _counts[eventId] = counts;
            }
            return counts;
        }

        /// <summary>
        /// Every event that had at least one late subscriber, most affected first.
        /// </summary>
        /// <remarks>These are the events to examine for <see cref="EventDelivery.Sticky"/>. An event
        /// here is one whose subscribers are demonstrably arriving too late to hear it.</remarks>
        public static List<Observation> GetLateDeliveries()
        {
            var results = new List<Observation>();
            foreach (KeyValuePair<EventId, Counts> entry in _counts)
            {
                if (entry.Value.LateSubscribes > 0)
                    results.Add(new Observation(entry.Key, entry.Value.Publishes, entry.Value.LateSubscribes));
            }
            results.Sort((a, b) => b.LateSubscribes.CompareTo(a.LateSubscribes));
            return results;
        }

        /// <summary>Everything observed, including events with no late subscriber.</summary>
        public static List<Observation> GetAll()
        {
            var results = new List<Observation>();
            foreach (KeyValuePair<EventId, Counts> entry in _counts)
                results.Add(new Observation(entry.Key, entry.Value.Publishes, entry.Value.LateSubscribes));
            results.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return results;
        }

        /// <summary>Discards everything recorded so far.</summary>
        /// <remarks>Call before a run whose behaviour you want to read in isolation — the interesting
        /// measurement is usually one boot sequence, not an editor session's accumulated noise.</remarks>
        public static void Reset()
        {
            _counts.Clear();
        }
    }
}
