# R7: Send to Computer through the iOS share sheet

**Decision:** ADR-0013 section 2. **Workstream:** Scanner.
**Who:** an agent writes the plugin and the C# side; the owner device-tests.

## Goal

**Send to Computer** (on the finish screen and on every saved scan) opens the
iOS share sheet with the bundle zip from `R6`. The user picks their Mac under
AirDrop, or Save to Files, iCloud Drive or Mail. No IP, no network code.

## Design

### Native plugin

`apps/scanner/Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm`:

```objc
extern "C" void GhostMapShare_ShareFile(const char* path, const char* callbackObject, const char* callbackMethod);
```

- Build an `NSURL` file URL, create a `UIActivityViewController` with it, and
  present it from `UnityGetGLViewController()`.
- On iPad, set `popoverPresentationController.sourceView` to the root view and
  a centred `sourceRect`, or it crashes.
- In `completionWithItemsHandler`, call
  `UnitySendMessage(callbackObject, callbackMethod, "<completed>|<activityType>|<error>")`.
- Present on the main thread.

### C# seam

```csharp
public interface IShareSheet
{
    bool IsAvailable { get; }
    void Share(string filePath, Action<ShareResult> onFinished);
}

public readonly struct ShareResult { public bool Completed; public string ActivityType; public string Error; }
```

- `IosShareSheet : MonoBehaviour, IShareSheet` uses `[DllImport("__Internal")]`
  under `UNITY_IOS && !UNITY_EDITOR` and receives the `UnitySendMessage`
  callback.
- `EditorShareSheet` (Editor and Simulator) reveals the file in Finder /
  logs the path, so the flow is testable without a phone.
- A fake for EditMode tests.

### UI and flow

- Replace `ScannerHudController.OnSendToComputerPressed`'s discovery path with
  `IShareSheet.Share(savedBundlePath, ...)`.
- `ScanGuide` wording: "Room saved. Tap Send to Computer and choose your Mac."
  After completion: "Sent with AirDrop" / "Saved to Files"; on cancel: no
  error, the button stays available.
- The saved-scans list (from `R6`) gets a Send button per scan.
- Leave `PeerDiscoveryClient` and the TCP client in place but unused by the
  product flow; `R10` deletes them.

## Tests (EditMode)

- Send is only offered when a saved bundle exists.
- The fake receives the exact saved path.
- Completed / cancelled / failed results map to the right guide message.
- Cancelled keeps the scan and the button.

## Device check (owner)

AirDrop to the Mac three times in a row; Save to Files once; Mail once (or skip
if no mail account). Each zip unzips on the Mac and `room.html` opens. Note any
permission prompts.

## Done when

CI green; device check recorded in `docs/status/integration.md`; status pages
and a handoff updated.
