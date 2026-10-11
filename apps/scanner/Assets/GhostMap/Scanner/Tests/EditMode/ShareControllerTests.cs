using System;
using System.IO;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.Share;
using GhostMap.Scanner.UI;
using GhostMap.Scanner.Workflow;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>Task R7: Send to Computer through the share sheet.</summary>
    public sealed class ShareControllerTests
    {
        private const string Path = "/saved/GhostMap-Room-20261011-1430.zip";

        private static ShareController Controller(FakeShareSheet sheet, bool fileExists = true)
            => new ShareController(sheet, _ => fileExists);

        // ---------------------------------------------------------------
        // ShareResult: the native plugin's reply
        // ---------------------------------------------------------------

        [Test]
        public void Parse_ReadsCompletedActivityAndError()
        {
            Assert.IsTrue(ShareResult.TryParse("1|com.apple.UIKit.activity.AirDrop|", out ShareResult sent));
            Assert.IsTrue(sent.Completed);
            Assert.AreEqual(ShareController.AirDropActivity, sent.ActivityType);
            Assert.IsFalse(sent.Cancelled);

            Assert.IsTrue(ShareResult.TryParse("0||", out ShareResult cancelled));
            Assert.IsTrue(cancelled.Cancelled);

            Assert.IsTrue(ShareResult.TryParse("0|com.apple.UIKit.activity.AirDrop|No Wi-Fi | Bluetooth", out ShareResult failed));
            Assert.IsFalse(failed.Completed);
            Assert.IsFalse(failed.Cancelled);
            Assert.AreEqual("No Wi-Fi | Bluetooth", failed.Error, "the error is last and may contain '|'");
        }

        [Test]
        public void Parse_RefusesAnythingElse()
        {
            foreach (string bad in new[] { null, "", "1", "1|x", "yes|x|", "2|x|" })
            {
                Assert.IsFalse(ShareResult.TryParse(bad, out _), bad ?? "null");
            }
        }

        // ---------------------------------------------------------------
        // ShareController
        // ---------------------------------------------------------------

        [Test]
        public void Share_HandsTheExactSavedPathToTheSheet()
        {
            var sheet = new FakeShareSheet();
            ShareController sharing = Controller(sheet);

            Assert.IsTrue(sharing.TryShare(Path));

            CollectionAssert.AreEqual(new[] { Path }, sheet.SharedPaths);
            Assert.AreEqual(ShareState.Sharing, sharing.State);
            StringAssert.Contains("AirDrop", sharing.Message);
        }

        [Test]
        public void Completed_SaysWhereItWent()
        {
            var cases = new[]
            {
                (ShareController.AirDropActivity, "Sent with AirDrop."),
                ("com.apple.DocumentManagerUICore.SaveToFiles", "Saved to Files."),
                (ShareController.MailActivity, "Sent with Mail."),
                (ShareController.MessageActivity, "Sent with Messages."),
                ("com.example.someapp.share", "Sent.")
            };

            foreach ((string activity, string expected) in cases)
            {
                var sheet = new FakeShareSheet();
                ShareController sharing = Controller(sheet);
                sharing.TryShare(Path);
                sheet.Finish(new ShareResult(true, activity, string.Empty));

                Assert.AreEqual(ShareState.Sent, sharing.State, activity);
                Assert.AreEqual(expected, sharing.Message, activity);
            }
        }

        [Test]
        public void Cancelled_SaysNothingAndCanBeSentAgain()
        {
            var sheet = new FakeShareSheet();
            ShareController sharing = Controller(sheet);

            sharing.TryShare(Path);
            sheet.Finish(new ShareResult(false, string.Empty, string.Empty));

            Assert.AreEqual(ShareState.Cancelled, sharing.State);
            Assert.AreEqual(string.Empty, sharing.Message);

            Assert.IsTrue(sharing.TryShare(Path), "the button works again");
            Assert.AreEqual(2, sheet.SharedPaths.Count);
        }

        [Test]
        public void Failed_ShowsTheReasonAndCanBeSentAgain()
        {
            var sheet = new FakeShareSheet();
            ShareController sharing = Controller(sheet);

            sharing.TryShare(Path);
            sheet.Finish(new ShareResult(false, ShareController.AirDropActivity, "The Mac declined."));

            Assert.AreEqual(ShareState.Failed, sharing.State);
            Assert.AreEqual("Not sent: The Mac declined.", sharing.Message);
            Assert.IsTrue(sharing.TryShare(Path));
        }

        [Test]
        public void Share_IsRefusedWhileASheetIsOpen()
        {
            var sheet = new FakeShareSheet();
            ShareController sharing = Controller(sheet);

            Assert.IsTrue(sharing.TryShare(Path));
            Assert.IsFalse(sharing.TryShare(Path));
            Assert.AreEqual(1, sheet.SharedPaths.Count);
        }

        [Test]
        public void Share_IsRefusedWithoutASheetOrWithoutTheFile()
        {
            var unavailable = new FakeShareSheet { IsAvailable = false };
            ShareController noSheet = Controller(unavailable);
            Assert.IsFalse(noSheet.TryShare(Path));
            Assert.AreEqual(ShareState.Failed, noSheet.State);
            Assert.IsEmpty(unavailable.SharedPaths);

            var sheet = new FakeShareSheet();
            ShareController missing = Controller(sheet, fileExists: false);
            Assert.IsFalse(missing.TryShare(Path));
            StringAssert.Contains("missing", missing.Message);
            Assert.IsEmpty(sheet.SharedPaths);
        }

        [Test]
        public void Reset_ForgetsTheLastOutcomeButNotAnOpenSheet()
        {
            var sheet = new FakeShareSheet();
            ShareController sharing = Controller(sheet);

            sharing.TryShare(Path);
            sharing.Reset();
            Assert.AreEqual(ShareState.Sharing, sharing.State, "an open sheet still reports back");

            sheet.Finish(new ShareResult(true, ShareController.AirDropActivity, string.Empty));
            sharing.Reset();
            Assert.AreEqual(ShareState.Idle, sharing.State);
            Assert.AreEqual(string.Empty, sharing.Message);
        }

        [Test]
        public void EditorSheet_RevealsAndReportsCompleted()
        {
            var sheet = new EditorShareSheet();
            ShareResult? result = null;
            string file = System.IO.Path.GetTempFileName();
            try
            {
                sheet.Share(file, r => result = r);
            }
            finally
            {
                File.Delete(file);
            }

            Assert.IsTrue(result.HasValue);
            Assert.IsTrue(result.Value.Completed);
            Assert.AreEqual("Shown in Finder (Editor).", ShareController.Describe(result.Value.ActivityType));
        }

        // ---------------------------------------------------------------
        // When Send to Computer is offered, and what the guide says
        // ---------------------------------------------------------------

        [Test]
        public void Send_IsOfferedOnlyAfterFinishWithAnExport()
        {
            var saved = new ExportResult(true, new SavedScan("a.zip", Path, 1, DateTime.UtcNow), null, 5);
            var failed = new ExportResult(false, default, "Not enough space on the iPhone to save this room.", 5);

            Assert.IsTrue(ScannerHudController.ShouldOfferSend(ScanPhase.Finalized, saved));
            Assert.IsTrue(ScannerHudController.ShouldOfferSend(ScanPhase.Finalized, failed), "pressing it retries the export");
            Assert.IsFalse(ScannerHudController.ShouldOfferSend(ScanPhase.Finalized, null), "nothing exported yet");
            Assert.IsFalse(ScannerHudController.ShouldOfferSend(ScanPhase.ReadyToFinalize, saved));
        }

        [Test]
        public void Guide_FollowsTheShareSheet()
        {
            ScanWorkflowController workflow = ScanWorkflowTestRoom.BuildToReadyToFinalize();
            Assert.IsTrue(workflow.TryFinalize(out _));

            ScanGuideStep Step(ShareState state, string message) => ScanGuide.Describe(
                workflow,
                new ScanGuideContext(true, Capture.FloorLockRejection.None, false, ExportState.Saved, null, state, message));

            ScanGuideStep idle = Step(ShareState.Idle, string.Empty);
            StringAssert.Contains("Tap Send to Computer and choose your Mac", idle.Instruction);
            Assert.AreEqual(string.Empty, idle.Message);

            ScanGuideStep sending = Step(ShareState.Sharing, "Choose your Mac under AirDrop, or Save to Files.");
            Assert.AreEqual("Sending", sending.Title);

            ScanGuideStep sent = Step(ShareState.Sent, "Sent with AirDrop.");
            Assert.AreEqual("Sent", sent.Title);
            Assert.AreEqual("Sent with AirDrop.", sent.Message);
            Assert.AreEqual(GuideMessageKind.Success, sent.MessageKind);

            ScanGuideStep cancelled = Step(ShareState.Cancelled, string.Empty);
            Assert.AreEqual(idle.Instruction, cancelled.Instruction, "a cancel is not an error");
            Assert.AreEqual(GuideMessageKind.None, cancelled.MessageKind);

            ScanGuideStep failed = Step(ShareState.Failed, "Not sent: The Mac declined.");
            Assert.AreEqual(GuideMessageKind.Warning, failed.MessageKind);
        }
    }
}
