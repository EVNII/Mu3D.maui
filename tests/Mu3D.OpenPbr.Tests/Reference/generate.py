#!/usr/bin/env python3
"""Regenerate the committed oracle with pinned, independently verified source archives.

Maintainer-only. This script does not download dependencies or run in application builds.
"""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile

PINS = {
    "openpbr": "3e9f0925c92b634d90830354e18845ee551a8384c36e761274f44a58b0e246ec",
    "glm": "9f3174561fd26904b23f0db5e560971cbf9b3cbda0b280f04d5c379d03bf234c",
}


def extract_verified(archive: Path, target: Path, key: str) -> Path:
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != PINS[key]:
        raise ValueError(f"{key} archive SHA-256 mismatch: {digest}")
    target.mkdir()
    with tarfile.open(archive) as package:
        members = package.getmembers()
        for member in members:
            resolved = (target / member.name).resolve()
            if not resolved.is_relative_to(target.resolve()) or member.issym() or member.islnk():
                raise ValueError(f"Unsafe archive member: {member.name}")
        package.extractall(target, members=members, filter="data")
    roots = list(target.iterdir())
    if len(roots) != 1 or not roots[0].is_dir():
        raise ValueError(f"Unexpected {key} archive layout")
    return roots[0]


def compare(reference, actual, path="root"):
    if isinstance(reference, (float, int)) and not isinstance(reference, bool):
        error = abs(reference - actual)
        if error > 2e-5 + 2e-4 * abs(reference):
            raise ValueError(f"Numerical mismatch at {path}: expected {reference}, got {actual}")
    elif isinstance(reference, dict):
        if reference.keys() != actual.keys():
            raise ValueError(f"Key mismatch at {path}")
        for key in reference:
            compare(reference[key], actual[key], f"{path}.{key}")
    elif isinstance(reference, list):
        if len(reference) != len(actual):
            raise ValueError(f"Array length mismatch at {path}")
        for index, (left, right) in enumerate(zip(reference, actual)):
            compare(left, right, f"{path}[{index}]")
    elif reference != actual:
        raise ValueError(f"Value mismatch at {path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--openpbr-archive", type=Path, required=True)
    parser.add_argument("--glm-archive", type=Path, required=True)
    parser.add_argument("--compiler", default="clang++")
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("oracle.json"))
    parser.add_argument("--check", action="store_true", help="Compare with existing fixture instead of writing it")
    args = parser.parse_args()
    compiler = shutil.which(args.compiler)
    if compiler is None:
        raise ValueError(f"C++ compiler unavailable: {args.compiler}")
    source = Path(__file__).resolve().with_name("oracle.cpp")
    with tempfile.TemporaryDirectory(prefix="mu3d-openpbr-reference-") as temporary:
        scratch = Path(temporary)
        upstream = extract_verified(args.openpbr_archive, scratch / "openpbr", "openpbr")
        glm = extract_verified(args.glm_archive, scratch / "glm", "glm")
        executable = scratch / "oracle"
        subprocess.run([compiler, "-std=c++17", "-O2", "-fno-fast-math", "-ffp-contract=off",
                        f"-I{upstream}", f"-I{glm}", str(source), "-o", str(executable)], check=True)
        encoded = subprocess.check_output([str(executable)], text=True)
        result = json.loads(encoded)
        if args.check:
            compare(json.loads(args.output.read_text()), result)
            print(f"Verified {len(result['cases'])} OpenPBR oracle cases and supplemental integrals.")
        else:
            # Only write after the upstream program and JSON validation both succeed.
            args.output.write_text(encoded)
            print(f"Wrote {args.output}")


if __name__ == "__main__":
    main()
