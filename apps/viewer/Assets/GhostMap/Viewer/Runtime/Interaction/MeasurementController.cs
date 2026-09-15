using System;
using GhostMap.Shared.Geometry;
using GhostMap.Viewer.Scene;
using UnityEngine;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// Task V5's measurement tool (implementation plan section 13.5): two
    /// world-space points from real <see cref="Physics"/> raycasts against
    /// whatever the room shell and furniture colliders return — floor, walls,
    /// furniture. Openings behave naturally as holes because
    /// <c>WallSliceGenerator</c> (V3) never builds a solid segment across one,
    /// so a ray through a door or window simply continues to whatever is
    /// behind it; no Viewer collider changes were needed to satisfy this.
    ///
    /// Distance always comes from the shared <c>MeasurementMath</c> over the
    /// two real hit points — never from screen-space pixels — so 3D distance
    /// and horizontal XZ distance are both exact arithmetic on GhostMap
    /// world-space (== Ghost-space) coordinates.
    /// </summary>
    public sealed class MeasurementController : MonoBehaviour
    {
        private const float MaxRayDistanceM = 1000f;
        private const float MarkerRadiusM = 0.05f;
        private const float LineWidthM = 0.015f;

        private static readonly Color LineColor = new Color(0.2f, 0.9f, 1f);

        private IViewerSceneSource _sceneSource;
        private string _lastSessionId;

        private LineRenderer _line;
        private GameObject _markerA;
        private GameObject _markerB;
        private Material _visualMaterial;

        public bool IsActive { get; private set; }

        public Vector3? PointA { get; private set; }

        public Vector3? PointB { get; private set; }

        public bool HasMeasurement => PointA.HasValue && PointB.HasValue;

        /// <summary>Straight-line 3D distance. Zero, not NaN, for two
        /// identical points.</summary>
        public float DistanceM => HasMeasurement ? MeasurementMath.Distance(PointA.Value, PointB.Value) : 0f;

        /// <summary>Horizontal distance, ignoring height (section 13.5).</summary>
        public float DistanceXZM => HasMeasurement ? MeasurementMath.DistanceXZ(PointA.Value, PointB.Value) : 0f;

        /// <summary>Vertical component only, for the HUD's diagonal/vertical
        /// breakdown.</summary>
        public float VerticalDistanceM => HasMeasurement ? Mathf.Abs(PointB.Value.y - PointA.Value.y) : 0f;

        /// <summary>Fired on every state change: mode toggled, a point
        /// placed, or clear/reset.</summary>
        public event Action Changed;

        /// <summary>
        /// A genuinely new Scanner session (different <c>sessionId</c>) clears
        /// any in-progress or completed measurement — it would otherwise
        /// reference points in a room that no longer exists. Edits and
        /// rebuilds within the *same* session never touch the measurement.
        /// </summary>
        public void Attach(IViewerSceneSource sceneSource)
        {
            Detach();

            _sceneSource = sceneSource;
            if (_sceneSource == null)
            {
                return;
            }

            _sceneSource.Changed += OnSceneChanged;

            if (_sceneSource.Current != null)
            {
                _lastSessionId = _sceneSource.Current.sessionId;
            }
        }

        public void Detach()
        {
            if (_sceneSource != null)
            {
                _sceneSource.Changed -= OnSceneChanged;
                _sceneSource = null;
            }
        }

        private void OnDestroy()
        {
            Detach();
            DestroyVisual();
            DestroyUnityObject(_visualMaterial);
        }

        private void OnSceneChanged(GhostMap.Shared.Domain.SceneSnapshot snapshot)
        {
            string sessionId = snapshot?.sessionId;
            if (_lastSessionId != null && sessionId != _lastSessionId)
            {
                Clear();
            }

            _lastSessionId = sessionId;
        }

        public void SetActive(bool active)
        {
            if (IsActive == active)
            {
                return;
            }

            IsActive = active;
            if (!active)
            {
                Clear();
                return;
            }

            Changed?.Invoke();
        }

        public void ToggleActive() => SetActive(!IsActive);

        /// <summary>
        /// Places the first point, the second point, or — if a measurement is
        /// already complete — starts a fresh one at this point (section
        /// "Measurement lifecycle": starting a new measurement). Does nothing
        /// and returns false if the ray hits nothing or the mode is inactive.
        /// </summary>
        public bool TryPlacePoint(Ray ray)
        {
            if (!IsActive)
            {
                return false;
            }

            if (!Physics.Raycast(ray, out RaycastHit hit, MaxRayDistanceM))
            {
                return false;
            }

            Vector3 point = hit.point;
            if (!IsFinite(point))
            {
                return false;
            }

            if (HasMeasurement)
            {
                PointA = point;
                PointB = null;
            }
            else if (PointA.HasValue)
            {
                PointB = point;
            }
            else
            {
                PointA = point;
            }

            UpdateVisual();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Clears the current measurement (both points and the
        /// visual) without leaving measurement mode.</summary>
        public void Clear()
        {
            PointA = null;
            PointB = null;
            UpdateVisual();
            Changed?.Invoke();
        }

        private void UpdateVisual()
        {
            if (!PointA.HasValue)
            {
                DestroyVisual();
                return;
            }

            EnsureVisualCreated();

            _markerA.SetActive(true);
            _markerA.transform.position = PointA.Value;

            if (PointB.HasValue)
            {
                _markerB.SetActive(true);
                _markerB.transform.position = PointB.Value;

                _line.enabled = true;
                _line.SetPosition(0, PointA.Value);
                _line.SetPosition(1, PointB.Value);
            }
            else
            {
                _markerB.SetActive(false);
                _line.enabled = false;
            }
        }

        private void EnsureVisualCreated()
        {
            if (_markerA != null)
            {
                return;
            }

            Material material = GetVisualMaterial();

            _markerA = CreateMarker("MeasurementPointA", material);
            _markerB = CreateMarker("MeasurementPointB", material);

            var lineGo = new GameObject("MeasurementLine");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.widthMultiplier = LineWidthM;
            _line.material = material;
            _line.useWorldSpace = true;
            _line.enabled = false;
        }

        private GameObject CreateMarker(string name, Material material)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = Vector3.one * (MarkerRadiusM * 2f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = material;
            DestroyUnityObject(marker.GetComponent<Collider>());
            marker.SetActive(false);
            return marker;
        }

        private void DestroyVisual()
        {
            DestroyUnityObject(_markerA);
            DestroyUnityObject(_markerB);
            DestroyUnityObject(_line != null ? _line.gameObject : null);
            _markerA = null;
            _markerB = null;
            _line = null;
        }

        private Material GetVisualMaterial()
        {
            if (_visualMaterial == null)
            {
                _visualMaterial = new Material(Shader.Find("Unlit/Color")) { color = LineColor };
            }

            return _visualMaterial;
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void DestroyUnityObject(UnityEngine.Object obj)
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
