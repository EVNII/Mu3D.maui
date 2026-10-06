#!/usr/bin/env python3
"""Save read-only Android compositor state while the user keeps HDR Probe visible."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import subprocess
import sys


def resolve_adb(explicit):
    if explicit:
        return explicit
    found = shutil.which("adb")
    if found:
        return found
    android_sdk_adb = Path.home() / "Library/Android/sdk/platform-tools/adb"
    if android_sdk_adb.is_file():
        return str(android_sdk_adb)
    raise RuntimeError("adb not found; pass --adb with the Android platform-tools executable.")


def select_device(adb, serial):
    result = subprocess.run([adb, "devices", "-l"], capture_output=True, text=True,
                            timeout=15, check=True)
    devices = {}
    for line in result.stdout.splitlines():
        fields = line.split()
        if len(fields) >= 2 and fields[0] != "List":
            devices[fields[0]] = fields[1]
    if serial:
        if devices.get(serial) != "device":
            raise RuntimeError("The selected device is absent, offline or awaiting USB-debug authorization.")
        return serial
    ready = [key for key, state in devices.items() if state == "device"]
    if not ready:
        raise RuntimeError("No authorized Android device. Connect the test phone and authorize USB debugging.")
    if len(ready) != 1:
        raise RuntimeError("Multiple authorized devices; choose the test phone with --serial.")
    return ready[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", help="Android platform-tools adb executable")
    parser.add_argument("--serial", help="Test phone serial when more than one device is connected")
    parser.add_argument("--output", type=Path, help="New output directory; existing directories are preserved")
    args = parser.parse_args()
    try:
        adb = resolve_adb(args.adb)
        serial = select_device(adb, args.serial)
    except (RuntimeError, OSError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        return 2

    repository = Path(__file__).resolve().parent.parent
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
    output = args.output or repository / "artifacts/validation/android-hdr-boundary/compositor" / stamp
    output.mkdir(parents=True, exist_ok=False)
    commands = [
        ("build-fingerprint", ["getprop", "ro.build.fingerprint"]),
        ("surfaceflinger-layers", ["dumpsys", "SurfaceFlinger", "--list"]),
        ("surfaceflinger-hdrinfo", ["dumpsys", "SurfaceFlinger", "--hdrinfo"]),
        ("surfaceflinger", ["dumpsys", "SurfaceFlinger"]),
        ("display", ["dumpsys", "display"]),
    ]
    manifest = {"serial": serial, "scope": "Read-only, sequential shell snapshots; not simultaneous frames or pixel/luminance capture",
                "app_package": "com.mu3d.adaptivegallery", "commands": []}
    incomplete = False
    for name, shell_args in commands:
        command = [adb, "-s", serial, "shell", *shell_args]
        entry = {"name": name, "command": command, "started_utc": datetime.now(timezone.utc).isoformat()}
        print(f"Reading {name}...", flush=True)
        try:
            result = subprocess.run(command, capture_output=True, timeout=30)
            (output / f"{name}.txt").write_bytes(result.stdout)
            (output / f"{name}.stderr.txt").write_bytes(result.stderr)
            entry["exit_code"] = result.returncode
            incomplete |= result.returncode != 0
        except (OSError, subprocess.SubprocessError) as error:
            entry["error"] = str(error)
            incomplete = True
        entry["finished_utc"] = datetime.now(timezone.utc).isoformat()
        manifest["commands"].append(entry)
        # Keep partial evidence if a later command fails or the caller interrupts collection.
        (output / "capture.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Saved: {output}")
    print("Missing/unsupported dump fields remain unknown. Review the actual buffer layer, not only its parent.")
    return 1 if incomplete else 0


if __name__ == "__main__":
    sys.exit(main())
