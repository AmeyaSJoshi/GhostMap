using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Scene;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V6's <see cref="ViewerEditableScene.LoadExternalSnapshot"/> —
    /// the one sanctioned way a saved file becomes the effective scene,
    /// through the same ownership layer every scanner snapshot and local
    /// edit already goes through (never a second, load-specific path).
    /// </summary>
    public sealed class ViewerEditableSceneLoadTests
    {
        private static SceneObjectModel Obj(string id, float x = 1f)
        {
            return new SceneObjectModel
            {
                id = id,
                type = "generic",
                center = new Vec3Dto(x, 0f, 1f),
                yawDeg = 0f,
                widthM = 1f,
                depthM = 1f,
                heightM = 1f
            };
        }

        private static SceneSnapshot Snapshot(string sessionId, int revision, bool finalized, params SceneObjectModel[] objects)
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

        [Test]
        public void LoadingAValidSnapshotInstallsItAndEnablesEditing()
        {
            var editable = new ViewerEditableScene();

            bool loaded = editable.LoadExternalSnapshot(Snapshot("loaded-1", 12, true, Obj("a")), out string error);

            Assert.IsTrue(loaded, error);
            Assert.IsTrue(editable.EditingEnabled);
            Assert.AreEqual("loaded-1", editable.Current.sessionId);
            Assert.AreEqual(1f, editable.Current.room.objects[0].center.x, 1e-4f);
        }

        [Test]
        public void LoadingFiresChanged()
        {
            var editable = new ViewerEditableScene();
            int changes = 0;
            editable.Changed += _ => changes++;

            editable.LoadExternalSnapshot(Snapshot("loaded-1", 0, true, Obj("a")), out _);

            Assert.AreEqual(1, changes);
        }

        [Test]
        public void ANullSnapshotIsRejectedWithoutTouchingCurrent()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("original", 0, true, Obj("a")), out _);

            bool loaded = editable.LoadExternalSnapshot(null, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNotEmpty(error);
            Assert.AreEqual("original", editable.Current.sessionId);
        }

        [Test]
        public void ASnapshotWithNoRoomIsRejectedWithoutTouchingCurrent()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("original", 0, true, Obj("a")), out _);

            bool loaded = editable.LoadExternalSnapshot(new SceneSnapshot { sessionId = "bad" }, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNotEmpty(error);
            Assert.AreEqual("original", editable.Current.sessionId);
        }

        [Test]
        public void EditingWorksImmediatelyAfterLoad()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("loaded-1", 0, true, Obj("a", x: 1f)), out _);

            bool applied = editable.TryApplyLocalEdit(Obj("a", x: 9f), out string error);

            Assert.IsTrue(applied, error);
            Assert.AreEqual(9f, editable.Current.room.objects[0].center.x, 1e-4f);
        }

        [Test]
        public void AStaleReconnectResendOfTheLoadedSessionDoesNotOverwriteTheLoadedScene()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            // A room was loaded from disk; its session id happens to match a
            // scanner session that later reconnects and resends its own
            // (older, pre-edit) view of the same session.
            editable.LoadExternalSnapshot(Snapshot("loaded-1", 5, true, Obj("a", x: 1f)), out _);
            editable.TryApplyLocalEdit(Obj("a", x: 7f), out _);

            store.TryApplyScannerSnapshot(Snapshot("loaded-1", 5, true, Obj("a", x: 1f)), out _);

            Assert.AreEqual(7f, editable.Current.room.objects[0].center.x, 1e-4f,
                "A duplicate/stale resend of the loaded session must never silently destroy the loaded/edited scene.");
        }

        [Test]
        public void AGenuinelyNewSessionStillReplacesALoadedScene()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            editable.LoadExternalSnapshot(Snapshot("loaded-1", 5, true, Obj("a", x: 1f)), out _);
            editable.TryApplyLocalEdit(Obj("a", x: 7f), out _);

            store.TryApplyScannerSnapshot(Snapshot("scanner-session-2", 0, false, Obj("a", x: 0.2f)), out _);

            Assert.AreEqual("scanner-session-2", editable.Current.sessionId);
            Assert.AreEqual(0.2f, editable.Current.room.objects[0].center.x, 1e-4f);
            Assert.IsFalse(editable.EditingEnabled);
        }
    }
}
