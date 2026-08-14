using System;

using CrawfisSoftware.Events.Editor;

using NUnit.Framework;

namespace CrawfisSoftware.Events.Tests
{
    [EventEnum]
    public enum CatalogTestEvents { Beta, Alpha }

    /// <summary>
    /// The Inspector dropdown and the runtime publisher must agree on the projected name, and an
    /// unrecognised serialized value must survive being displayed.
    /// </summary>
    public class EventNameCatalogTests : EventsTestBase
    {
        [Test]
        public void CatalogProjection_MatchesTheRuntimeProjection()
        {
            // The editor cannot call the generic runtime projection, so it reimplements it. If the two
            // ever diverge, the Inspector writes a name the publisher is not keyed on and the event
            // silently reaches nobody — which is the whole failure this control exists to prevent.
            string[] names = EventNameCatalog.BuildNames(new[] { typeof(CatalogTestEvents) });

            foreach (CatalogTestEvents value in Enum.GetValues(typeof(CatalogTestEvents)))
            {
                string runtimeName = EventsFor<CatalogTestEvents>.GetEventName(value);
                Assert.IsTrue(Array.IndexOf(names, runtimeName) >= 0,
                    "catalog is missing the runtime-projected name " + runtimeName);
            }
        }

        [Test]
        public void BuildNames_IsSortedAndDeduplicated()
        {
            string[] names = EventNameCatalog.BuildNames(new[] { typeof(CatalogTestEvents), typeof(CatalogTestEvents) });

            Assert.AreEqual(2, names.Length, "the same enum listed twice must not duplicate entries");
            Assert.AreEqual("CatalogTestEvents/Alpha", names[0]);
            Assert.AreEqual("CatalogTestEvents/Beta", names[1]);
        }

        [Test]
        public void BuildNames_IgnoresNullsAndNonEnums()
        {
            Assert.AreEqual(0, EventNameCatalog.BuildNames(null).Length);
            Assert.AreEqual(0, EventNameCatalog.BuildNames(new Type[] { null, typeof(string) }).Length);
        }

        [Test]
        public void Options_SelectNoneWhenUnset()
        {
            string[] options = EventNamePopup.BuildOptions(new[] { "A/One" }, string.Empty, out int selected);

            Assert.AreEqual(0, selected);
            Assert.AreEqual("<none>", options[0]);
        }

        [Test]
        public void Options_SelectTheMatchingKnownName()
        {
            string[] options = EventNamePopup.BuildOptions(new[] { "A/One", "A/Two" }, "A/Two", out int selected);

            Assert.AreEqual("A/Two", options[selected]);
        }

        [Test]
        public void Options_SurfaceAnUnknownValueRatherThanClearingIt()
        {
            // A name whose enum is not marked [EventEnum], or that has since been renamed, must not be
            // silently replaced by the first alphabetical event the moment the Inspector paints it.
            string[] options = EventNamePopup.BuildOptions(new[] { "A/One" }, "Legacy/Baked", out int selected);

            Assert.AreEqual("Missing/Legacy/Baked", options[selected]);
            Assert.IsTrue(selected > 0, "the unknown value must be selected, not <none>");
        }
    }
}
