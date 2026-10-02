#!/usr/bin/env python3
"""Verify and stage the two locally built native RIDs for an explicit maintainer dotnet pack."""
import argparse
import ctypes
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--work", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True, help="Runtime root passed as Mu3DOpenColorIORuntimeRoot")
args = parser.parse_args()
work, output = args.work.resolve(), args.output.resolve()
here = Path(__file__).resolve().parent
manifest = json.loads((here / "manifest.json").read_text())
subprocess.run([sys.executable, str(here / "verify_catalyst.py"),
                "--build", str(work / "build-maccatalyst-arm64")], check=True)
host_library = work / "build/libmu3d_opencolorio.dylib"
metadata = subprocess.check_output(["xcrun", "vtool", "-show-build", str(host_library)], text=True)
if "platform MACOS" not in metadata:
    raise RuntimeError("The host shared library is not marked MACOS.")
if subprocess.check_output(["lipo", "-archs", str(host_library)], text=True).strip() != "arm64":
    raise RuntimeError("The host shared library is not exactly arm64.")
native = ctypes.CDLL(str(host_library))
native.mu3d_ocio_version.restype = ctypes.c_char_p
if native.mu3d_ocio_abi_version() != manifest["abiVersion"] or native.mu3d_ocio_version().decode() != manifest["openColorIO"]["version"]:
    raise RuntimeError("The host shared library does not match the pinned ABI/release.")
subprocess.run([sys.executable, str(here / "generate_reference.py"),
                "--oracle", str(work / "build/mu3d_ocio_oracle"), "--check"], check=True)
sources = {
    "osx-arm64/native/libmu3d_opencolorio.dylib": host_library,
    "maccatalyst-arm64/native/libmu3d_opencolorio.a": work / "build-maccatalyst-arm64/runtimes/maccatalyst-arm64/native/libmu3d_opencolorio.a"
}
files = []
for relative, source in sources.items():
    destination = output / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    files.append({"path": relative, "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(), "bytes": destination.stat().st_size})
(output / "verification.json").write_text(json.dumps({
    "abiVersion": manifest["abiVersion"], "openColorIO": manifest["openColorIO"],
    "nativeReferenceComponentsPerTarget": 384, "maccatalystArchiveObjects": 256,
    "applicationValidation": "Full .NET Mac Catalyst application linking, signing and presentation pending.",
    "files": files
}, indent=2) + "\n")
print(f"Verified runtime staging: {output}")
