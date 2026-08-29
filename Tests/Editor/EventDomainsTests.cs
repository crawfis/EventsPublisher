using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using CrawfisSoftware.Events.Editor;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum DomainListingTestEvents
    {
        [EventPayload(typeof(string))]
        [EventDelivery(EventDelivery.Sticky)]
        ConfigLoaded,

        Jumped,

        [EventDelivery(EventDelivery.Replay)]
        SegmentCreated,
    }

    [EventEnum(Prefix = "DomainListingPrefixed")]
    public enum DomainPrefixOverrideTestEvents { Alpha, Beta }

    /// <summary>Unmarked on purpose: it registers on first touch, not in the sweep.</summary>
    public enum DomainUnmarkedTestEvents { Only }

    namespace DomainCollidingA { public enum DomainCollisionEvents { Alpha } }
    namespace DomainCollidingB { public enum DomainCollisionEvents { Alpha } }

    /// <summary>
    /// Enumerating the registered domains, and the summary the <c>List Domains</c> menu prints from it.
    /// </summary>
    public class EventDomainsTests : EventsTestBase
    {
        private static EventDomain Describe(Type enumType)
        {
            List<EventDomain> domains = EventDomainCatalog.Describe(new[] { enumType });
            Assert.AreEqual(1, domains.Count);
            return domains[0];
        }

        private static EventDomain Find(IEnumerable<EventDomain> domains, Type enumType)
        {
            foreach (EventDomain domain in domains)
                if (domain.EnumType == enumType) return domain;
            throw new AssertionException("no domain reported for " + enumType.FullName);
        }

        private static int CountOf(Type enumType)
        {
            int count = 0;
            foreach (Type registered in EventsRegistry.RegisteredEnumTypes)
                if (registered == enumType) count++;
            return count;
        }

        [Test]
        public void MarkedFamily_IsListedOnceTheSweepHasRun()
        {
            // The enumeration reports what has registered, not what the project contains — which is why
            // the menu sweeps before reading it rather than assuming something already has.
            CollectionAssert.DoesNotContain(EventsRegistry.RegisteredEnumTypes, typeof(DomainListingTestEvents));

            EventsRegistry.RegisterAnnotatedEventEnums(OwnAssembly);

            CollectionAssert.Contains(EventsRegistry.RegisteredEnumTypes, typeof(DomainListingTestEvents));
        }

        [Test]
        public void TheListing_ExcludesFixturesFromTheTestAssembly()
        {
            // The symptom the exclusion exists for: "CrawfisSoftware > Events > List Domains" in a
            // consuming project printed this suite's fixtures alongside the project's own domains,
            // which makes the listing useless as the authoritative answer to "what domains are there".
            // The real entry point, so what is covered is the filtering as it actually runs. It sweeps
            // the whole AppDomain, which in a consuming project registers that project's own families
            // too — and anything they log on the way would otherwise fail this test for reasons that
            // have nothing to do with it. The assertion below is what this test is about.
            LogAssert.ignoreFailingMessages = true;
            try { EventsRegistry.RegisterAnnotatedEventEnums(); }
            finally { LogAssert.ignoreFailingMessages = false; }

            foreach (EventDomain domain in EventDomainCatalog.Describe(EventsRegistry.RegisteredEnumTypes))
            {
                Assert.AreNotEqual(typeof(EventsTestBase).Assembly, domain.EnumType.Assembly,
                                   "the listing must not report the fixture " + domain.EnumType.FullName);
            }
        }

        [Test]
        public void UnmarkedFamily_IsListedFromItsFirstTouch()
        {
            // [EventEnum] governs when a family registers, not whether it counts as a domain: an unmarked
            // one is a domain from the moment its facade is built, and the listing must say so.
            EventsRegistry.RegisterAnnotatedEventEnums(OwnAssembly);
            CollectionAssert.DoesNotContain(EventsRegistry.RegisteredEnumTypes, typeof(DomainUnmarkedTestEvents));

            EventsFor<DomainUnmarkedTestEvents>.EnsureRegistered();

            CollectionAssert.Contains(EventsRegistry.RegisteredEnumTypes, typeof(DomainUnmarkedTestEvents));
        }

        [Test]
        public void ReRegisteringTheSameFamily_ListsItOnce()
        {
            // Must stay idempotent: statics survive a domain reload when the project disables it, and a
            // family listed twice would read as two domains sharing a prefix, which is the error case.
            EventsFor<DomainUnmarkedTestEvents>.EnsureRegistered();
            EventsRegistry.ClaimPrefix("DomainUnmarkedTestEvents", typeof(DomainUnmarkedTestEvents));

            Assert.AreEqual(1, CountOf(typeof(DomainUnmarkedTestEvents)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CollidingFamilies_AreBothListed()
        {
            LogAssert.Expect(LogType.Error, new Regex("both project onto the event name prefix 'DomainCollisionEvents/'"));

            EventsFor<DomainCollidingA.DomainCollisionEvents>.EnsureRegistered();
            EventsFor<DomainCollidingB.DomainCollisionEvents>.EnsureRegistered();

            // Only the first claimant holds the prefix, but both families registered and both publish
            // under it. Listing only the winner would hide the half of the collision that is surprising.
            CollectionAssert.Contains(EventsRegistry.RegisteredEnumTypes, typeof(DomainCollidingA.DomainCollisionEvents));
            CollectionAssert.Contains(EventsRegistry.RegisteredEnumTypes, typeof(DomainCollidingB.DomainCollisionEvents));
        }

        [Test]
        public void Reset_EmptiesTheEnumeration()
        {
            EventsFor<DomainUnmarkedTestEvents>.EnsureRegistered();
            Assert.Greater(EventsRegistry.RegisteredEnumTypes.Count, 0);

            EventsRegistry.ResetStaticState();

            Assert.AreEqual(0, EventsRegistry.RegisteredEnumTypes.Count);
        }

        [Test]
        public void Enumeration_IsTheSameCollectionEachRead()
        {
            EventsFor<DomainUnmarkedTestEvents>.EnsureRegistered();

            // The same collection every read, rather than a fresh snapshot per call.
            Assert.AreSame(EventsRegistry.RegisteredEnumTypes, EventsRegistry.RegisteredEnumTypes);
            CollectionAssert.AreEqual(new List<Type>(EventsRegistry.RegisteredEnumTypes),
                                      new List<Type>(EventsRegistry.RegisteredEnumTypes));
        }

        [Test]
        public void Enumeration_CannotBeMutatedThroughItsReference()
        {
            var asList = EventsRegistry.RegisteredEnumTypes as IList<Type>;

            Assert.IsNotNull(asList, "the enumeration is expected to be a read-only view, not a bare list");
            Assert.Throws<NotSupportedException>(() => asList.Add(typeof(DomainUnmarkedTestEvents)));
            Assert.AreEqual(0, EventsRegistry.RegisteredEnumTypes.Count);
        }

        [Test]
        public void PrefixOverride_IsWhatTheDomainReports()
        {
            EventDomain domain = Describe(typeof(DomainPrefixOverrideTestEvents));

            Assert.AreEqual("DomainListingPrefixed", domain.Prefix);
            // Pinned against the runtime projection: a reported prefix that is not the one the family
            // publishes under would send someone looking for events under a name nothing is keyed on.
            Assert.AreEqual(EventsFor<DomainPrefixOverrideTestEvents>.GetEventName(DomainPrefixOverrideTestEvents.Alpha),
                            domain.Prefix + "/" + DomainPrefixOverrideTestEvents.Alpha);
        }

        [Test]
        public void Describe_CountsMembersPayloadsAndRetainingPolicies()
        {
            EventDomain domain = Describe(typeof(DomainListingTestEvents));

            Assert.AreEqual(3, domain.MemberCount);
            Assert.AreEqual(1, domain.PayloadCount);
            Assert.AreEqual(1, domain.StickyCount);
            Assert.AreEqual(1, domain.ReplayCount);
        }

        [Test]
        public void Describe_IgnoresNullsAndNonEnums()
        {
            Assert.AreEqual(0, EventDomainCatalog.Describe(null).Count);
            Assert.AreEqual(0, EventDomainCatalog.Describe(new Type[] { null, typeof(string) }).Count);
        }

        [Test]
        public void Describe_OrdersByPrefix()
        {
            List<EventDomain> domains = EventDomainCatalog.Describe(new[]
            {
                typeof(DomainUnmarkedTestEvents), typeof(DomainListingTestEvents), typeof(DomainPrefixOverrideTestEvents),
            });

            Assert.AreEqual("DomainListingPrefixed", domains[0].Prefix);
            Assert.AreEqual("DomainListingTestEvents", domains[1].Prefix);
            Assert.AreEqual("DomainUnmarkedTestEvents", domains[2].Prefix);
        }

        [Test]
        public void Format_NamesThePrefixAndTheFullTypeName()
        {
            string line = EventDomainCatalog.Format(Describe(typeof(DomainListingTestEvents)));

            StringAssert.Contains("DomainListingTestEvents/", line);
            StringAssert.Contains(typeof(DomainListingTestEvents).FullName, line);
            StringAssert.Contains("3 member(s)", line);
        }

        [Test]
        public void SweepThenDescribe_SummarisesWhatTheRegistryHolds()
        {
            // What the menu does, without the editor API: sweep, then summarise the registry itself.
            EventsRegistry.RegisterAnnotatedEventEnums(OwnAssembly);

            EventDomain domain = Find(EventDomainCatalog.Describe(EventsRegistry.RegisteredEnumTypes),
                                      typeof(DomainListingTestEvents));

            Assert.AreEqual("DomainListingTestEvents", domain.Prefix);
            Assert.AreEqual(3, domain.MemberCount);
        }
    }
}
