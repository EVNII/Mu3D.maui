#!/usr/bin/env bash
set -euo pipefail

repository="${MU3D_GITHUB_REPOSITORY:-EVNII/Mu3d}"
workflow="${MU3D_ULTRAHDR_WORKFLOW:-native-codecs-platforms.yml}"
run_id="${1:-}"
artifact_name="Mu3D.Native.UltraHdr.Runtimes.PlatformMatrixVerified"
version="v2.0.1"

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

download_root="$(mktemp -d "${TMPDIR:-/tmp}/mu3d-ultrahdr-runtimes.XXXXXX")"
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
  android-arm/native/libuhdr.so
  android-arm/native/libturbojpeg.so
  android-arm64/native/libuhdr.so
  android-arm64/native/libturbojpeg.so
  android-x86/native/libuhdr.so
  android-x86/native/libturbojpeg.so
  android-x64/native/libuhdr.so
  android-x64/native/libturbojpeg.so
  ios-arm64/native/libuhdr.a
  ios-arm64/native/libturbojpeg.a
  iossimulator-arm64/native/libuhdr.a
  iossimulator-arm64/native/libturbojpeg.a
  iossimulator-x64/native/libuhdr.a
  iossimulator-x64/native/libturbojpeg.a
  maccatalyst-arm64/native/libuhdr.a
  maccatalyst-arm64/native/libturbojpeg.a
  maccatalyst-x64/native/libuhdr.a
  maccatalyst-x64/native/libturbojpeg.a
  win-arm64/native/uhdr.dll
  win-arm64/native/turbojpeg.dll
  win-x86/native/uhdr.dll
  win-x86/native/turbojpeg.dll
  win-x64/native/uhdr.dll
  win-x64/native/turbojpeg.dll
)

for relative_path in "${required[@]}"; do
  if [[ ! -f "$download_root/runtimes/$relative_path" ]]; then
    echo "Verified runtime is missing: $relative_path" >&2
    exit 1
  fi
done

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/.." && pwd)"
destination="$repository_root/artifacts/ultrahdr/$version/release"
mkdir -p "$destination/runtimes"
cp -R "$download_root/runtimes/." "$destination/runtimes/"
cp "$download_root/SHA256SUMS" "$destination/SHA256SUMS"

echo "Staged verified libultrahdr/libjpeg-turbo runtimes from workflow run $run_id:"
echo "  $destination/runtimes"
echo "Gallery automatically enables the matching Ultra HDR harness when these files are present."
