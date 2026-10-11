using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Tasks V2-V3: wall placement for the renderer. The geometry now lives in
    /// the shared <see cref="WallLayout"/> (moved in task R6 so the phone's
    /// room exporter builds identical walls); this keeps the Viewer's name.
    /// </summary>
    public static class WallRenderer
    {
        /// <summary>Wall thickness per implementation plan section 12.3.</summary>
        public const float WallThicknessM = WallLayout.WallThicknessM;

        public static IReadOnlyList<WallRenderSpec> BuildWalls(RoomModel room)
            => WallLayout.BuildWalls(room);

        public static IReadOnlyList<WallRenderSpec> BuildWalls(RoomModel room, IList<string> diagnostics)
            => WallLayout.BuildWalls(room, diagnostics);
    }
}
