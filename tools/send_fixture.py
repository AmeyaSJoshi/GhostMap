#!/usr/bin/env python3
"""Send a GhostMap fixture to a running Viewer over protocol v1.

This lets the Viewer be developed and demonstrated without a phone, and lets the
wire format be exercised without building the Scanner.

A fixture file holds a bare SceneSnapshot -- the payload, not the envelope. This
tool wraps it in the protocol v1 messages a real scanner would send:

    hello  ->  scene.snapshot  ->  scan.finalized (only if the snapshot is final)

Usage:
    python3 tools/send_fixture.py --host 192.168.1.20
    python3 tools/send_fixture.py --fixture fixtures/room-with-door-window-v1.json
    python3 tools/send_fixture.py --host 10.0.0.5 --port 47831 --dry-run

Exit codes:
    0  sent successfully
    1  bad arguments, unreadable fixture, or connection failure
"""

import argparse
import json
import os
import socket
import sys
import time

PROTOCOL_VERSION = 1
SCHEMA_VERSION = 1
DEFAULT_PORT = 47831
MAX_LINE_BYTES = 262144

TYPE_HELLO = "hello"
TYPE_SCENE_SNAPSHOT = "scene.snapshot"
TYPE_SCAN_FINALIZED = "scan.finalized"

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_FIXTURE = os.path.join(REPO_ROOT, "fixtures", "valid-room-v1.json")


def now_ms():
    return int(time.time() * 1000)


def header(msg_type, session_id, sequence):
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "type": msg_type,
        "sessionId": session_id,
        "sequence": sequence,
        "unixTimeMs": now_ms(),
    }


def load_fixture(path):
    if not os.path.isfile(path):
        sys.exit(f"error: fixture not found: {path}")

    try:
        with open(path, "r", encoding="utf-8") as handle:
            snapshot = json.load(handle)
    except json.JSONDecodeError as exc:
        sys.exit(f"error: fixture is not valid JSON: {exc}")

    if not isinstance(snapshot, dict):
        sys.exit("error: fixture root must be a JSON object (a SceneSnapshot).")

    version = snapshot.get("schemaVersion")
    if version != SCHEMA_VERSION:
        sys.exit(
            f"error: fixture schemaVersion is {version!r}, expected {SCHEMA_VERSION}."
        )

    if "room" not in snapshot:
        sys.exit("error: fixture has no 'room'.")

    # Strip documentation-only keys so the wire payload matches the schema.
    return {k: v for k, v in snapshot.items() if not k.startswith("_")}


def build_messages(snapshot):
    session_id = snapshot.get("sessionId", "fixture-session")
    sequence = 0

    hello = header(TYPE_HELLO, session_id, sequence)
    hello["appVersion"] = "fixture-tool/1.0"
    hello["deviceName"] = "send_fixture.py"
    messages = [hello]

    sequence += 1
    snapshot_msg = header(TYPE_SCENE_SNAPSHOT, session_id, sequence)
    snapshot_msg["snapshot"] = snapshot
    messages.append(snapshot_msg)

    # The scanner always sends the final snapshot immediately BEFORE finalizing.
    if snapshot.get("finalized"):
        sequence += 1
        finalized = header(TYPE_SCAN_FINALIZED, session_id, sequence)
        finalized["finalRevision"] = snapshot.get("revision", 0)
        messages.append(finalized)

    return messages


def encode(message):
    line = (json.dumps(message, separators=(",", ":")) + "\n").encode("utf-8")

    if len(line) > MAX_LINE_BYTES:
        sys.exit(
            f"error: {message['type']} is {len(line)} bytes, over the "
            f"{MAX_LINE_BYTES} byte protocol limit."
        )

    return line


def main():
    parser = argparse.ArgumentParser(
        description="Send a GhostMap fixture to a Viewer over protocol v1."
    )
    parser.add_argument("--host", default="127.0.0.1",
                        help="Viewer host or LAN IP (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT,
                        help=f"Viewer port (default: {DEFAULT_PORT})")
    parser.add_argument("--fixture", default=DEFAULT_FIXTURE,
                        help="Fixture JSON file (default: fixtures/valid-room-v1.json)")
    parser.add_argument("--timeout", type=float, default=5.0,
                        help="Connect timeout in seconds (default: 5)")
    parser.add_argument("--dry-run", action="store_true",
                        help="Print the messages instead of sending them.")
    args = parser.parse_args()

    snapshot = load_fixture(args.fixture)
    messages = build_messages(snapshot)

    room = snapshot["room"]
    print(f"fixture : {os.path.relpath(args.fixture, REPO_ROOT)}")
    print(f"session : {snapshot.get('sessionId')}  revision {snapshot.get('revision')}")
    print(f"room    : {len(room.get('corners', []))} corners, "
          f"{len(room.get('openings', []))} openings, "
          f"{len(room.get('objects', []))} objects, "
          f"height {room.get('heightM')} m")

    if args.dry_run:
        print("\n-- dry run, nothing sent --")
        for message in messages:
            line = encode(message)
            print(f"[{len(line):>6} bytes] {message['type']}")
        return 0

    print(f"\nconnecting to {args.host}:{args.port} ...")

    try:
        with socket.create_connection((args.host, args.port), timeout=args.timeout) as sock:
            for message in messages:
                sock.sendall(encode(message))
                print(f"  sent {message['type']}")
            # Give the viewer a moment to read before the socket closes.
            time.sleep(0.25)
    except (ConnectionRefusedError, socket.timeout, OSError) as exc:
        print(f"error: could not send to {args.host}:{args.port}: {exc}", file=sys.stderr)
        print("Is the Viewer running and listening? Same Wi-Fi? Correct LAN IP?",
              file=sys.stderr)
        return 1

    print("done.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
