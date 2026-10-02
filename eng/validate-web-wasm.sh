#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
dotnet_cmd="${MU3D_DOTNET:-$repo_root/artifacts/toolchain/dotnet/dotnet}"
if [[ ! -x "$dotnet_cmd" ]]; then dotnet_cmd=dotnet; fi
project=tests/Mu3D.Web.Validation/Mu3D.Web.Validation.csproj
mkdir -p artifacts/validation/web-wasm

# Install wasm-tools separately using the SDK/workload version pinned by global.json.
# No native runtime download, MAUI application, browser UI, or hosted workflow is started.
suites=(Smoke Creative Graphics HostContracts)
if [[ -n "${MU3D_EMDAWN_ROOT:-}" ]]; then
    suites+=(WgpuAbi)
fi
for suite in "${suites[@]}"; do
    printf 'Publishing and running %s for browser-wasm...\n' "$suite"
    "$dotnet_cmd" restore "$project" --disable-parallel "-p:Mu3DWebSuite=$suite" \
        > "artifacts/validation/web-wasm/$suite-restore.log" 2>&1
    "$dotnet_cmd" publish "$project" -c Release --no-restore -m:1 -nr:false \
        -p:UseSharedCompilation=false "-p:Mu3DWebSuite=$suite" \
        "-p:EmdawnWebGpuRoot=${MU3D_EMDAWN_ROOT:-}" \
        -o "artifacts/web-wasm/$suite" > "artifacts/validation/web-wasm/$suite-build.log" 2>&1
    run_args=("artifacts/web-wasm/$suite/wwwroot/main.mjs")
    if [[ "$suite" == WgpuAbi ]]; then run_args+=(--flat-callbacks); fi
    node "${run_args[@]}" 2>&1 | tee "artifacts/validation/web-wasm/$suite-run.log"
done
