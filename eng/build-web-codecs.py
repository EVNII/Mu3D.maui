#!/usr/bin/env python3
"""Maintainer-only wasm32 codec build using pinned local source checkouts; never fetches sources."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--codecs", choices=["all", "jpeg"], default="all")
parser.add_argument("--source-root", type=Path, default=repo / "artifacts/native-build")
parser.add_argument("--output", type=Path, default=repo / "artifacts/web-wasm/codecs")
parser.add_argument("--jobs", type=int, default=2)
parser.add_argument("--dotnet-root", type=Path,
                    help="SDK installation root containing packs; otherwise use MU3D_DOTNET, the local SDK, or PATH")
args = parser.parse_args()
if args.jobs < 1:
    parser.error("--jobs must be positive")
args.source_root = args.source_root.resolve()
root = args.output.resolve()
logs = repo / "artifacts/validation/web-wasm"
logs.mkdir(parents=True, exist_ok=True)
root.mkdir(parents=True, exist_ok=True)
stage = root / "lib"
stage.mkdir(exist_ok=True)

def props(name):
    return {e.tag: e.text for e in ET.parse(repo / "eng" / name).iter() if e.text and e.tag.startswith("Mu3D")}

pins = props("Mu3D.UltraHdrNativeVersion.props") | props("Mu3D.KtxNativeVersion.props")
sources = {"jpeg": args.source_root / "jpeg-turbo-source", "uhdr": args.source_root / "ultrahdr-source"}
if args.codecs == "all":
    sources["ktx"] = args.source_root / "ktx-source"
source_pins = {"jpeg": pins["Mu3DJpegTurboNativeCommit"], "uhdr": pins["Mu3DUltraHdrNativeCommit"], "ktx": pins["Mu3DKtxNativeCommit"]}
for name, source in sources.items():
    if not (source / "CMakeLists.txt").is_file():
        raise SystemExit(f"Missing pinned {name} checkout: {source}; stage official source before this build.")
    actual = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if actual != source_pins[name] or subprocess.check_output(["git", "-C", str(source), "status", "--porcelain"], text=True).strip():
        raise SystemExit(f"{name} must be a clean checkout of {source_pins[name]}.")

if args.dotnet_root:
    dotnet_root = args.dotnet_root.resolve()
else:
    local_dotnet = repo / "artifacts/toolchain/dotnet/dotnet"
    dotnet = os.environ.get("MU3D_DOTNET") or (str(local_dotnet) if local_dotnet.is_file() else shutil.which("dotnet"))
    if not dotnet:
        parser.error("Install the pinned .NET SDK and wasm-tools workload, or specify --dotnet-root.")
    executable = shutil.which(dotnet) or dotnet
    dotnet_root = Path(executable).resolve().parent
packs = dotnet_root / "packs"

def pack_path(kind, suffix):
    matches = sorted(packs.glob(f"Microsoft.NET.Runtime.Emscripten.3.1.56.{kind}.*/10.0.12/{suffix}"))
    if len(matches) != 1:
        raise SystemExit(f"Expected one pinned Emscripten 3.1.56 / 10.0.12 {kind} pack under {packs}; found {len(matches)}. Install wasm-tools for workload set 10.0.401.")
    return matches[0]

sdk = pack_path("Sdk", "tools")
node = pack_path("Node", "tools/bin/node.exe" if os.name == "nt" else "tools/bin/node")
cache = pack_path("Cache", "tools/emscripten/cache")
env = os.environ.copy()
env.update(DOTNET_EMSCRIPTEN_LLVM_ROOT=str(sdk / "bin"), DOTNET_EMSCRIPTEN_BINARYEN_ROOT=str(sdk),
           DOTNET_EMSCRIPTEN_NODE_JS=str(node), EM_CACHE=str(cache), FROZEN_CACHE="True")
env["PATH"] = str(sdk / "emscripten") + os.pathsep + str(sdk / "bin") + os.pathsep + env["PATH"]
toolchain = sdk / "emscripten/cmake/Modules/Platform/Emscripten.cmake"
common = ["-G", "Ninja", f"-DCMAKE_TOOLCHAIN_FILE={toolchain}", "-DCMAKE_BUILD_TYPE=Release",
          "-DCMAKE_C_FLAGS=-fwasm-exceptions", "-DCMAKE_CXX_FLAGS=-fwasm-exceptions", "-DCMAKE_POLICY_VERSION_MINIMUM=3.5"]

def run(name, command):
    with (logs / f"codec-{name}.log").open("w") as log:
        log.write(subprocess.list2cmdline([str(x) for x in command]) + "\n")
        log.flush()
        result = subprocess.run(command, env=env, cwd=repo, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        raise SystemExit(f"{name} failed; see {logs / ('codec-' + name + '.log')}")
    print(name + " complete", flush=True)

def build(name, source, options, targets):
    path = root / (name + "-build")
    run(name + "-configure", ["cmake", "-S", str(source), "-B", str(path), *common, *options])
    run(name + "-build", ["cmake", "--build", str(path), "--target", *targets, "--parallel", str(args.jobs)])
    return path

jpeg = build("jpeg", sources["jpeg"], ["-DENABLE_SHARED=OFF", "-DENABLE_STATIC=ON", "-DWITH_TURBOJPEG=ON",
                                       "-DWITH_SIMD=OFF", "-DWITH_JAVA=OFF"], ["jpeg-static", "turbojpeg-static"])
# This build copy changes scheduling only: a single-thread browser runtime cannot create pthreads.
uhdr_source = root / "ultrahdr-source"
shutil.copytree(sources["uhdr"], uhdr_source, dirs_exist_ok=True, ignore=shutil.ignore_patterns(".git"))
worker_source = uhdr_source / "lib/src/jpegr.cpp"
original = "unsigned int GetCPUCoreCount() { return (std::max)(1u, std::thread::hardware_concurrency()); }"
content = worker_source.read_text()
if content.count(original) != 1:
    raise SystemExit("Pinned UltraHDR worker adaptation no longer matches.")
worker_source.write_text(content.replace(original, """unsigned int GetCPUCoreCount() {
#if defined(__EMSCRIPTEN__) && !defined(__EMSCRIPTEN_PTHREADS__)
  return 1;
#else
  return (std::max)(1u, std::thread::hardware_concurrency());
#endif
}"""))
# Upstream's WASM check otherwise downloads an unrelated Emscripten JPEG port.
# Test the already built, ABI-locked libjpeg-turbo instead.
cmake_source = uhdr_source / "CMakeLists.txt"
content = cmake_source.read_text()
old_check = 'set(CMAKE_REQUIRED_FLAGS "--use-port=libjpeg")\n    set(CMAKE_REQUIRED_LINK_OPTIONS "--use-port=libjpeg")'
if content.count(old_check) != 1:
    raise SystemExit("Pinned UltraHDR JPEG dependency check no longer matches.")
cmake_source.write_text(content.replace(old_check, 'set(CMAKE_REQUIRED_INCLUDES ${JPEG_INCLUDE_DIR})\n    set(CMAKE_REQUIRED_LIBRARIES ${JPEG_LIBRARY})\n    set(CMAKE_REQUIRED_LINK_OPTIONS "-fwasm-exceptions")'))
uhdr = build("uhdr", uhdr_source, ["-DBUILD_SHARED_LIBS=OFF", "-DUHDR_BUILD_DEPS=OFF", "-DUHDR_BUILD_EXAMPLES=OFF",
    "-DUHDR_BUILD_TESTS=OFF", "-DUHDR_BUILD_BENCHMARK=OFF", "-DUHDR_ENABLE_INSTALL=OFF", "-DUHDR_ENABLE_HEIF=OFF",
    "-DUHDR_ENABLE_GLES=OFF", "-DUHDR_ENABLE_INTRINSICS=OFF", f"-DJPEG_LIBRARY={jpeg / 'libjpeg.a'}",
    f"-DJPEG_INCLUDE_DIR={sources['jpeg'] / 'src'};{jpeg}", "-UHAVE_JPEG"], ["uhdr"])
libraries = {"libjpeg.a": jpeg / "libjpeg.a", "libturbojpeg.a": jpeg / "libturbojpeg.a", "libuhdr.a": uhdr / "libuhdr.a"}
if args.codecs == "all":
    ktx = build("ktx", sources["ktx"], ["-DBUILD_SHARED_LIBS=OFF", "-DKTX_FEATURE_TOOLS=OFF", "-DKTX_FEATURE_TESTS=OFF",
        "-DKTX_FEATURE_LOADTEST_APPS=OFF", "-DKTX_FEATURE_GL_UPLOAD=OFF", "-DKTX_FEATURE_VK_UPLOAD=OFF",
        "-DKTX_FEATURE_ETC_UNPACK=OFF", "-DKTX_FEATURE_KTX1=ON", "-DBASISU_SUPPORT_OPENCL=OFF",
        "-DBASISU_SUPPORT_SSE=OFF", "-DASTCENC_ISA_NONE=ON"], ["ktx_read"])
    libraries["libktx.a"] = ktx / "libktx_read.a"
    libraries["libastcenc-none.a"] = next(ktx.rglob("libastcenc-none-static.a"))
report = {"sourceCommits": {n: source_pins[n] for n in sources}, "emscripten": "3.1.56", "exceptionHandling": "wasm",
          "ultraHdrSingleThreadBuildAdaptation": True, "ultraHdrPinnedJpegDependencyCheck": True, "libraries": {}}
required_symbols = {
    "libjpeg.a": ["jpeg_read_header", "jpeg_start_decompress"],
    "libturbojpeg.a": ["tj3Init", "tj3DecompressHeader", "tj3Decompress8"],
    "libuhdr.a": ["uhdr_create_encoder", "uhdr_encode", "uhdr_decode", "uhdr_get_decoded_image"],
    "libktx.a": ["ktxTexture2_CreateFromMemory", "ktxTexture2_TranscodeBasis", "ktxTexture2_GetImageOffset"],
}
for name, source in libraries.items():
    destination = stage / name
    shutil.copy2(source, destination)
    members = subprocess.check_output([str(sdk / "bin/llvm-ar"), "t", str(destination)], text=True).splitlines()
    if not members:
        raise SystemExit(f"Empty codec archive: {destination}")
    first = subprocess.check_output([str(sdk / "bin/llvm-ar"), "p", str(destination), members[0]])
    if first[:4] != b"\0asm":
        raise SystemExit(f"Codec archive is not wasm32: {destination}")
    symbols = subprocess.check_output([str(sdk / "bin/llvm-nm"), "--defined-only", str(destination)], text=True)
    for symbol in required_symbols.get(name, []):
        if not re.search(r"\b" + re.escape(symbol) + r"$", symbols, re.MULTILINE):
            raise SystemExit(f"Codec archive is missing required symbol {symbol}: {destination}")
    report["libraries"][name] = {"bytes": destination.stat().st_size, "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(), "objects": len(members)}
(logs / "codec-build.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
