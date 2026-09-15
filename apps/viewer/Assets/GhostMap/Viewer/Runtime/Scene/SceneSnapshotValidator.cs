using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;

namespace GhostMap.Viewer.Scene
{
    /// <summary>
    /// The one place that checks a fully-deserialized <see cref="SceneSnapshot"/>
    /// is a legal room, by composing the shared package's own validators
    /// (<c>AGENTS.md</c> rule 3: never a Viewer reimplementation of the
    /// rules). Extracted from <see cref="ViewerSceneStore"/> in Task V6 so
    /// <c>Persistence.ScenePersistence</c> can run exactly the same check on a
    /// loaded file instead of duplicating it a second time.
    /// </summary>
    internal static class SceneSnapshotValidator
    {
        public static bool TryValidate(SceneSnapshot snapshot, out string error)
        {
            RoomModel room = snapshot?.room;

            if (room == null)
            {
                error = "Snapshot has no room.";
                return false;
            }

            ValidationResult roomResult = RoomValidator.ValidateRoom(room);
            if (!roomResult.IsValid)
            {
                error = roomResult.Error;
                return false;
            }

            OpeningModel[] openings = room.openings ?? Array.Empty<OpeningModel>();
            foreach (OpeningModel opening in openings)
            {
                ValidationResult openingResult = OpeningValidator.Validate(opening, room);
                if (!openingResult.IsValid)
                {
                    error = openingResult.Error;
                    return false;
                }
            }

            SceneObjectModel[] objects = room.objects ?? Array.Empty<SceneObjectModel>();
            foreach (SceneObjectModel sceneObject in objects)
            {
                ValidationResult objectResult = FurnitureValidator.Validate(sceneObject);
                if (!objectResult.IsValid)
                {
                    error = objectResult.Error;
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
