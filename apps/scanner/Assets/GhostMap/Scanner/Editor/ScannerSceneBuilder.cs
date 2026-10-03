using System;
using System.Collections.Generic;
using System.IO;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Bootstrap;
using GhostMap.Scanner.UI;
using GhostMap.Shared.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Builds the scanner scene from scratch and saves it. Uses AR Foundation's
    /// own "GameObject/XR/AR Session" and "GameObject/XR/XR Origin (Mobile AR)"
    /// menu commands to create the AR Session and AR Camera hierarchy, so the
    /// tracked-pose wiring matches exactly what the package expects, then adds
    /// ARPlaneManager and ARRaycastManager and the scan UI.
    ///
    /// <para><b>The UI is three layers, none positioned by pixel:</b></para>
    /// <list type="bullet">
    /// <item><description>a top bar with Restart, the computer chip, Details,
    /// and a header card naming the step and showing progress;</description></item>
    /// <item><description>the crosshair at dead center, with a bubble beside it
    /// naming what to point at;</description></item>
    /// <item><description>one bottom sheet holding the instruction, a message
    /// line, and every step's buttons in shared rows.</description></item>
    /// </list>
    ///
    /// <para>Each HUD still shows only its own step's buttons, and
    /// <see cref="ScannerGuideHud"/> collapses rows with nothing visible, so
    /// the sheet always fits exactly the current step. The per-step debug
    /// readouts the S1-S6 device tests rely on are kept, behind Details.</para>
    /// </summary>
    public static class ScannerSceneBuilder
    {
        private const string ScenePath = "Assets/GhostMap/Scanner/Scanner.unity";

        [MenuItem("GhostMap/Build Scanner Scene")]
        public static void BuildScene()
        {
            ScannerUiKit.EnsureSprites();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject arSessionGo = CreateFromMenu("GameObject/XR/AR Session");
            GameObject xrOriginGo = CreateFromMenu("GameObject/XR/XR Origin (Mobile AR)");

            var planeManager = xrOriginGo.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            var raycastManager = xrOriginGo.AddComponent<ARRaycastManager>();

            Camera arCamera = xrOriginGo.GetComponentInChildren<Camera>();
            if (arCamera == null)
            {
                throw new InvalidOperationException("XR Origin (Mobile AR) did not create an AR camera.");
            }

            var spatialProvider = xrOriginGo.AddComponent<ArSpatialProvider>();
            AssignSerializedReferences(
                spatialProvider,
                ("raycastManager", raycastManager),
                // ADR-0006 enumerates detected planes for furniture detection,
                // so the provider needs the plane manager and not only the
                // raycast manager built on top of it.
                ("planeManager", planeManager),
                ("arCamera", arCamera));

            // The Button needs an EventSystem to receive touches at all. The
            // Input System backend is the one this project ships (see
            // ScannerInputSettings), so the matching module is the Input System
            // one; the legacy StandaloneInputModule reads UnityEngine.Input,
            // which is exactly the layer that was dead in the second S1 device
            // failure.
            CreateEventSystem();

            Transform canvas = CreateCanvas().transform;

            // ---------------------------------------------------------------
            // Crosshair and the bubble that names what to point at
            // ---------------------------------------------------------------

            // Outside the safe area on purpose: the raycast fires through the
            // exact screen center (ISpatialProvider.CenterScreenPoint), so the
            // crosshair must sit there regardless of notch or home indicator.
            Image crosshair = ScannerUiKit.Dot(canvas, "Crosshair", 84f, Color.white, ScannerUiKit.Ring);
            Center(crosshair.rectTransform, Vector2.zero);
            Image crosshairCenter = ScannerUiKit.Dot(crosshair.transform, "CrosshairCenter", 14f, Color.white);
            Center(crosshairCenter.rectTransform, Vector2.zero);

            RectTransform bubble = ScannerUiKit.Rect("CoachBubble", canvas);
            Center(bubble, new Vector2(0f, 64f));
            bubble.pivot = new Vector2(0.5f, 0f);
            Image bubbleBackground = ScannerUiKit.PanelBackground(bubble.gameObject, new Color(0.07f, 0.08f, 0.10f, 0.86f), 30f);
            bubbleBackground.raycastTarget = false;
            var bubbleLayout = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(30, 30, 14, 14);
            bubbleLayout.childControlWidth = true;
            bubbleLayout.childControlHeight = true;
            var bubbleFitter = bubble.gameObject.AddComponent<ContentSizeFitter>();
            bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Text coachText = ScannerUiKit.Label(
                bubble, "CoachText", "Aim at the floor", 30, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            coachText.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform safeArea = ScannerUiKit.Rect("SafeArea", canvas);
            ScannerUiKit.Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            // ---------------------------------------------------------------
            // Bottom sheet
            // ---------------------------------------------------------------

            RectTransform sheet = ScannerUiKit.Card("Sheet", safeArea, ScannerUiKit.CardColor, 44f, 32, 16f);
            sheet.anchorMin = new Vector2(0f, 0f);
            sheet.anchorMax = new Vector2(1f, 0f);
            sheet.pivot = new Vector2(0.5f, 0f);
            sheet.offsetMin = new Vector2(16f, 16f);
            sheet.offsetMax = new Vector2(-16f, 16f);
            ScannerUiKit.HugHeight(sheet.gameObject);

            Text instructionText = ScannerUiKit.Label(sheet, "Instruction", string.Empty, 38, ScannerUiKit.TextPrimary);

            RectTransform messagePill = ScannerUiKit.Rect("MessagePill", sheet);
            Image messageBackground = ScannerUiKit.PanelBackground(messagePill.gameObject, ScannerUiKit.Fill, 22f);
            messageBackground.raycastTarget = false;
            ScannerUiKit.Column(messagePill.gameObject, 22, 14, 0f);
            Text messageText = ScannerUiKit.Label(messagePill, "MessageText", string.Empty, 29, ScannerUiKit.TextSecondary);
            messageText.lineSpacing = 1f;

            // Above the manual Place row: when a surface has been detected,
            // accepting it is the quicker path, and the row collapses away
            // entirely when nothing is detected.
            RectTransform detectRow = ScannerUiKit.Row("DetectRow", sheet, 12f);
            RectTransform primaryRow = ScannerUiKit.Row("PrimaryRow", sheet, 16f);
            RectTransform choiceRow = ScannerUiKit.Row("ChoiceRow", sheet, 16f);
            RectTransform manualRow = ScannerUiKit.Row("ManualHeightRow", sheet, 16f);
            RectTransform sizeRow1 = ScannerUiKit.Row("SizeRow1", sheet, 12f);
            RectTransform sizeRow2 = ScannerUiKit.Row("SizeRow2", sheet, 12f);
            RectTransform secondaryRow = ScannerUiKit.Row("SecondaryRow", sheet, 16f);

            // Primary row: exactly one of these is visible in any phase, and
            // it takes the full width.
            Button lockFloorButton = ScannerUiKit.Button(primaryRow, "LockFloorButton", "Lock Floor", ButtonStyle.Primary, out _);
            Button scanRoomButton = ScannerUiKit.Button(primaryRow, "ScanRoomButton", "Scan Room", ButtonStyle.Primary, out Text scanRoomLabel);
            Button sweepPrimaryButton = ScannerUiKit.Button(primaryRow, "SweepWallButton", "Start Tracing Walls", ButtonStyle.Primary, out Text sweepPrimaryLabel);
            Button cornerPrimaryButton = ScannerUiKit.Button(primaryRow, "CaptureCornerButton", "Capture Corner", ButtonStyle.Primary, out Text cornerPrimaryLabel);
            Button captureHeightButton = ScannerUiKit.Button(primaryRow, "CaptureHeightButton", "Measure Height", ButtonStyle.Primary, out Text captureHeightLabel);
            Button captureOpeningPointButton = ScannerUiKit.Button(primaryRow, "CaptureOpeningPointButton", "Mark Bottom-Left", ButtonStyle.Primary, out Text captureOpeningPointLabel);
            Button placeObjectButton = ScannerUiKit.Button(primaryRow, "PlaceObjectButton", "Place", ButtonStyle.Primary, out _);
            Button finalizeButton = ScannerUiKit.Button(primaryRow, "FinalizeButton", "Finish & Send", ButtonStyle.Primary, out _);

            // Choice row: which wall, which kind of thing.
            Button selectWallButton = ScannerUiKit.Button(choiceRow, "SelectWallButton", "Wall 1 of 4", ButtonStyle.Secondary, out Text selectWallLabel);
            Button selectOpeningWallButton = ScannerUiKit.Button(choiceRow, "SelectOpeningWallButton", "Wall 1 of 4", ButtonStyle.Secondary, out Text selectOpeningWallLabel);
            Button toggleOpeningTypeButton = ScannerUiKit.Button(choiceRow, "ToggleOpeningTypeButton", "Type: Door", ButtonStyle.Secondary, out Text toggleOpeningTypeLabel);
            Button selectObjectTypeButton = ScannerUiKit.Button(choiceRow, "SelectObjectTypeButton", "Type: Bed", ButtonStyle.Secondary, out Text selectObjectTypeLabel);

            // ADR-0006 detected furniture: what it is, pass on it, see the
            // next one, take it. Shown only while a candidate exists.
            Button detectTypeButton = ScannerUiKit.Button(detectRow, "DetectTypeButton", "Is: Generic", ButtonStyle.Secondary, out Text detectTypeLabel);
            Button detectSkipButton = ScannerUiKit.Button(detectRow, "DetectSkipButton", "Skip", ButtonStyle.Secondary, out _);
            Button detectNextButton = ScannerUiKit.Button(detectRow, "DetectNextButton", "Next", ButtonStyle.Secondary, out _);
            Button detectAddButton = ScannerUiKit.Button(detectRow, "DetectAddButton", "Add", ButtonStyle.Next, out Text detectAddLabel);
            Button addAllDetectedButton = ScannerUiKit.Button(detectRow, "AddAllDetectedButton", "Add All", ButtonStyle.Next, out _);

            // Typed height: the plan's fallback when the ceiling cannot be aimed at.
            InputField manualHeightInput = ScannerUiKit.Field(manualRow, "ManualHeightInput", "Or type height, e.g. 2.45", 100f);
            manualHeightInput.contentType = InputField.ContentType.DecimalNumber;
            ScannerUiKit.Size(manualHeightInput.gameObject, 100f, flexibleWidth: 2f);
            Button useManualHeightButton = ScannerUiKit.Button(manualRow, "UseManualHeightButton", "Use", ButtonStyle.Secondary, out _);

            // Furniture size, shown only once something has been placed.
            Button widthMinusButton = ScannerUiKit.Button(sizeRow1, "WidthMinusButton", "Width −", ButtonStyle.Secondary, out _, 84f);
            Button widthPlusButton = ScannerUiKit.Button(sizeRow1, "WidthPlusButton", "Width +", ButtonStyle.Secondary, out _, 84f);
            Button depthMinusButton = ScannerUiKit.Button(sizeRow1, "DepthMinusButton", "Depth −", ButtonStyle.Secondary, out _, 84f);
            Button depthPlusButton = ScannerUiKit.Button(sizeRow1, "DepthPlusButton", "Depth +", ButtonStyle.Secondary, out _, 84f);
            Button heightMinusButton = ScannerUiKit.Button(sizeRow2, "HeightMinusButton", "Height −", ButtonStyle.Secondary, out _, 84f);
            Button heightPlusButton = ScannerUiKit.Button(sizeRow2, "HeightPlusButton", "Height +", ButtonStyle.Secondary, out _, 84f);
            Button yawMinusButton = ScannerUiKit.Button(sizeRow2, "YawMinusButton", "Turn Left", ButtonStyle.Secondary, out _, 84f);
            Button yawPlusButton = ScannerUiKit.Button(sizeRow2, "YawPlusButton", "Turn Right", ButtonStyle.Secondary, out _, 84f);

            // Secondary row: undo, redo, cancel, and moving on.
            Button autoHelpButton = ScannerUiKit.Button(secondaryRow, "AutoHelpButton", "Help GhostMap", ButtonStyle.Secondary, out Text autoHelpLabel);
            Button sweepUndoButton = ScannerUiKit.Button(secondaryRow, "UndoWallButton", "Undo", ButtonStyle.Secondary, out _);
            Button sweepCancelButton = ScannerUiKit.Button(secondaryRow, "CancelSweepButton", "Cancel", ButtonStyle.Secondary, out _);
            Button walkCornersButton = ScannerUiKit.Button(secondaryRow, "WalkCornersButton", "Walk Corners Instead", ButtonStyle.Secondary, out _);
            Button cornerUndoButton = ScannerUiKit.Button(secondaryRow, "UndoCornerButton", "Undo", ButtonStyle.Secondary, out _);
            Button redoButton = ScannerUiKit.Button(secondaryRow, "RedoCornersButton", "Redo Walls", ButtonStyle.Secondary, out _);
            Button undoOpeningButton = ScannerUiKit.Button(secondaryRow, "UndoOpeningButton", "Undo", ButtonStyle.Secondary, out _);
            Button finishOpeningsButton = ScannerUiKit.Button(secondaryRow, "FinishOpeningsButton", "Next", ButtonStyle.Next, out _);
            Button undoObjectButton = ScannerUiKit.Button(secondaryRow, "UndoObjectButton", "Undo", ButtonStyle.Secondary, out _);
            Button finishObjectsButton = ScannerUiKit.Button(secondaryRow, "FinishObjectsButton", "Next", ButtonStyle.Next, out _);

            // ---------------------------------------------------------------
            // Top bar
            // ---------------------------------------------------------------

            RectTransform topBar = ScannerUiKit.Rect("TopBar", safeArea);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.offsetMin = new Vector2(16f, -8f);
            topBar.offsetMax = new Vector2(-16f, -8f);
            ScannerUiKit.Column(topBar.gameObject, 0, 0, 14f);
            ScannerUiKit.HugHeight(topBar.gameObject);

            RectTransform topRow = ScannerUiKit.Row("TopRow", topBar, 12f);
            topRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

            Button resetButton = ScannerUiKit.Button(topRow, "ResetButton", "Restart", ButtonStyle.Pill, out Text resetLabel);
            ScannerUiKit.Size(resetButton.gameObject, 80f, 210f, 0f);

            RectTransform spacer = ScannerUiKit.Rect("Spacer", topRow);
            ScannerUiKit.Size(spacer.gameObject, 80f, 0f, 1f);

            Button connectionChip = ScannerUiKit.Button(topRow, "ConnectionChip", "Connect computer", ButtonStyle.Pill, out Text connectionChipLabel);
            ScannerUiKit.Size(connectionChip.gameObject, 80f, 340f, 0f);
            connectionChipLabel.rectTransform.offsetMin = new Vector2(52f, 4f);
            Image connectionDot = ScannerUiKit.Dot(connectionChip.transform, "StatusDot", 18f, new Color(1f, 1f, 1f, 0.45f));
            connectionDot.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            connectionDot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            connectionDot.rectTransform.anchoredPosition = new Vector2(36f, 0f);

            Button detailsButton = ScannerUiKit.Button(topRow, "DetailsButton", "Details", ButtonStyle.Pill, out Text detailsButtonLabel);
            ScannerUiKit.Size(detailsButton.gameObject, 80f, 170f, 0f);

            RectTransform header = ScannerUiKit.Card("HeaderCard", topBar, ScannerUiKit.CardColor, 40f, 28, 8f);
            Text stepLabel = ScannerUiKit.Label(header, "StepLabel", "STEP 1 OF 7", 24, ScannerUiKit.Accent, FontStyle.Bold);
            Text titleText = ScannerUiKit.Label(header, "Title", "Find the floor", 50, ScannerUiKit.TextPrimary, FontStyle.Bold);

            RectTransform progress = ScannerUiKit.Row("Progress", header, 8f);
            var segments = new Image[ScanGuide.StepCount];
            for (int i = 0; i < segments.Length; i++)
            {
                RectTransform segment = ScannerUiKit.Rect($"Segment{i + 1}", progress);
                segments[i] = ScannerUiKit.Background(segment.gameObject, new Color(1f, 1f, 1f, 0.18f), 4f);
                segments[i].raycastTarget = false;
                ScannerUiKit.Size(segment.gameObject, 8f, flexibleWidth: 1f);
            }

            RectTransform demoBadge = ScannerUiKit.Rect("DemoBadge", topBar);
            ScannerUiKit.PanelBackground(demoBadge.gameObject, new Color(1f, 0.71f, 0.28f, 0.22f), 26f).raycastTarget = false;
            ScannerUiKit.Column(demoBadge.gameObject, 24, 12, 0f);
            ScannerUiKit.Label(
                demoBadge, "DemoText", "Demo room — no camera here. Drag to look around.", 26,
                new Color(1f, 0.86f, 0.62f), FontStyle.Normal, TextAnchor.MiddleCenter);

            RectTransform connectPanel = ScannerUiKit.Card("ConnectPanel", topBar, ScannerUiKit.CardColor, 40f, 28, 16f);
            ScannerUiKit.Label(connectPanel, "ConnectTitle", "Connect to your computer", 36, ScannerUiKit.TextPrimary, FontStyle.Bold);
            ScannerUiKit.Label(
                connectPanel, "ConnectHelp",
                "Open GhostMap on your computer, on the same Wi-Fi. Type the address it shows.", 28,
                ScannerUiKit.TextSecondary);
            RectTransform addressRow = ScannerUiKit.Row("AddressRow", connectPanel, 12f);
            InputField hostInput = ScannerUiKit.Field(addressRow, "HostInput", "Address, e.g. 192.168.1.20", 96f);
            ScannerUiKit.Size(hostInput.gameObject, 96f, flexibleWidth: 3f);
            InputField portInput = ScannerUiKit.Field(addressRow, "PortInput", "Port", 96f);
            ScannerUiKit.Size(portInput.gameObject, 96f, 190f, 0f);
            portInput.text = ProtocolConstants.Port.ToString();
            portInput.contentType = InputField.ContentType.IntegerNumber;
            Button connectButton = ScannerUiKit.Button(connectPanel, "ConnectButton", "Connect", ButtonStyle.Primary, out _, 104f);
            Text networkStatusText = ScannerUiKit.Label(connectPanel, "NetworkStatusText", string.Empty, 24, ScannerUiKit.TextSecondary);

            // ---------------------------------------------------------------
            // Details: every per-step debug readout, out of the user's way
            // ---------------------------------------------------------------

            // Created after the top bar so it covers the header card, and
            // starting below the top row so Details/Close stays reachable.
            RectTransform details = ScannerUiKit.Rect("DetailsPanel", safeArea);
            ScannerUiKit.Stretch(details, 16f, 16f, 16f, 120f);
            ScannerUiKit.Background(details.gameObject, new Color(0.05f, 0.06f, 0.08f, 0.96f), 40f);

            RectTransform viewport = ScannerUiKit.Rect("Viewport", details);
            ScannerUiKit.Stretch(viewport, 28f, 28f, 28f, 28f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform detailsContent = ScannerUiKit.Rect("Content", viewport);
            detailsContent.anchorMin = new Vector2(0f, 1f);
            detailsContent.anchorMax = new Vector2(1f, 1f);
            detailsContent.pivot = new Vector2(0.5f, 1f);
            detailsContent.offsetMin = Vector2.zero;
            detailsContent.offsetMax = Vector2.zero;
            ScannerUiKit.Column(detailsContent.gameObject, 0, 0, 22f);
            ScannerUiKit.HugHeight(detailsContent.gameObject);

            var scroll = details.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = detailsContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            ScannerUiKit.Label(detailsContent, "DetailsTitle", "Developer details", 36, ScannerUiKit.TextPrimary, FontStyle.Bold);
            Text scanStatusText = DetailsText(detailsContent, "ScanStatusText");
            Text floorLockReadout = DetailsText(detailsContent, "FloorLockReadout");
            Text autoScanReadout = DetailsText(detailsContent, "AutoScanReadout");
            Text sweepReadout = DetailsText(detailsContent, "WallSweepReadout");
            Text cornerReadout = DetailsText(detailsContent, "CornerCaptureReadout");
            Text heightReadout = DetailsText(detailsContent, "HeightCaptureReadout");
            Text openingReadout = DetailsText(detailsContent, "OpeningCaptureReadout");
            Text detectionReadout = DetailsText(detailsContent, "FurnitureDetectionReadout");
            Text objectReadout = DetailsText(detailsContent, "ObjectPlacementReadout");
            Text diagnosticsText = DetailsText(detailsContent, "DiagnosticsText");

            // ---------------------------------------------------------------
            // Behaviours
            // ---------------------------------------------------------------

            var bootstrapGo = new GameObject("ScannerBootstrap", typeof(ScannerBootstrap));
            AssignSerializedReferences(
                bootstrapGo.GetComponent<ScannerBootstrap>(),
                ("arSession", arSessionGo.GetComponent<ARSession>()),
                ("planeManager", planeManager),
                ("arCamera", arCamera),
                ("diagnosticsText", diagnosticsText));

            var hudGo = new GameObject("FloorLockHud", typeof(FloorLockHud));
            var floorLockHud = hudGo.GetComponent<FloorLockHud>();
            AssignSerializedReferences(
                floorLockHud,
                ("spatialProvider", spatialProvider),
                ("lockFloorButton", lockFloorButton),
                ("readoutText", floorLockReadout));

            // The automatic room scan: turn once, GhostMap finds the walls.
            var autoScanHudGo = new GameObject("AutoScanHud", typeof(AutoScanHud));
            AssignSerializedReferences(
                autoScanHudGo.GetComponent<AutoScanHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("primaryButton", scanRoomButton),
                ("primaryButtonLabel", scanRoomLabel),
                ("secondaryButton", autoHelpButton),
                ("secondaryButtonLabel", autoHelpLabel),
                ("addAllButton", addAllDetectedButton),
                ("readoutText", autoScanReadout));

            // ADR-0005 wall sweeping — now the "Help GhostMap" fallback.
            var sweepHudGo = new GameObject("WallSweepHud", typeof(WallSweepHud));
            AssignSerializedReferences(
                sweepHudGo.GetComponent<WallSweepHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("primaryButton", sweepPrimaryButton),
                ("primaryButtonLabel", sweepPrimaryLabel),
                ("cancelButton", sweepCancelButton),
                ("undoButton", sweepUndoButton),
                ("walkCornersButton", walkCornersButton),
                ("readoutText", sweepReadout));

            // Task S3 corner capture — retained as the ADR-0005 fallback.
            var cornerHudGo = new GameObject("CornerCaptureHud", typeof(CornerCaptureHud));
            AssignSerializedReferences(
                cornerHudGo.GetComponent<CornerCaptureHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("primaryButton", cornerPrimaryButton),
                ("primaryButtonLabel", cornerPrimaryLabel),
                ("undoButton", cornerUndoButton),
                ("redoButton", redoButton),
                ("readoutText", cornerReadout));

            // Task S4 height capture.
            var heightHudGo = new GameObject("HeightCaptureHud", typeof(HeightCaptureHud));
            AssignSerializedReferences(
                heightHudGo.GetComponent<HeightCaptureHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("selectWallButton", selectWallButton),
                ("selectWallLabel", selectWallLabel),
                ("captureHeightButton", captureHeightButton),
                ("captureHeightLabel", captureHeightLabel),
                ("manualHeightInput", manualHeightInput),
                ("useManualHeightButton", useManualHeightButton),
                ("readoutText", heightReadout));

            // Task S5 Part 1: openings.
            var openingHudGo = new GameObject("OpeningCaptureHud", typeof(OpeningCaptureHud));
            AssignSerializedReferences(
                openingHudGo.GetComponent<OpeningCaptureHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("selectWallButton", selectOpeningWallButton),
                ("selectWallLabel", selectOpeningWallLabel),
                ("toggleTypeButton", toggleOpeningTypeButton),
                ("toggleTypeLabel", toggleOpeningTypeLabel),
                ("capturePointButton", captureOpeningPointButton),
                ("capturePointLabel", captureOpeningPointLabel),
                ("undoOpeningButton", undoOpeningButton),
                ("finishOpeningsButton", finishOpeningsButton),
                ("readoutText", openingReadout));

            // ADR-0006 furniture detection. Feeds the same object store as
            // manual placement, which stays available as the fallback.
            var detectionHudGo = new GameObject("FurnitureDetectionHud", typeof(FurnitureDetectionHud));
            AssignSerializedReferences(
                detectionHudGo.GetComponent<FurnitureDetectionHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("typeButton", detectTypeButton),
                ("typeButtonLabel", detectTypeLabel),
                ("nextButton", detectNextButton),
                ("addButton", detectAddButton),
                ("addButtonLabel", detectAddLabel),
                ("skipButton", detectSkipButton),
                ("readoutText", detectionReadout));

            // Task S5 Part 2: furniture.
            var objectHudGo = new GameObject("ObjectPlacementHud", typeof(ObjectPlacementHud));
            AssignSerializedReferences(
                objectHudGo.GetComponent<ObjectPlacementHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("selectTypeButton", selectObjectTypeButton),
                ("selectTypeLabel", selectObjectTypeLabel),
                ("placeObjectButton", placeObjectButton),
                ("undoObjectButton", undoObjectButton),
                ("widthPlusButton", widthPlusButton),
                ("widthMinusButton", widthMinusButton),
                ("depthPlusButton", depthPlusButton),
                ("depthMinusButton", depthMinusButton),
                ("heightPlusButton", heightPlusButton),
                ("heightMinusButton", heightMinusButton),
                ("yawPlusButton", yawPlusButton),
                ("yawMinusButton", yawMinusButton),
                ("finishObjectsButton", finishObjectsButton),
                ("readoutText", objectReadout));

            // Task S6: networking, finalization, restart.
            var scannerHudGo = new GameObject("ScannerHudController", typeof(ScannerHudController));
            var scannerHud = scannerHudGo.GetComponent<ScannerHudController>();
            AssignSerializedReferences(
                scannerHud,
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("hostInput", hostInput),
                ("portInput", portInput),
                ("connectButton", connectButton),
                ("networkStatusText", networkStatusText),
                ("resetButton", resetButton),
                ("finalizeButton", finalizeButton),
                ("statusText", scanStatusText),
                ("resetLabel", resetLabel),
                ("connectionChipButton", connectionChip),
                ("connectionChipLabel", connectionChipLabel),
                ("connectionChipDot", connectionDot),
                ("connectPanel", connectPanel.gameObject));

            // The guide: words, progress, crosshair state, row collapsing.
            var guideGo = new GameObject("ScannerGuideHud", typeof(ScannerGuideHud));
            var guide = guideGo.GetComponent<ScannerGuideHud>();
            AssignSerializedReferences(
                guide,
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("scannerHud", scannerHud),
                ("stepLabel", stepLabel),
                ("titleText", titleText),
                ("demoBadge", demoBadge.gameObject),
                ("crosshair", crosshair),
                ("coachBubble", bubble.gameObject),
                ("coachBubbleBackground", bubbleBackground),
                ("coachText", coachText),
                ("instructionText", instructionText),
                ("messagePill", messagePill.gameObject),
                ("messageBackground", messageBackground),
                ("messageText", messageText),
                ("detailsButton", detailsButton),
                ("detailsButtonLabel", detailsButtonLabel),
                ("detailsPanel", details.gameObject));
            AssignSerializedArray(guide, "progressSegments", segments);
            AssignSerializedArray(
                guide,
                "collapsibleRows",
                new UnityEngine.Object[]
                {
                    detectRow.gameObject,
                    primaryRow.gameObject,
                    choiceRow.gameObject,
                    manualRow.gameObject,
                    sizeRow1.gameObject,
                    sizeRow2.gameObject,
                    secondaryRow.gameObject
                });

            // Start hidden; the HUDs and the guide reveal what each step needs.
            details.gameObject.SetActive(false);
            connectPanel.gameObject.SetActive(false);
            demoBadge.gameObject.SetActive(false);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) !);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"GhostMap: scanner scene written to {ScenePath}");
        }

        /// <summary>
        /// Opens the saved scene and asserts that every component the scan
        /// needs is present and wired.
        ///
        /// This exists for the same reason
        /// <c>ScannerXrSettings.VerifyIosArKitConfiguration</c> does: a scene
        /// reference that is silently null fails only on a phone, and looks
        /// identical to a tracking problem when it does. Throws on the first
        /// broken link.
        /// </summary>
        public static void VerifyScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                Require(FindInScene<ARSession>(scene) != null, "no ARSession");
                Require(FindInScene<ARRaycastManager>(scene) != null, "no ARRaycastManager");
                Require(FindInScene<EventSystem>(scene) != null, "no EventSystem; UI touches are dead");
                Require(
                    FindInScene<BaseInputModule>(scene) != null,
                    "no input module on the EventSystem; the Lock Floor button can never fire");

                var planeManager = FindInScene<ARPlaneManager>(scene);
                Require(planeManager != null, "no ARPlaneManager");
                Require(
                    (planeManager.requestedDetectionMode & PlaneDetectionMode.Horizontal) != 0,
                    "ARPlaneManager does not request horizontal planes, so no floor can be detected");

                var provider = FindInScene<ArSpatialProvider>(scene);
                Require(provider != null, "no ArSpatialProvider");
                RequireAssigned(provider, "raycastManager", "planeManager", "arCamera");

                Require(FindInScene<SafeAreaFitter>(scene) != null, "no SafeAreaFitter; the UI would sit under the Dynamic Island");

                var hud = FindInScene<FloorLockHud>(scene);
                Require(hud != null, "no FloorLockHud");
                RequireAssigned(hud, "spatialProvider", "lockFloorButton", "readoutText");

                var autoHud = FindInScene<AutoScanHud>(scene);
                Require(autoHud != null, "no AutoScanHud");
                RequireAssigned(
                    autoHud,
                    "floorLockHud",
                    "spatialProvider",
                    "primaryButton",
                    "primaryButtonLabel",
                    "secondaryButton",
                    "secondaryButtonLabel",
                    "addAllButton",
                    "readoutText");

                var sweepHud = FindInScene<WallSweepHud>(scene);
                Require(sweepHud != null, "no WallSweepHud");
                RequireAssigned(
                    sweepHud,
                    "floorLockHud",
                    "spatialProvider",
                    "primaryButton",
                    "primaryButtonLabel",
                    "cancelButton",
                    "undoButton",
                    "walkCornersButton",
                    "readoutText");

                var cornerHud = FindInScene<CornerCaptureHud>(scene);
                Require(cornerHud != null, "no CornerCaptureHud");
                RequireAssigned(
                    cornerHud,
                    "floorLockHud",
                    "spatialProvider",
                    "primaryButton",
                    "primaryButtonLabel",
                    "undoButton",
                    "redoButton",
                    "readoutText");

                var heightHud = FindInScene<HeightCaptureHud>(scene);
                Require(heightHud != null, "no HeightCaptureHud");
                RequireAssigned(
                    heightHud,
                    "floorLockHud",
                    "spatialProvider",
                    "selectWallButton",
                    "selectWallLabel",
                    "captureHeightButton",
                    "captureHeightLabel",
                    "manualHeightInput",
                    "useManualHeightButton",
                    "readoutText");

                var openingHud = FindInScene<OpeningCaptureHud>(scene);
                Require(openingHud != null, "no OpeningCaptureHud");
                RequireAssigned(
                    openingHud,
                    "floorLockHud",
                    "spatialProvider",
                    "selectWallButton",
                    "selectWallLabel",
                    "toggleTypeButton",
                    "toggleTypeLabel",
                    "capturePointButton",
                    "capturePointLabel",
                    "undoOpeningButton",
                    "finishOpeningsButton",
                    "readoutText");

                var detectionHud = FindInScene<FurnitureDetectionHud>(scene);
                Require(detectionHud != null, "no FurnitureDetectionHud");
                RequireAssigned(
                    detectionHud,
                    "floorLockHud",
                    "spatialProvider",
                    "typeButton",
                    "typeButtonLabel",
                    "nextButton",
                    "addButton",
                    "addButtonLabel",
                    "skipButton",
                    "readoutText");

                var objectHud = FindInScene<ObjectPlacementHud>(scene);
                Require(objectHud != null, "no ObjectPlacementHud");
                RequireAssigned(
                    objectHud,
                    "floorLockHud",
                    "spatialProvider",
                    "selectTypeButton",
                    "selectTypeLabel",
                    "placeObjectButton",
                    "undoObjectButton",
                    "widthPlusButton",
                    "widthMinusButton",
                    "depthPlusButton",
                    "depthMinusButton",
                    "heightPlusButton",
                    "heightMinusButton",
                    "yawPlusButton",
                    "yawMinusButton",
                    "finishObjectsButton",
                    "readoutText");

                var scannerHud = FindInScene<ScannerHudController>(scene);
                Require(scannerHud != null, "no ScannerHudController");
                RequireAssigned(
                    scannerHud,
                    "floorLockHud",
                    "spatialProvider",
                    "hostInput",
                    "portInput",
                    "connectButton",
                    "networkStatusText",
                    "resetButton",
                    "finalizeButton",
                    "statusText",
                    "resetLabel",
                    "connectionChipButton",
                    "connectionChipLabel",
                    "connectionChipDot",
                    "connectPanel");

                var guide = FindInScene<ScannerGuideHud>(scene);
                Require(guide != null, "no ScannerGuideHud");
                RequireAssigned(
                    guide,
                    "floorLockHud",
                    "spatialProvider",
                    "scannerHud",
                    "stepLabel",
                    "titleText",
                    "demoBadge",
                    "crosshair",
                    "coachBubble",
                    "coachBubbleBackground",
                    "coachText",
                    "instructionText",
                    "messagePill",
                    "messageBackground",
                    "messageText",
                    "detailsButton",
                    "detailsButtonLabel",
                    "detailsPanel");
                RequireArray(guide, "progressSegments", ScanGuide.StepCount);
                RequireArray(guide, "collapsibleRows", 1);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static void Require(bool condition, string whatIsWrong)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Scanner scene is not capture-ready: {whatIsWrong}.");
            }
        }

        private static void RequireAssigned(UnityEngine.Object target, params string[] fieldNames)
        {
            var serialized = new SerializedObject(target);
            foreach (string fieldName in fieldNames)
            {
                SerializedProperty property = serialized.FindProperty(fieldName);
                Require(property != null, $"{target.GetType().Name}.{fieldName} does not exist");
                Require(
                    property.objectReferenceValue != null,
                    $"{target.GetType().Name}.{fieldName} is not assigned");
            }
        }

        /// <summary>Asserts an array field has at least <paramref name="minimum"/> entries, none null.</summary>
        private static void RequireArray(UnityEngine.Object target, string fieldName, int minimum)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            string name = $"{target.GetType().Name}.{fieldName}";

            Require(property != null && property.isArray, $"{name} does not exist");
            Require(property.arraySize >= minimum, $"{name} has {property.arraySize} entries, expected {minimum}+");

            for (int i = 0; i < property.arraySize; i++)
            {
                Require(property.GetArrayElementAtIndex(i).objectReferenceValue != null, $"{name}[{i}] is not assigned");
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(includeInactive: true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void AssignSerializedReferences(
            UnityEngine.Object target,
            params (string Field, UnityEngine.Object Value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach ((string field, UnityEngine.Object value) in references)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null)
                {
                    throw new InvalidOperationException(
                        $"{target.GetType().Name} has no serialized field '{field}'.");
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignSerializedArray(
            UnityEngine.Object target,
            string field,
            IReadOnlyList<UnityEngine.Object> values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null || !property.isArray)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name} has no serialized array '{field}'.");
            }

            property.arraySize = values.Count;

            for (int i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateEventSystem()
        {
            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            var module = eventSystemGo.AddComponent<InputSystemUIInputModule>();

            // Without actions the module resolves nothing and every tap is
            // swallowed. The defaults cover point/click, which is all the
            // buttons need.
            module.AssignDefaultActions();
        }

        private static GameObject CreateCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvasGo;
        }

        private static void Center(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
        }

        private static Text DetailsText(Transform parent, string name)
        {
            return ScannerUiKit.Label(parent, name, string.Empty, 24, ScannerUiKit.TextSecondary);
        }

        private static GameObject CreateFromMenu(string menuPath)
        {
            Selection.activeGameObject = null;
            EditorApplication.ExecuteMenuItem(menuPath);
            GameObject created = Selection.activeGameObject;
            if (created == null)
            {
                throw new InvalidOperationException($"Menu item '{menuPath}' did not create a GameObject.");
            }

            return created;
        }
    }
}
