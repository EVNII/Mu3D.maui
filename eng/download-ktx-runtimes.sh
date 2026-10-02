#!/usr/bin/env bash
set -euo pipefail

repository="${MU3D_GITHUB_REPOSITORY:-EVNII/Mu3d}"
workflow="${MU3D_KTX_WORKFLOW:-native-ktx-platforms.yml}"
run_id="${1:-}"
artifact_name="Mu3D.Native.Ktx.Runtimes.PlatformMatrixVerified"
version="v4.4.2"

if ! command -v gh >/dev/null 2>&1; then
  echo "GitHub CLI is required: https://cli.github.com/" >&2
  exit 1
fi

if ! gh auth status --hostname github.com >/dev/null 2>&1; then
  echo "GitHub CLI is not authenticated. Run: gh auth login -h github.com" >&2
  exit 1
fi

if [[ -z "$run_id" ]]; then
  run_id="$(gh run list \
    --repo "$repository" \
    --workflow "$workflow" \
    --status success \
    --limit 1 \
    --json databaseId \
    --jq '.[0].databaseId')"
fi

if [[ -z "$run_id" || "$run_id" == "null" ]]; then
  echo "No successful $workflow run was found in $repository." >&2
  exit 1
fi

download_root="$(mktemp -d "${TMPDIR:-/tmp}/mu3d-ktx-runtimes.XXXXXX")"
trap 'rm -rf "$download_root"' EXIT

gh run download "$run_id" \
  --repo "$repository" \
  --name "$artifact_name" \
  --dir "$download_root"

if [[ ! -d "$download_root/runtimes" || ! -f "$download_root/SHA256SUMS" ]]; then
  echo "The downloaded artifact does not contain runtimes/ and SHA256SUMS." >&2
  exit 1
fi

(
  cd "$download_root"
  shasum -a 256 -c SHA256SUMS
)

required=(
  android-arm/native/libktx.so
  android-arm64/native/libktx.so
  android-x86/native/libktx.so
  android-x64/native/libktx.so
  ios-arm64/native/libktx.a
  iossimulator-arm64/native/libktx.a
  iossimulator-x64/native/libktx.a
  maccatalyst-arm64/native/libktx.a
  maccatalyst-x64/native/libktx.a
  win-arm64/native/ktx.dll
  win-x86/native/ktx.dll
  win-x64/native/ktx.dll
)

for relative_path in "${required[@]}"; do
  if [[ ! -f "$download_root/runtimes/$relative_path" ]]; then
    echo "Verified runtime is missing: $relative_path" >&2
    exit 1
  fi
done

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/.." && pwd)"
destination="$repository_root/artifacts/ktx/$version/release"
mkdir -p "$destination/runtimes"
cp -R "$download_root/runtimes/." "$destination/runtimes/"
cp "$download_root/SHA256SUMS" "$destination/SHA256SUMS"

echo "Staged verified KTX runtimes from workflow run $run_id:"
echo "  $destination/runtimes"
echo "Enable the Gallery harness with: -p:Mu3DEnableKtxHarness=true"
