using System.Collections.Generic;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V4: fills the room's <c>Objects</c> container from
    /// <c>SceneSnapshot.room.objects</c>, one logical root per
    /// <see cref="SceneObjectModel"/>.
    ///
    /// Stateless on purpose. <see cref="RoomRenderer"/> destroys and rebuilds
    /// the whole <c>RenderedRoom</c> on every accepted snapshot, so "objects
    /// must not accumulate", "a removed object disappears" and "a duplicate
    /// revision does not duplicate objects" all fall out of that single
    /// rebuild rather than out of diffing logic that could drift.
    /// </summary>
    public static class FurnitureRenderer
    {
        /// <returns>How many objects were actually rendered.</returns>
        public static int Populate(
            RoomModel room,
            Transform objectsRoot,
            FurnitureFactory factory,
            IList<string> diagnostics)
        {
            if (room?.objects == null || objectsRoot == null || factory == null)
            {
                return 0;
            }

            int rendered = 0;

            for (int i = 0; i < room.objects.Length; i++)
            {
                if (factory.Create(room.objects[i], objectsRoot, diagnostics) != null)
                {
                    rendered++;
                }
            }

            return rendered;
        }
    }
}
