# Shared Package Test Host

This is a **minimal Unity project whose only purpose is to run the EditMode tests
of `com.ghostmap.shared`.** It contains no GhostMap application code and never
will.

## Why it exists

A Unity package cannot run its own tests. The Test Runner only discovers tests
inside a project, and for a package referenced by relative path the package must
additionally be listed in `testables`. Without a host project the shared
contract tests would be unrunnable from a clean clone, and the foundation gate
("shared tests pass") would not be verifiable by anyone else.

Implementation plan Task F1 explicitly sanctions this: *"Open a tiny test Unity
project or one app project and confirm compile."* Using an app project was not an
option, because `apps/scanner` and `apps/viewer` do not exist until tasks S1 and
V1, which are gated behind the foundation.

## Running the tests

From the repository root:

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-tests.xml \
  -logFile /tmp/ghostmap-tests.log
```

Exit code `0` means every test passed. The XML holds per-test results; the log
holds compiler errors if the run aborted before testing.

## Rules

- Do **not** add application code here. Scanner code belongs in `apps/scanner`,
  viewer code in `apps/viewer`.
- `Library/`, `Logs/` and `UserSettings/` are generated and git-ignored.
- `Packages/manifest.json` and `Packages/packages-lock.json` **are** committed, so
  package resolution is reproducible.
- Serialization is Force Text with visible meta files, per implementation plan
  section 4.7.
