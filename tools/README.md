# Tools

Command-line helpers. None of them need the Unity editor except
`run_unity_tests.sh`.

| Tool | Use it to |
| --- | --- |
| `run_unity_tests.sh` | Run the EditMode suites (`shared`, `viewer`, `scanner`) and print pass/fail per suite, or regenerate both scenes with `--rebuild-scenes` |
| `inspect_snapshot.py` | Print and validate a `SceneSnapshot` JSON file without Unity. Exit `0` valid, `1` invalid |
| `send_fixture.py` | Send a fixture to a running Viewer as `hello`, `scene.snapshot`, `scan.finalized`, exactly as a scanner would |

```bash
./tools/run_unity_tests.sh                       # all three suites
./tools/run_unity_tests.sh viewer                # one suite
./tools/run_unity_tests.sh --rebuild-scenes      # regenerate Scanner.unity and Viewer.unity

python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json
python3 tools/send_fixture.py --host 127.0.0.1                 # Viewer on this machine
python3 tools/send_fixture.py --dry-run --fixture fixtures/room-with-door-window-v1.json
```

`run_unity_tests.sh` looks for Unity `6000.3.24f1` in the standard Hub paths;
set `UNITY_PATH` to override. All three suites run on macOS or Linux; the
scanner's iOS post-build step is guarded by `UNITY_IOS`, so iOS Build Support is
only needed to build the app.

`inspect_snapshot.py` re-implements the shared validation rules in Python so it
can answer "is the data bad or is the renderer bad?" without Unity. **Any change
to a limit in `shared/com.ghostmap.shared/Runtime/Validation/` must be mirrored
here in the same commit.**
