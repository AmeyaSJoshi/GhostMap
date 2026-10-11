using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// Demo mode: a furnished virtual room the user looks around by dragging,
    /// standing in for ARKit where ARKit cannot run — the Unity Editor and the
    /// iOS Simulator.
    ///
    /// <para><b>This exists so the scan UI can be seen and clicked through
    /// without a phone.</b> It says nothing about real-world accuracy. Only
    /// <see cref="ArSpatialProvider"/> creates it, and only when
    /// <see cref="ShouldSimulate"/> says so, which is never on a physical
    /// iPhone: a device whose ARKit loader failed must keep failing visibly, as
    /// the S1 device work requires, not quietly turn into a demo.</para>
    ///
    /// <para>The camera stands still near the middle of the room, the same
    /// stance ADR-0005's swept capture asks of a real user. Floor hits are an
    /// exact ray-plane intersection with <c>y = 0</c>, clipped to the room, so
    /// every capture path works against it unchanged.</para>
    /// </summary>
    public sealed class SimulatedRoom : MonoBehaviour
    {
        public const float RoomWidthM = 4.2f;
        public const float RoomDepthM = 3.6f;
        public const float RoomHeightM = 2.5f;

        /// <summary>
        /// How long the demo pretends to be looking for tracking, so the
        /// "move your phone around" step is visible rather than skipped.
        /// </summary>
        private const float WarmUpSeconds = 1.5f;

        private const float EyeHeightM = 1.45f;
        private const float DegreesPerScreenWidth = 110f;
        private const float MinPitchDeg = -60f;
        private const float MaxPitchDeg = 80f;
        private const float LookSmoothing = 18f;

        private static readonly Vector3 EyePosition = new Vector3(0.25f, EyeHeightM, -0.35f);

        private Camera viewCamera;
        private float startTime;

        private float targetYaw;
        private float targetPitch = 42f;
        private float yaw;
        private float pitch = 42f;

        private Shader litShader;

        private bool dragging;
        private Vector2 lastPointer;

        /// <summary>
        /// True in the Unity Editor and the iOS Simulator when no XR loader is
        /// running, and never otherwise.
        /// </summary>
        public static bool ShouldSimulate(bool isEditor, bool isIosSimulator, bool hasActiveXrLoader)
        {
            return !hasActiveXrLoader && (isEditor || isIosSimulator);
        }

        /// <summary>
        /// Whether this process is an iOS Simulator app. The device model is
        /// no use here — the Simulator reports the model it imitates, such as
        /// "iPhone18,3" — but it sets SIMULATOR_UDID for every app it runs,
        /// and a physical device never does.
        /// </summary>
        public static bool IsRunningInIosSimulator()
        {
            return Application.platform == RuntimePlatform.IPhonePlayer
                && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SIMULATOR_UDID"));
        }

        /// <summary>
        /// Intersects a world ray with the virtual floor. False when it misses
        /// the floor or lands outside the room's walls.
        /// </summary>
        public static bool TryRaycastFloor(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;

            if (ray.direction.y >= -1e-4f)
            {
                return false;
            }

            float distance = -ray.origin.y / ray.direction.y;
            Vector3 hit = ray.origin + (ray.direction * distance);

            if (Mathf.Abs(hit.x) > RoomWidthM * 0.5f || Mathf.Abs(hit.z) > RoomDepthM * 0.5f)
            {
                return false;
            }

            point = new Vector3(hit.x, 0f, hit.z);
            return true;
        }

        public bool IsTrackingGood => Time.unscaledTime - startTime >= WarmUpSeconds;

        public void Initialize(Camera arCamera)
        {
            viewCamera = arCamera;
            startTime = Time.unscaledTime;

            DetachCameraFromAr();
            BuildRoom();
            ApplyCameraPose();
        }

        private void Update()
        {
            if (viewCamera == null)
            {
                return;
            }

            ReadDrag();

            float blend = 1f - Mathf.Exp(-LookSmoothing * Time.unscaledDeltaTime);
            yaw = Mathf.LerpAngle(yaw, targetYaw, blend);
            pitch = Mathf.Lerp(pitch, targetPitch, blend);

            ApplyCameraPose();
        }

        /// <summary>
        /// Drag anywhere that is not a button to look around. Drags that start
        /// on UI belong to the UI.
        /// </summary>
        private void ReadDrag()
        {
            Pointer pointer = Pointer.current;

            if (pointer == null)
            {
                return;
            }

            Vector2 position = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame)
            {
                EventSystem eventSystem = EventSystem.current;
                dragging = eventSystem == null || !eventSystem.IsPointerOverGameObject();
                lastPointer = position;
                return;
            }

            if (!pointer.press.isPressed)
            {
                dragging = false;
                return;
            }

            if (!dragging)
            {
                return;
            }

            Vector2 delta = position - lastPointer;
            lastPointer = position;

            float degreesPerPixel = DegreesPerScreenWidth / Mathf.Max(1f, Screen.width);

            // Drag the world, like panning a photo: dragging left turns right.
            targetYaw -= delta.x * degreesPerPixel;
            targetPitch = Mathf.Clamp(targetPitch + (delta.y * degreesPerPixel), MinPitchDeg, MaxPitchDeg);
        }

        private void ApplyCameraPose()
        {
            Transform cameraTransform = viewCamera.transform;
            cameraTransform.SetPositionAndRotation(EyePosition, Quaternion.Euler(pitch, yaw, 0f));
        }

        /// <summary>
        /// Stops the AR stack from fighting the demo camera. The tracked pose
        /// driver is matched by name because it lives in a package assembly
        /// this one does not otherwise need.
        /// </summary>
        private void DetachCameraFromAr()
        {
            foreach (Behaviour behaviour in viewCamera.GetComponents<Behaviour>())
            {
                if (behaviour is ARCameraBackground
                    || behaviour is ARCameraManager
                    || behaviour.GetType().Name == "TrackedPoseDriver")
                {
                    behaviour.enabled = false;
                }
            }

            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            viewCamera.nearClipPlane = 0.05f;
            viewCamera.fieldOfView = 62f;
        }

        // -------------------------------------------------------------------
        // Room
        // -------------------------------------------------------------------

        private void BuildRoom()
        {
            litShader = Shader.Find("Legacy Shaders/Diffuse");

            var root = new GameObject("Simulated Room").transform;
            root.SetParent(transform, worldPositionStays: false);

            float halfW = RoomWidthM * 0.5f;
            float halfD = RoomDepthM * 0.5f;
            const float wallThickness = 0.1f;

            Color floor = new Color(0.56f, 0.43f, 0.33f);
            Color wall = new Color(0.86f, 0.85f, 0.82f);
            Color accentWall = new Color(0.62f, 0.72f, 0.78f);
            Color ceiling = new Color(0.95f, 0.95f, 0.94f);
            Color skirting = new Color(0.30f, 0.30f, 0.32f);

            Box(root, "Floor", new Vector3(0f, -0.01f, 0f), new Vector3(RoomWidthM, 0.02f, RoomDepthM), floor);
            Box(root, "Ceiling", new Vector3(0f, RoomHeightM + 0.01f, 0f), new Vector3(RoomWidthM, 0.02f, RoomDepthM), ceiling);

            Box(root, "Wall North", new Vector3(0f, RoomHeightM * 0.5f, halfD + (wallThickness * 0.5f)),
                new Vector3(RoomWidthM + (wallThickness * 2f), RoomHeightM, wallThickness), accentWall);
            Box(root, "Wall South", new Vector3(0f, RoomHeightM * 0.5f, -halfD - (wallThickness * 0.5f)),
                new Vector3(RoomWidthM + (wallThickness * 2f), RoomHeightM, wallThickness), wall);
            Box(root, "Wall East", new Vector3(halfW + (wallThickness * 0.5f), RoomHeightM * 0.5f, 0f),
                new Vector3(wallThickness, RoomHeightM, RoomDepthM), wall);
            Box(root, "Wall West", new Vector3(-halfW - (wallThickness * 0.5f), RoomHeightM * 0.5f, 0f),
                new Vector3(wallThickness, RoomHeightM, RoomDepthM), wall);

            // Dark skirting boards make the floor/wall join — the line the
            // user traces — easy to see.
            const float skirtH = 0.07f;
            const float skirtT = 0.015f;
            Box(root, "Skirting North", new Vector3(0f, skirtH * 0.5f, halfD - (skirtT * 0.5f)), new Vector3(RoomWidthM, skirtH, skirtT), skirting);
            Box(root, "Skirting South", new Vector3(0f, skirtH * 0.5f, -halfD + (skirtT * 0.5f)), new Vector3(RoomWidthM, skirtH, skirtT), skirting);
            Box(root, "Skirting East", new Vector3(halfW - (skirtT * 0.5f), skirtH * 0.5f, 0f), new Vector3(skirtT, skirtH, RoomDepthM), skirting);
            Box(root, "Skirting West", new Vector3(-halfW + (skirtT * 0.5f), skirtH * 0.5f, 0f), new Vector3(skirtT, skirtH, RoomDepthM), skirting);

            // A door on the west wall and a window on the east wall, to try
            // the openings step on.
            Box(root, "Door", new Vector3(-halfW + 0.01f, 1.0f, 0.6f), new Vector3(0.02f, 2.0f, 0.9f), new Color(0.42f, 0.30f, 0.22f));
            Box(root, "Window", new Vector3(halfW - 0.01f, 1.45f, -0.2f), new Vector3(0.02f, 1.0f, 1.3f), new Color(0.62f, 0.82f, 0.95f));

            // Furniture, including a bed that hides part of a wall, as real
            // rooms do.
            Box(root, "Bed", new Vector3(halfW - 0.75f, 0.25f, halfD - 1.05f), new Vector3(1.4f, 0.5f, 2.0f), new Color(0.93f, 0.93f, 0.96f));
            Box(root, "Bed Headboard", new Vector3(halfW - 0.75f, 0.55f, halfD - 0.04f), new Vector3(1.4f, 1.1f, 0.06f), new Color(0.35f, 0.28f, 0.24f));
            Box(root, "Desk", new Vector3(-halfW + 0.45f, 0.37f, -halfD + 0.9f), new Vector3(0.7f, 0.74f, 1.3f), new Color(0.75f, 0.62f, 0.45f));
            Box(root, "Rug", new Vector3(0.1f, 0.005f, -0.3f), new Vector3(1.8f, 0.01f, 1.2f), new Color(0.48f, 0.55f, 0.60f));

            var lightGo = new GameObject("Simulated Light", typeof(Light));
            lightGo.transform.SetParent(root, worldPositionStays: false);
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            Light sun = lightGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);
        }

        private void Box(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, worldPositionStays: false);
            box.transform.localPosition = center;
            box.transform.localScale = size;

            // Nothing raycasts against these; a collider would only cost time.
            Collider collider = box.GetComponent<Collider>();

            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = box.GetComponent<Renderer>();

            // "Legacy Shaders/Diffuse" is in Always Included Shaders by default;
            // the primitive's own Standard material is stripped from a build that
            // never references it, and renders magenta.
            if (litShader != null)
            {
                renderer.material = new Material(litShader);
            }

            renderer.material.color = color;
        }
    }
}
