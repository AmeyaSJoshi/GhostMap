# Continuous Integration

`.github/workflows/tests.yml` runs on every push, every pull request, and on
demand (**Actions → tests → Run workflow**).

| Job | What it checks | Needs |
| --- | --- | --- |
| `tools` | `inspect_snapshot.py` accepts the two valid fixtures and rejects the malformed one; `send_fixture.py --dry-run` builds protocol v1 messages; `run_unity_tests.sh` parses | Nothing |
| `unity (shared)` | Shared package EditMode suite in `shared/TestProject` | Unity licence secrets |
| `unity (viewer)` | Viewer EditMode suite (re-runs the shared tests too) | Unity licence secrets |
| `unity (scanner)` | Scanner EditMode suite (re-runs the shared tests too) | Unity licence secrets |

The Unity jobs use GameCI's `unity-test-runner` with its Unity `6000.3.24f1`
Linux image. Results appear as check runs named **EditMode results (...)** on the
commit and as downloadable `test-results-*` artifacts.

Expected counts after task `R1`: **shared 202, viewer 548, scanner 577**.

## Why CI and not the agent's container

Cloud agent sessions cannot run Unity. Unity's download and licensing servers
are blocked by the session's network policy, and Unity needs a licence tied to
the owner's Unity account. GitHub's own runners have neither problem.

## One-time setup (repository owner)

The Unity jobs fail with "Unity licence secrets are missing" until three
repository secrets exist. In GitHub: **Settings → Secrets and variables →
Actions → New repository secret**.

| Secret | Value |
| --- | --- |
| `UNITY_EMAIL` | The email of the Unity account that is signed in to Unity Hub on the Mac |
| `UNITY_PASSWORD` | That account's password |
| `UNITY_LICENSE` | The **entire contents** of the licence file on the Mac where Unity is already activated: `/Library/Application Support/Unity/Unity_lic.ulf` |

To copy the licence file on the Mac:

```bash
pbcopy < "/Library/Application Support/Unity/Unity_lic.ulf"
```

then paste it as the value of `UNITY_LICENSE`.

Then run the workflow once (**Actions → tests → Run workflow**) and record the
three counts in `docs/status/integration.md`.

## If licence activation fails

Unity has been changing how free (Personal) licences work in CI. GameCI's
activation guide is the reference: <https://game.ci/docs/github/activation/>.
Things to try, in order:

1. Re-copy `Unity_lic.ulf` after opening Unity Hub on the Mac (the file is
   refreshed periodically).
2. Check the pinned action version in `tests.yml` (`v4.3.2`). A third-party
   report says `v4.4.0` broke Personal-licence activation; newer versions may
   have fixed it.
3. Run the suites locally instead: `./tools/run_unity_tests.sh` on the Mac.

## Caching

Each suite caches its project's `Library/` folder, keyed on the project's
`packages-lock.json` and the shared package. The first run imports every project
from scratch and takes noticeably longer than later runs.
