using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Scene;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V5's ownership-transition layer. These tests drive the *real*
    /// pipeline — a real <see cref="ViewerSceneStore"/> feeding a real
    /// <see cref="ViewerEditableScene"/> — for every scenario the finalized
    /// ownership rule (<c>ADR-0003</c>) and the task brief's "Regression /
    /// live data verification" section (A-K) require: live pre-finalization
    /// rendering, the one-time finalize transition, local edits, protection
    /// from duplicate/reconnect/stale resends of the same finalized revision,
    /// and a clean reset on a genuinely new session.
    /// </summary>
    public sealed class ViewerEditableSceneTests
    {
        private static SceneObjectModel Obj(
            string id,
            string type = "generic",
            float x = 1f,
            float z = 1f,
            float yawDeg = 0f,
            float widthM = 1f,
            float depthM = 1f,
            float heightM = 1f)
        {
            return new SceneObjectModel
            {
                id = id,
                type = type,
                center = new Vec3Dto(x, 0f, z),
                yawDeg = yawDeg,
                widthM = widthM,
                depthM = depthM,
                heightM = heightM
            };
        }

        private static SceneSnapshot Snapshot(
            string sessionId,
            int revision,
            bool finalized,
            params SceneObjectModel[] objects)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
                revision = revision,
                scanPhase = finalized ? "Finalized" : "AddObjects",
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
                    objects = objects ?? Array.Empty<SceneObjectModel>()
                }
            };
        }

        // ---- Pre-finalization: pure pass-through, editing disabled ----

        [Test]
        public void PreFinalizationSnapshotsPassThroughAndEditingIsDisabled()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a", x: 1f)), out _);

            Assert.IsFalse(editable.EditingEnabled);
            Assert.AreEqual(1f, editable.Current.room.objects[0].center.x, 1e-4f);

            store.TryApplyScannerSnapshot(Snapshot("s1", 1, false, Obj("a", x: 2f)), out _);

            Assert.IsFalse(editable.EditingEnabled);
            Assert.AreEqual(2f, editable.Current.room.objects[0].center.x, 1e-4f, "Live data must keep updating before finalization.");
        }

        [Test]
        public void EditingIsDisabledBeforeFinalization()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a")), out _);

            bool applied = editable.TryApplyLocalEdit(Obj("a", x: 5f), out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
        }

        // ---- The finalize transition ----

        [Test]
        public void FinalizedSnapshotEnablesEditingExactlyOnce()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a")), out _);
            Assert.IsFalse(editable.EditingEnabled);

            store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a")), out _);

            Assert.IsTrue(editable.EditingEnabled);
            Assert.IsTrue(editable.Current.finalized);
        }

        [Test]
        public void EditingIsEnabledAfterFinalization()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, true, Obj("a")), out _);

            bool applied = editable.TryApplyLocalEdit(Obj("a", x: 5f), out string error);

            Assert.IsTrue(applied, error);
        }

        // ---- Local edits: each field, independence, validation ----

        [Test]
        public void LocalEditUpdatesPositionCorrectly()
        {
            var editable = FinalizedEditableScene(Obj("a", x: 1f, z: 1f));

            editable.TryApplyLocalEdit(Obj("a", x: 3.5f, z: 2.25f), out string error);

            SceneObjectModel result = editable.Current.room.objects[0];
            Assert.AreEqual(3.5f, result.center.x, 1e-4f, error);
            Assert.AreEqual(2.25f, result.center.z, 1e-4f);
        }

        [Test]
        public void LocalEditUpdatesYawCorrectly()
        {
            var editable = FinalizedEditableScene(Obj("a", yawDeg: 0f));

            editable.TryApplyLocalEdit(Obj("a", yawDeg: 135f), out string error);

            Assert.AreEqual(135f, editable.Current.room.objects[0].yawDeg, 1e-4f, error);
        }

        [Test]
        public void LocalEditUpdatesWidthCorrectly()
        {
            var editable = FinalizedEditableScene(Obj("a", widthM: 1f));

            editable.TryApplyLocalEdit(Obj("a", widthM: 2.4f), out string error);

            Assert.AreEqual(2.4f, editable.Current.room.objects[0].widthM, 1e-4f, error);
        }

        [Test]
        public void LocalEditUpdatesDepthCorrectly()
        {
            var editable = FinalizedEditableScene(Obj("a", depthM: 1f));

            editable.TryApplyLocalEdit(Obj("a", depthM: 1.8f), out string error);

            Assert.AreEqual(1.8f, editable.Current.room.objects[0].depthM, 1e-4f, error);
        }

        [Test]
        public void LocalEditUpdatesHeightCorrectly()
        {
            var editable = FinalizedEditableScene(Obj("a", heightM: 1f));

            editable.TryApplyLocalEdit(Obj("a", heightM: 0.6f), out string error);

            Assert.AreEqual(0.6f, editable.Current.room.objects[0].heightM, 1e-4f, error);
        }

        [Test]
        public void InvalidDimensionsAreRejectedAndCurrentIsUnchanged()
        {
            var editable = FinalizedEditableScene(Obj("a", widthM: 1f));
            SceneObjectModel before = editable.Current.room.objects[0];

            bool applied = editable.TryApplyLocalEdit(Obj("a", widthM: -1f), out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(before.widthM, editable.Current.room.objects[0].widthM, 1e-4f);
        }

        [Test]
        public void NonFiniteDimensionIsRejected()
        {
            var editable = FinalizedEditableScene(Obj("a", widthM: 1f));

            bool applied = editable.TryApplyLocalEdit(Obj("a", widthM: float.NaN), out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void EditingOneObjectDoesNotModifyAnother()
        {
            var editable = FinalizedEditableScene(Obj("a", x: 1f), Obj("b", x: 2f));

            editable.TryApplyLocalEdit(Obj("a", x: 9f), out string error);

            SceneObjectModel[] objects = editable.Current.room.objects;
            Assert.AreEqual(9f, ObjById(objects, "a").center.x, 1e-4f, error);
            Assert.AreEqual(2f, ObjById(objects, "b").center.x, 1e-4f, "Editing one object must not touch another.");
        }

        [Test]
        public void EachAcceptedLocalEditIncrementsTheRevision()
        {
            var editable = FinalizedEditableScene(Obj("a"));
            int before = editable.Current.revision;

            editable.TryApplyLocalEdit(Obj("a", x: 5f), out _);

            Assert.AreEqual(before + 1, editable.Current.revision);
        }

        [Test]
        public void EditingAnUnknownObjectIdFails()
        {
            var editable = FinalizedEditableScene(Obj("a"));

            bool applied = editable.TryApplyLocalEdit(Obj("does-not-exist"), out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
        }

        // ---- Full A-K regression / live-data verification ----

        [Test]
        public void DuplicateFinalizedResendDoesNotWipeLocalEdits()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            // A-D: non-finalized, then finalized, editing enabled.
            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a", x: 1f)), out _);
            Assert.IsFalse(editable.EditingEnabled);
            store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a", x: 1f)), out _);
            Assert.IsTrue(editable.EditingEnabled);

            // E: local edit.
            Assert.IsTrue(editable.TryApplyLocalEdit(Obj("a", x: 7f), out string editError), editError);

            // F: reconnect resends the exact same finalized revision.
            bool applied = store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a", x: 1f)), out _);

            // G: local edit remains.
            Assert.IsFalse(applied, "The store itself must treat this as a duplicate revision.");
            Assert.AreEqual(7f, editable.Current.room.objects[0].center.x, 1e-4f);
        }

        [Test]
        public void StaleSnapshotDoesNotWipeLocalEdits()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a", x: 1f)), out _);
            store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a", x: 1f)), out _);
            editable.TryApplyLocalEdit(Obj("a", x: 7f), out _);

            // H: a stale (lower-revision) snapshot for the same session.
            bool applied = store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a", x: 0.5f)), out _);

            // I: local edit remains.
            Assert.IsFalse(applied);
            Assert.AreEqual(7f, editable.Current.room.objects[0].center.x, 1e-4f);
        }

        [Test]
        public void ANewScannerSessionDoesNotInheritOldLocalEdits()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a", x: 1f)), out _);
            store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a", x: 1f)), out _);
            editable.TryApplyLocalEdit(Obj("a", x: 7f), out _);

            // J: a genuinely new session (Reset on the scanner).
            store.TryApplyScannerSnapshot(Snapshot("s2", 0, false, Obj("a", x: 0.2f)), out _);

            // K: the old local edit must not leak into the new room.
            Assert.IsFalse(editable.EditingEnabled, "A fresh, non-finalized session must disable editing again.");
            Assert.AreEqual(0.2f, editable.Current.room.objects[0].center.x, 1e-4f);
            Assert.AreEqual("s2", editable.Current.sessionId);
        }

        [Test]
        public void ANewSessionResetsEvenIfTheOldSessionWasNeverFinalized()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 5, false, Obj("a", x: 1f)), out _);

            store.TryApplyScannerSnapshot(Snapshot("s2", 0, false, Obj("a", x: 9f)), out _);

            Assert.AreEqual("s2", editable.Current.sessionId);
            Assert.AreEqual(9f, editable.Current.room.objects[0].center.x, 1e-4f);
        }

        [Test]
        public void ChangedFiresForEveryGenuinelyNewStateIncludingLocalEdits()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            int changes = 0;
            editable.Attach(store);
            editable.Changed += _ => changes++;

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a")), out _);
            Assert.AreEqual(1, changes);

            store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a")), out _);
            Assert.AreEqual(2, changes);

            editable.TryApplyLocalEdit(Obj("a", x: 3f), out _);
            Assert.AreEqual(3, changes);

            // A duplicate never reaches ViewerEditableScene at all.
            store.TryApplyScannerSnapshot(Snapshot("s1", 1, true, Obj("a")), out _);
            Assert.AreEqual(3, changes);
        }

        private static ViewerEditableScene FinalizedEditableScene(params SceneObjectModel[] objects)
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot("s1", 0, true, objects), out _);
            return editable;
        }

        private static SceneObjectModel ObjById(SceneObjectModel[] objects, string id)
        {
            foreach (SceneObjectModel candidate in objects)
            {
                if (candidate.id == id)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
