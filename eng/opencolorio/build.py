#!/usr/bin/env python3
"""Maintainer-only pinned native build. Never invoked by a consumer MSBuild target."""
import argparse
import hashlib
import json
import os
import platform
from pathlib import Path
import re
import subprocess
import tarfile
import urllib.request

HERE = Path(__file__).resolve().parent
MANIFEST = json.loads((HERE / "manifest.json").read_text())

def source(entry, work, download):
    archive = work / entry["archive"]
    if not archive.exists():
        if not download:
            raise RuntimeError(f"Missing {archive}; supply the pinned archive or explicitly use --download.")
        archive.write_bytes(urllib.request.urlopen(entry["url"], timeout=90).read())
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != entry["sha256"]:
        raise RuntimeError(f"SHA256 mismatch for {archive.name}: {digest}")
    destination = work / "sources" / entry["name"]
    if not destination.exists():
        staging = work / "extract" / entry["name"]
        staging.mkdir(parents=True, exist_ok=True)
        with tarfile.open(archive) as data:
            data.extractall(staging, filter="data")
        roots = list(staging.iterdir())
        if len(roots) != 1 or not roots[0].is_dir():
            raise RuntimeError(f"Unexpected archive layout: {archive}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        roots[0].rename(destination)
    return destination

def run(*args):
    print(" ".join(map(str, args)), flush=True)
    subprocess.run(list(map(str, args)), check=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", required=True, type=Path)
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--jobs", type=int, default=4)
    parser.add_argument("--target", choices=("osx-arm64", "maccatalyst-arm64"), default="osx-arm64")
    args = parser.parse_args()
    if platform.system() != "Darwin" or platform.machine() != "arm64":
        raise RuntimeError("These two native recipes require an arm64 macOS host and its Xcode toolchain; other host/target combinations are not verified.")
    work = args.work.resolve(); work.mkdir(parents=True, exist_ok=True)
    ocio = source(MANIFEST["openColorIO"], work, args.download)
    for dependency in MANIFEST["dependencies"]:
        dep = source(dependency, work, args.download)
        # Use exactly verified local sources with the upstream's own build settings. Only
        # its temporary ExternalProject download clauses change; no library implementation does.
        module = ocio / "share/cmake/modules/install" / f"Install{dependency['name']}.cmake"
        content = module.read_text()
        if "MU3D_PINNED_SOURCE" not in content:
            replacement = f'SOURCE_DIR "{dep.as_posix()}" # MU3D_PINNED_SOURCE\n            DOWNLOAD_COMMAND ""\n            UPDATE_COMMAND ""'
            content, count = re.subn(r'GIT_REPOSITORY[^\n]*\n\s*GIT_TAG[^\n]*\n\s*GIT_CONFIG[^\n]*\n\s*GIT_SHALLOW TRUE', replacement, content)
            if count != 1:
                raise RuntimeError(f"Pinned upstream build recipe changed: {module}")
            module.write_text(content)
    build = work / ("build" if args.target == "osx-arm64" else "build-maccatalyst-arm64")
    target_args = [f"-DMU3D_OCIO_TARGET={args.target}", "-DCMAKE_OSX_ARCHITECTURES=arm64"]
    targets = ["mu3d_opencolorio", "mu3d_ocio_oracle"]
    if args.target == "maccatalyst-arm64":
        os.environ["MU3D_APPLE_ARCH"] = "arm64"
        target_args.append(f"-DCMAKE_TOOLCHAIN_FILE={HERE.parent / 'native/Mu3D.MacCatalyst.toolchain.cmake'}")
        targets += ["mu3d_opencolorio_apple_archive", "mu3d_ocio_native_smoke"]
    run("cmake", "-S", HERE, "-B", build, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Release",
        f"-DMU3D_OCIO_SOURCE={ocio}", "-DCMAKE_POLICY_VERSION_MINIMUM=3.5", *target_args)
    os.environ["CMAKE_BUILD_PARALLEL_LEVEL"] = str(args.jobs)
    run("cmake", "--build", build, "--target", *targets, "--parallel", args.jobs)
    print(f"Built {args.target} native artifacts in {build}; distribution and application signing remain explicit maintainer steps.")

if __name__ == "__main__":
    main()
