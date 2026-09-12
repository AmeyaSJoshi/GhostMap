using UnityEngine;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// One wall, derived from two consecutive corners.
    ///
    /// Walls are never serialized. This type is produced on demand by
    /// <c>RoomGeometry.BuildWalls</c> so that wall state cannot drift out of
    /// agreement with the corners it comes from.
    /// </summary>
    public readonly struct WallDefinition
    {
        public string StartCornerId { get; }
        public string EndCornerId { get; }

        public Vector3 Start { get; }
        public Vector3 End { get; }

        /// <summary>Unit vector pointing from <see cref="Start"/> to <see cref="End"/>.</summary>
        public Vector3 Tangent { get; }

        public float LengthM { get; }

        public WallDefinition(
            string startCornerId,
            string endCornerId,
            Vector3 start,
            Vector3 end)
        {
            StartCornerId = startCornerId;
            EndCornerId = endCornerId;
            Start = start;
            End = end;

            Vector3 delta = end - start;
            LengthM = delta.magnitude;
            Tangent = LengthM > Mathf.Epsilon ? delta / LengthM : Vector3.right;
        }

        public override string ToString()
            => $"Wall {StartCornerId}->{EndCornerId} ({LengthM:F2} m)";
    }
}
