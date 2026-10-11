using System;
using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V4: builds the parametric furniture of implementation plan
    /// section 12.4 — "do not render only anonymous boxes" — from simple
    /// primitive boxes.
    ///
    /// Every type's parts are expressed as fractions of the object's declared
    /// width/depth/height, so the union of a type's parts is always exactly
    /// the W x D x H box the <see cref="SceneObjectModel"/> declares, resting
    /// on the floor. Correct position, dimensions, yaw and type distinction
    /// are the requirements here; visual fidelity to real furniture is not,
    /// and no attempt is made at it.
    ///
    /// The part table itself is the shared <see cref="FurnitureParts"/>
    /// (moved there in task R6 so the phone's exporter uses it too);
    /// <see cref="Create"/> is the thin Unity layer that instantiates it.
    /// </summary>
    public sealed class FurnitureFactory : IDisposable
    {
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        /// <summary>
        /// The display colour for a furniture type. Lives in the shared
        /// <see cref="FurnitureParts"/> (task R6) so exported files and the
        /// screen use one colour table.
        /// </summary>
        public static Color ColorFor(string type) => FurnitureParts.ColorFor(type);

        /// <summary>The shared part table (<see cref="FurnitureParts.BuildParts(SceneObjectModel)"/>).</summary>
        public static IReadOnlyList<FurniturePartSpec> BuildParts(SceneObjectModel model)
            => FurnitureParts.BuildParts(model);

        /// <summary>The shared part table, with diagnostics for rejected objects.</summary>
        public static IReadOnlyList<FurniturePartSpec> BuildParts(
            SceneObjectModel model,
            IList<string> diagnostics)
            => FurnitureParts.BuildParts(model, diagnostics);

        /// <summary>
        /// Instantiates one object root at the model's centre, rotated by its
        /// yaw, carrying the metadata component and exactly one selection
        /// collider covering the full bounding box (section 12.4). Returns
        /// <c>null</c> — rendering nothing — if the model cannot be built.
        /// </summary>
        public GameObject Create(SceneObjectModel model, Transform parent)
            => Create(model, parent, null);

        public GameObject Create(SceneObjectModel model, Transform parent, IList<string> diagnostics)
        {
            IReadOnlyList<FurniturePartSpec> parts = BuildParts(model, diagnostics);

            if (parts.Count == 0)
            {
                return null;
            }

            var root = new GameObject($"Object_{model.type}_{model.id}");
            root.transform.SetParent(parent, false);
            root.transform.position = model.center.ToVector3();
            root.transform.rotation = Quaternion.Euler(0f, model.yawDeg, 0f);

            root.AddComponent<SceneObjectBinding>().Bind(model);

            // One collider for the whole object: V5 selects objects, never
            // individual mattress slabs or chair legs.
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(model.widthM, model.heightM, model.depthM);
            collider.center = new Vector3(0f, model.heightM * 0.5f, 0f);

            Material material = GetMaterial(model.type);

            for (int i = 0; i < parts.Count; i++)
            {
                FurniturePartSpec part = parts[i];

                GameObject partGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                partGo.name = part.Name;
                partGo.transform.SetParent(root.transform, false);
                partGo.transform.localPosition = part.LocalCenter;
                partGo.transform.localRotation = Quaternion.identity;
                partGo.transform.localScale = part.LocalSize;
                partGo.GetComponent<MeshRenderer>().sharedMaterial = material;

                // The primitive's own collider would defeat "one collider per
                // furniture root".
                DestroyUnityObject(partGo.GetComponent<Collider>());
            }

            return root;
        }

        public void Dispose()
        {
            foreach (Material material in _materials.Values)
            {
                DestroyUnityObject(material);
            }

            _materials.Clear();
        }

        private Material GetMaterial(string type)
        {
            if (_materials.TryGetValue(type, out Material existing) && existing != null)
            {
                return existing;
            }

            Color color = FurnitureParts.ColorFor(type);

            var material = new Material(Shader.Find("Unlit/Color")) { color = color };
            _materials[type] = material;
            return material;
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
