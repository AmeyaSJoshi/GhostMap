using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Scene;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V2: rebuilds the room shell (floor, ceiling, walls) whenever
    /// <see cref="ViewerSceneStore.Changed"/> fires.
    ///
    /// Deliberately listens only to the scene store's <c>Changed</c> event,
    /// never to the TCP server or a raw socket message (implementation plan
    /// section 17, Task V2's architecture note): the store has already
    /// resolved which snapshot is current, stale, or a harmless duplicate, so
    /// every <see cref="Rebuild"/> here is in response to a genuinely new
    /// accepted scene, and a disconnect — which never touches the store —
    /// leaves the last rendered room untouched with no extra bookkeeping.
    /// </summary>
    public sealed class RoomRenderer : MonoBehaviour
    {
        private static readonly Color FloorColor = new Color(0.55f, 0.45f, 0.35f);
        private static readonly Color CeilingColor = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color WallColor = new Color(0.75f, 0.72f, 0.65f);

        private readonly List<string> _diagnostics = new List<string>();

        private ViewerSceneStore _sceneStore;
        private Material _floorMaterial;
        private Material _ceilingMaterial;
        private Material _wallMaterial;

        public GameObject Root { get; private set; }

        public void Attach(ViewerSceneStore sceneStore)
        {
            Detach();

            _sceneStore = sceneStore;
            if (_sceneStore == null)
            {
                return;
            }

            _sceneStore.Changed += Rebuild;

            if (_sceneStore.Current != null)
            {
                Rebuild(_sceneStore.Current);
            }
        }

        public void Detach()
        {
            if (_sceneStore != null)
            {
                _sceneStore.Changed -= Rebuild;
                _sceneStore = null;
            }
        }

        private void OnDestroy()
        {
            Detach();
            DestroyUnityObject(_floorMaterial);
            DestroyUnityObject(_ceilingMaterial);
            DestroyUnityObject(_wallMaterial);
        }

        private void Rebuild(SceneSnapshot snapshot)
        {
            DestroyUnityObject(Root);

            Root = new GameObject("RenderedRoom");
            Root.transform.SetParent(transform, false);

            RoomModel room = snapshot?.room;

            BuildFloor(room);
            BuildCeiling(room);
            BuildWalls(room);

            var objectsGo = new GameObject("Objects");
            objectsGo.transform.SetParent(Root.transform, false);
        }

        private void BuildFloor(RoomModel room)
        {
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(Root.transform, false);

            if (FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh))
            {
                AttachMesh(floorGo, mesh, GetFloorMaterial());
            }
        }

        private void BuildCeiling(RoomModel room)
        {
            var ceilingGo = new GameObject("Ceiling");
            ceilingGo.transform.SetParent(Root.transform, false);

            if (FloorCeilingRenderer.TryBuildCeilingMesh(room, out Mesh mesh))
            {
                AttachMesh(ceilingGo, mesh, GetCeilingMaterial());
            }
        }

        private void BuildWalls(RoomModel room)
        {
            var wallsGo = new GameObject("Walls");
            wallsGo.transform.SetParent(Root.transform, false);

            _diagnostics.Clear();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room, _diagnostics);

            for (int i = 0; i < walls.Count; i++)
            {
                WallRenderSpec wall = walls[i];

                // The wall itself is a container, not geometry: V3 renders one
                // cuboid per solid segment left after its doors and windows are
                // cut out. A wall with no openings has exactly one segment,
                // placed exactly where V2's single cuboid was.
                var wallGo = new GameObject($"Wall_{i}_{wall.StartCornerId}_{wall.EndCornerId}");
                wallGo.transform.SetParent(wallsGo.transform, false);

                for (int s = 0; s < wall.Segments.Count; s++)
                {
                    WallSegmentSpec segment = wall.Segments[s];

                    var segmentGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    segmentGo.name = $"Segment_{s}";
                    segmentGo.transform.SetParent(wallGo.transform, false);
                    segmentGo.transform.position = segment.Position;
                    segmentGo.transform.rotation = segment.Rotation;
                    segmentGo.transform.localScale = segment.Scale;
                    segmentGo.GetComponent<MeshRenderer>().sharedMaterial = GetWallMaterial();
                }
            }

            for (int i = 0; i < _diagnostics.Count; i++)
            {
                // Openings are validated before a snapshot is accepted, so
                // reaching here means bad data arrived by another route. The
                // room still renders — the affected wall stays solid — but the
                // reason must not be swallowed.
                Debug.LogWarning($"[RoomRenderer] {_diagnostics[i]}");
            }
        }

        private static void AttachMesh(GameObject go, Mesh mesh, Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private Material GetFloorMaterial()
        {
            if (_floorMaterial == null)
            {
                _floorMaterial = CreateUnlitMaterial(FloorColor);
            }

            return _floorMaterial;
        }

        private Material GetCeilingMaterial()
        {
            if (_ceilingMaterial == null)
            {
                _ceilingMaterial = CreateUnlitMaterial(CeilingColor);
            }

            return _ceilingMaterial;
        }

        private Material GetWallMaterial()
        {
            if (_wallMaterial == null)
            {
                _wallMaterial = CreateUnlitMaterial(WallColor);
            }

            return _wallMaterial;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            var material = new Material(Shader.Find("Unlit/Color"));
            material.color = color;
            return material;
        }

        private static void DestroyUnityObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
