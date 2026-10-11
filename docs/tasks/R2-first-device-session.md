# R2: First device session

**Who:** the owner, on their iPhone, in a real furnished room. An agent's job is
to keep this checklist accurate and to turn the results into
`docs/status/integration.md` entries and follow-up tasks.

**Why:** everything imported in `R1` (auto scan, sweep, furniture detection,
guided UI) has passed EditMode tests but has never run on a phone.

## Before the session

1. Pull `main`. Run `./tools/run_unity_tests.sh` (or confirm CI is green).
2. In `apps/scanner`: **GhostMap > Configure Scanner XR (iOS)**, restart Unity,
   then **GhostMap > Build Scanner (iOS)**.
3. Open `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, choose the signing
   team, **delete the old GhostMap app from the phone**, then Product > Clean
   Build Folder and Run.
4. In Xcode's console, filter on `GhostMap` to see the diagnostic lines:
   `GhostMap auto:`, `GhostMap sweep:`, `GhostMap closure:`, `GhostMap detect:`.

## Steps and what to record

| # | Do | Record |
| --- | --- | --- |
| 1 | Launch; point at the floor; **Lock Floor** | Seconds until the button enabled; any tracking warnings |
| 2 | **Scan Room**; stand near the centre and turn slowly once | Walls found (0-4); whether it finished; the guide's messages; `GhostMap auto:` lines; time taken |
| 3 | If fewer than 4 walls: follow the "look toward" hint, then **Help GhostMap** | Whether the hint pointed at the missing wall; sweep results per wall (`GhostMap sweep:`) |
| 4 | **Restart** (two taps), lock the floor, tap **Trace Walls Instead** (the sweep) | Same as 3 |
| 5 | Restart, lock the floor, **Trace Walls Instead**, then **Walk Corners Instead** and tap the four corners | Closure error (`GhostMap closure:`) |
| 6 | Height: was a ceiling plane used? If not, aim or type | Height value; tape-measured height |
| 7 | Openings: were any detected automatically? Add one door manually | Door width vs tape |
| 8 | Furniture: point at a bed, desk, table and a chair | Which surfaces appeared, suggested type, measured W×D×H vs tape (`GhostMap detect:`) |
| 9 | **Add All**, then **Finalize GhostMap** | Object count; any refusal message |
| 10 | Note the frame rate feel during steps 2, 3 and 8 | Smooth / stutters / freezes |
| 11 | Screenshots of anything confusing | Attach or describe |

Send to Computer is **not** part of this session: today it uses the UDP/TCP
path that `R7` replaces. Optional: if the desktop Viewer is running on the same
Wi-Fi, try it and note the result.

## Done when

Every step has a result row in `docs/status/integration.md` ("R2 device log"),
failures included, and each failure has a follow-up item.
