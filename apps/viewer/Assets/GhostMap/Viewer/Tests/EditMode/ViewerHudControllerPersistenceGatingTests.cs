using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using GhostMap.Viewer.UI;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Post-V6-review fix: persistence authority (ADR-0003) must be enforced
    /// at the HUD's Save/Load boundary, not just inside <c>ScenePersistence</c>
    /// or <c>ViewerEditableScene</c>. <see cref="ViewerHudController.CanSave"/>
    /// and <see cref="ViewerHudController.CanLoad"/> are the pure/testable
    /// pieces of an otherwise-thin, button-driven controller — the same split
    /// <c>ViewerInteractionRouter.IsClick</c> already established — and
    /// <see cref="ViewerHudController.ResetTransientStateAfterLoad"/> is
    /// exercised directly against real controllers via the <c>SetX</c>
    /// test-wiring methods.
    /// </summary>
    public sealed class ViewerHudControllerPersistenceGatingTests
    {
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
                    openings = System.Array.Empty<OpeningModel>(),
                    objects = objects ?? System.Array.Empty<SceneObjectModel>()
                }
            };
        }

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

        // ---- 1. Save gating ----

        [Test]
        public void CanSaveIsFalseWhenEditableIsNull()
        {
            Assert.IsFalse(ViewerHudController.CanSave(null));
        }

        [Test]
        public void CanSaveIsFalseWhenNoSceneExistsYet()
        {
            var editable = new ViewerEditableScene();

            Assert.IsFalse(ViewerHudController.CanSave(editable));
        }

        [Test]
        public void CanSaveIsFalseForAnUnfinalizedScannerOwnedScene()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a")), out _);

            Assert.IsFalse(editable.EditingEnabled);
            Assert.IsFalse(ViewerHudController.CanSave(editable),
                "Saving a Scanner-owned, in-progress scan would violate ADR-0003.");
        }

        [Test]
        public void CanSaveIsTrueForAFinalizedViewerOwnedScene()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot("s1", 0, true, Obj("a")), out _);

            Assert.IsTrue(editable.EditingEnabled);
            Assert.IsTrue(ViewerHudController.CanSave(editable));
        }

        [Test]
        public void CanSaveIsStillTrueAfterAViewerEditOnAFinalizedScene()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot("s1", 0, true, Obj("a", x: 1f)), out _);

            Assert.IsTrue(editable.TryApplyLocalEdit(Obj("a", x: 5f), out string editError), editError);

            Assert.IsTrue(ViewerHudController.CanSave(editable),
                "Viewer edits must still be saveable — they are exactly what Save is for.");
            Assert.AreEqual(5f, editable.Current.room.objects[0].center.x, 1e-4f);
        }

        // ---- 3. Load gating ----

        [Test]
        public void CanLoadIsTrueWhenEditableIsNull()
        {
            Assert.IsTrue(ViewerHudController.CanLoad(null));
        }

        [Test]
        public void CanLoadIsTrueWhenNoSceneExistsYet()
        {
            var editable = new ViewerEditableScene();

            Assert.IsTrue(ViewerHudController.CanLoad(editable));
        }

        [Test]
        public void CanLoadIsTrueWhenTheCurrentSceneIsAlreadyViewerOwned()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("loaded-1", 0, true, Obj("a")), out _);

            Assert.IsTrue(editable.EditingEnabled);
            Assert.IsTrue(ViewerHudController.CanLoad(editable));
        }

        [Test]
        public void CanLoadIsFalseWhileAnUnfinalizedScanIsActive()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot("s1", 0, false, Obj("a")), out _);

            Assert.IsFalse(editable.EditingEnabled);
            Assert.IsNotNull(editable.Current);
            Assert.IsFalse(ViewerHudController.CanLoad(editable),
                "Loading a saved file must never interrupt a Scanner-owned, in-progress scan.");
        }

        // ---- 4. Resetting transient interaction state after a successful load ----

        [Test]
        public void ResetTransientStateAfterLoadClearsSelection()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("loaded-1", 0, true, Obj("a", x: 1f)), out _);

            var roomRendererGo = new GameObject("RoomRenderer_Test", typeof(RoomRenderer));
            var roomRenderer = roomRendererGo.GetComponent<RoomRenderer>();
            roomRenderer.Attach(editable);

            var cameraGo = new GameObject("Camera_Test", typeof(Camera));
            var selectionGo = new GameObject("Selection_Test", typeof(ObjectSelectionController));
            var selection = selectionGo.GetComponent<ObjectSelectionController>();
            selection.SetRoomRenderer(roomRenderer);
            selection.SetCamera(cameraGo.GetComponent<Camera>());
            selection.Attach();

            Transform bedRoot = FindChildRecursive(roomRenderer.Root.transform, "Object_generic_a");
            Assert.IsNotNull(bedRoot, "Expected the loaded object's GameObject to exist.");
            Ray ray = new Ray(bedRoot.position + Vector3.up * 5f, Vector3.down);
            Assert.IsTrue(selection.TrySelectAt(ray));
            Assert.IsTrue(selection.HasSelection);

            var hudGo = new GameObject("Hud_Test", typeof(ViewerHudController));
            var hud = hudGo.GetComponent<ViewerHudController>();
            hud.SetSelectionController(selection);

            hud.ResetTransientStateAfterLoad();

            Assert.IsFalse(selection.HasSelection, "A successful load must clear any carried-over selection.");

            selection.Detach();
            roomRenderer.Detach();
            Object.DestroyImmediate(hudGo);
            Object.DestroyImmediate(selectionGo);
            Object.DestroyImmediate(cameraGo);
            Object.DestroyImmediate(roomRendererGo);
        }

        [Test]
        public void ResetTransientStateAfterLoadClearsAndDeactivatesMeasurement()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("loaded-1", 0, true, Obj("a")), out _);

            // A real floor collider to raycast against comes from a real
            // RoomRenderer rebuild — matching the exact pattern
            // MeasurementControllerTests already uses.
            var roomRendererGo = new GameObject("RoomRenderer_Test", typeof(RoomRenderer));
            var roomRenderer = roomRendererGo.GetComponent<RoomRenderer>();
            roomRenderer.Attach(editable);

            var measurementGo = new GameObject("Measurement_Test", typeof(MeasurementController));
            var measurement = measurementGo.GetComponent<MeasurementController>();
            measurement.Attach(editable);
            measurement.SetActive(true);

            Physics.SyncTransforms();
            bool placedA = measurement.TryPlacePoint(new Ray(new Vector3(1f, 5f, 1f), Vector3.down));
            Assert.IsTrue(placedA, "Expected the floor raycast to hit inside the loaded room.");

            var hudGo = new GameObject("Hud_Test", typeof(ViewerHudController));
            var hud = hudGo.GetComponent<ViewerHudController>();
            hud.SetMeasurementController(measurement);

            hud.ResetTransientStateAfterLoad();

            Assert.IsFalse(measurement.IsActive, "A successful load must leave measurement mode OFF.");
            Assert.IsFalse(measurement.HasMeasurement);
            Assert.IsFalse(measurement.PointA.HasValue);

            measurement.Detach();
            roomRenderer.Detach();
            Object.DestroyImmediate(hudGo);
            Object.DestroyImmediate(measurementGo);
            Object.DestroyImmediate(roomRendererGo);
        }

        [Test]
        public void ResetTransientStateAfterLoadFramesTheCamera()
        {
            var editable = new ViewerEditableScene();
            editable.LoadExternalSnapshot(Snapshot("loaded-1", 0, true, Obj("a")), out _);

            var roomRendererGo = new GameObject("RoomRenderer_Test", typeof(RoomRenderer));
            var roomRenderer = roomRendererGo.GetComponent<RoomRenderer>();
            roomRenderer.Attach(editable);

            var cameraGo = new GameObject("Camera_Test", typeof(Camera));
            var cameraController = cameraGo.AddComponent<OrbitCameraController>();
            cameraController.SetRoomRenderer(roomRenderer);
            cameraController.Attach(editable);

            // Simulate the user having orbited/panned away before the load.
            cameraGo.transform.position = new Vector3(500f, 500f, 500f);

            var hudGo = new GameObject("Hud_Test", typeof(ViewerHudController));
            var hud = hudGo.GetComponent<ViewerHudController>();
            hud.SetCameraController(cameraController);

            hud.ResetTransientStateAfterLoad();

            Assert.IsTrue(cameraController.Rig.IsValid);
            Assert.AreEqual(cameraController.Rig.Position, cameraGo.transform.position,
                "FrameRoom() must have been applied to the transform.");

            // The loaded room's corners span roughly (0,0)-(4,3), so a framed
            // camera must land close to that, nowhere near the (500,500,500)
            // drift position set above.
            float distanceFromDrift = Vector3.Distance(cameraGo.transform.position, new Vector3(500f, 500f, 500f));
            Assert.Greater(distanceFromDrift, 400f,
                "The camera must have moved away from where it drifted to, back toward the loaded room.");
            Assert.Less(Vector3.Distance(cameraGo.transform.position, new Vector3(2f, 0f, 1.5f)), 60f,
                "The framed camera must land near the loaded room, within OrbitCameraRig's own distance clamp.");

            cameraController.Detach();
            roomRenderer.Detach();
            Object.DestroyImmediate(hudGo);
            Object.DestroyImmediate(cameraGo);
            Object.DestroyImmediate(roomRendererGo);
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            foreach (Transform child in root)
            {
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
