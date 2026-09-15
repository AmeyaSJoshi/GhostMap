using System;
using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// One primitive box making up a piece of furniture, described in the
    /// object's own local frame: the origin is the object's centre on the
    /// floor, +x is width, +y is up, +z is depth. Yaw and world placement are
    /// applied once, on the object root, so the part table never has to know
    /// about either.
    /// </summary>
    public readonly struct FurniturePartSpec
    {
        public string Name { get; }
        public Vector3 LocalCenter { get; }
        public Vector3 LocalSize { get; }

        public FurniturePartSpec(string name, Vector3 localCenter, Vector3 localSize)
        {
            Name = name;
            LocalCenter = localCenter;
            LocalSize = localSize;
        }

        public override string ToString() => $"{Name} @{LocalCenter} x{LocalSize}";
    }

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
    /// <see cref="BuildParts(SceneObjectModel)"/> is a pure function and is
    /// where all the geometry lives; <see cref="Create"/> is the thin Unity
    /// layer that instantiates it.
    /// </summary>
    public sealed class FurnitureFactory : IDisposable
    {
        private static readonly Dictionary<string, Color> TypeColors =
            new Dictionary<string, Color>
            {
                { "bed",     new Color(0.42f, 0.48f, 0.62f) },
                { "desk",    new Color(0.58f, 0.42f, 0.30f) },
                { "chair",   new Color(0.70f, 0.55f, 0.28f) },
                { "couch",   new Color(0.40f, 0.52f, 0.44f) },
                { "table",   new Color(0.62f, 0.46f, 0.34f) },
                { "dresser", new Color(0.52f, 0.38f, 0.32f) },
                { "tv",      new Color(0.22f, 0.22f, 0.26f) },
                { "generic", new Color(0.60f, 0.60f, 0.64f) }
            };

        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        public static IReadOnlyList<FurniturePartSpec> BuildParts(SceneObjectModel model)
            => BuildParts(model, null);

        /// <param name="diagnostics">
        /// Optional sink for a line per rejected object. <c>ViewerSceneStore</c>
        /// runs <c>FurnitureValidator</c> before a snapshot is accepted, so
        /// anything reported here means bad data reached the renderer by
        /// another route; nothing is invented for it and nothing crashes.
        /// </param>
        public static IReadOnlyList<FurniturePartSpec> BuildParts(
            SceneObjectModel model,
            IList<string> diagnostics)
        {
            if (model == null)
            {
                diagnostics?.Add("Scene object is null; nothing rendered.");
                return Array.Empty<FurniturePartSpec>();
            }

            if (!FurnitureValidator.IsSupportedType(model.type))
            {
                diagnostics?.Add(
                    $"Object '{model.id}' has unsupported type '{model.type}'; nothing rendered. " +
                    "The viewer never invents object types outside the shared schema.");
                return Array.Empty<FurniturePartSpec>();
            }

            if (!IsUsable(model.widthM) || !IsUsable(model.depthM) || !IsUsable(model.heightM))
            {
                diagnostics?.Add(
                    $"Object '{model.id}' has unusable dimensions " +
                    $"({model.widthM} x {model.depthM} x {model.heightM}); nothing rendered.");
                return Array.Empty<FurniturePartSpec>();
            }

            float w = model.widthM;
            float d = model.depthM;
            float h = model.heightM;

            switch (model.type)
            {
                case "bed": return Bed(w, d, h);
                case "desk": return Desk(w, d, h);
                case "chair": return Chair(w, d, h);
                case "couch": return Couch(w, d, h);
                case "table": return Table(w, d, h);
                case "dresser": return Dresser(w, d, h);
                case "tv": return Tv(w, d, h);
                default: return Generic(w, d, h);
            }
        }

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

            Color color = TypeColors.TryGetValue(type, out Color mapped)
                ? mapped
                : TypeColors["generic"];

            var material = new Material(Shader.Find("Unlit/Color")) { color = color };
            _materials[type] = material;
            return material;
        }

        // ---- Part tables. Every fraction is chosen so that the union of a
        // ---- type's parts is exactly the W x D x H box, sitting on y = 0.

        private static FurniturePartSpec[] Bed(float w, float d, float h)
        {
            float hd = d * 0.5f;

            return new[]
            {
                new FurniturePartSpec("Headboard",
                    new Vector3(0f, h * 0.5f, -hd + 0.03f * d),
                    new Vector3(w, h, 0.06f * d)),
                new FurniturePartSpec("Frame",
                    new Vector3(0f, 0.175f * h, 0.03f * d),
                    new Vector3(w, 0.35f * h, 0.94f * d)),
                new FurniturePartSpec("Mattress",
                    new Vector3(0f, 0.60f * h, 0.05f * d),
                    new Vector3(0.94f * w, 0.50f * h, 0.90f * d))
            };
        }

        private static FurniturePartSpec[] Desk(float w, float d, float h)
        {
            var parts = new List<FurniturePartSpec>
            {
                new FurniturePartSpec("Top",
                    new Vector3(0f, 0.97f * h, 0f),
                    new Vector3(w, 0.06f * h, d))
            };

            parts.AddRange(Legs(w, d, 0.06f, 0.94f * h, 0.47f * h));

            // A modesty panel is what keeps a desk visually distinct from a
            // table, which section 12.4 otherwise describes identically.
            parts.Add(new FurniturePartSpec("BackPanel",
                new Vector3(0f, 0.65f * h, -d * 0.5f + 0.02f * d),
                new Vector3(0.90f * w, 0.50f * h, 0.04f * d)));

            return parts.ToArray();
        }

        private static FurniturePartSpec[] Table(float w, float d, float h)
        {
            var parts = new List<FurniturePartSpec>
            {
                new FurniturePartSpec("Top",
                    new Vector3(0f, 0.96f * h, 0f),
                    new Vector3(w, 0.08f * h, d))
            };

            parts.AddRange(Legs(w, d, 0.08f, 0.92f * h, 0.46f * h));

            return parts.ToArray();
        }

        private static FurniturePartSpec[] Chair(float w, float d, float h)
        {
            var parts = new List<FurniturePartSpec>();

            parts.AddRange(Legs(w, d, 0.08f, 0.45f * h, 0.225f * h));

            parts.Add(new FurniturePartSpec("Seat",
                new Vector3(0f, 0.49f * h, 0f),
                new Vector3(w, 0.08f * h, d)));
            parts.Add(new FurniturePartSpec("Back",
                new Vector3(0f, 0.765f * h, -d * 0.5f + 0.045f * d),
                new Vector3(w, 0.47f * h, 0.09f * d)));

            return parts.ToArray();
        }

        private static FurniturePartSpec[] Couch(float w, float d, float h)
        {
            float hw = w * 0.5f;
            float hd = d * 0.5f;

            return new[]
            {
                new FurniturePartSpec("Base",
                    new Vector3(0f, 0.225f * h, 0f),
                    new Vector3(w, 0.45f * h, d)),
                new FurniturePartSpec("Back",
                    new Vector3(0f, 0.725f * h, -hd + 0.11f * d),
                    new Vector3(w, 0.55f * h, 0.22f * d)),
                new FurniturePartSpec("ArmLeft",
                    new Vector3(-hw + 0.06f * w, 0.625f * h, 0f),
                    new Vector3(0.12f * w, 0.35f * h, d)),
                new FurniturePartSpec("ArmRight",
                    new Vector3(hw - 0.06f * w, 0.625f * h, 0f),
                    new Vector3(0.12f * w, 0.35f * h, d)),
                new FurniturePartSpec("Cushion0",
                    new Vector3(-0.19f * w, 0.51f * h, 0.12f * d),
                    new Vector3(0.36f * w, 0.12f * h, 0.76f * d)),
                new FurniturePartSpec("Cushion1",
                    new Vector3(0.19f * w, 0.51f * h, 0.12f * d),
                    new Vector3(0.36f * w, 0.12f * h, 0.76f * d))
            };
        }

        private static FurniturePartSpec[] Dresser(float w, float d, float h)
        {
            float hd = d * 0.5f;

            return new[]
            {
                new FurniturePartSpec("Body",
                    new Vector3(0f, h * 0.5f, -0.03f * d),
                    new Vector3(w, h, 0.94f * d)),
                new FurniturePartSpec("Drawer0",
                    new Vector3(0f, 0.18f * h, hd - 0.03f * d),
                    new Vector3(0.86f * w, 0.26f * h, 0.06f * d)),
                new FurniturePartSpec("Drawer1",
                    new Vector3(0f, 0.50f * h, hd - 0.03f * d),
                    new Vector3(0.86f * w, 0.26f * h, 0.06f * d)),
                new FurniturePartSpec("Drawer2",
                    new Vector3(0f, 0.82f * h, hd - 0.03f * d),
                    new Vector3(0.86f * w, 0.26f * h, 0.06f * d))
            };
        }

        private static FurniturePartSpec[] Tv(float w, float d, float h)
        {
            return new[]
            {
                new FurniturePartSpec("StandBase",
                    new Vector3(0f, 0.015f * h, 0f),
                    new Vector3(0.40f * w, 0.03f * h, d)),
                new FurniturePartSpec("Stand",
                    new Vector3(0f, 0.10f * h, 0f),
                    new Vector3(0.08f * w, 0.20f * h, 0.30f * d)),
                new FurniturePartSpec("Screen",
                    new Vector3(0f, 0.60f * h, 0f),
                    new Vector3(w, 0.80f * h, 0.35f * d))
            };
        }

        private static FurniturePartSpec[] Generic(float w, float d, float h)
        {
            return new[]
            {
                new FurniturePartSpec("Box",
                    new Vector3(0f, h * 0.5f, 0f),
                    new Vector3(w, h, d))
            };
        }

        private static IEnumerable<FurniturePartSpec> Legs(
            float w,
            float d,
            float thicknessFraction,
            float legHeight,
            float legCenterY)
        {
            float inset = thicknessFraction * 0.5f;
            float x = w * (0.5f - inset);
            float z = d * (0.5f - inset);

            var size = new Vector3(thicknessFraction * w, legHeight, thicknessFraction * d);

            yield return new FurniturePartSpec("Leg0", new Vector3(-x, legCenterY, -z), size);
            yield return new FurniturePartSpec("Leg1", new Vector3(x, legCenterY, -z), size);
            yield return new FurniturePartSpec("Leg2", new Vector3(-x, legCenterY, z), size);
            yield return new FurniturePartSpec("Leg3", new Vector3(x, legCenterY, z), size);
        }

        private static bool IsUsable(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

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
