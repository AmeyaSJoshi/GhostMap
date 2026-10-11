using System;
using System.Collections.Generic;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.UI;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>Task R7: how saved rooms are listed (plan section 19).</summary>
    public sealed class SavedRoomLabelTests
    {
        [Test]
        public void Label_IsRoomAndTime()
        {
            Assert.AreEqual("Bedroom · 11 Oct, 14:30", SavedRoomLabel.Describe("GhostMap-Bedroom-20261011-1430.zip"));
            Assert.AreEqual("Fixture Bedroom · 9 Oct, 10:05", SavedRoomLabel.Describe("GhostMap-Fixture_Bedroom-20261009-1005.zip"));
            Assert.AreEqual("Room · 11 Oct, 14:30", SavedRoomLabel.Describe("GhostMap-Room-20261011-1430-2.zip"), "a same-minute second save");
        }

        [Test]
        public void Label_MatchesWhatTheBuilderNames()
        {
            var snapshot = new GhostMap.Shared.Domain.SceneSnapshot
            {
                room = new GhostMap.Shared.Domain.RoomModel { name = "Kid's room" }
            };
            string fileName = ExportBundleBuilder.FileNameFor(snapshot, new DateTime(2026, 1, 2, 3, 4, 0));
            Assert.AreEqual("Kid s room · 2 Jan, 03:04", SavedRoomLabel.Describe(fileName));
        }

        [Test]
        public void Label_FallsBackToTheFileName()
        {
            Assert.AreEqual("notes.zip", SavedRoomLabel.Describe("notes.zip"));
            Assert.AreEqual("GhostMap-Room-20261399-9999.zip", SavedRoomLabel.Describe("GhostMap-Room-20261399-9999.zip"));
            Assert.AreEqual(string.Empty, SavedRoomLabel.Describe(null));
        }

        [Test]
        public void List_ShowsTheNewestFiveAndCountsTheRest()
        {
            var scans = new List<SavedScan>();
            for (int i = 0; i < 7; i++)
            {
                scans.Add(new SavedScan($"GhostMap-Room{i}-20261011-14{i:00}.zip", "/p", 1, DateTime.UtcNow));
            }

            IReadOnlyList<string> labels = SavedRoomsHud.Labels(scans, SavedRoomsHud.Slots, out int older);

            Assert.AreEqual(5, labels.Count);
            Assert.AreEqual("Room0 · 11 Oct, 14:00", labels[0]);
            Assert.AreEqual(2, older);

            Assert.AreEqual(0, SavedRoomsHud.Labels(new List<SavedScan>(), 5, out older).Count);
            Assert.AreEqual(0, older);
        }
    }
}
