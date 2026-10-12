using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// The demo room's layout, in world metres (the room is centred on the
    /// origin, floor at y = 0). <see cref="SimulatedRoom"/> builds its meshes
    /// from it and <see cref="SimulatedPlanes"/> its AR planes, so the two
    /// cannot disagree.
    /// </summary>
    public static class SimulatedLayout
    {
        public const float WidthM = SimulatedRoom.RoomWidthM;
        public const float DepthM = SimulatedRoom.RoomDepthM;
        public const float HeightM = SimulatedRoom.RoomHeightM;

        public static float HalfWidth => WidthM * 0.5f;
        public static float HalfDepth => DepthM * 0.5f;

        /// <summary>On the west wall (x = -half width), facing +X.</summary>
        public static readonly Bounds Door = new Bounds(new Vector3(-WidthM * 0.5f + 0.01f, 1.0f, 0.6f), new Vector3(0.02f, 2.0f, 0.9f));

        /// <summary>On the east wall (x = +half width), facing -X.</summary>
        public static readonly Bounds Window = new Bounds(new Vector3(WidthM * 0.5f - 0.01f, 1.45f, -0.2f), new Vector3(0.02f, 1.0f, 1.3f));

        public static readonly Bounds Bed = new Bounds(new Vector3(WidthM * 0.5f - 0.75f, 0.25f, DepthM * 0.5f - 1.05f), new Vector3(1.4f, 0.5f, 2.0f));
        public static readonly Bounds Desk = new Bounds(new Vector3(-WidthM * 0.5f + 0.45f, 0.37f, -DepthM * 0.5f + 0.9f), new Vector3(0.7f, 0.74f, 1.3f));
    }

    /// <summary>
    /// Task R9: the AR planes ARKit would report in the demo room: four
    /// labelled walls, a door and a window frame, the ceiling, the floor and
    /// the furniture tops. Without them demo mode's automatic room scan never
    /// saw a wall and could not finish.
    ///
    /// <para>Like ARKit, a plane is reported only once the camera has looked
    /// at it (<see cref="MarkSeen"/>), so the scan's "keep turning" guidance
    /// behaves as it does on a phone. Plain C#, so the planes are
    /// EditMode-tested.</para>
    /// </summary>
    public sealed class SimulatedPlanes
    {
        /// <summary>A plane counts as seen when one of these points across it is in view.</summary>
        private static readonly float[] SampleFractions = { 0.1f, 0.5f, 0.9f };

        private const float MinViewDepthM = 0.1f;

        private readonly List<Plane> planes = BuildAll();

        private sealed class Plane
        {
            public DetectedSurface Surface;
            public Vector3[] Samples;
            public bool Seen;
        }

        /// <summary>Every plane in the room, seen or not.</summary>
        public static IReadOnlyList<DetectedSurface> All()
        {
            var all = new List<DetectedSurface>();
            foreach (Plane plane in BuildAll())
            {
                all.Add(plane.Surface);
            }

            return all;
        }

        public int SeenCount
        {
            get
            {
                int count = 0;
                foreach (Plane plane in planes)
                {
                    count += plane.Seen ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>Marks every plane the camera can see now. Call once per frame.</summary>
        /// <param name="worldToViewport">A camera's WorldToViewportPoint: x, y in 0..1 on screen, z the depth.</param>
        public void MarkSeen(Func<Vector3, Vector3> worldToViewport)
        {
            foreach (Plane plane in planes)
            {
                if (plane.Seen)
                {
                    continue;
                }

                foreach (Vector3 sample in plane.Samples)
                {
                    Vector3 view = worldToViewport(sample);
                    if (view.z > MinViewDepthM && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f)
                    {
                        plane.Seen = true;
                        break;
                    }
                }
            }
        }

        /// <summary>The planes seen so far, as <see cref="ISpatialProvider.TryGetDetectedSurfaces"/> reports them.</summary>
        public void CopySeen(List<DetectedSurface> into)
        {
            into.Clear();
            foreach (Plane plane in planes)
            {
                if (plane.Seen)
                {
                    into.Add(plane.Surface);
                }
            }
        }

        // -------------------------------------------------------------------

        private static List<Plane> BuildAll()
        {
            float w = SimulatedLayout.WidthM;
            float d = SimulatedLayout.DepthM;
            float h = SimulatedLayout.HeightM;
            float hw = SimulatedLayout.HalfWidth;
            float hd = SimulatedLayout.HalfDepth;
            ulong id = 0x6768_6f73_7400_0100;

            var all = new List<Plane>
            {
                // Walls: local Y is the normal, into the room; local Z runs up.
                Vertical(id++, new Vector3(0f, h * 0.5f, hd), Vector3.back, w, h, PlaneClassifications.WallFace),
                Vertical(id++, new Vector3(0f, h * 0.5f, -hd), Vector3.forward, w, h, PlaneClassifications.WallFace),
                Vertical(id++, new Vector3(hw, h * 0.5f, 0f), Vector3.left, d, h, PlaneClassifications.WallFace),
                Vertical(id++, new Vector3(-hw, h * 0.5f, 0f), Vector3.right, d, h, PlaneClassifications.WallFace),

                // Openings, a few millimetres proud of their walls.
                Vertical(id++, new Vector3(-hw + 0.02f, SimulatedLayout.Door.center.y, SimulatedLayout.Door.center.z), Vector3.right,
                    SimulatedLayout.Door.size.z, SimulatedLayout.Door.size.y, PlaneClassifications.DoorFrame),
                Vertical(id++, new Vector3(hw - 0.02f, SimulatedLayout.Window.center.y, SimulatedLayout.Window.center.z), Vector3.left,
                    SimulatedLayout.Window.size.z, SimulatedLayout.Window.size.y, PlaneClassifications.WindowFrame),

                Horizontal(id++, new Vector3(0f, h, 0f), w, d, PlaneAlignment.HorizontalDown, PlaneClassifications.Ceiling),
                Horizontal(id++, Vector3.zero, w, d, PlaneAlignment.HorizontalUp, PlaneClassifications.Floor),
                Top(id++, SimulatedLayout.Bed, PlaneClassifications.None),
                Top(id++, SimulatedLayout.Desk, PlaneClassifications.Table)
            };

            return all;
        }

        private static Plane Vertical(ulong id, Vector3 center, Vector3 normal, float width, float height, PlaneClassifications label)
        {
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, normal);
            Vector3 along = rotation * Vector3.right;
            return Make(id, center, rotation, new Vector2(width, height), PlaneAlignment.Vertical, label, along * width, Vector3.up * height);
        }

        private static Plane Horizontal(ulong id, Vector3 center, float width, float depth, PlaneAlignment alignment, PlaneClassifications label)
        {
            Quaternion rotation = Quaternion.LookRotation(Vector3.forward, alignment == PlaneAlignment.HorizontalDown ? Vector3.down : Vector3.up);
            return Make(id, center, rotation, new Vector2(width, depth), alignment, label, Vector3.right * width, Vector3.forward * depth);
        }

        private static Plane Top(ulong id, Bounds box, PlaneClassifications label)
            => Horizontal(id, new Vector3(box.center.x, box.max.y, box.center.z), box.size.x, box.size.z, PlaneAlignment.HorizontalUp, label);

        private static Plane Make(
            ulong id, Vector3 center, Quaternion rotation, Vector2 extents, PlaneAlignment alignment,
            PlaneClassifications label, Vector3 spanU, Vector3 spanV)
        {
            var samples = new List<Vector3>();
            foreach (float u in SampleFractions)
            {
                foreach (float v in SampleFractions)
                {
                    samples.Add(center + spanU * (u - 0.5f) + spanV * (v - 0.5f));
                }
            }

            return new Plane
            {
                Surface = new DetectedSurface(new TrackableId(id, 2), center, rotation, extents, alignment, label),
                Samples = samples.ToArray()
            };
        }
    }
}
