using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V4: the object metadata component implementation plan section 17
    /// (Task V4) requires on every furniture root — the link from a rendered
    /// GameObject back to the <see cref="SceneObjectModel"/> it came from.
    ///
    /// V5's selection raycast hits the root's single collider and reads this
    /// to populate the inspector, which is why the identity lives on the root
    /// and not on the individual primitive parts.
    /// </summary>
    public sealed class SceneObjectBinding : MonoBehaviour
    {
        /// <summary>
        /// The model this object was rendered from. Treated as read-only by
        /// the renderer: the scene store owns scene state.
        /// </summary>
        public SceneObjectModel Model { get; private set; }

        public string ObjectId => Model?.id;

        public string ObjectType => Model?.type;

        public void Bind(SceneObjectModel model)
        {
            Model = model;
        }
    }
}
