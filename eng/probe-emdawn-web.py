#!/usr/bin/env python3
"""Maintainer-only pinned Emdawn link experiment. Does not install a production backend."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--download", action="store_true", help="Allow downloading the pinned official source zip")
parser.add_argument("--stack-dependency-compat", action="store_true", help="Experiment with old Emscripten dependency names in an isolated copy")
args = parser.parse_args()
repo = Path(__file__).resolve().parent.parent
root = repo / "artifacts/web-wasm"
root.mkdir(parents=True, exist_ok=True)
version = "v20260928.195327"
digest = "c142ad66d1f2cea912a14477850404088b08f93d18851d85acee1e417010f2a1"
archive = root / f"emdawnwebgpu_pkg-{version}.zip"
if not archive.exists():
    if not args.download:
        parser.error("Pinned zip is missing; use --download to fetch the official source package.")
    url = f"https://github.com/google/dawn/releases/download/{version}/{archive.name}"
    with urllib.request.urlopen(url, timeout=60) as response:
        archive.write_bytes(response.read())
if hashlib.sha256(archive.read_bytes()).hexdigest() != digest:
    raise SystemExit("Emdawn package checksum mismatch.")
variant = "compat" if args.stack_dependency_compat else "upstream"
source_dir = root / f"emdawn-{variant}"
with zipfile.ZipFile(archive) as package:
    package.extractall(source_dir)
port_root = source_dir / "emdawnwebgpu_pkg"
if args.stack_dependency_compat:
    library = port_root / "webgpu/src/library_webgpu.js"
    content = library.read_text()
    old = "errorCallback__deps: ['$stackSave', '$stackRestore', '$stringToUTF8OnStack']"
    new = "errorCallback__deps: ['stackSave', 'stackRestore', '$stringToUTF8OnStack']"
    if content.count(old) != 1:
        raise SystemExit("Expected pinned compatibility edit no longer matches.")
    library.write_text(content.replace(old, new))

dotnet = os.getenv("MU3D_DOTNET", str(repo / "artifacts/toolchain/dotnet/dotnet"))
if not Path(dotnet).is_file():
    dotnet = shutil.which(dotnet) or shutil.which("dotnet") or dotnet
properties = ["EmscriptenSdkToolsPath", "EmscriptenUpstreamBinPath", "EmscriptenUpstreamEmscriptenPath",
              "EmscriptenNodeBinPath", "EmscriptenVersion"]
project = repo / "tests/Mu3D.Web.Validation/Mu3D.Web.Validation.csproj"
settings = json.loads(subprocess.check_output(
    [dotnet, "msbuild", str(project), "-getProperty:" + ",".join(properties)], cwd=repo))["Properties"]
env = os.environ.copy()
env.update({
    "DOTNET_EMSCRIPTEN_LLVM_ROOT": settings["EmscriptenUpstreamBinPath"],
    "DOTNET_EMSCRIPTEN_BINARYEN_ROOT": settings["EmscriptenSdkToolsPath"],
    "DOTNET_EMSCRIPTEN_NODE_JS": str(Path(settings["EmscriptenNodeBinPath"]) / "node"),
    "EM_CACHE": str(root / "emdawn-cache"),
    "FROZEN_CACHE": "",
})
emcc = Path(settings["EmscriptenUpstreamEmscriptenPath"]) / "emcc.py"
logs = repo / "artifacts/validation/web-wasm"
logs.mkdir(parents=True, exist_ok=True)
output = root / f"emdawn-{variant}-probe.cjs"
command = [sys.executable, str(emcc), f"--use-port={port_root / 'emdawnwebgpu.port.py'}",
           str(project.parent / "emdawn_link_probe.c"), "-o", str(output)]
with (logs / f"Emdawn-{variant}-link.log").open("w") as log:
    result = subprocess.run(command, cwd=repo, env=env, stdout=log, stderr=subprocess.STDOUT)
report = {"dawn": version, "sha256": digest, "emscripten": settings["EmscriptenVersion"],
          "variant": variant, "linkExitCode": result.returncode, "portRoot": str(port_root)}
if result.returncode == 0:
    with (logs / f"Emdawn-{variant}-run.log").open("w") as log:
        run = subprocess.run([env["DOTNET_EMSCRIPTEN_NODE_JS"], str(output)], cwd=repo,
                             stdout=log, stderr=subprocess.STDOUT, timeout=30)
    report["instanceOnlyExitCode"] = run.returncode
(logs / f"Emdawn-{variant}-result.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
sys.exit(report.get("instanceOnlyExitCode", result.returncode))
