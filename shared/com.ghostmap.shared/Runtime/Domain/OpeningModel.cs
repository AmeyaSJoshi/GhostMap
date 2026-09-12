using System;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// A rectangular door or window cut into one wall.
    ///
    /// The parent wall is identified by the ordered pair of corner IDs it spans
    /// rather than by a wall ID, because walls are derived from corners and have
    /// no persisted identity of their own.
    ///
    /// Geometry is expressed in wall-local coordinates:
    /// <list type="bullet">
    /// <item><c>offsetM</c> is the distance along the wall from the start corner;</item>
    /// <item><c>sillHeightM</c> is the height of the opening's bottom edge above the floor;</item>
    /// <item>a door normally has <c>sillHeightM = 0</c>; a window has a positive sill;</item>
    /// <item>the opening must fit completely inside the wall.</item>
    /// </list>
    /// </summary>
    [Serializable]
    public sealed class OpeningModel
    {
        public string id;

        /// <summary>"door" or "window".</summary>
        public string type;

        public string wallStartCornerId;
        public string wallEndCornerId;

        /// <summary>Distance along the wall from the start corner, in meters.</summary>
        public float offsetM;

        public float widthM;

        /// <summary>Height of the opening's bottom edge above the floor, in meters.</summary>
        public float sillHeightM;

        public float heightM;
    }
}
