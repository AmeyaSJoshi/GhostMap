using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Scene;
using GhostMap.Viewer.UI;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// ADR-0006: asset export is gated at the HUD boundary by everything
    /// <see cref="ViewerHudController.CanSave"/> requires — a finalized,
    /// Viewer-owned scene, per ADR-0003 — plus at least one object to export.
    ///
    /// <para>Mirrors <see cref="ViewerHudControllerPersistenceGatingTests"/>:
    /// the gate is the pure, testable piece of an otherwise thin button-driven
    /// controller.</para>
    /// </summary>
    public sealed class ViewerHudControllerExportGatingTests
    {
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

        private static SceneObjectModel Desk(string id)
        {
            return new SceneObjectModel
            {
                id = id,
                type = "desk",
                center = new Vec3Dto(2f, 0f, 1.5f),
                yawDeg = 0f,
                widthM = 1.4f,
                depthM = 0.7f,
                heightM = 0.73f
            };
        }

        private static ViewerEditableScene SceneWith(SceneSnapshot snapshot)
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(snapshot, out _);
            return editable;
        }

        [Test]
        public void CanExportIsFalseWhenEditableIsNull()
        {
            Assert.IsFalse(ViewerHudController.CanExportAssets(null));
        }

        [Test]
        public void CanExportIsFalseBeforeAnySceneArrives()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            Assert.IsFalse(ViewerHudController.CanExportAssets(editable));
        }

        /// <summary>
        /// ADR-0003: while the scanner still owns the scene, the viewer must not
        /// write anything out — exactly as Save is blocked.
        /// </summary>
        [Test]
        public void CanExportIsFalseForAScannerOwnedInProgressScan()
        {
            ViewerEditableScene editable = SceneWith(
                Snapshot("s1", 0, finalized: false, Desk("a")));

            Assert.IsFalse(editable.EditingEnabled);
            Assert.IsFalse(ViewerHudController.CanExportAssets(editable));
            Assert.IsFalse(ViewerHudController.CanSave(editable));
        }

        [Test]
        public void CanExportIsFalseForAFinalizedRoomWithNoFurniture()
        {
            ViewerEditableScene editable = SceneWith(Snapshot("s1", 0, finalized: true));

            // The authority gate passes; there is simply nothing to export.
            Assert.IsTrue(ViewerHudController.CanSave(editable));
            Assert.IsFalse(ViewerHudController.CanExportAssets(editable));
        }

        [Test]
        public void CanExportIsTrueForAFinalizedRoomWithFurniture()
        {
            ViewerEditableScene editable = SceneWith(
                Snapshot("s1", 0, finalized: true, Desk("a")));

            Assert.IsTrue(editable.EditingEnabled);
            Assert.IsTrue(ViewerHudController.CanExportAssets(editable));
        }

        /// <summary>
        /// The two gates must be distinguishable, because the HUD reports a
        /// different message for each and the user cannot otherwise tell an
        /// authority problem from an empty room.
        /// </summary>
        [Test]
        public void ExportGateIsStrictlyNarrowerThanTheSaveGate()
        {
            ViewerEditableScene empty = SceneWith(Snapshot("s1", 0, finalized: true));
            ViewerEditableScene furnished = SceneWith(
                Snapshot("s2", 0, finalized: true, Desk("a")));

            Assert.IsTrue(ViewerHudController.CanSave(empty));
            Assert.IsTrue(ViewerHudController.CanSave(furnished));

            Assert.IsFalse(ViewerHudController.CanExportAssets(empty));
            Assert.IsTrue(ViewerHudController.CanExportAssets(furnished));
        }
    }
}
