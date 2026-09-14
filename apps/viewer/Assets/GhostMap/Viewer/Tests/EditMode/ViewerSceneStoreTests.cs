using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Scene;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V1's <see cref="ViewerSceneStore"/>: revision arbitration (via the
    /// shared <see cref="SnapshotRevisionPolicy"/>, never reimplemented here),
    /// pre-apply validation, and the guarantee that nothing but a newer valid
    /// snapshot ever changes <see cref="ViewerSceneStore.Current"/>.
    /// </summary>
    public sealed class ViewerSceneStoreTests
    {
        private static SceneSnapshot BuildValidSnapshot(string sessionId, int revision, string phase, bool finalized)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
                revision = revision,
                scanPhase = phase,
                finalized = finalized,
                closureErrorM = 0.05f,
                room = new RoomModel
                {
                    id = "room-1",
                    name = "Bedroom",
                    heightM = 2.5f,
                    corners = new[]
                    {
                        new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                        new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                        new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                        new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
                    },
                    openings = Array.Empty<OpeningModel>(),
                    objects = Array.Empty<SceneObjectModel>()
                }
            };
        }

        [Test]
        public void FirstSnapshotForANewSessionIsAccepted()
        {
            var store = new ViewerSceneStore();
            SceneSnapshot snapshot = BuildValidSnapshot("session-1", 0, "FloorLocked", false);

            bool applied = store.TryApplyScannerSnapshot(snapshot, out string error);

            Assert.IsTrue(applied, error);
            Assert.AreSame(snapshot, store.Current);
        }

        [Test]
        public void ANewerRevisionInTheSameSessionIsAccepted()
        {
            var store = new ViewerSceneStore();
            store.TryApplyScannerSnapshot(BuildValidSnapshot("session-1", 3, "AddOpenings", false), out _);

            SceneSnapshot newer = BuildValidSnapshot("session-1", 4, "AddObjects", false);
            bool applied = store.TryApplyScannerSnapshot(newer, out string error);

            Assert.IsTrue(applied, error);
            Assert.AreSame(newer, store.Current);
        }

        [Test]
        public void AStaleRevisionInTheSameSessionIsIgnored()
        {
            var store = new ViewerSceneStore();
            SceneSnapshot current = BuildValidSnapshot("session-1", 5, "ReadyToFinalize", false);
            store.TryApplyScannerSnapshot(current, out _);

            SceneSnapshot stale = BuildValidSnapshot("session-1", 2, "AddOpenings", false);
            bool applied = store.TryApplyScannerSnapshot(stale, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.AreSame(current, store.Current, "A stale revision must not replace the current scene.");
        }

        [Test]
        public void ADuplicateRevisionInTheSameSessionIsIgnoredAsAHarmlessResend()
        {
            var store = new ViewerSceneStore();
            SceneSnapshot current = BuildValidSnapshot("session-1", 5, "ReadyToFinalize", false);
            store.TryApplyScannerSnapshot(current, out _);

            // A resend after a reconnect: same session, same revision, a
            // distinct object reference (as a real resend over the wire
            // would deserialize into a new instance).
            SceneSnapshot duplicate = BuildValidSnapshot("session-1", 5, "ReadyToFinalize", false);
            bool applied = store.TryApplyScannerSnapshot(duplicate, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.AreSame(current, store.Current, "A duplicate resend must be harmless, not swap the reference.");
        }

        [Test]
        public void ADifferentSessionIsAcceptedEvenAtALowerRevision()
        {
            var store = new ViewerSceneStore();
            store.TryApplyScannerSnapshot(BuildValidSnapshot("session-1", 20, "Finalized", true), out _);

            // A Reset on the scanner starts a brand-new session at revision 0.
            SceneSnapshot freshSession = BuildValidSnapshot("session-2", 0, "Boot", false);
            bool applied = store.TryApplyScannerSnapshot(freshSession, out string error);

            Assert.IsTrue(applied, error);
            Assert.AreSame(freshSession, store.Current);
        }

        [Test]
        public void ReconnectResendingTheSameSnapshotLeavesCurrentUnchanged()
        {
            var store = new ViewerSceneStore();
            SceneSnapshot original = BuildValidSnapshot("session-1", 9, "AddObjects", false);
            store.TryApplyScannerSnapshot(original, out _);

            // Simulates the exact resend protocol v1 requires after reconnect.
            SceneSnapshot resend = BuildValidSnapshot("session-1", 9, "AddObjects", false);
            store.TryApplyScannerSnapshot(resend, out _);

            Assert.AreSame(original, store.Current);
            Assert.AreEqual(9, store.Current.revision);
        }

        [Test]
        public void AFinalizedSnapshotPreservesTheFinalizedFlag()
        {
            var store = new ViewerSceneStore();
            SceneSnapshot finalized = BuildValidSnapshot("session-1", 12, "Finalized", true);

            bool applied = store.TryApplyScannerSnapshot(finalized, out string error);

            Assert.IsTrue(applied, error);
            Assert.IsTrue(store.Current.finalized);
            Assert.AreEqual("Finalized", store.Current.scanPhase);
        }

        [Test]
        public void AnInvalidRoomIsRejectedAndCurrentIsUnchanged()
        {
            var store = new ViewerSceneStore();
            SceneSnapshot valid = BuildValidSnapshot("session-1", 1, "CaptureHeight", false);
            store.TryApplyScannerSnapshot(valid, out _);

            SceneSnapshot invalid = BuildValidSnapshot("session-1", 2, "CaptureHeight", false);
            invalid.room.heightM = 9.9f; // outside the 2.0-4.0 m validated range

            bool applied = store.TryApplyScannerSnapshot(invalid, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.AreSame(valid, store.Current, "An invalid room must never replace a valid current scene.");
        }

        [Test]
        public void ChangedFiresOnlyWhenASnapshotIsActuallyApplied()
        {
            var store = new ViewerSceneStore();
            int changedCount = 0;
            store.Changed += _ => changedCount++;

            store.TryApplyScannerSnapshot(BuildValidSnapshot("session-1", 0, "Boot", false), out _);
            Assert.AreEqual(1, changedCount);

            // A duplicate must not fire Changed again.
            store.TryApplyScannerSnapshot(BuildValidSnapshot("session-1", 0, "Boot", false), out _);
            Assert.AreEqual(1, changedCount);

            store.TryApplyScannerSnapshot(BuildValidSnapshot("session-1", 1, "FloorLocked", false), out _);
            Assert.AreEqual(2, changedCount);
        }

        [Test]
        public void NullSnapshotIsRejected()
        {
            var store = new ViewerSceneStore();
            bool applied = store.TryApplyScannerSnapshot(null, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.IsNull(store.Current);
        }
    }
}
