using System;
using System.IO;
using System.Linq;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.UI;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>Task R6: export on finalize, and what the guide then says.</summary>
    public sealed class ScanExporterTests
    {
        private const string Template = "<script type=\"application/json\" id=\"ghostmap-data\">__GHOSTMAP_DATA__</script>";
        private static readonly DateTime Now = new DateTime(2026, 10, 11, 9, 30, 0);

        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ghostmap-export-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        private static SceneSnapshot Fixture()
        {
            string path = Path.Combine(Application.dataPath, "..", "..", "..", "fixtures", "valid-room-v1.json");
            return JsonUtility.FromJson<SceneSnapshot>(File.ReadAllText(path));
        }

        [Test]
        public void Export_SavesOneBundleNamedForTheMinute()
        {
            var exporter = new ScanExporter(() => Template, new SavedScanStore(directory), () => Now);

            ExportResult result = exporter.Export(Fixture());

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.AreEqual("GhostMap-Fixture_Bedroom-20261011-0930.zip", result.Saved.FileName);
            Assert.IsTrue(File.Exists(result.Saved.Path));
            Assert.GreaterOrEqual(result.ElapsedMs, 0);
            Assert.AreEqual(1, exporter.Store.List().Count);
        }

        [Test]
        public void Export_OfAnUnfinalizedScanSavesNothing()
        {
            var exporter = new ScanExporter(() => Template, new SavedScanStore(directory), () => Now);
            SceneSnapshot snapshot = Fixture();
            snapshot.finalized = false;

            ExportResult result = exporter.Export(snapshot);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNotEmpty(result.Error);
            Assert.AreEqual(0, exporter.Store.List().Count);
        }

        [Test]
        public void Export_ReportsAMissingTemplateInsteadOfThrowing()
        {
            var exporter = new ScanExporter(
                () => throw new InvalidOperationException("Resources/GhostMapRoomTemplate is missing"),
                new SavedScanStore(directory),
                () => Now);

            ExportResult result = exporter.Export(Fixture());

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("template", result.Error);
            Assert.AreEqual(0, exporter.Store.List().Count);
        }

        [Test]
        public void Export_FromARealFinalizedWorkflow()
        {
            // The snapshot the HUD exports: a scan driven to Finalized through
            // the real workflow, not a hand-written fixture.
            ScanWorkflowController workflow = FinalizedWorkflow();
            var exporter = new ScanExporter(() => Template, new SavedScanStore(directory), () => Now);

            ExportResult result = exporter.Export(workflow.Snapshot);

            Assert.IsTrue(result.Succeeded, result.Error);
            StringAssert.StartsWith("GhostMap-Room-", result.Saved.FileName);
        }

        [Test]
        public void Guide_SaysSavedAfterASuccessfulExport()
        {
            ScanGuideStep step = ScanGuide.Describe(
                FinalizedWorkflow(),
                new ScanGuideContext(true, FloorLockRejection.None, false, ExportState.Saved));

            StringAssert.Contains("saved on this iPhone", step.Instruction);
            StringAssert.Contains("Send to Computer", step.Instruction);
        }

        [Test]
        public void Guide_ShowsTheErrorAndHowToRetryAfterAFailedExport()
        {
            ScanGuideStep step = ScanGuide.Describe(
                FinalizedWorkflow(),
                new ScanGuideContext(true, FloorLockRejection.None, false, ExportState.Failed,
                    "Not enough space on the iPhone to save this room."));

            Assert.AreEqual("Not saved yet", step.Title);
            StringAssert.Contains("try again", step.Instruction);
            Assert.AreEqual("Not enough space on the iPhone to save this room.", step.Message);
            Assert.AreEqual(GuideMessageKind.Warning, step.MessageKind);
        }

        private static ScanWorkflowController FinalizedWorkflow()
        {
            ScanWorkflowController workflow = ScanWorkflowTestRoom.BuildToReadyToFinalize();
            Assert.IsTrue(workflow.TryFinalize(out _));
            Assert.AreEqual(ScanPhase.Finalized, workflow.Phase);
            return workflow;
        }
    }
}
