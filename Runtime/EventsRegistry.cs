using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Non-generic home for state that must be shared across every <see cref="EventsFor{T}"/>, and the
    /// entry points that run before any scene loads.
    /// </summary>
    /// <remarks>
    /// This type is deliberately non-generic. A static field declared inside a generic type exists
    /// once per constructed type — <c>Foo&lt;A&gt;</c> and <c>Foo&lt;B&gt;</c> get separate copies — so
    /// anything that must compare one enum family against another cannot live there.
    /// </remarks>
    public static class EventsRegistry
    {
        // Which enum type has claimed a given "TypeName/" prefix, across all enum families.
        private static readonly Dictionary<string, Type> _claimedPrefixes = new Dictionary<string, Type>();

        // Reset callbacks published by each EventsFor<T> that has been initialized.
        private static readonly List<Action> _resetHandlers = new List<Action>();

        // Delivery policy per event name. Deliberately stack-global: RegisterEvent reaches only the top
        // publisher frame, but PublishEvent visits every frame, so a per-frame policy would mean a
        // pushed frame knew nothing about how to treat the events published into it. The retained
        // values themselves stay per-frame, so Pop() still discards them.
        private static readonly Dictionary<string, EventDelivery> _policies = new Dictionary<string, EventDelivery>();

        // The intern table: event name <-> EventId handle. Handles are one-based so that
        // default(EventId) is 0 and therefore invalid.
        private static readonly Dictionary<string, int> _handlesByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<string> _namesByHandle = new List<string>();

        // Declared payload type per event. Keyed on the id rather than the name because this is read on
        // the publish path under StrictMode, and the id is already resolved by then.
        private static readonly Dictionary<EventId, Type> _payloadTypes = new Dictionary<EventId, Type>();

        /// <summary>
        /// Resolves an event name to its <see cref="EventId"/>, interning it on first use.
        /// </summary>
        /// <remarks>Costs one dictionary lookup. Publishing or subscribing through the returned id
        /// costs none, which is the point: <see cref="EventsFor{T}"/> resolves each member of an enum
        /// family once and never hashes a name again.</remarks>
        public static EventId Intern(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return default;
            if (_handlesByName.TryGetValue(eventName, out int handle)) return new EventId(handle);

            _namesByHandle.Add(eventName);
            handle = _namesByHandle.Count;              // one-based
            _handlesByName[eventName] = handle;
            return new EventId(handle);
        }

        /// <summary>Gets the name an <see cref="EventId"/> was interned from.</summary>
        internal static string GetInternedName(EventId eventId)
        {
            int handle = eventId.Handle;
            return handle == 0 ? null : _namesByHandle[handle - 1];
        }

        /// <summary>
        /// Number of retained entries after which a <see cref="EventDelivery.Replay"/> journal is
        /// reported as growing without bound. Reported, never truncated — a silent cap would read as
        /// "everything was replayed" when it was not.
        /// </summary>
        public const int JournalWarningThreshold = 1000;

        /// <summary>
        /// Declares the delivery policy for an event name.
        /// </summary>
        /// <remarks>
        /// <para>First declaration wins; a second, differing one is reported and ignored.</para>
        /// <para>Only an outright declaration — an <see cref="EventDeliveryAttribute"/> or the
        /// <c>RegisterEvent(name, delivery)</c> overload — records anything here. Registering a name
        /// without a policy, as <c>SubscribeToEvent</c> does on the fly, records nothing and leaves
        /// <see cref="GetPolicy"/> falling through to its <see cref="EventDelivery.Transient"/>
        /// default. That is what stops an early subscriber from pinning an event to Transient and
        /// silently turning a later <c>[EventDelivery(Sticky)]</c> into a no-op.</para>
        /// <para>Note this makes a declaration ordering-sensitive against publishes, not against
        /// subscribes: a policy declared <em>after</em> an event has already been published does not
        /// retroactively retain the value that was published under the old policy.</para>
        /// </remarks>
        /// <param name="eventName">The projected event name.</param>
        /// <param name="delivery">The policy to apply.</param>
        internal static void DeclarePolicy(string eventName, EventDelivery delivery)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            if (!_policies.TryGetValue(eventName, out EventDelivery existing))
            {
                _policies[eventName] = delivery;
                return;
            }

            if (existing != delivery)
            {
                Debug.LogError(
                    $"EventsRegistry: '{eventName}' is already declared as {existing} and cannot be " +
                    $"redeclared as {delivery}. The first declaration is kept.");
            }
        }

        /// <summary>
        /// Gets the delivery policy for an event name, defaulting to
        /// <see cref="EventDelivery.Transient"/>.
        /// </summary>
        public static EventDelivery GetPolicy(string eventName)
        {
            if (!string.IsNullOrEmpty(eventName) && _policies.TryGetValue(eventName, out EventDelivery policy))
                return policy;
            return EventDelivery.Transient;
        }

        /// <summary>
        /// Declares the payload type an event carries.
        /// </summary>
        /// <remarks>
        /// <para>First declaration wins; a second, differing one is reported and ignored. That report is
        /// the point of this table — two call sites disagreeing about what an event carries is the one
        /// failure a typed identity cannot turn into a compile error, so it is turned into a loud error
        /// at the moment the second one is minted instead.</para>
        /// <para>Declaring <c>typeof(object)</c> means "anything goes" and disables the publish-time
        /// check for that event, which is what an event with a genuinely untyped payload wants.</para>
        /// </remarks>
        internal static void DeclarePayload(EventId eventId, Type payloadType)
        {
            if (!eventId.IsValid || payloadType == null) return;

            if (!_payloadTypes.TryGetValue(eventId, out Type existing))
            {
                _payloadTypes[eventId] = payloadType;
                return;
            }

            if (existing != payloadType)
            {
                Debug.LogError(
                    $"EventsRegistry: '{eventId.Name}' is already declared to carry {existing.FullName} and " +
                    $"cannot be redeclared as {payloadType.FullName}. The first declaration is kept, so the " +
                    "second call site will see payloads it cannot use.");
            }
        }

        /// <summary>
        /// Gets the declared payload type for an event, or <see langword="null"/> when nothing declared one.
        /// </summary>
        /// <remarks>Null means unchecked, not "carries nothing" — an event with no
        /// <see cref="EventPayloadAttribute"/> and no typed identity behaves exactly as it did before
        /// payloads could be declared.</remarks>
        public static Type GetPayloadType(EventId eventId)
        {
            return _payloadTypes.TryGetValue(eventId, out Type payloadType) ? payloadType : null;
        }

        /// <summary>
        /// Tests a payload against an event's declaration, describing the mismatch when there is one.
        /// </summary>
        /// <returns><see langword="true"/> when the payload is acceptable, or nothing was declared.</returns>
        internal static bool IsPayloadAssignable(EventId eventId, object data, out Type declared)
        {
            declared = GetPayloadType(eventId);
            if (declared == null || declared == typeof(object)) return true;

            return data == null ? AcceptsNull(declared) : declared.IsInstanceOfType(data);
        }

        /// <summary>
        /// True when <paramref name="payloadType"/> can hold a null payload.
        /// </summary>
        /// <remarks>The single definition of that rule. The publish-time check and the wrapper around a
        /// typed handler both call it, so they cannot disagree about whether a null payload is legal —
        /// if they did, one would report an event the other delivered as <c>default(TData)</c>.</remarks>
        internal static bool AcceptsNull(Type payloadType)
        {
            return !payloadType.IsValueType || Nullable.GetUnderlyingType(payloadType) != null;
        }

        /// <summary>
        /// Gets the event-name prefix an enum type projects onto.
        /// </summary>
        /// <remarks>
        /// <para>Defaults to <see cref="Type.Name"/>, so <c>GameFlowEvents.GameStarting</c> becomes
        /// <c>"GameFlowEvents/GameStarting"</c>. <see cref="EventEnumAttribute.Prefix"/> overrides it,
        /// which is how two enums with the same simple name in different namespaces are told apart
        /// without renaming either type.</para>
        /// <para>This is the single definition of the projection. The runtime facade and the editor's
        /// Inspector dropdown both call it, so they cannot drift — if they did, the Inspector would
        /// write a name the publisher is not keyed on.</para>
        /// </remarks>
        public static string GetPrefix(Type enumType)
        {
            if (enumType == null) return string.Empty;
            var attribute = (EventEnumAttribute)Attribute.GetCustomAttribute(enumType, typeof(EventEnumAttribute));
            string prefix = attribute == null ? null : attribute.Prefix;
            return string.IsNullOrEmpty(prefix) ? enumType.Name : prefix;
        }

        /// <summary>
        /// Records that <paramref name="enumType"/> projects onto <paramref name="prefix"/>, and reports
        /// a second enum type claiming the same one.
        /// </summary>
        /// <remarks>Event names are built from <see cref="Type.Name"/> rather than
        /// <see cref="Type.FullName"/>, so two same-named enums in different namespaces would otherwise
        /// silently share every event. Re-claiming by the same type is not a collision, which keeps this
        /// idempotent when statics survive a domain reload.</remarks>
        internal static void ClaimPrefix(string prefix, Type enumType)
        {
            if (string.IsNullOrEmpty(prefix) || enumType == null) return;
            if (_claimedPrefixes.TryGetValue(prefix, out Type existing))
            {
                if (existing != enumType)
                {
                    Debug.LogError(
                        $"EventsRegistry: '{enumType.FullName}' and '{existing.FullName}' both project onto the " +
                        $"event name prefix '{prefix}/'. Their events will collide. Rename one of the enum types.");
                }
                return;
            }
            _claimedPrefixes[prefix] = enumType;
        }

        /// <summary>
        /// Registers a callback that drops a generic facade's cached state, so it is rebuilt on next use.
        /// </summary>
        internal static void AddResetHandler(Action resetHandler)
        {
            if (resetHandler != null) _resetHandlers.Add(resetHandler);
        }

        /// <summary>
        /// Clears static state carried over from a previous play session.
        /// </summary>
        /// <remarks>
        /// <para>Runs at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>, the earliest
        /// runtime hook, so it completes before the <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>
        /// sweep below.</para>
        /// <para>With domain reload enabled this is a no-op on already-empty state. It matters when
        /// <em>Enter Play Mode Options</em> has domain reload disabled, where statics survive between
        /// play sessions. Resetting explicitly rather than relying on that project setting keeps this
        /// correct either way.</para>
        /// <para>This deliberately does not clear <see cref="EventsPublisher"/> itself. Dropping live
        /// subscriptions is a behavioral choice the project already exposes through the editor's
        /// "Clear Events on Exiting Play Mode" toggle, and is not silently taken here.</para>
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStaticState()
        {
            for (int i = 0; i < _resetHandlers.Count; i++)
            {
                try { _resetHandlers[i](); }
                catch (Exception e) { Debug.LogError($"EventsRegistry: reset handler failed: {e}"); }
            }
            _resetHandlers.Clear();
            _claimedPrefixes.Clear();
            _policies.Clear();
            _payloadTypes.Clear();
            TypedSubscriptions.Clear();
            // Cleared at the start of a play session, not the end, so what the audit reads afterwards is
            // one boot sequence rather than an editor session's accumulated noise.
            EventsDiagnostics.Reset();
            // The intern table is deliberately NOT cleared. Handles are values that callers may still
            // hold; recycling them would silently repoint an EventId at a different event. It is bounded
            // by the number of distinct event names in the project, so letting it persist costs nothing.

        }

        /// <summary>
        /// Registers every enum marked with <see cref="EventEnumAttribute"/> before the first scene loads.
        /// </summary>
        /// <remarks>See <see cref="EventEnumAttribute"/> for why this exists when
        /// <see cref="EventsFor{T}"/> already registers lazily on first use.</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void RegisterAnnotatedEventEnums()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;

                Type[] types;
                // A single unloadable dependency must not stop the sweep; take whatever loaded.
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }

                foreach (Type type in types)
                {
                    if (type == null || !type.IsEnum) continue;
                    if (!type.IsDefined(typeof(EventEnumAttribute), false)) continue;
                    try
                    {
                        // EnsureRegistered is a non-generic method on a generic type, so the type is
                        // closed first; MakeGenericMethod would be wrong here.
                        Type facade = typeof(EventsFor<>).MakeGenericType(type);
                        facade.GetMethod(EnsureRegisteredMethodName, BindingFlags.Public | BindingFlags.Static)
                              .Invoke(null, null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"EventsRegistry: could not register event enum '{type.FullName}': {e}");
                    }
                }
            }
        }

        internal const string EnsureRegisteredMethodName = "EnsureRegistered";
    }
}
