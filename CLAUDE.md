# GhostMap: start here

1. Read `AGENTS.md`. Its numbered rules are binding.
2. Read `docs/onboarding.md` once, completely. It explains the product, the
   current state, how a scan flows through the code, every known trap, and what
   your environment can and cannot do.
3. Your next task is under "Next safe task" in `docs/status/integration.md`.
   Its implementation brief is in `docs/tasks/`.

Facts that change how you work in a cloud session:

- Unity cannot run here (not installed; its servers are blocked). The EditMode
  suites run in CI (`.github/workflows/tests.yml`, `docs/ci.md`) or on the
  owner's Mac. Never report a Unity test as passing unless it ran.
- Nothing can be tested on an iPhone here. Write the device procedure; the owner
  runs it.
- `python3` and `node` are available. Check fixtures with
  `python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json`.
- Generated scenes (`*.unity`) are rebuilt from their scene builders in Unity,
  never hand-edited. If you change a builder, say so in your handoff.

## Where Unity fits, and why

The browser viewer (`room.html`, ADR-0013) replaces only the **desktop Unity
Viewer**. Unity stays in two places:

1. **The iPhone app is a Unity app** (`apps/scanner`, AR Foundation + ARKit).
   Capture, the YOLO-n detector (`R4`), the export bundle (`R6`) and Send to
   Computer (`R7`) are all built inside it. The shared C# package
   (`shared/com.ghostmap.shared`) is part of it.
2. **Unity is where the owner uses the result.** They drag `room.glb` and
   `objects/*.glb` into a Unity project. `room.html` is for looking and
   measuring without opening Unity.

```text
iPhone app (Unity)  ->  zip via AirDrop  ->  room.html in a browser (look, measure)
                                         ->  .glb files into Unity (build with it)
```

**Why the phone app is Unity:** chosen at the start (ADR-0001), when the plan
had a desktop Unity Viewer too, so both apps could share one C# package. With
the Viewer retiring, that reason is gone. Native Swift would fit the remaining
work better: Core ML/Vision for YOLO-n and the share sheet are built in, and it
builds on any Mac with no Unity licence. Against that, all capture code and its
577 tests exist in Unity, built but never run on an iPhone.

**Owner's decision (2026-10-11): stay on Unity for now; revisit after `R2`.**
If the stand-in-place scan works on the device in `R2`, stay. If it does not,
capture code gets rewritten anyway, and that is the cheapest point to consider
native Swift. Do not start a rewrite, or argue for one mid-task: switching
needs a new ADR and a rewritten roadmap, and only the owner decides.

## Before you finish: update every document your work touched

Do this at the end of **every** task, however small, before your final commit.
The next agent has no chat history; if a document is stale, it will act on
wrong information. Docs-only and planning work count too.

**Always:**

1. **Integration tracker** `docs/status/integration.md`: the `R` task's state in
   the roadmap table, the "Current state" line, "Tests run" (one row per run:
   date, where, suites, the observed result; write "not run" and why when you
   could not run them), "Open decisions for the owner" (add new questions,
   remove answered ones), and "Next safe task".
2. **Workstream status** for every workstream you touched
   (`docs/status/scanner.md`, `viewer.md`, `shared.md`): current state, last
   verified commit, tests run, known issues, next safe task.
3. **Handoff:** a new file `docs/handoffs/YYYY-MM-DD-<workstream>-<short-description>.md`
   from the template in `docs/handoffs/README.md`, plus its row in that README's
   table. Never edit an existing handoff.

**When the change calls for it:**

| If you... | Also update |
| --- | --- |
| Finished, started or changed the scope of an `R` task | Plan section 18 (`docs/plans/ghostmap-implementation-plan.md`), `docs/tasks/README.md`, and that task's brief if what you found contradicts it |
| Found that a brief, the onboarding guide or any doc is wrong about the code | Fix that document in the same commit |
| Changed anything in `shared/com.ghostmap.shared` that crosses an app boundary | The contract doc in `docs/contracts/`, tests, a dedicated commit (`AGENTS.md` rule 8) |
| Made or reversed a design decision | A new ADR in `docs/decisions/` and its index row |
| Added, moved, renamed or deleted a file, folder, class or tool | `docs/architecture/overview.md`, the folder's README, the repo tree in `README.md` and `docs/onboarding.md` |
| Changed what the product can do (built, verified on device, removed) | The status tables in `README.md` and `docs/onboarding.md` |
| Changed a test suite's size, the runner or CI | Expected counts in `docs/ci.md`, `.github/workflows/tests.yml` comments, `docs/status/integration.md` |
| Hit a trap the next agent would also hit | "Gotchas" in `docs/onboarding.md` |
| Added a document | The map in `docs/README.md` and, if agents must read it, this file |

**Then check:**

- `grep -rn` the old name of anything you renamed or removed; no document may
  still point at it.
- Every relative link in the Markdown you touched resolves.
- Nothing says "passing", "verified" or "done" that you did not observe
  (`AGENTS.md` rules 12 and 13).
- Your final message to the owner lists which documents you updated and any
  open decision you added.
