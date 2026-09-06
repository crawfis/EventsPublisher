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

        // Depth of re-entrant Drain calls. A publish made from inside a callback drains the shared
        // queue itself, re-entrantly, so the published event is fully delivered before the publishing
        // statement returns — the contract auto-chained events are built on. The depth is tracked so
        // that a subscribe-triggered replay stays deferred while a drain is in flight, and so that only
        // the outermost drain performs the escaped-exception cleanup in Drain's finally.
        private int _drainDepth;

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
                    // Transient retains nothing, so a subscriber arriving after this event has already
                    // been published hears nothing. That is the symptom the bool mirrors exist to work
                    // around, and the migration wants it counted rather than guessed at.
                    EventsDiagnostics.NoteTransientSubscribe(eventId);
                    return;
            }
            // Unlike a nested publish, a replay stays deferred while a drain is in flight: it is
            // delivered after the callbacks already queued, not inside the subscriber's Subscribe
            // call. Decided when replay moved from end-of-frame to immediate, and pinned by
            // SubscribeFromInsideAHandler_EnqueuesTheReplayRatherThanNesting.
            if (_drainDepth == 0) Drain();
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
        /// <remarks>
        /// <para>Two guarantees hold together here. Callbacks run in FIFO order off one shared queue,
        /// so the remaining callbacks for the <em>current</em> event run before any newly published
        /// event's callbacks. And a publish drains re-entrantly, so by the time <c>PublishEvent</c>
        /// returns — at any nesting depth — everything it enqueued has been delivered. Auto-chained
        /// events depend on the second guarantee: a producer that publishes an event whose subscribers
        /// derive state, then publishes a second event whose subscribers consume that state, needs the
        /// first chain complete before the second publish is made.</para>
        /// <para>A consequence of holding both at once: a nested publish also flushes the current
        /// event's still-queued callbacks, since they sit ahead of the nested event's in the queue.
        /// They could not run any later without breaking one of the two guarantees.</para>
        /// <para>Subscribe-triggered replay is the one caller that stays deferred while a drain is in
        /// flight — see <see cref="ReplayTo"/>.</para>
        /// </remarks>
        private void Drain()
        {
            _drainDepth++;
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
                        try
                        {
                            UnityEngine.Debug.LogError(
                                $"Exception publishing {message.eventName} to {callback.Target}: {e}");
                        }
                        catch (Exception loggingFailure)
                        {
                            // Formatting the message runs ToString on the handler's target and on the
                            // exception, either of which can itself throw. A log line must never abort
                            // the drain and discard the callbacks still queued.
                            UnityEngine.Debug.LogError(
                                $"Exception publishing {message.eventName}; the details could not be formatted " +
                                $"({loggingFailure.GetType().Name} thrown while logging {e.GetType().Name}).");
                        }
                    }
                }
            }
            finally
            {
                _drainDepth--;
                // Nothing pends here on any non-fatal path: the loop runs to empty, handler exceptions
                // are contained above, and an exception escaping a nested drain is caught by the level
                // that invoked the publishing handler. If a process-level exception still escapes the
                // outermost drain, discard rather than leave entries to flush during an unrelated
                // later publish.
                if (_drainDepth == 0) _callbackQueue.Clear();
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

        /// <summary>
        /// Drops what a play session accumulated — subscriptions, "all events" subscribers, retained
        /// values, anything still queued — and keeps the registrations.
        /// </summary>
        /// <remarks>A registration is a declaration: the event exists. The keys stay so that
        /// <see cref="EventsPublisher.StrictMode"/> still knows the name and the editor menu still lists
        /// it; only the delegates behind them go. <see cref="Clear"/> is the stronger operation that
        /// drops the registrations too.</remarks>
        internal void DropSessionState()
        {
            var registered = new List<EventId>(events.Keys);
            foreach (EventId eventId in registered) events[eventId] = NullCallback;
            allSubscribers.Clear();
            _stickyValues.Clear();
            _journals.Clear();
            _callbackQueue.Clear();
            _drainDepth = 0;
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
