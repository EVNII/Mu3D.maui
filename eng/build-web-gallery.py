#!/usr/bin/env python3
"""Build the existing full Web Gallery, optionally preparing its pinned WASM dependencies."""
import argparse
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
PROJECT = REPO / "samples/Mu3D.Gallery/Web/Mu3D.GalleryApp.Web.csproj"
WEB_ROOT = REPO / "artifacts/web-wasm"


def run(command, env):
    command = [str(item) for item in command]
    print("+ " + shlex.join(command), flush=True)
    subprocess.run(command, cwd=REPO, env=env, check=True)


def source_pins():
    pins = {}
    for name in ("Mu3D.UltraHdrNativeVersion.props", "Mu3D.KtxNativeVersion.props"):
        pins.update({entry.tag: entry.text for entry in ET.parse(REPO / "eng" / name).iter()
                     if entry.tag.startswith("Mu3D") and entry.text})
    return (
        ("jpeg-turbo-source", "https://github.com/libjpeg-turbo/libjpeg-turbo.git", pins["Mu3DJpegTurboNativeCommit"]),
        ("ultrahdr-source", "https://github.com/google/libultrahdr.git", pins["Mu3DUltraHdrNativeCommit"]),
        ("ktx-source", "https://github.com/KhronosGroup/KTX-Software.git", pins["Mu3DKtxNativeCommit"]),
    )


def verify_source(source, commit, env):
    if not (source / "CMakeLists.txt").is_file():
        raise RuntimeError(f"Missing pinned codec checkout: {source}")
    actual = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], env=env, text=True).strip()
    dirty = subprocess.check_output(["git", "-C", str(source), "status", "--porcelain"], env=env, text=True).strip()
    if actual != commit or dirty:
        raise RuntimeError(f"Preserving existing checkout {source}: it must be clean at {commit}.")


def prepare_sources(env):
    root = REPO / "artifacts/native-build"
    root.mkdir(parents=True, exist_ok=True)
    for name, url, commit in source_pins():
        source = root / name
        if not source.exists():
            # Only this new temporary checkout is owned by the script. Existing sources
            # are checked, never reset or overwritten. No test-only KTX CTS/LFS inputs are needed.
            temporary = Path(tempfile.mkdtemp(prefix=name + "-", dir=root))
            try:
                run(["git", "init", str(temporary)], env)
                run(["git", "-C", temporary, "remote", "add", "origin", url], env)
                run(["git", "-C", temporary, "fetch", "--depth", "1", "origin", commit], env)
                run(["git", "-C", temporary, "-c", "core.autocrlf=false", "checkout", "--detach", commit], env)
                verify_source(temporary, commit, env)
                temporary.rename(source)
            finally:
                if temporary.exists():
                    shutil.rmtree(temporary)
        verify_source(source, commit, env)


def resolve_dotnet(value):
    local = REPO / "artifacts/toolchain/dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    value = value or os.environ.get("MU3D_DOTNET") or (str(local) if local.is_file() else shutil.which("dotnet"))
    if not value:
        raise RuntimeError("Install the pinned .NET SDK and wasm-tools workload, or specify --dotnet.")
    executable = Path(shutil.which(value) or value).resolve()
    if not executable.is_file():
        raise RuntimeError(f"The selected .NET executable does not exist: {executable}")
    return executable


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", help=".NET executable; defaults to MU3D_DOTNET, the local SDK, or PATH")
    parser.add_argument("--prepare", action="store_true", help="Download pinned official sources and build the existing WASM dependencies")
    parser.add_argument("--mode", choices=("both", "aot", "interpreted"), default="both")
    parser.add_argument("--output-root", type=Path, default=WEB_ROOT)
    parser.add_argument("--print-profile-directory", type=Path,
                        help="Bundle supplied local .icc files for Gallery verification; no profiles are downloaded")
    parser.add_argument("--jobs", type=int, default=2, help="Parallel codec compilation jobs (default: 2)")
    args = parser.parse_args()
    if args.jobs < 1:
        parser.error("--jobs must be positive")
    profiles = args.print_profile_directory.resolve() if args.print_profile_directory else None
    if profiles is not None and (not profiles.is_dir() or not any(path.is_file() for path in profiles.glob("*.icc"))):
        parser.error("--print-profile-directory must contain supplied .icc files")
    dotnet = resolve_dotnet(args.dotnet)
    output = args.output_root.resolve()
    codecs = output / "codecs"
    port = WEB_ROOT / "emdawn-compat/emdawnwebgpu_pkg"
    env = os.environ.copy()
    env.update(MU3D_DOTNET=str(dotnet), GIT_LFS_SKIP_SMUDGE="1", FROZEN_CACHE="")
    if args.prepare:
        for tool in ("git", "cmake", "ninja"):
            if not shutil.which(tool):
                raise RuntimeError(f"Install {tool} before --prepare.")
        # The unmodified port is incompatible with the pinned Emscripten dependency names.
        run([sys.executable, REPO / "eng/probe-emdawn-web.py", "--download", "--stack-dependency-compat"], env)
        prepare_sources(env)
        run([sys.executable, REPO / "eng/build-web-codecs.py", "--dotnet-root", dotnet.parent,
             "--output", codecs, "--jobs", args.jobs], env)
    required = [port / "emdawnwebgpu.port.py", port / "webgpu/include/webgpu/webgpu.h"]
    required.extend(codecs / "lib" / name for name in
                    ("libjpeg.a", "libturbojpeg.a", "libuhdr.a", "libktx.a", "libastcenc-none.a"))
    missing = [str(path) for path in required if not path.is_file()]
    if missing:
        raise RuntimeError("Missing WASM build inputs; run with --prepare:\n" + "\n".join(missing))
    common = ["-p:NuGetAudit=false", "-p:PublishTrimmed=true", "-p:UseSharedCompilation=false",
              f"-p:WasmCachePath={output / 'emdawn-cache'}", f"-p:EmdawnWebGpuRoot={port}",
              f"-p:Mu3DBrowserCodecRoot={codecs / 'lib'}"]
    if profiles is not None:
        common.append(f"-p:Mu3DPrintProfileDirectory={profiles}")
    modes = ("interpreted", "aot") if args.mode == "both" else (args.mode,)
    published = {}
    route_assemblies = []
    for mode in modes:
        aot = mode == "aot"
        properties = [*common, f"-p:RunAOTCompilation={'true' if aot else 'false'}"]
        if aot:
            properties += ["-p:DisableParallelAot=true", "-p:DisableParallelEmccCompile=true"]
        destination = output / ("GalleryAot" if aot else "Gallery")
        run([dotnet, "restore", PROJECT, "--disable-parallel", "-p:Configuration=Release", *properties], env)
        run([dotnet, "publish", PROJECT, "-c", "Release", "--no-restore", "--disable-build-servers",
             "-m:1", "-nr:false", *properties, "-o", destination], env)
        root = destination / "wwwroot"
        if not (root / "index.html").is_file() or not (root / "_framework/blazor.webassembly.js").is_file():
            raise RuntimeError(f"Gallery publish omitted its boot files: {root}")
        files = [path for path in root.rglob("*") if path.is_file()]
        published[mode] = {"wwwroot": str(root), "files": len(files), "bytes": sum(path.stat().st_size for path in files)}
        route_assemblies.append(PROJECT.parent / "obj/Release" / ("Aot" if aot else "Interpreted") /
                                "net10.0/linked/Mu3D.GalleryApp.Web.dll")
    # SPA HTTP 200 does not establish that trimming retained the routable component.
    checks = REPO / "tests/Mu3D.Web.Viewer.Tests/Mu3D.Web.Viewer.Tests.csproj"
    run([dotnet, "build", checks, "-c", "Release", "--disable-build-servers", "-m:1", "-nr:false",
         "-p:UseSharedCompilation=false", "-p:NuGetAudit=false", "-p:EnableMu3DPrinting=true"], env)
    run([dotnet, checks.parent / "bin/Release/net10.0/Mu3D.Web.Viewer.Tests.dll",
         "--published-gallery", *route_assemblies], env)
    print(json.dumps({"published": published}, indent=2), flush=True)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error))
