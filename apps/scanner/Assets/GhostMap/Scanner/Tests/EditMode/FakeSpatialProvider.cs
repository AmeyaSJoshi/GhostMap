using GhostMap.Scanner.AR;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// A scripted <see cref="ISpatialProvider"/>. Every floor-lock rule can be
    /// driven from here, so none of them needs a phone to verify.
    /// </summary>
    internal sealed class FakeSpatialProvider : ISpatialProvider
    {
        public bool IsTrackingGood { get; set; } = true;

        public Vector2 CenterScreenPoint { get; set; } = new Vector2(540f, 1200f);

        public bool HasCameraPose { get; set; } = true;

        public Pose CameraPose { get; set; } =
            new Pose(new Vector3(0f, 1.5f, 0f), Quaternion.Euler(30f, 0f, 0f));

        public bool HasFloorHit { get; set; } = true;

        public Vector3 FloorHitWorldPosition { get; set; } = Vector3.zero;

        public PlaneAlignment FloorHitAlignment { get; set; } = PlaneAlignment.HorizontalUp;

        public Ray ScreenRay { get; set; } = new Ray(Vector3.zero, Vector3.forward);

        /// <summary>Screen point the last <see cref="TryGetFloorHit"/> was asked about.</summary>
        public Vector2 LastRaycastScreenPoint { get; private set; }

        /// <summary>
        /// How many AR plane raycasts have been asked for. Task S3 corner
        /// capture must not need any: plane extents lag behind the room and
        /// stop at furniture, so a corner is routinely off every detected
        /// plane. Counting them is how that stays true.
        /// </summary>
        public int FloorHitRequestCount { get; private set; }

        /// <summary>Screen point the last <see cref="GetScreenRay"/> was asked about.</summary>
        public Vector2 LastScreenRayPoint { get; private set; }

        public int ScreenRayRequestCount { get; private set; }

        public bool TryGetFloorHit(Vector2 screenPoint, out FloorHit hit)
        {
            LastRaycastScreenPoint = screenPoint;
            FloorHitRequestCount++;

            if (!HasFloorHit)
            {
                hit = default;
                return false;
            }

            hit = new FloorHit(FloorHitWorldPosition, FloorHitAlignment, default);
            return true;
        }

        public Ray GetScreenRay(Vector2 screenPoint)
        {
            LastScreenRayPoint = screenPoint;
            ScreenRayRequestCount++;
            return ScreenRay;
        }

        public bool TryGetCameraPose(out Pose pose)
        {
            pose = CameraPose;
            return HasCameraPose;
        }
    }
}
