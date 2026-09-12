# Handoff

## Branch
`main`

Foundation tasks F0–F3 are executed by a single integrator **before** the
Scanner/Viewer split (implementation plan §28, Stage A). There is no parallel
workstream to isolate yet and nothing to merge into, so foundation work is
committed directly to `main`. The `foundation/<task>` branch convention in
§4.2 applies once more than one worker is active.

## Base commit
`none — initial commit, repository had no history`

## Head commit
`9c28d7a4dc30ef76966680a3ee7a99257b715262`

## What changed

Task **F0: Repository + collaboration scaffold**.

- Initialized the git repository on `main`. The directory was previously not a
  git repository at all.
- Relocated the two source documents into the layout the plan requires:
  - `GhostMap_Project_Spec_UML_CRC.md` → `docs/specs/ghostmap-project-spec.md`
  - `GhostMap_Master_Implementation_Plan.md` → `docs/plans/ghostmap-implementation-plan.md`
- Created the full repository layout from implementation plan §3, including the
  empty Unity project skeletons for scanner and viewer.
- Added `.gitignore` (Unity generated folders, iOS/Xcode output, IDE files, macOS
  junk, Python caches), `.editorconfig` (UTF-8, LF, 4-space C# indentation), and
  `.gitattributes` (LF normalization, Unity YAML text serialization, binary
  classification).
- Added `AGENTS.md` containing rules 1–13 **verbatim** from implementation plan
  §5, plus a clearly-subordinate navigational quick-reference section.
- Added `README.md`.
- Added `docs/architecture/overview.md` describing the frozen MVP architecture.
- Wrote the four ADRs:
  - `ADR-0001-two-unity-projects.md`
  - `ADR-0002-snapshot-protocol.md`
  - `ADR-0003-scanner-authority.md`
  - `ADR-0004-no-dense-depth-in-mvp.md`
- Created the four workstream status files in the mandated format.
- Created `docs/handoffs/README.md` with the handoff rules, template and index.
- Added clearly-marked **placeholder** contract documents at
  `docs/contracts/scene-schema-v1.md` and `docs/contracts/protocol-v1.md`. Each
  states that it is produced by F1 / F3 respectively and that the implementation
  plan remains authoritative until then. They are placeholders so that their
  absence is never mistaken for permission to invent a schema.
- Added `.gitkeep` files so the empty skeleton directories are tracked.

## Contract impact

**None.** F0 creates no code and freezes no contract. The scene schema is frozen
by F1 and the wire protocol by F3.

## How to test

F0 produces no compilable code. Verification is structural.

1. Confirm no generated Unity content is tracked:

   ```bash
   git ls-files | grep -iE 'Library/|Temp/|Logs/|obj/|\.DS_Store|xcodeproj' ; echo "exit=$?"
   ```

   Expect no matches (grep exit 1).

2. Confirm the ignore rules actually work:

   ```bash
   mkdir -p apps/viewer/Library Builds && touch apps/viewer/Library/x Builds/x
   git status --porcelain | grep -iE 'Library|Builds' ; echo "exit=$?"
   rm -rf apps/viewer/Library Builds
   ```

   Expect no matches (grep exit 1).

3. Confirm every file required by plan §3 exists (see the inventory in "Test
   results" below).

4. Confirm ownership and workflow are derivable from documentation alone: open
   `AGENTS.md` and check that directory ownership, branch naming, commit
   convention, status-file duties, handoff duties and the definition of done are
   all stated without reference to any chat history.

## Test results

**No automated tests were executed. F0 contains no code to test.** This is not a
skipped step — the task produces documentation and scaffolding only.

Structural checks actually run, and their observed results:

- Generated-Unity-content check — **PASS**, nothing matching `Library/`, `Temp/`,
  `Logs/`, `obj/`, `.DS_Store` or `xcodeproj` is staged.
- Empirical ignore check — **PASS**. `git check-ignore -v` confirmed
  `.gitignore:5` ignores `apps/viewer/Library/x`, `.gitignore:60` ignores
  `.DS_Store`, `.gitignore:9` ignores `Builds/x`.
- File inventory against plan §3 — **PASS**, all 19 required top-level and docs
  files present.
- `AGENTS.md` rules 1–13 — **PASS**, verbatim against plan §5.

## Known failures

None in F0 itself.

**One blocking environment issue for the tasks that follow:**

The development machine has **no C# toolchain**. There is no Unity
`6000.3.24f1`, no Unity Hub, no .NET SDK and no Mono. `~/.dotnet` contains only
orphaned `8.0.318` sentinel files; the SDK itself is absent. `python3` and `git`
are available.

F1, F2 and F3 each mandate Unity EditMode tests. **Those tests cannot be
executed until Unity `6000.3.24f1` is installed.** Code for those tasks can be
written to specification, but no one may record their tests as passing until
they have actually been run.

## Files most important to read next

1. `AGENTS.md` — the execution contract.
2. `docs/plans/ghostmap-implementation-plan.md` §6 — scene schema v1, the exact
   input to F1.
3. `docs/plans/ghostmap-implementation-plan.md` §15 — foundation tasks F0–F3.
4. `docs/architecture/overview.md` — the frozen architecture.
5. `docs/decisions/ADR-0002-snapshot-protocol.md` — why synchronization is
   full-snapshot, needed before touching F3.
6. `docs/status/shared.md` — current shared-workstream state.

## Next task

**F1: Create shared local package and data schema.**

Test-driven, in this order:

1. Write failing EditMode serialization/data tests that build a complete
   `SceneSnapshot` and assert four corners, one door, one object, and
   `schemaVersion`/`revision` all survive a round trip.
2. Create `shared/com.ghostmap.shared/package.json` and
   `Runtime/GhostMap.Shared.asmdef`.
3. Implement `Runtime/Domain/*.cs`.
4. Run the EditMode tests.
5. Write `docs/contracts/scene-schema-v1.md`, replacing the placeholder.
6. Commit as `feat(shared): define scene schema v1`.

**Prerequisite:** install Unity `6000.3.24f1` so step 4 can actually be
performed. Do not mark F1 complete on the strength of code that has never been
compiled.

Scanner (`S*`) and Viewer (`V*`) tasks remain blocked until F0–F3 are all merged
and the foundation gate passes.
