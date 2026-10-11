# Handoff

## Branch
`scanner/r7-share-sheet`, stacked on `integration/r6-export-bundle` (PR #32),
then #31 and #30. Merge in order: #30, #31, #32, this.

## Base commit
`dcdae42` (R6 docs)

## Head commit
The commit that adds this file.

## What changed
Task `R7` (ADR-0013 section 2): **Send to Computer is the iOS share sheet.**

- **Native plugin** `apps/scanner/Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm`:
  `GhostMapShare_ShareFile(path, object, method)` presents
  `UIActivityViewController` on the main thread (popover anchored for iPad),
  and reports `"<0|1>|<activity>|<error>"` through `UnitySendMessage`. Unity's
  C functions are declared `extern "C"` (it is Objective-C++).
- **C# seam** (`Runtime/Share/`): `IShareSheet`, `ShareResult.TryParse`,
  `ShareController` (Idle/Sharing/Sent/Cancelled/Failed and the wording: "Sent
  with AirDrop.", "Saved to Files.", ...; refuses while open, without a sheet,
  or when the file is gone), `IosShareSheet` (a runtime-created GameObject
  receives the callback, so the scene does not depend on it),
  `EditorShareSheet` (reveals the zip in Finder), `ShareSheets.CreateDefault`.
- **UI** (`ScannerSceneBuilder`, `Scanner.unity` regenerated): the Finalize
  button is now **Finish**; a **Send to Computer** button follows it; **Rooms**
  in the top bar opens "Your rooms" with the five newest saved rooms
  (`SavedRoomLabel`: "Bedroom · 11 Oct, 14:30") and a Send button each
  (`SavedRoomsHud`). The network chip is **Live view** (developer), its card
  no longer opens by itself, and its button is **Find Viewer**.
- **Guide** (`ScanGuide`): finishing no longer mentions connecting; after
  Finish it shows Sending / Sent / the error; a cancel says nothing.
- 16 tests (`ShareControllerTests` with `FakeShareSheet`,
  `SavedRoomLabelTests`); `ScanGuideTests` updated for the new wording.

## Contract impact
- None. No shared, schema, protocol or export-bundle change.

## How to test
1. `./tools/run_unity_tests.sh scanner`
2. iOS: Configure Scanner XR, Build Scanner, run on the iPhone. Finish a scan,
   tap **Send to Computer**, pick the Mac under AirDrop. Then Save to Files
   once, Mail once. Each zip must unzip on the Mac and its `room.html` open.
   Cancel the sheet once: no error, the button works again.
3. Restart, open **Rooms**: the room is listed; send it again from there.

## Test results
All on the owner's Mac, 2026-10-11:
- Scanner EditMode **710/710**; `ScannerSceneTests` passes on the regenerated
  scene. Shared (292) and viewer (569) unchanged by this task.
- `ConfigureXr` + `BuildScanner` + unsigned `xcodebuild` Release iphoneos:
  `BUILD SUCCEEDED`; `_GhostMapShare_ShareFile` is in `UnityFramework`.
- **iOS Simulator** (iPhone 17, iOS 26.5, Simulator demo build): with a bundle
  placed in `Documents/scans`, **Rooms** listed it, **Send to Computer** opened
  the real share sheet with the 182 KB zip, **Save to Files** → Save called back
  and the panel said "Saved to Files."; the saved file was byte-identical.
  The project settings changed by the Simulator build were restored.
- Not done: AirDrop (no AirDrop in the Simulator), a real iPhone, iPad.

## Known failures
- None in the automated suites.
- Simulator demo mode: the automatic scan reached "100% seen" but would not
  finish ("Having trouble? Tap Help GhostMap."). Pre-existing behaviour of the
  demo, not touched by this task; recorded in `docs/status/scanner.md`.

## Known limitations
- If iOS never calls the completion handler, the Send buttons stay disabled
  until Restart (no timeout).
- Saved rooms cannot be deleted from the app yet.

## Files most important to read next
- `apps/scanner/Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Share/ShareController.cs`
- `docs/tasks/R2-first-device-session.md` (the session that now exercises all of it)

## Next task
- Owner: `R2` on the iPhone, ending with Finish and AirDrop to the Mac.
- Agent: `R4` (YOLO-n identification) code and tests.
