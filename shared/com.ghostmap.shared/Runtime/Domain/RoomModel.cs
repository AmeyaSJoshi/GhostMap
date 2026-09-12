using System;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// The complete structured room.
    ///
    /// Walls are deliberately NOT serialized. They are derived from consecutive
    /// corners, which prevents duplicated state from disagreeing. For four
    /// corners the walls are:
    ///
    /// <code>
    /// wall 0 = corner 0 -> corner 1
    /// wall 1 = corner 1 -> corner 2
    /// wall 2 = corner 2 -> corner 3
    /// wall 3 = corner 3 -> corner 0
    /// </code>
    ///
    /// Arrays are used rather than lists because JsonUtility serializes arrays
    /// of concrete [Serializable] types reliably.
    /// </summary>
    [Serializable]
    public sealed class RoomModel
    {
        public string id;
        public string name;

        /// <summary>Floor-to-ceiling height in meters. Valid range is 2.0 to 4.0.</summary>
        public float heightM;

        public CornerModel[] corners;
        public OpeningModel[] openings;
        public SceneObjectModel[] objects;
    }
}
