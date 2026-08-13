using System;
using System.Collections.Generic;
using System.Linq;

namespace CrawfisSoftware.Events
{
    internal class EventsPublisherInternal : IEventsPublisher<string>, IEventIdPublisher
    {
        // Define the events that occur in the game
        // Keyed on the interned EventId rather than the name: an int compare per lookup instead of a
        // string hash, and the name is still what every callback receives.
        private readonly Dictionary<EventId, Action<string, object, object>> events = new Dictionary<EventId, Action<string, object, object>>();
        private readonly List<Action<string, object, object>> allSubscribers = new List<Action<string, object, object>>();
        private Queue<(string eventName, Action<string, object, object> callback, object sender, object data)> _callbackQueue
            = new Queue<(string eventName, Action<string, object, object> callback, object sender, object data)>();

        // Guards against a callback that publishes an event, or subscribes to one, starting a second
        // nested drain of the shared queue. Nested work enqueues and returns; the outermost drain
        // processes it in order.
        private bool _isDraining;

        // Retained values, per policy. These are per-frame by design: the policy registry is
        // stack-global, but what was actually published lives in the frame it was published into, so
        // Pop() discards it.
        private readonly Dictionary<EventId, RetainedValue> _stickyValues = new Dictionary<EventId, RetainedValue>();
        private readonly Dictionary<EventId, List<RetainedValue>> _journals = new Dictionary<EventId, List<RetainedValue>>();

        /// <summary>A published <c>(sender, data)</c> pair held for later delivery.</summary>
        private readonly struct RetainedValue
        {
            public readonly object Sender;
            public readonly object Data;
            public RetainedValue(object sender, object data)
            {
                Sender = sender;
                Data = data;
            }
        }

        public void RegisterEvent(string eventName)
        {
            RegisterEvent(EventsRegistry.Intern(eventName));
        }

        /// <inheritdoc/>
        public void RegisterEvent(EventId eventId)
        {
            if (!eventId.IsValid) return;
            if (!events.ContainsKey(eventId))
            {
                events.Add(eventId, NullCallback);
            }
        }
        /// <inheritdoc/>
        public void RegisterEvent(string eventName, EventDelivery delivery)
        {
            EventsRegistry.DeclarePolicy(eventName, delivery);
            RegisterEvent(eventName);
        }

        public void SubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            SubscribeToEvent(EventsRegistry.Intern(eventName), callback);
        }

        /// <inheritdoc/>
        public void SubscribeToEvent(EventId eventId, Action<string, object, object> callback)
        {
            if (!eventId.IsValid || callback == null) return;
            RegisterEvent(eventId);
            events[eventId] += callback;
            ReplayTo(eventId, callback);
        }

        /// <summary>
        /// Delivers whatever this frame has retained for <paramref name="eventName"/> to a subscriber
        /// that has just arrived.
        /// </summary>
        /// <remarks>
        /// <para>Replay is immediate rather than deferred, so a subscriber knows the current state by
        /// the time <c>Subscribe</c> returns. It fires exactly when the subscriber chose to subscribe,
        /// which is a moment the subscriber controls — subscribing at the end of <c>Awake</c>, in
        /// <c>OnEnable</c>, or in <c>Start</c> is sufficient to be fully constructed first.</para>
        /// <para>It is routed through the same queue as a publish, so a <c>Subscribe</c> made from
        /// inside a handler — while a drain is already in progress — enqueues and runs in order rather
        /// than nesting.</para>
        /// </remarks>
        private void ReplayTo(EventId eventId, Action<string, object, object> callback)
        {
            string eventName = eventId.Name;
            switch (EventsRegistry.GetPolicy(eventName))
            {
                case EventDelivery.Sticky:
                    if (_stickyValues.TryGetValue(eventId, out RetainedValue retained))
                        _callbackQueue.Enqueue((eventName, callback, retained.Sender, retained.Data));
                    break;

                case EventDelivery.Replay:
                    if (_journals.TryGetValue(eventId, out List<RetainedValue> journal))
                        for (int i = 0; i < journal.Count; i++)
                            _callbackQueue.Enqueue((eventName, callback, journal[i].Sender, journal[i].Data));
                    break;

                default:
                    return; // Transient retains nothing.
            }
            Drain();
        }

        /// <summary>
        /// Records a published value according to the event's delivery policy.
        /// </summary>
        private void Retain(EventId eventId, string eventName, object sender, object data)
        {
            switch (EventsRegistry.GetPolicy(eventName))
            {
                case EventDelivery.Sticky:
                    _stickyValues[eventId] = new RetainedValue(sender, data);
                    break;

                case EventDelivery.Replay:
                    if (!_journals.TryGetValue(eventId, out List<RetainedValue> journal))
                    {
                        journal = new List<RetainedValue>();
                        _journals[eventId] = journal;
                    }
                    journal.Add(new RetainedValue(sender, data));
                    // Reported, never truncated: a silent cap would read as "everything was replayed".
                    if (journal.Count == EventsRegistry.JournalWarningThreshold)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"EventsPublisher: the Replay journal for '{eventName}' has reached " +
                            $"{EventsRegistry.JournalWarningThreshold} entries and keeps growing. Scope it by " +
                            "publishing into a pushed publisher frame that is popped when its scene unloads.");
                    }
                    break;
            }
        }

        /// <summary>
        /// Returns the most recently retained value for <paramref name="eventName"/>, if this frame has one.
        /// </summary>
        public bool TryGetLast(string eventName, out object sender, out object data)
        {
            return TryGetLast(EventsRegistry.Intern(eventName), out sender, out data);
        }

        /// <inheritdoc/>
        public bool TryGetLast(EventId eventId, out object sender, out object data)
        {
            if (eventId.IsValid)
            {
                if (_stickyValues.TryGetValue(eventId, out RetainedValue retained))
                {
                    sender = retained.Sender;
                    data = retained.Data;
                    return true;
                }
                if (_journals.TryGetValue(eventId, out List<RetainedValue> journal) && journal.Count > 0)
                {
                    sender = journal[journal.Count - 1].Sender;
                    data = journal[journal.Count - 1].Data;
                    return true;
                }
            }
            sender = null;
            data = null;
            return false;
        }

        public void UnsubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            UnsubscribeToEvent(EventsRegistry.Intern(eventName), callback);
        }

        /// <inheritdoc/>
        public void UnsubscribeToEvent(EventId eventId, Action<string, object, object> callback)
        {
            if (!eventId.IsValid || callback == null) return;
            if (events.ContainsKey(eventId))
                events[eventId] -= callback;
        }

        /// <summary>
        /// Returns true if <paramref name="eventName"/> has been registered with this publisher.
        /// </summary>
        internal bool IsEventRegistered(string eventName)
        {
            EventId eventId = EventsRegistry.Intern(eventName);
            return eventId.IsValid && events.ContainsKey(eventId);
        }

        public void SubscribeToAllEvents(Action<string, object, object> callback)
        {
            allSubscribers.Add(callback);
        }

        public void UnsubscribeToAllEvents(Action<string, object, object> callback)
        {
            allSubscribers.Remove(callback);
        }

        public void PublishEvent(string eventName, object sender, object data)
        {
            PublishEvent(EventsRegistry.Intern(eventName), sender, data, retain: true);
        }

        /// <inheritdoc/>
        public void PublishEvent(EventId eventId, object sender, object data)
        {
            PublishEvent(eventId, sender, data, retain: true);
        }

        /// <summary>
        /// Dispatches an event, optionally recording it under this frame's delivery policy.
        /// </summary>
        /// <remarks>The stack dispatches every publish to every frame so that subscribers on lower
        /// frames still hear it, but retains only on the frame that was top at publish time. Retaining
        /// on every frame would mean <c>Pop()</c> discarded nothing, since the value would also be
        /// sitting in the frames underneath.</remarks>
        internal void PublishEvent(EventId eventId, object sender, object data, bool retain)
        {
            if (!eventId.IsValid) return;

            string eventName = eventId.Name;
            if (retain) Retain(eventId, eventName, sender, data);

            if (events.TryGetValue(eventId, out Action<string, object, object> eventDelegate))
            {
                var callbacks = eventDelegate.GetInvocationList();

                // Queue up each callback. This ensures that if a callback publishes an event, that the
                // other callbacks for *this* event are called before the newly published event's callbacks.
                foreach (var callback in callbacks)
                    _callbackQueue.Enqueue((eventName, (Action<string, object, object>)callback, sender, data));
            }
            foreach (var handler in allSubscribers)
                _callbackQueue.Enqueue((eventName, handler, sender, data));

            Drain();
        }

        /// <summary>
        /// Invokes queued callbacks until the queue is empty.
        /// </summary>
        /// <remarks>Nested work — a callback that publishes an event, or subscribes to one and
        /// triggers a replay — only enqueues. The outermost call owns the drain, so the remaining
        /// callbacks for the <em>current</em> event run first, as intended.</remarks>
        private void Drain()
        {
            if (_isDraining) return;

            _isDraining = true;
            try
            {
                while (_callbackQueue.Count > 0)
                {
                    var message = _callbackQueue.Dequeue();
                    Action<string, object, object> callback = message.callback;
                    try
                    {
                        callback(message.eventName, message.sender, message.data);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(
                            $"Exception publishing {message.eventName} to {callback.Target}: {e}");
                    }
                }
            }
            finally
            {
                // Never leave stale callbacks queued; they would otherwise flush during an unrelated publish.
                _callbackQueue.Clear();
                _isDraining = false;
            }
        }

        public IEnumerable<string> GetRegisteredEvents()
        {
            foreach (EventId eventId in events.Keys) yield return eventId.Name;
        }

        public IEnumerable<(string eventName, string typeName)> GetSubscribers()
        {
            foreach (var eventId in events.Keys)
            {
                if (events.TryGetValue(eventId, out var eventDelegate) && eventDelegate != null)
                {
                    foreach (var handler in eventDelegate.GetInvocationList().Skip(1))
                    {
                        yield return (eventId.Name, handler.Method.DeclaringType.ToString());
                    }
                }
            }
            foreach (var handler in allSubscribers)
                yield return ("all events", handler.Method.DeclaringType.ToString());
        }

        private void NullCallback(string eventName, object sender, object data)
        {
        }

        public void Clear()
        {
            events.Clear();
            allSubscribers.Clear();
            _stickyValues.Clear();
            _journals.Clear();
        }
    }
}