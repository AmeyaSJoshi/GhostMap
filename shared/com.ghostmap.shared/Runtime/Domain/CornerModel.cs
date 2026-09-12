using System;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// One floor corner of the room, in GhostMap coordinates.
    ///
    /// Rules:
    /// <list type="bullet">
    /// <item>corners are ordered clockwise or counter-clockwise;</item>
    /// <item>the order must not change after finalization;</item>
    /// <item><c>position.y</c> must be 0 within tolerance.</item>
    /// </list>
    ///
    /// Walls are derived from consecutive corners, so corner order is load
    /// bearing: reordering corners silently reshapes the room.
    /// </summary>
    [Serializable]
    public sealed class CornerModel
    {
        public string id;
        public Vec3Dto position;
    }
}
