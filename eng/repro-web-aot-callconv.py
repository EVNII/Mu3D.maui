#!/usr/bin/env python3
"""Compile one P/Invoke three ways; retain the negative case and both positive controls.

Uses only the installed, pinned SDK packs. Does not install tools, launch a browser,
load wgpu-native, or modify generated bindings. The negative compiler exits by abort.
"""
import json
import os
from pathlib import Path
import platform
import subprocess

root = Path(__file__).resolve().parent.parent
sdk = root / "artifacts/toolchain/dotnet"
packs = sdk / "packs"
version = "10.0.12"
if platform.system() != "Darwin" or platform.machine() != "arm64":
    raise SystemExit("This reproduction is pinned to the macOS ARM64 cross-compiler.")
compiler = packs / f"Microsoft.NETCore.App.Runtime.AOT.osx-arm64.Cross.browser-wasm/{version}/tools/mono-aot-cross"
runtime = packs / f"Microsoft.NETCore.App.Runtime.Mono.browser-wasm/{version}/runtimes/browser-wasm"
llvm = packs / f"Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.osx-arm64/{version}/tools/bin"
project = root / "tests/Mu3D.Web.Validation/AotCallConvRepro/Mu3D.AotCallConvRepro.csproj"
output = root / "artifacts/validation/web-wasm/aot-callconv-repro"
for required in [sdk / "dotnet", compiler, runtime / "native/System.Private.CoreLib.dll", llvm]:
    if not required.exists():
        raise SystemExit(f"Missing pinned toolchain input: {required}")

results = []
for variant, define in [("explicit", "EXPLICIT_CDECL"), ("default", "DEFAULT_CALLCONV"), ("legacy", "LEGACY_CDECL")]:
    dest = output / variant
    dest.mkdir(parents=True, exist_ok=True)
    with (dest / "build.log").open("w") as log:
        subprocess.run([str(sdk / "dotnet"), "build", str(project), "-c", "Release", "-o", str(dest),
                        "-m:1", "-nr:false", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false",
                        f"-p:DefineConstants={define}"], cwd=root, stdout=log, stderr=subprocess.STDOUT,
                       check=True, timeout=120)
    env = os.environ.copy()
    env["MONO_PATH"] = f"{dest}:{runtime}/native:{runtime}/lib/net10.0"
    env["MONO_ENV_OPTIONS"] = ""
    bitcode = dest / "repro.bc"
    bitcode.unlink(missing_ok=True)
    args = [str(compiler), "--wasm-exceptions", "--llvm",
            f"--aot=no-opt,static,direct-icalls,deterministic,mattr=simd,nodebug,llvm-path={llvm}/,"
            f"llvmonly,interp,asmonly,llvm-outfile={bitcode},temp-path={dest}/temp",
            "Mu3D.AotCallConvRepro.dll"]
    with (dest / "aot.log").open("w") as log:
        compiled = subprocess.run(args, cwd=dest, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=30)
    diagnostic = (dest / "aot.log").read_text()
    matched = ((compiled.returncode != 0 and "sgen-alloc.c:409" in diagnostic and "*p == NULL" in diagnostic)
               if variant == "explicit" else (compiled.returncode == 0 and bitcode.exists()))
    results.append({"variant": variant, "exitCode": compiled.returncode,
                    "expectedResult": matched, "llvmWritten": bitcode.exists()})
    print(f"{variant}: exit {compiled.returncode}; expected result = {matched}", flush=True)

(output / "results.json").write_text(json.dumps({"runtimeVersion": version, "results": results}, indent=2) + "\n")
if not all(item["expectedResult"] for item in results):
    raise SystemExit("Reproduction changed; inspect the logs before removing or extending the workaround.")
