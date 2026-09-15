using System;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using UnityEngine;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// Task V5's object selection (implementation plan section 13.2): a click
    /// raycasts against <see cref="Physics"/>, and only a hit whose collider
    /// carries a <see cref="SceneObjectBinding"/> — the furniture root's
    /// single bounding-box collider, per Task V4 — counts as a selection.
    /// Floor, ceiling and wall colliders have no such component, so they can
    /// never accidentally become a "furniture" selection; this is
    /// deterministic identity via <see cref="SceneObjectBinding.ObjectId"/>,
    /// never brittle <see cref="GameObject"/> name parsing.
    ///
    /// Selection is allowed at any time — before or after finalization — and
    /// is independent of editing (implementation plan section 13.2 has no
    /// finalized gate; only dragging/resizing do, per section 13.3/13.4 and
    /// <see cref="ObjectEditController"/>).
    ///
    /// Re-resolves the selected object after every
    /// <see cref="RoomRenderer.Rebuilt"/> — never after
    /// <c>IViewerSceneSource.Changed</c> directly, which would race the
    /// renderer that has not rebuilt yet — clearing the selection safely if
    /// the id no longer exists in the newly rendered room.
    /// </summary>
    public sealed class ObjectSelectionController : MonoBehaviour
    {
        private const float MaxRayDistanceM = 1000f;

        private static readonly Color HighlightColor = new Color(1f, 0.82f, 0.15f);

        [SerializeField] private RoomRenderer roomRenderer;
        [SerializeField] private Camera targetCamera;

        private Material _highlightMaterial;
        private GameObject _highlightGo;
        private bool _attached;

        public string SelectedObjectId { get; private set; }

        public bool HasSelection => !string.IsNullOrEmpty(SelectedObjectId);

        /// <summary>Fired with the newly selected id, or null when cleared.</summary>
        public event Action<string> SelectionChanged;

        public void SetRoomRenderer(RoomRenderer renderer) => roomRenderer = renderer;

        public void SetCamera(Camera camera) => targetCamera = camera;

        public void Attach()
        {
            Detach();

            _attached = true;
            if (roomRenderer != null)
            {
                roomRenderer.Rebuilt += OnRoomRebuilt;
            }
        }

        public void Detach()
        {
            if (_attached && roomRenderer != null)
            {
                roomRenderer.Rebuilt -= OnRoomRebuilt;
            }

            _attached = false;
            ClearSelection();
        }

        private void OnDestroy()
        {
            Detach();
            DestroyUnityObject(_highlightMaterial);
        }

        private void OnRoomRebuilt()
        {
            if (!HasSelection)
            {
                return;
            }

            if (TryFindBinding(SelectedObjectId, out SceneObjectBinding binding))
            {
                ShowHighlightFor(binding);
            }
            else
            {
                // The authoritative scene no longer contains the object the
                // user had selected (removed before finalization, or a
                // genuinely new session). Clear safely rather than leaving a
                // highlight pointed at nothing.
                ClearSelection();
            }
        }

        /// <summary>
        /// Raycasts from the given camera-space screen point. Convenience
        /// wrapper over <see cref="TrySelectAt(Ray)"/> for input code that
        /// only has a screen position.
        /// </summary>
        public bool TrySelectAt(Vector2 screenPoint)
        {
            if (targetCamera == null)
            {
                return false;
            }

            return TrySelectAt(targetCamera.ScreenPointToRay(screenPoint));
        }

        /// <summary>
        /// Clicking empty space (floor, wall, ceiling, or nothing at all)
        /// clears the selection — implementation plan section 13.2.
        /// </summary>
        public bool TrySelectAt(Ray ray)
        {
            if (TryRaycastBinding(ray, out SceneObjectBinding binding))
            {
                Select(binding);
                return true;
            }

            ClearSelection();
            return false;
        }

        /// <summary>
        /// True if <paramref name="ray"/> hits the currently selected
        /// object's own collider. Used by the interaction router to decide
        /// whether a mouse-down should begin a furniture drag rather than a
        /// camera orbit.
        /// </summary>
        public bool IsPointerOverSelected(Ray ray)
        {
            if (!HasSelection)
            {
                return false;
            }

            return TryRaycastBinding(ray, out SceneObjectBinding binding)
                && binding.ObjectId == SelectedObjectId;
        }

        public void ClearSelection()
        {
            bool hadSelection = HasSelection;
            SelectedObjectId = null;
            HideHighlight();

            if (hadSelection)
            {
                SelectionChanged?.Invoke(null);
            }
        }

        /// <summary>The live model for whatever is currently selected, freshly
        /// resolved from the rendered scene — never a cached copy that could
        /// go stale across a rebuild.</summary>
        public SceneObjectModel GetSelectedModel()
        {
            if (!HasSelection)
            {
                return null;
            }

            return TryFindBinding(SelectedObjectId, out SceneObjectBinding binding)
                ? binding.Model
                : null;
        }

        private bool TryRaycastBinding(Ray ray, out SceneObjectBinding binding)
        {
            binding = null;

            if (!Physics.Raycast(ray, out RaycastHit hit, MaxRayDistanceM))
            {
                return false;
            }

            binding = hit.collider != null
                ? hit.collider.GetComponentInParent<SceneObjectBinding>()
                : null;

            return binding != null && binding.Model != null;
        }

        private void Select(SceneObjectBinding binding)
        {
            SelectedObjectId = binding.ObjectId;
            ShowHighlightFor(binding);
            SelectionChanged?.Invoke(SelectedObjectId);
        }

        private bool TryFindBinding(string objectId, out SceneObjectBinding binding)
        {
            binding = null;

            if (roomRenderer == null || roomRenderer.Root == null || string.IsNullOrEmpty(objectId))
            {
                return false;
            }

            foreach (SceneObjectBinding candidate in roomRenderer.Root.GetComponentsInChildren<SceneObjectBinding>())
            {
                if (candidate.ObjectId == objectId)
                {
                    binding = candidate;
                    return true;
                }
            }

            return false;
        }

        private void ShowHighlightFor(SceneObjectBinding binding)
        {
            HideHighlight();

            SceneObjectModel model = binding.Model;
            _highlightGo = new GameObject("SelectionOutline");
            _highlightGo.transform.SetParent(binding.transform, false);

            Material material = GetHighlightMaterial();

            foreach (SelectionEdgeBar bar in SelectionOutlineBuilder.BuildEdges(
                model.widthM, model.depthM, model.heightM))
            {
                GameObject barGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barGo.name = "Edge";
                barGo.transform.SetParent(_highlightGo.transform, false);
                barGo.transform.localPosition = bar.LocalCenter;
                barGo.transform.localRotation = Quaternion.identity;
                barGo.transform.localScale = bar.LocalSize;
                barGo.GetComponent<MeshRenderer>().sharedMaterial = material;

                // The highlight must never itself be raycast-hittable — it
                // would shadow the real furniture collider it sits around.
                DestroyUnityObject(barGo.GetComponent<Collider>());
            }
        }

        private void HideHighlight()
        {
            DestroyUnityObject(_highlightGo);
            _highlightGo = null;
        }

        private Material GetHighlightMaterial()
        {
            if (_highlightMaterial == null)
            {
                _highlightMaterial = new Material(Shader.Find("Unlit/Color")) { color = HighlightColor };
            }

            return _highlightMaterial;
        }

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
