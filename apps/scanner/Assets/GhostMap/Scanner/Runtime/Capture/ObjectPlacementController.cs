using System;
using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// Why an object placement, adjustment or undo was refused.
    /// <see cref="None"/> means it succeeded.
    /// </summary>
    public enum ObjectPlacementRejection
    {
        None = 0,

        /// <summary>There is no GhostMap frame yet. Lock the floor first.</summary>
        FloorNotLocked,

        /// <summary>The session is not tracking, or reports a not-tracking reason.</summary>
        TrackingNotGood,

        /// <summary>The scan phase does not permit this operation.</summary>
        WrongPhase,

        /// <summary>The selected type is not one of the MVP's supported furniture types.</summary>
        UnsupportedType,

        /// <summary>The center-screen ray is parallel to the floor, or aimed away from it.</summary>
        RayMissedFloor,

        /// <summary>The shared <see cref="FurnitureValidator"/> refused the candidate object.</summary>
        ValidationFailed,

        /// <summary>Undo was asked for with no object placed.</summary>
        NoObjectsToUndo,

        /// <summary>An adjustment referenced an object index that does not exist.</summary>
        IndexOutOfRange
    }

    /// <summary>
    /// Task S5 Part 2: places parametric furniture on the locked GhostMap
    /// floor plane, exactly as implementation plan section 11.1 describes —
    /// intersect the center-screen ray with the floor, apply the type's
    /// default dimensions, and let the user adjust width, depth, height and
    /// yaw.
    ///
    /// <para><b>No mesh scanning, no visual reconstruction.</b> Furniture is
    /// parametric structured data: a center, a yaw and three dimensions. This
    /// class never inspects camera pixels for shape.</para>
    ///
    /// <para><b>The frame is read, never written.</b> Nothing here can move
    /// the S2 frame; it is reached through a read-only reference exactly like
    /// every other Task S3-S5 controller.</para>
    ///
    /// <para>Validation is the shared package's: every candidate and every
    /// adjustment runs <see cref="FurnitureValidator.Validate"/>, and the MVP
    /// default dimensions come from <see cref="FurnitureValidator.TryGetDefaultDimensions"/>
    /// rather than a scanner-owned copy (<c>AGENTS.md</c> rules 2 and 3).</para>
    ///
    /// <para>Plain C# rather than a MonoBehaviour, for the same reason as
    /// every other capture controller: every rule below is testable without a
    /// device. <see cref="UI.ObjectPlacementHud"/> is the scene-facing
    /// wrapper.</para>
    /// </summary>
    public sealed class ObjectPlacementController
    {
        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly List<SceneObjectModel> objects = new List<SceneObjectModel>();

        public ObjectPlacementController(ISpatialProvider provider, FloorLockController floorLock)
        {
            this.provider = provider;
            this.floorLock = floorLock;
            SelectedType = FurnitureValidator.SupportedTypes[0];
        }

        /// <summary>The frame Task S2 locked, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>The furniture type the next placement will use.</summary>
        public string SelectedType { get; private set; }

        /// <summary>Objects placed so far, in placement order.</summary>
        public IReadOnlyList<SceneObjectModel> Objects => objects;

        public int ObjectCount => objects.Count;

        /// <summary>Why the most recent operation was refused.</summary>
        public ObjectPlacementRejection LastRejection { get; private set; }

        /// <summary>
        /// The shared validator's message for the most recent
        /// <see cref="ObjectPlacementRejection.ValidationFailed"/>, shown to
        /// the user verbatim. Empty when the last operation did not fail the
        /// rule.
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Whether placement should be enabled. Whether the crosshair actually
        /// hits the floor is only known once the attempt runs.
        /// </summary>
        public bool CanPlace => Frame != null && provider.IsTrackingGood;

        // -------------------------------------------------------------------
        // Type selection
        // -------------------------------------------------------------------

        /// <summary>Chooses the furniture type the next placement will use.</summary>
        public bool SetType(string type)
        {
            if (!FurnitureValidator.IsSupportedType(type))
            {
                return false;
            }

            SelectedType = type;
            return true;
        }

        // -------------------------------------------------------------------
        // The floor ray
        // -------------------------------------------------------------------

        /// <summary>
        /// Projects the center-screen camera ray onto the locked floor plane
        /// and returns the result in Ghost coordinates, with Y forced to
        /// exactly zero — the same arithmetic and the same forcing
        /// <see cref="CornerCaptureController.TryProjectCrosshairToFloor"/>
        /// uses, so an object's floor reference is exact rather than within
        /// float noise of it.
        /// </summary>
        public bool TryProjectCrosshairToFloor(out Vector3 ghostPoint)
        {
            ghostPoint = Vector3.zero;

            GhostCoordinateFrame frame = Frame;

            if (frame == null)
            {
                return false;
            }

            Ray worldRay = provider.GetScreenRay(provider.CenterScreenPoint);

            if (!RayPlaneMath.TryIntersectHorizontalPlane(
                    worldRay, frame.FloorWorldY, out Vector3 worldPoint))
            {
                return false;
            }

            Vector3 ghost = frame.WorldToGhost(worldPoint);
            ghostPoint = new Vector3(ghost.x, 0f, ghost.z);
            return true;
        }

        // -------------------------------------------------------------------
        // Placement
        // -------------------------------------------------------------------

        /// <summary>
        /// Places an object of <see cref="SelectedType"/> under the crosshair,
        /// with the type's default dimensions and yaw 0.
        /// </summary>
        public bool TryPlaceObject(out ObjectPlacementRejection rejection)
        {
            LastError = string.Empty;

            rejection = EvaluatePlacement(out SceneObjectModel candidate);
            LastRejection = rejection;

            if (rejection != ObjectPlacementRejection.None)
            {
                return false;
            }

            objects.Add(candidate);
            return true;
        }

        private ObjectPlacementRejection EvaluatePlacement(out SceneObjectModel candidate)
        {
            candidate = null;

            if (Frame == null)
            {
                return ObjectPlacementRejection.FloorNotLocked;
            }

            if (!provider.IsTrackingGood)
            {
                return ObjectPlacementRejection.TrackingNotGood;
            }

            if (!FurnitureValidator.IsSupportedType(SelectedType))
            {
                return ObjectPlacementRejection.UnsupportedType;
            }

            if (!TryProjectCrosshairToFloor(out Vector3 ghost))
            {
                return ObjectPlacementRejection.RayMissedFloor;
            }

            FurnitureValidator.TryGetDefaultDimensions(
                SelectedType, out float widthM, out float depthM, out float heightM);

            var model = new SceneObjectModel
            {
                id = Guid.NewGuid().ToString(),
                type = SelectedType,
                center = Vec3Dto.FromVector3(ghost),
                yawDeg = 0f,
                widthM = widthM,
                depthM = depthM,
                heightM = heightM
            };

            ValidationResult validation = FurnitureValidator.Validate(model);

            if (!validation.IsValid)
            {
                LastError = validation.Error;
                return ObjectPlacementRejection.ValidationFailed;
            }

            candidate = model;
            return ObjectPlacementRejection.None;
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------

        /// <summary>Removes the most recently placed object.</summary>
        public bool TryUndoLastObject(out ObjectPlacementRejection rejection)
        {
            LastError = string.Empty;

            if (objects.Count == 0)
            {
                rejection = ObjectPlacementRejection.NoObjectsToUndo;
                LastRejection = rejection;
                return false;
            }

            objects.RemoveAt(objects.Count - 1);

            rejection = ObjectPlacementRejection.None;
            LastRejection = rejection;
            return true;
        }

        // -------------------------------------------------------------------
        // Adjustment
        // -------------------------------------------------------------------

        /// <summary>Adjusts width. Rejected — and left unchanged — if the result would be invalid.</summary>
        public bool TrySetWidth(int index, float widthM, out ObjectPlacementRejection rejection)
            => TryAdjust(index, m => { m.widthM = widthM; return m; }, out rejection);

        /// <summary>Adjusts depth. Rejected — and left unchanged — if the result would be invalid.</summary>
        public bool TrySetDepth(int index, float depthM, out ObjectPlacementRejection rejection)
            => TryAdjust(index, m => { m.depthM = depthM; return m; }, out rejection);

        /// <summary>Adjusts height. Rejected — and left unchanged — if the result would be invalid.</summary>
        public bool TrySetHeight(int index, float heightM, out ObjectPlacementRejection rejection)
            => TryAdjust(index, m => { m.heightM = heightM; return m; }, out rejection);

        /// <summary>Adjusts yaw, in degrees about +Y. Rejected — and left unchanged — if the result would be invalid.</summary>
        public bool TrySetYaw(int index, float yawDeg, out ObjectPlacementRejection rejection)
            => TryAdjust(index, m => { m.yawDeg = yawDeg; return m; }, out rejection);

        private bool TryAdjust(
            int index,
            Func<SceneObjectModel, SceneObjectModel> mutate,
            out ObjectPlacementRejection rejection)
        {
            LastError = string.Empty;

            if (index < 0 || index >= objects.Count)
            {
                rejection = ObjectPlacementRejection.IndexOutOfRange;
                LastRejection = rejection;
                return false;
            }

            SceneObjectModel candidate = mutate(Clone(objects[index]));
            ValidationResult validation = FurnitureValidator.Validate(candidate);

            if (!validation.IsValid)
            {
                LastError = validation.Error;
                rejection = ObjectPlacementRejection.ValidationFailed;
                LastRejection = rejection;
                return false;
            }

            objects[index] = candidate;

            rejection = ObjectPlacementRejection.None;
            LastRejection = rejection;
            return true;
        }

        private static SceneObjectModel Clone(SceneObjectModel source)
        {
            return new SceneObjectModel
            {
                id = source.id,
                type = source.type,
                center = source.center,
                yawDeg = source.yawDeg,
                widthM = source.widthM,
                depthM = source.depthM,
                heightM = source.heightM
            };
        }

        // -------------------------------------------------------------------
        // Snapshot support
        // -------------------------------------------------------------------

        /// <summary>
        /// Objects as a fresh array of fresh <see cref="SceneObjectModel"/>
        /// values, for the same reason <c>CornerCaptureController.CopyCorners</c>
        /// copies: a published snapshot is a value, not a window onto live
        /// state.
        /// </summary>
        public SceneObjectModel[] CopyObjects()
        {
            var copy = new SceneObjectModel[objects.Count];

            for (int i = 0; i < objects.Count; i++)
            {
                copy[i] = Clone(objects[i]);
            }

            return copy;
        }
    }
}
