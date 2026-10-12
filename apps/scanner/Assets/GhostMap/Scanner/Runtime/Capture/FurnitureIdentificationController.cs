using System;
using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Capture
{
    /// <summary>One piece of furniture the detector keeps seeing (task R4).</summary>
    public sealed class IdentifiedFurniture
    {
        internal IdentifiedFurniture(int id, string type, string className)
        {
            Id = id;
            Type = type;
            ClassName = className;
        }

        public int Id { get; }

        /// <summary>GhostMap type, e.g. <c>desk</c>.</summary>
        public string Type { get; internal set; }

        /// <summary>What YOLO called it, e.g. <c>dining table</c> (kept as the label in task R5).</summary>
        public string ClassName { get; }

        /// <summary>Ghost-space centre on the floor.</summary>
        public Vector3 CenterGhost { get; internal set; }

        /// <summary>The measured surface it sits on, when one matched.</summary>
        public FurnitureCandidate? Surface { get; internal set; }

        public int Hits { get; internal set; }
        public float BestConfidence { get; internal set; }
        public float LastSeenSeconds { get; internal set; }

        /// <summary>Seen often enough to offer (<see cref="FurnitureIdentificationController.MinHits"/>).</summary>
        public bool IsProposed => Hits >= FurnitureIdentificationController.MinHits;

        /// <summary>Task R11: a JPEG crop of the best sighting so far, or null.</summary>
        public byte[] Photo { get; internal set; }

        /// <summary>The detector's confidence in the sighting <see cref="Photo"/> shows.</summary>
        public float PhotoConfidence { get; internal set; }

        /// <summary>
        /// True when this frame's sighting is clearly better than the photo:
        /// photograph <see cref="PhotoBox"/> now and hand it to
        /// <see cref="FurnitureIdentificationController.SetPhoto"/>.
        /// </summary>
        public bool WantsPhoto { get; internal set; }

        /// <summary>This frame's screen box for the photo it wants.</summary>
        public Rect PhotoBox { get; internal set; }

        internal float WantedPhotoConfidence { get; set; }
    }

    /// <summary>
    /// Task R4 (ADR-0011): turns the detector's per-frame boxes into furniture
    /// the user can add with one tap. Plain C#; the detector is behind
    /// <see cref="IObjectDetector"/>, so every rule here is EditMode-tested.
    ///
    /// <para>It runs from floor lock on, so furniture seen while the user turns
    /// to scan the room is already found when the furniture step opens.</para>
    ///
    /// <para>For each detection: a ray through the box's bottom-centre meets the
    /// locked floor (the object's front edge on the floor); points further than
    /// <see cref="MaxRangeM"/>, outside the room once it exists, or on furniture
    /// already added are ignored; the measured ARKit surface under it
    /// (ADR-0006), if any, supplies size, height and yaw; the COCO class becomes
    /// a GhostMap type (<see cref="FurnitureClassMap"/>). A tracker joins
    /// detections of the same type within <see cref="SameObjectDistanceM"/>
    /// across frames and proposes an object after <see cref="MinHits"/>
    /// confident frames. A track not yet proposed is forgotten after
    /// <see cref="ForgetAfterSeconds"/> unseen; a proposal is kept until it is
    /// added, removed, or found to be outside the room, because the user has
    /// usually turned away from it by the time they add furniture.</para>
    ///
    /// <para><b>The model only names things.</b> Every number comes from ARKit
    /// and GhostMap's geometry, or from the type's default size when no
    /// surface matched (AGENTS.md rule 6).</para>
    /// </summary>
    public sealed class FurnitureIdentificationController
    {
        public const float MinConfidence = 0.5f;
        public const int MinHits = 5;
        public const float ForgetAfterSeconds = 3f;
        public const float SameObjectDistanceM = 0.5f;

        /// <summary>A floor point further than this from the camera is too far to place reliably.</summary>
        public const float MaxRangeM = 8f;

        /// <summary>A detection within this distance of a surface's footprint is that surface.</summary>
        public const float SurfaceMatchMarginM = 0.3f;

        /// <summary>A new photo is taken only when a sighting beats the current one by this much confidence.</summary>
        public const float PhotoImprovement = 0.05f;

        /// <summary>How far a smoothed position moves toward each new sighting.</summary>
        private const float Smoothing = 0.3f;

        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly CornerCaptureController corners;
        private readonly List<IdentifiedFurniture> tracks = new List<IdentifiedFurniture>();
        private readonly List<Vector3> footprint = new List<Vector3>();
        private readonly HashSet<IdentifiedFurniture> updatedThisFrame = new HashSet<IdentifiedFurniture>();
        private int nextId = 1;

        public FurnitureIdentificationController(
            ISpatialProvider provider,
            FloorLockController floorLock,
            CornerCaptureController corners)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.floorLock = floorLock ?? throw new ArgumentNullException(nameof(floorLock));
            this.corners = corners ?? throw new ArgumentNullException(nameof(corners));
        }

        /// <summary>Everything being tracked, proposed or not.</summary>
        public IReadOnlyList<IdentifiedFurniture> Tracked => tracks;

        /// <summary>Furniture seen often enough to offer, oldest first.</summary>
        public IReadOnlyList<IdentifiedFurniture> Proposals
        {
            get
            {
                var proposed = new List<IdentifiedFurniture>();
                foreach (IdentifiedFurniture track in tracks)
                {
                    if (track.IsProposed)
                    {
                        proposed.Add(track);
                    }
                }

                return proposed;
            }
        }

        /// <summary>Detector frames processed, for the Details readout.</summary>
        public int FramesProcessed { get; private set; }

        /// <summary>
        /// Folds one detector frame in.
        /// </summary>
        /// <param name="detections">This frame's boxes.</param>
        /// <param name="surfaces">The measured surface candidates (<see cref="FurnitureDetectionController.Candidates"/>).</param>
        /// <param name="existing">Objects already in the room; nothing is proposed on top of them.</param>
        /// <param name="nowSeconds">A monotonic clock.</param>
        /// <returns>False when identification cannot run (no floor lock, tracking lost).</returns>
        public bool Update(
            IReadOnlyList<ObjectDetection> detections,
            IReadOnlyList<FurnitureCandidate> surfaces,
            IReadOnlyList<SceneObjectModel> existing,
            float nowSeconds)
        {
            GhostCoordinateFrame frame = floorLock.Frame;

            if (frame == null || !provider.IsTrackingGood)
            {
                return false;
            }

            FramesProcessed++;
            bool hasRoom = RebuildFootprint();
            updatedThisFrame.Clear();

            foreach (IdentifiedFurniture track in tracks)
            {
                track.WantsPhoto = false;
            }

            Vector3 forward = FlatForward(frame);
            bool hasEye = TryGetEye(frame, out Vector3 eye);

            foreach (ObjectDetection detection in detections ?? Array.Empty<ObjectDetection>())
            {
                if (detection.Confidence < MinConfidence || !FurnitureClassMap.IsFurniture(detection.ClassName))
                {
                    continue;
                }

                if (!TryFloorPoint(frame, detection.ScreenBox, out Vector3 floorPoint)
                    || (hasEye && HorizontalDistance(eye, floorPoint) > MaxRangeM)
                    || (hasRoom && !RoomGeometry.ContainsPointXZ(footprint, floorPoint)))
                {
                    continue;
                }

                FurnitureCandidate? surface = MatchSurface(detection.ClassName, floorPoint, surfaces);

                if (!FurnitureClassMap.TryMap(detection.ClassName, surface, out string type))
                {
                    continue;
                }

                Vector3 center = surface.HasValue ? surface.Value.CenterGhost : EstimateCenter(floorPoint, forward, type);

                if (IsOnExistingObject(center, type, existing))
                {
                    continue;
                }

                Fold(detection, type, center, surface, nowSeconds);
            }

            ResolveSurfaces(surfaces);
            Forget(nowSeconds, existing, hasRoom);
            return true;
        }

        /// <summary>
        /// The object a proposal becomes: the measured surface's size and yaw
        /// when one matched (a seat's height raised to the type's default),
        /// otherwise the type's default size facing +Z.
        /// </summary>
        public static bool TryBuild(IdentifiedFurniture item, out SceneObjectModel model, out string error)
        {
            model = null;

            float width;
            float depth;
            float height;
            float yaw = 0f;

            if (item.Surface.HasValue)
            {
                FurnitureCandidate surface = item.Surface.Value;
                width = surface.WidthM;
                depth = surface.DepthM;
                height = surface.HeightM;
                yaw = surface.YawDeg;

                // A seat is not the top of a chair: keep at least the default height.
                if (FurnitureClassMap.SurfaceIsSeat(item.Type)
                    && FurnitureValidator.TryGetDefaultDimensions(item.Type, out _, out _, out float defaultHeight))
                {
                    height = Mathf.Max(height, defaultHeight);
                }
            }
            else if (!FurnitureValidator.TryGetDefaultDimensions(item.Type, out width, out depth, out height))
            {
                error = $"'{item.Type}' has no default size.";
                return false;
            }

            var built = new SceneObjectModel
            {
                id = Guid.NewGuid().ToString(),
                type = item.Type,
                center = Vec3Dto.FromVector3(new Vector3(item.CenterGhost.x, 0f, item.CenterGhost.z)),
                yawDeg = yaw,
                widthM = width,
                depthM = depth,
                heightM = height
            };

            ValidationResult result = FurnitureValidator.Validate(built);

            if (!result.IsValid)
            {
                error = result.Error;
                return false;
            }

            model = built;
            error = string.Empty;
            return true;
        }

        /// <summary>Task R11: tracks whose sighting this frame should be photographed.</summary>
        public IReadOnlyList<IdentifiedFurniture> WantingPhotos()
        {
            var wanting = new List<IdentifiedFurniture>();
            foreach (IdentifiedFurniture track in tracks)
            {
                if (track.WantsPhoto)
                {
                    wanting.Add(track);
                }
            }

            return wanting;
        }

        /// <summary>
        /// Task R11: keeps the photo taken of a track's sighting this frame.
        /// Refused for anything but an acceptable JPEG
        /// (<see cref="ObjectPhotoRules.IsAcceptable"/>) or a track that did
        /// not ask for one.
        /// </summary>
        public bool SetPhoto(int id, byte[] jpeg)
        {
            if (!ObjectPhotoRules.IsAcceptable(jpeg))
            {
                return false;
            }

            foreach (IdentifiedFurniture track in tracks)
            {
                if (track.Id == id && track.WantsPhoto)
                {
                    track.Photo = jpeg;
                    track.PhotoConfidence = track.WantedPhotoConfidence;
                    track.WantsPhoto = false;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The proposal sitting on a measured surface, if any.</summary>
        public IdentifiedFurniture ProposalOn(TrackableId surfaceId)
        {
            foreach (IdentifiedFurniture track in tracks)
            {
                if (track.IsProposed && track.Surface.HasValue && track.Surface.Value.SurfaceId == surfaceId)
                {
                    return track;
                }
            }

            return null;
        }

        /// <summary>The identified type for each matched surface, for the surface candidates' type.</summary>
        public IReadOnlyDictionary<TrackableId, string> SurfaceTypes()
        {
            var types = new Dictionary<TrackableId, string>();

            foreach (IdentifiedFurniture track in tracks)
            {
                if (track.IsProposed && track.Surface.HasValue)
                {
                    types[track.Surface.Value.SurfaceId] = track.Type;
                }
            }

            return types;
        }

        /// <summary>Stops offering one item (it was added, or the user removed it).</summary>
        public bool Remove(int id)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].Id == id)
                {
                    tracks.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            tracks.Clear();
            FramesProcessed = 0;
        }

        /// <summary>
        /// "bed, desk, 2 chairs": the proposals as the guide says them, in the
        /// order they were first seen.
        /// </summary>
        public static string Describe(IReadOnlyList<IdentifiedFurniture> proposals)
        {
            var counts = new List<KeyValuePair<string, int>>();

            foreach (IdentifiedFurniture item in proposals)
            {
                string name = item.Type == "generic" ? item.ClassName : item.Type;
                int index = counts.FindIndex(pair => pair.Key == name);

                if (index < 0)
                {
                    counts.Add(new KeyValuePair<string, int>(name, 1));
                }
                else
                {
                    counts[index] = new KeyValuePair<string, int>(name, counts[index].Value + 1);
                }
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                parts.Add(pair.Value == 1 ? pair.Key : $"{pair.Value} {Plural(pair.Key)}");
            }

            return string.Join(", ", parts);
        }

        // -------------------------------------------------------------------

        private bool TryFloorPoint(GhostCoordinateFrame frame, Rect screenBox, out Vector3 ghostPoint)
        {
            Ray ghostRay = frame.WorldRayToGhost(provider.GetScreenRay(DetectionGeometry.BottomCentre(screenBox)));

            if (!RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out ghostPoint))
            {
                return false;
            }

            ghostPoint.y = 0f;
            return true;
        }

        /// <summary>
        /// The nearest surface whose footprint, grown by the margin, holds the
        /// point, and whose height suits the class
        /// (<see cref="FurnitureClassMap.CanSitUnder"/>).
        /// </summary>
        private static FurnitureCandidate? MatchSurface(
            string className, Vector3 floorPoint, IReadOnlyList<FurnitureCandidate> surfaces)
        {
            FurnitureCandidate? best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; surfaces != null && i < surfaces.Count; i++)
            {
                FurnitureCandidate surface = surfaces[i];

                if (!FurnitureClassMap.CanSitUnder(className, surface.HeightM))
                {
                    continue;
                }

                Quaternion yaw = Quaternion.Euler(0f, surface.YawDeg, 0f);
                Vector3 offset = floorPoint - new Vector3(surface.CenterGhost.x, 0f, surface.CenterGhost.z);
                float along = Mathf.Abs(Vector3.Dot(offset, yaw * Vector3.right));
                float across = Mathf.Abs(Vector3.Dot(offset, yaw * Vector3.forward));

                if (along > surface.WidthM * 0.5f + SurfaceMatchMarginM || across > surface.DepthM * 0.5f + SurfaceMatchMarginM)
                {
                    continue;
                }

                float distance = new Vector2(offset.x, offset.z).magnitude;
                if (distance < bestDistance)
                {
                    best = surface;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// The box's bottom-centre meets the floor at the object's near edge;
        /// its centre is half a default depth further from the camera.
        /// </summary>
        private static Vector3 EstimateCenter(Vector3 floorPoint, Vector3 forward, string type)
        {
            float depth = FurnitureValidator.TryGetDefaultDimensions(type, out _, out float d, out _) ? d : 0f;
            return floorPoint + forward * (depth * 0.5f);
        }

        private Vector3 FlatForward(GhostCoordinateFrame frame)
        {
            if (!provider.TryGetCameraPose(out Pose pose))
            {
                return Vector3.zero;
            }

            Vector3 forward = frame.WorldDirectionToGhost(pose.rotation * Vector3.forward);
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.zero;
        }

        /// <summary>
        /// True when a point is inside an existing object's footprint, or
        /// within <see cref="SameObjectDistanceM"/> of one of the same type.
        /// </summary>
        public static bool IsOnExistingObject(Vector3 center, string type, IReadOnlyList<SceneObjectModel> existing)
        {
            for (int i = 0; existing != null && i < existing.Count; i++)
            {
                SceneObjectModel model = existing[i];
                Vector3 objectCenter = model.center.ToVector3();
                Vector3 offset = center - new Vector3(objectCenter.x, 0f, objectCenter.z);
                offset.y = 0f;

                if (model.type == type && offset.magnitude <= SameObjectDistanceM)
                {
                    return true;
                }

                Quaternion yaw = Quaternion.Euler(0f, model.yawDeg, 0f);
                if (Mathf.Abs(Vector3.Dot(offset, yaw * Vector3.right)) <= model.widthM * 0.5f
                    && Mathf.Abs(Vector3.Dot(offset, yaw * Vector3.forward)) <= model.depthM * 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        private void Fold(ObjectDetection detection, string type, Vector3 center, FurnitureCandidate? surface, float now)
        {
            IdentifiedFurniture match = null;
            float bestDistance = SameObjectDistanceM;

            foreach (IdentifiedFurniture track in tracks)
            {
                if (track.Type != type || updatedThisFrame.Contains(track))
                {
                    continue;
                }

                float distance = Vector3.Distance(track.CenterGhost, center);
                if (distance <= bestDistance)
                {
                    match = track;
                    bestDistance = distance;
                }
            }

            if (match == null)
            {
                match = new IdentifiedFurniture(nextId++, type, detection.ClassName) { CenterGhost = center };
                tracks.Add(match);
            }
            else
            {
                match.CenterGhost = surface.HasValue ? center : Vector3.Lerp(match.CenterGhost, center, Smoothing);
            }

            if (surface.HasValue)
            {
                match.Surface = surface;
            }

            match.Hits++;
            match.BestConfidence = Mathf.Max(match.BestConfidence, detection.Confidence);

            float photoBar = match.Photo == null ? 0f : match.PhotoConfidence + PhotoImprovement;
            if (detection.Confidence >= photoBar && (!match.WantsPhoto || detection.Confidence > match.WantedPhotoConfidence))
            {
                match.WantsPhoto = true;
                match.PhotoBox = detection.ScreenBox;
                match.WantedPhotoConfidence = detection.Confidence;
            }
            match.LastSeenSeconds = now;
            updatedThisFrame.Add(match);
        }

        private void Forget(float now, IReadOnlyList<SceneObjectModel> existing, bool hasRoom)
        {
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                IdentifiedFurniture track = tracks[i];
                bool stale = !track.IsProposed && now - track.LastSeenSeconds > ForgetAfterSeconds;
                bool outside = hasRoom && !RoomGeometry.ContainsPointXZ(footprint, track.CenterGhost);

                if (stale || outside || IsOnExistingObject(track.CenterGhost, track.Type, existing))
                {
                    tracks.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// A track first seen before the furniture step had no surfaces to
        /// match. Once surfaces are measured, one under its centre gives it its
        /// measured size and, for a dining table, its desk-or-table type.
        /// </summary>
        private void ResolveSurfaces(IReadOnlyList<FurnitureCandidate> surfaces)
        {
            if (surfaces == null || surfaces.Count == 0)
            {
                return;
            }

            foreach (IdentifiedFurniture track in tracks)
            {
                if (track.Surface.HasValue)
                {
                    continue;
                }

                FurnitureCandidate? surface = MatchSurface(track.ClassName, track.CenterGhost, surfaces);

                if (surface.HasValue && FurnitureClassMap.TryMap(track.ClassName, surface, out string type))
                {
                    track.Surface = surface;
                    track.CenterGhost = surface.Value.CenterGhost;
                    track.Type = type;
                }
            }
        }

        /// <summary>The room footprint, once the room is closed. False before.</summary>
        private bool RebuildFootprint()
        {
            footprint.Clear();

            if (!corners.IsComplete)
            {
                return false;
            }

            foreach (CornerModel corner in corners.Corners)
            {
                footprint.Add(corner.position.ToVector3());
            }

            return true;
        }

        private bool TryGetEye(GhostCoordinateFrame frame, out Vector3 eye)
        {
            if (!provider.TryGetCameraPose(out Pose pose))
            {
                eye = default;
                return false;
            }

            eye = frame.WorldToGhost(pose.position);
            return true;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
            => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static string Plural(string name)
        {
            if (name.EndsWith("ch", StringComparison.Ordinal) || name.EndsWith("sh", StringComparison.Ordinal))
            {
                return name + "es";
            }

            return name == "tv" ? "TVs" : name + "s";
        }
    }
}
