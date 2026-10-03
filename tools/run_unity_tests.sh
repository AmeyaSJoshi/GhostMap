#!/usr/bin/env bash
#
# Runs GhostMap's three EditMode suites and prints a per-suite pass/fail
# summary.
#
# Why this exists: every command in docs/status/* is a macOS path
# (/Applications/Unity/Hub/Editor/...), and the editor lives somewhere else on
# Linux. This finds it on either, so the documented workflow is one command
# instead of three hand-edited ones.
#
#   ./tools/run_unity_tests.sh                       # all three suites
#   ./tools/run_unity_tests.sh shared                # just one
#   ./tools/run_unity_tests.sh viewer scanner
#   ./tools/run_unity_tests.sh --rebuild-scenes      # regenerate both .unity files
#   ./tools/run_unity_tests.sh --rebuild-scenes viewer
#
# Exit code is non-zero if any suite failed, so it is usable as a gate.
#
# The scanner suite is run WITHOUT -buildTarget iOS, unlike the documented
# macOS command. Switching to the iOS target needs the iOS Build Support
# module, which Unity does not offer for the Linux editor. ScannerIosPostBuild
# is guarded behind UNITY_IOS for exactly this reason, so the suite compiles
# and runs on Linux; the iOS build itself still requires a Mac with Xcode.

set -uo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.24f1}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULTS_DIR="${GHOSTMAP_TEST_OUTPUT:-/tmp/ghostmap-tests}"

find_unity() {
    if [[ -n "${UNITY_PATH:-}" ]]; then
        printf '%s' "$UNITY_PATH"
        return 0
    fi

    local candidates=(
        "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
        "/opt/unity/editors/$UNITY_VERSION/Editor/Unity"
        "/opt/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
        "/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
    )

    local candidate
    for candidate in "${candidates[@]}"; do
        if [[ -x "$candidate" ]]; then
            printf '%s' "$candidate"
            return 0
        fi
    done

    return 1
}

# Project paths, relative to the repo root.
project_path_for() {
    case "$1" in
        shared)  printf 'shared/TestProject' ;;
        viewer)  printf 'apps/viewer' ;;
        scanner) printf 'apps/scanner' ;;
        *)       return 1 ;;
    esac
}

# Reads totals straight out of the NUnit XML the test runner writes. Parsed
# rather than scraped from the log, because the log's wording changes between
# Unity versions and the XML attributes do not.
summarise() {
    local xml="$1"

    if [[ ! -f "$xml" ]]; then
        printf 'no results file'
        return 1
    fi

    python3 - "$xml" <<'PY'
import sys, xml.etree.ElementTree as ET

try:
    root = ET.parse(sys.argv[1]).getroot()
except ET.ParseError as exc:
    print(f"unreadable results XML ({exc})")
    sys.exit(1)

total = root.get("total") or "?"
passed = root.get("passed") or "?"
failed = root.get("failed") or "?"
skipped = root.get("skipped") or "0"
print(f"{total} tests, {passed} passed, {failed} failed, {skipped} skipped")

# Name the first few failures; a wall of stack traces helps nobody.
names = [
    c.get("fullname") or c.get("name")
    for c in root.iter("test-case")
    if c.get("result") not in (None, "Passed", "Skipped", "Inconclusive")
]
for name in names[:15]:
    print(f"    FAILED  {name}")
if len(names) > 15:
    print(f"    ... and {len(names) - 15} more")
PY
}

UNITY_BIN="$(find_unity)" || {
    cat >&2 <<EOF
Could not find Unity $UNITY_VERSION.

Looked in:
  \$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity          (Linux, Unity Hub default)
  /opt/unity/editors/$UNITY_VERSION/Editor/Unity               (Linux, manual install)
  /Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/...  (macOS)

Set UNITY_PATH to the editor binary, or UNITY_VERSION if you are
deliberately using a different editor than the pinned one.

The project pins $UNITY_VERSION. Do not change that without the dedicated
dependency-change PR AGENTS.md rule 10 requires.
EOF
    exit 1
}

# ---------------------------------------------------------------------------
# Scene regeneration
#
# The committed .unity files are asserted by the scene tests, so any change to
# a scene builder has to be followed by a rebuild before those tests pass.
#
# This is attempted with -executeMethod under a timeout, because the Viewer
# status records -executeMethod hanging indefinitely in headless Unity at the
# "Start Indexing on Editor startup" step - confirmed twice there, killed after
# 26+ minutes. If that reproduces, the script says so and points at the GUI
# menu items rather than sitting there looking busy.
# ---------------------------------------------------------------------------
REBUILD_TIMEOUT="${REBUILD_TIMEOUT:-420}"

rebuild_scene() {
    local suite="$1" project method log rc
    project="$(project_path_for "$suite")" || return 1

    case "$suite" in
        scanner) method='GhostMap.Scanner.Editor.ScannerSceneBuilder.BuildScene' ;;
        viewer)  method='GhostMap.Viewer.Editor.ViewerSceneBuilder.BuildScene' ;;
        *)       printf '    %s has no scene to rebuild\n' "$suite"; return 0 ;;
    esac

    log="$RESULTS_DIR/rebuild-$suite.log"
    printf '==> rebuilding %s scene (timeout %ss)\n' "$suite" "$REBUILD_TIMEOUT"

    timeout "$REBUILD_TIMEOUT" "$UNITY_BIN" \
        -batchmode -nographics -quit \
        -projectPath "$REPO_ROOT/$project" \
        -executeMethod "$method" \
        -logFile "$log" \
        >/dev/null 2>&1
    rc=$?

    if [[ $rc -eq 124 ]]; then
        cat <<EOF
    TIMED OUT after ${REBUILD_TIMEOUT}s - this is the documented -executeMethod
    hang (see "Sandbox environment finding" in docs/status/viewer.md).

    Do it from the GUI instead:
      open the $project project, then menu GhostMap > Build $( [[ $suite == scanner ]] && echo Scanner || echo Viewer ) Scene
    Then commit the regenerated .unity file.
EOF
        return 1
    fi

    if [[ $rc -ne 0 ]]; then
        printf '    exit %s - see %s\n' "$rc" "$log"
        grep -E 'error CS[0-9]+|Exception|error:' "$log" 2>/dev/null | head -10 | sed 's/^/      /'
        return 1
    fi

    printf '    ok\n'
    return 0
}

if [[ "${1:-}" == "--rebuild-scenes" ]]; then
    shift
    targets=("$@")
    if [[ ${#targets[@]} -eq 0 ]]; then
        targets=(scanner viewer)
    fi

    mkdir -p "$RESULTS_DIR"
    printf 'Unity: %s\n\n' "$UNITY_BIN"

    rebuild_status=0
    for t in "${targets[@]}"; do
        rebuild_scene "$t" || rebuild_status=1
        printf '\n'
    done

    if [[ $rebuild_status -eq 0 ]]; then
        printf 'Scenes rebuilt. Commit the changed .unity files, then run the suites.\n'
    fi
    exit $rebuild_status
fi

suites=("$@")
if [[ ${#suites[@]} -eq 0 ]]; then
    suites=(shared viewer scanner)
fi

mkdir -p "$RESULTS_DIR"

printf 'Unity:   %s\n' "$UNITY_BIN"
printf 'Results: %s\n\n' "$RESULTS_DIR"

overall=0

for suite in "${suites[@]}"; do
    project="$(project_path_for "$suite")" || {
        printf 'Unknown suite "%s" (expected shared, viewer or scanner)\n' "$suite" >&2
        overall=1
        continue
    }

    xml="$RESULTS_DIR/$suite.xml"
    log="$RESULTS_DIR/$suite.log"
    rm -f "$xml"

    printf '==> %s (%s)\n' "$suite" "$project"

    "$UNITY_BIN" \
        -batchmode \
        -nographics \
        -projectPath "$REPO_ROOT/$project" \
        -runTests \
        -testPlatform EditMode \
        -testResults "$xml" \
        -logFile "$log" \
        >/dev/null 2>&1
    unity_exit=$?

    printf '    exit %s  |  %s\n' "$unity_exit" "$(summarise "$xml")"

    if [[ $unity_exit -ne 0 ]]; then
        overall=1

        # A non-zero exit with no results file almost always means the project
        # failed to compile, which is the interesting case on a fresh machine.
        if [[ ! -f "$xml" ]]; then
            printf '    no results written - likely a compile error. First errors:\n'
            grep -E 'error CS[0-9]+|Compilation failed|Assembly.*could not be' "$log" 2>/dev/null \
                | head -20 | sed 's/^/      /'
            printf '    full log: %s\n' "$log"
        fi
    fi

    printf '\n'
done

if [[ $overall -eq 0 ]]; then
    printf 'All requested suites passed.\n'
else
    printf 'At least one suite failed or did not run. Logs in %s\n' "$RESULTS_DIR"
fi

exit $overall
