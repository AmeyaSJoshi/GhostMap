using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Viewer.Scene;
using UnityEngine;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// Task V5's furniture editing (implementation plan sections 13.3/13.4):
    /// drag on the floor plane, and inspector-driven yaw/width/depth/height.
    /// Every method here takes explicit values (a <see cref="Ray"/> or a
    /// number) rather than reading <c>Input</c> itself, so it is fully
    /// testable in EditMode without a Play-mode loop — the pattern
    /// <c>OrbitCameraRig</c> established in V4.
    ///
    /// Gated on <see cref="ViewerEditableScene.EditingEnabled"/>: every Try*
    /// method fails cleanly, leaving the scene untouched, if the scan is not
    /// finalized or nothing is selected. Every accepted edit is validated by
    /// the shared <c>FurnitureValidator</c> (via
    /// <see cref="ViewerEditableScene.TryApplyLocalEdit"/>) before it is
    /// applied — never a Viewer reimplementation of the dimension rules.
    /// </summary>
    public sealed class ObjectEditController : MonoBehaviour
    {
        [SerializeField] private ObjectSelectionController selection;

        private ViewerEditableScene _editableScene;

        public void SetSelectionController(ObjectSelectionController controller) => selection = controller;

        public void Attach(ViewerEditableScene editableScene) => _editableScene = editableScene;

        public void Detach() => _editableScene = null;

        public bool EditingEnabled => _editableScene != null && _editableScene.EditingEnabled;

        /// <summary>Projects <paramref name="ray"/> onto the floor plane
        /// (y = 0, exactly the room's own floor — Ghost space is Viewer world
        /// space, no ARKit/XR transform) and moves the selected object's
        /// centre there. Height (y) is preserved untouched.</summary>
        public bool TryDragToFloorPoint(Ray ray, out string error)
        {
            if (!TryGetSelectedModel(out SceneObjectModel model, out error))
            {
                return false;
            }

            if (!RayPlaneMath.TryIntersectHorizontalPlane(ray, 0f, out Vector3 point))
            {
                error = "Drag ray does not intersect the floor.";
                return false;
            }

            SceneObjectModel next = Clone(model);
            next.center = new Vec3Dto(point.x, model.center.y, point.z);
            return Apply(next, out error);
        }

        public bool TrySetPositionXZ(float x, float z, out string error)
        {
            if (!TryGetSelectedModel(out SceneObjectModel model, out error))
            {
                return false;
            }

            SceneObjectModel next = Clone(model);
            next.center = new Vec3Dto(x, model.center.y, z);
            return Apply(next, out error);
        }

        public bool TrySetYaw(float yawDeg, out string error)
        {
            if (!TryGetSelectedModel(out SceneObjectModel model, out error))
            {
                return false;
            }

            SceneObjectModel next = Clone(model);
            next.yawDeg = yawDeg;
            return Apply(next, out error);
        }

        public bool TrySetWidth(float widthM, out string error)
        {
            if (!TryGetSelectedModel(out SceneObjectModel model, out error))
            {
                return false;
            }

            SceneObjectModel next = Clone(model);
            next.widthM = widthM;
            return Apply(next, out error);
        }

        public bool TrySetDepth(float depthM, out string error)
        {
            if (!TryGetSelectedModel(out SceneObjectModel model, out error))
            {
                return false;
            }

            SceneObjectModel next = Clone(model);
            next.depthM = depthM;
            return Apply(next, out error);
        }

        public bool TrySetHeight(float heightM, out string error)
        {
            if (!TryGetSelectedModel(out SceneObjectModel model, out error))
            {
                return false;
            }

            SceneObjectModel next = Clone(model);
            next.heightM = heightM;
            return Apply(next, out error);
        }

        private bool TryGetSelectedModel(out SceneObjectModel model, out string error)
        {
            model = null;

            if (_editableScene == null || !_editableScene.EditingEnabled)
            {
                error = "Editing is disabled until the scan is finalized.";
                return false;
            }

            model = selection != null ? selection.GetSelectedModel() : null;
            if (model == null)
            {
                error = "No object is selected.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool Apply(SceneObjectModel next, out string error)
            => _editableScene.TryApplyLocalEdit(next, out error);

        private static SceneObjectModel Clone(SceneObjectModel model) => new SceneObjectModel
        {
            id = model.id,
            type = model.type,
            center = model.center,
            yawDeg = model.yawDeg,
            widthM = model.widthM,
            depthM = model.depthM,
            heightM = model.heightM
        };
    }
}
