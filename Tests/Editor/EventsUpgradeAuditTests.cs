using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using CrawfisSoftware.Events.Editor;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum AuditMarkedEvents
    {
        [EventDelivery(EventDelivery.Sticky)]
        [EventPayload(typeof(string))]
        ConfigLoaded,

        Jumped,
    }

    public enum AuditUnmarkedEvents { ServicesInitialized, Failing }

    /// <summary>Stands in for a consuming project's component, for the serialized-field checks.</summary>
    public class AuditProbeComponent
    {
        [SerializeField] private string _eventToFire = null;
        [SerializeField] private string _trigger = null;
        [EventName] [SerializeField] private string _alreadyMarked = null;
        [SerializeField] private string _unrelatedLabel = null;
    }

    /// <summary>
    /// The upgrade audit's reasoning. Gathering is an editor concern and is not covered here; what a
    /// given project state should produce is.
    /// </summary>
    public class EventsUpgradeAuditTests : EventsTestBase
    {
        private static FieldInfo Field(string name)
        {
            return typeof(AuditProbeComponent).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static UpgradeFinding Step(List<UpgradeFinding> findings, string stepPrefix)
        {
            foreach (UpgradeFinding finding in findings)
                if (finding.Step.StartsWith(stepPrefix, StringComparison.Ordinal)) return finding;
            throw new AssertionException("no finding for step " + stepPrefix);
        }

        private static string Joined(UpgradeFinding finding)
        {
            return string.Join(" | ", finding.Details);
        }

        [Test]
        public void MissingTestables_IsAnAction()
        {
            var input = new UpgradeAuditInput { TestablesConfigured = false };

            Assert.AreEqual(UpgradeSeverity.Action, Step(EventsUpgradeAudit.Analyze(input), "1.").Severity);
        }

        [Test]
        public void ConfiguredTestables_IsOk()
        {
            var input = new UpgradeAuditInput { TestablesConfigured = true };

            Assert.AreEqual(UpgradeSeverity.Ok, Step(EventsUpgradeAudit.Analyze(input), "1.").Severity);
        }

        [Test]
        public void UnmarkedEventEnum_IsReportedByName()
        {
            var input = new UpgradeAuditInput
            {
                EventEnums = new[] { typeof(AuditMarkedEvents), typeof(AuditUnmarkedEvents) },
            };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "2.");
            Assert.AreEqual(UpgradeSeverity.Action, finding.Severity);
            Assert.AreEqual(1, finding.Details.Count, "only the unmarked enum should be listed");
            Assert.IsTrue(Joined(finding).Contains("AuditUnmarkedEvents"));
        }

        [Test]
        public void AllEnumsMarked_IsOk()
        {
            var input = new UpgradeAuditInput { EventEnums = new[] { typeof(AuditMarkedEvents) } };

            Assert.AreEqual(UpgradeSeverity.Ok, Step(EventsUpgradeAudit.Analyze(input), "2.").Severity);
        }

        [Test]
        public void SerializedFieldHoldingAnEventName_IsReportedEvenWhenItsNameSaysNothing()
        {
            // The check that has no heuristic in it. A field called _trigger is invisible to any amount
            // of guessing from field names, and is found because an asset holds an event name in it.
            var input = new UpgradeAuditInput
            {
                SerializedStringFields = new[] { Field("_trigger"), Field("_unrelatedLabel") },
                SerializedEventNames = new[]
                {
                    new SerializedEventName("Assets/Main.unity", "_trigger", "AuditMarkedEvents/Jumped"),
                },
            };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "3.");
            Assert.AreEqual(UpgradeSeverity.Action, finding.Severity);
            Assert.AreEqual(1, finding.Details.Count, "_unrelatedLabel holds no event name and is not guessable");
            Assert.IsTrue(Joined(finding).Contains("_trigger"));
        }

        [Test]
        public void FieldNamedLikeAnEvent_IsOnlyASuggestion()
        {
            // Nothing in any asset confirms this one, so it is a guess and must not be presented as more.
            var input = new UpgradeAuditInput { SerializedStringFields = new[] { Field("_eventToFire") } };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "3.");
            Assert.AreEqual(UpgradeSeverity.Suggestion, finding.Severity);
            Assert.IsTrue(Joined(finding).Contains("_eventToFire"));
        }

        [Test]
        public void AlreadyMarkedField_IsNotReported()
        {
            var input = new UpgradeAuditInput
            {
                SerializedStringFields = new[] { Field("_alreadyMarked") },
                SerializedEventNames = new[]
                {
                    new SerializedEventName("Assets/Main.unity", "_alreadyMarked", "AuditMarkedEvents/Jumped"),
                },
            };

            Assert.AreEqual(UpgradeSeverity.Ok, Step(EventsUpgradeAudit.Analyze(input), "3.").Severity);
        }

        [Test]
        public void UndeclaredDeliveryPolicies_AreCountedAndLevelsGuessedAt()
        {
            var input = new UpgradeAuditInput { EventEnums = new[] { typeof(AuditUnmarkedEvents) } };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "6. Delivery");
            Assert.AreEqual(UpgradeSeverity.Suggestion, finding.Severity, "edge-or-level is a judgement, never an instruction");
            Assert.IsTrue(finding.Summary.Contains("2 of 2"));
            Assert.IsTrue(Joined(finding).Contains("ServicesInitialized"), "a level-sounding name should be suggested");
            Assert.IsFalse(Joined(finding).Contains("Failing"), "an edge-sounding name should not be");
        }

        [Test]
        public void SingletonSubclasses_AreReportedWithTheirExecutionOrder()
        {
            var input = new UpgradeAuditInput { SingletonSubclasses = new[] { typeof(AuditOrderedSingleton) } };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "4.");
            Assert.AreEqual(UpgradeSeverity.Action, finding.Severity);
            Assert.IsTrue(Joined(finding).Contains("DefaultExecutionOrder"));
        }

        [Test]
        public void NoRuntimeData_SaysSoRatherThanReportingACleanRun()
        {
            var input = new UpgradeAuditInput { DiagnosticsHaveData = false };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "6. Delivery (evidence)");
            Assert.AreEqual(UpgradeSeverity.Suggestion, finding.Severity);
            Assert.IsTrue(finding.Summary.Contains("No runtime data"));
        }

        [Test]
        public void RecordedLateDeliveries_AreReportedAsTheStickyCandidates()
        {
            EventsPublisher.Instance.RegisterEvent("Audit/Late");
            EventsPublisher.Instance.PublishEvent("Audit/Late", null, null);
            EventsPublisher.Instance.SubscribeToEvent("Audit/Late", Recorder("late"));

            var input = new UpgradeAuditInput
            {
                DiagnosticsHaveData = true,
                LateDeliveries = EventsDiagnostics.GetLateDeliveries(),
            };

            UpgradeFinding finding = Step(EventsUpgradeAudit.Analyze(input), "6. Delivery (evidence)");
            Assert.AreEqual(UpgradeSeverity.Action, finding.Severity);
            Assert.IsTrue(Joined(finding).Contains("Audit/Late"));
        }

        [Test]
        public void FindingsAreOrderedMostUrgentFirst()
        {
            var input = new UpgradeAuditInput
            {
                TestablesConfigured = true,                              // Ok
                EventEnums = new[] { typeof(AuditUnmarkedEvents) },      // Action + Suggestion
            };

            List<UpgradeFinding> findings = EventsUpgradeAudit.Analyze(input);
            for (int i = 1; i < findings.Count; i++)
                Assert.IsTrue(findings[i - 1].Severity >= findings[i].Severity, "findings must not un-sort");
        }
    }

    [UnityEngine.DefaultExecutionOrder(-10000)]
    public class AuditOrderedSingleton { }
}
