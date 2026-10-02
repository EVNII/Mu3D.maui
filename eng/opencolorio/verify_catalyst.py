#!/usr/bin/env python3
"""Verify every object is macabi, then execute native CPU smoke and independent OCIO references."""
import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--build", type=Path, required=True)
args = parser.parse_args()
build = args.build.resolve()
archive = build / "runtimes/maccatalyst-arm64/native/libmu3d_opencolorio.a"
headers = subprocess.check_output(["otool", "-l", str(archive)], text=True)
platforms = re.findall(r"\bplatform\s+(\S+)", headers)
objects = re.findall(r"\):\n", headers)
# Apple's PLATFORM_MACCATALYST is 6. Every source object, including each dependency, must
# carry that platform; a renamed macOS (1) or iOS (2) binary fails before it can be staged.
if not platforms or len(platforms) != len(objects) or set(platforms) != {"6"}:
    raise RuntimeError(f"Invalid Mac Catalyst archive: objects={len(objects)}, platforms={platforms}")
for name in ("mu3d_ocio_native_smoke", "mu3d_ocio_oracle"):
    metadata = subprocess.check_output(["xcrun", "vtool", "-show-build", str(build / name)], text=True)
    if "platform MACCATALYST" not in metadata:
        raise RuntimeError(f"{name} is not a Mac Catalyst executable")
subprocess.run([str(build / "mu3d_ocio_native_smoke")], check=True)
subprocess.run([sys.executable, str(Path(__file__).with_name("generate_reference.py")),
                "--oracle", str(build / "mu3d_ocio_oracle"), "--check"], check=True)
print(f"Verified {len(objects)} Mac Catalyst objects; SHA256 {hashlib.sha256(archive.read_bytes()).hexdigest()}")
