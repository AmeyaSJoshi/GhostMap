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
