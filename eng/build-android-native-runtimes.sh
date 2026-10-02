#!/usr/bin/env bash
set -Eeuo pipefail

readonly KTX_VERSION="v4.4.2"
readonly KTX_COMMIT="4d6fc70eaf62ad0558e63e8d97eb9766118327a6"
readonly ULTRAHDR_VERSION="v2.0.1"
readonly ULTRAHDR_COMMIT="a532a8c1418890cff7ce1e90cbe59ba6ebc6fa6d"
readonly JPEG_TURBO_VERSION="3.1.0"
readonly JPEG_TURBO_COMMIT="20ade4dea9589515a69793e447a6c6220b464535"
readonly WGPU_VERSION="v29.0.1.1"
readonly NDK_VERSION="28.2.13676358"
readonly NDK_ARCHIVE="android-ndk-r28c-linux.zip"
readonly NDK_URL="https://dl.google.com/android/repository/${NDK_ARCHIVE}"
readonly NDK_SIZE="722261334"
readonly NDK_SHA1="a7b54a5de87fecd125a17d54f73c446199e72a64"

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly REPOSITORY_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd -P)"
readonly CACHE_ROOT="${MU3D_ANDROID_CACHE_ROOT:-${XDG_CACHE_HOME:-${HOME}/.cache}/mu3d-android-runtimes}"
readonly NDK_ROOT="${CACHE_ROOT}/android-ndk-r28c"
readonly LLVM_BIN="${NDK_ROOT}/toolchains/llvm/prebuilt/linux-x86_64/bin"
readonly BUILD_JOBS="${MU3D_BUILD_JOBS:-$(nproc)}"
readonly FORCE_REBUILD="${MU3D_FORCE_REBUILD:-false}"

WORK_ROOT=""

declare -ar RIDS=(android-arm android-arm64 android-x86 android-x64)
declare -A ABIS=(
  [android-arm]="armeabi-v7a"
  [android-arm64]="arm64-v8a"
  [android-x86]="x86"
  [android-x64]="x86_64"
)
declare -A ELF_CLASSES=(
  [android-arm]="ELF32"
  [android-arm64]="ELF64"
  [android-x86]="ELF32"
  [android-x64]="ELF64"
)
declare -A ELF_MACHINES=(
  [android-arm]="ARM"
  [android-arm64]="AArch64"
  [android-x86]="Intel 80386"
  [android-x64]="Advanced Micro Devices X86-64"
)

declare -ar KTX_EXPORTS=(
  ktxTexture2_CreateFromMemory
  ktxTexture2_Destroy
  ktxTexture2_NeedsTranscoding
  ktxTexture2_GetTransferFunction_e
  ktxTexture2_GetPrimaries_e
  ktxTexture2_GetPremultipliedAlpha
  ktxTexture2_TranscodeBasis
  ktxTexture2_GetImageOffset
  ktxTexture_GetData
  ktxTexture_GetDataSize
  ktxHashList_FindValue
)

declare -ar UHDR_EXPORTS=(
  uhdr_create_decoder
  uhdr_release_decoder
  uhdr_dec_set_image
  uhdr_dec_set_out_img_format
  uhdr_dec_set_out_color_transfer
  uhdr_dec_probe
  uhdr_dec_get_gainmap_width
  uhdr_dec_get_gainmap_height
  uhdr_decode
  uhdr_get_decoded_image
  is_uhdr_image
  uhdr_create_encoder
  uhdr_release_encoder
  uhdr_enc_set_raw_image
  uhdr_enc_set_quality
  uhdr_enc_set_using_multi_channel_gainmap
  uhdr_enc_set_gainmap_scale_factor
  uhdr_enc_set_target_display_peak_brightness
  uhdr_enc_set_preset
  uhdr_enc_set_output_format
  uhdr_encode
  uhdr_get_encoded_stream
)

declare -ar TURBOJPEG_EXPORTS=(
  tj3Init
  tj3Destroy
  tj3GetErrorStr
  tj3Get
  tj3Set
  tj3Compress8
  tj3Free
  tj3DecompressHeader
  tj3Decompress8
)

log() {
  printf '[Mu3D Android native] %s\n' "$*"
}

die() {
  printf '[Mu3D Android native] ERROR: %s\n' "$*" >&2
  exit 1
}

run_as_root() {
  if [[ "$(id -u)" -eq 0 ]]; then
    "$@"
  elif command -v sudo >/dev/null 2>&1; then
    sudo "$@"
  else
    die "Root privileges are required to install Ubuntu build packages."
  fi
}

install_build_packages() {
  local command_name
  for command_name in cc c++ cmake curl git ninja nasm perl python3 unzip; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
      break
    fi
  done
  if [[ "${command_name}" == "unzip" ]] && command -v unzip >/dev/null 2>&1; then
    log "Using installed Ubuntu build prerequisites"
    return
  fi

  log "Installing Ubuntu build prerequisites"
  run_as_root apt-get update
  run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y \
    build-essential ca-certificates cmake curl git ninja-build nasm perl python3 unzip
}

install_ndk() {
  if [[ -x "${LLVM_BIN}/llvm-readelf" ]]; then
    local installed_revision
    installed_revision="$(sed -n 's/^Pkg.Revision = //p' "${NDK_ROOT}/source.properties")"
    [[ "${installed_revision}" == "${NDK_VERSION}" ]] || \
      die "Unexpected cached NDK revision '${installed_revision}' in '${NDK_ROOT}'."
    log "Using cached Android NDK ${NDK_VERSION}"
    return
  fi

  mkdir -p -- "${CACHE_ROOT}"
  local archive_path="${CACHE_ROOT}/${NDK_ARCHIVE}"
  log "Downloading pinned Android NDK ${NDK_VERSION} (r28c)"
  curl --fail --location --retry 3 --output "${archive_path}" "${NDK_URL}"

  local actual_size
  actual_size="$(stat --format='%s' "${archive_path}")"
  [[ "${actual_size}" == "${NDK_SIZE}" ]] || \
    die "NDK archive size mismatch: expected ${NDK_SIZE}, got ${actual_size}."
  printf '%s  %s\n' "${NDK_SHA1}" "${archive_path}" | sha1sum --check --status || \
    die "NDK archive SHA-1 mismatch."

  local extract_root
  extract_root="$(mktemp -d --tmpdir mu3d-ndk-r28c.XXXXXXXX)"
  unzip -q "${archive_path}" -d "${extract_root}"
  [[ -x "${extract_root}/android-ndk-r28c/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-readelf" ]] || \
    die "Downloaded NDK does not contain the expected Linux LLVM toolchain."
  mv -- "${extract_root}/android-ndk-r28c" "${NDK_ROOT}"
  rmdir -- "${extract_root}"

  local installed_revision
  installed_revision="$(sed -n 's/^Pkg.Revision = //p' "${NDK_ROOT}/source.properties")"
  [[ "${installed_revision}" == "${NDK_VERSION}" ]] || \
    die "Downloaded NDK revision '${installed_revision}' does not match '${NDK_VERSION}'."
}

checkout_exact_commit() {
  local repository_url="$1"
  local commit="$2"
  local destination="$3"
  local fetch_ref="${4:-${commit}}"

  git init --quiet "${destination}"
  git -C "${destination}" remote add origin "${repository_url}"
  git -C "${destination}" fetch --quiet --depth 1 origin "${fetch_ref}"
  git -C "${destination}" checkout --quiet --detach FETCH_HEAD
  [[ "$(git -C "${destination}" rev-parse HEAD)" == "${commit}" ]] || \
    die "Source commit verification failed for '${repository_url}'."
}

assert_elf_identity() {
  local library="$1"
  local rid="$2"
  local header
  header="$("${LLVM_BIN}/llvm-readelf" --file-header "${library}")"
  grep -Eq "Class:[[:space:]]+${ELF_CLASSES[${rid}]}$" <<<"${header}" || \
    die "ELF class mismatch for ${rid}: ${library}"
  grep -Eq "Machine:[[:space:]]+${ELF_MACHINES[${rid}]}$" <<<"${header}" || \
    die "ELF machine mismatch for ${rid}: ${library}"
}

assert_no_shared_cpp() {
  local library="$1"
  if "${LLVM_BIN}/llvm-readelf" -d "${library}" | grep -Fq 'libc++_shared.so'; then
    die "${library} unexpectedly depends on libc++_shared.so."
  fi
}

assert_exports() {
  local library="$1"
  shift
  local symbols
  symbols="$("${LLVM_BIN}/llvm-nm" --dynamic --defined-only "${library}")"
  local symbol
  for symbol in "$@"; do
    grep -Eq "[[:space:]]${symbol}(@@?[^[:space:]]+)?$" <<<"${symbols}" || \
      die "Missing native export '${symbol}' in '${library}'."
  done
}

build_ktx() {
  local source_root="$1"
  local work_root="$2"
  local stage_root="$3"
  local rid="$4"
  local abi="${ABIS[${rid}]}"
  local build_root="${work_root}/ktx-${rid}"
  local destination="${stage_root}/ktx/${KTX_VERSION}/release/runtimes/${rid}/native"

  log "Configuring KTX ${KTX_VERSION} for ${rid} (${abi})"
  cmake -S "${source_root}" -B "${build_root}" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_ROOT}/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI="${abi}" \
    -DANDROID_PLATFORM=android-24 \
    -DANDROID_STL=c++_static \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=ON \
    -DKTX_FEATURE_TOOLS=OFF \
    -DKTX_FEATURE_TESTS=OFF \
    -DKTX_FEATURE_LOADTEST_APPS=OFF \
    -DKTX_FEATURE_GL_UPLOAD=OFF \
    -DKTX_FEATURE_VK_UPLOAD=OFF \
    -DKTX_FEATURE_ETC_UNPACK=OFF \
    -DKTX_FEATURE_KTX1=ON \
    -DBASISU_SUPPORT_OPENCL=OFF \
    -DBASISU_SUPPORT_SSE=OFF \
    -DMU3D_ALLOW_ANDROID_X86=ON \
    -DASTCENC_ISA_SSE2=ON
  cmake --build "${build_root}" --config Release --target ktx_read --parallel "${BUILD_JOBS}"

  local library
  library="$(find "${build_root}" -type f -name libktx_read.so -print -quit)"
  [[ -n "${library}" ]] || die "KTX build did not produce libktx_read.so for ${rid}."
  mkdir -p -- "${destination}"
  cp -- "${library}" "${destination}/libktx.so"
  assert_elf_identity "${destination}/libktx.so" "${rid}"
  assert_exports "${destination}/libktx.so" "${KTX_EXPORTS[@]}"
  assert_no_shared_cpp "${destination}/libktx.so"
}

build_codecs() {
  local ultrahdr_source="$1"
  local turbo_source="$2"
  local work_root="$3"
  local stage_root="$4"
  local rid="$5"
  local abi="${ABIS[${rid}]}"
  local turbo_build="${work_root}/turbo-${rid}"
  local uhdr_build="${work_root}/uhdr-${rid}"
  local destination="${stage_root}/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes/${rid}/native"

  log "Configuring libjpeg-turbo ${JPEG_TURBO_VERSION} for ${rid} (${abi})"
  cmake -S "${turbo_source}" -B "${turbo_build}" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_ROOT}/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI="${abi}" \
    -DANDROID_PLATFORM=android-24 \
    -DANDROID_STL=c++_static \
    -DCMAKE_BUILD_TYPE=Release \
    -DENABLE_SHARED=ON \
    -DENABLE_STATIC=ON \
    -DWITH_TURBOJPEG=ON
  cmake --build "${turbo_build}" --config Release --target jpeg-static turbojpeg --parallel "${BUILD_JOBS}"

  log "Configuring JPEG-only libultrahdr ${ULTRAHDR_VERSION} for ${rid} (${abi})"
  cmake -S "${ultrahdr_source}" -B "${uhdr_build}" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_ROOT}/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI="${abi}" \
    -DANDROID_PLATFORM=android-24 \
    -DANDROID_STL=c++_static \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=ON \
    -DUHDR_BUILD_DEPS=OFF \
    -DUHDR_BUILD_TESTS=OFF \
    -DUHDR_BUILD_EXAMPLES=OFF \
    -DUHDR_BUILD_BENCHMARK=OFF \
    -DUHDR_ENABLE_HEIF=OFF \
    -DUHDR_ENABLE_INSTALL=OFF \
    -DUHDR_WRITE_ISO=ON \
    -DUHDR_WRITE_XMP=OFF \
    -DJPEG_LIBRARY="${turbo_build}/libjpeg.a" \
    "-DJPEG_INCLUDE_DIR=${turbo_source}/src;${turbo_build}"
  cmake --build "${uhdr_build}" --config Release --target uhdr --parallel "${BUILD_JOBS}"

  local uhdr_library
  local turbo_library
  uhdr_library="$(find "${uhdr_build}" -type f -name libuhdr.so -print -quit)"
  turbo_library="$(find "${turbo_build}" -type f -name libturbojpeg.so -print -quit)"
  [[ -n "${uhdr_library}" ]] || die "UltraHDR build did not produce libuhdr.so for ${rid}."
  [[ -n "${turbo_library}" ]] || die "libjpeg-turbo build did not produce libturbojpeg.so for ${rid}."

  mkdir -p -- "${destination}"
  cp -- "${uhdr_library}" "${destination}/libuhdr.so"
  cp -- "${turbo_library}" "${destination}/libturbojpeg.so"
  assert_elf_identity "${destination}/libuhdr.so" "${rid}"
  assert_elf_identity "${destination}/libturbojpeg.so" "${rid}"
  assert_exports "${destination}/libuhdr.so" "${UHDR_EXPORTS[@]}"
  assert_exports "${destination}/libturbojpeg.so" "${TURBOJPEG_EXPORTS[@]}"
  assert_no_shared_cpp "${destination}/libuhdr.so"
  assert_no_shared_cpp "${destination}/libturbojpeg.so"
}

verify_wgpu_runtimes() {
  local rid
  for rid in "${RIDS[@]}"; do
    local library="${REPOSITORY_ROOT}/artifacts/wgpu-native/${WGPU_VERSION}/release/runtimes/${rid}/native/libwgpu_native.so"
    [[ -f "${library}" ]] || \
      die "Missing ${library}. Run eng/stage-android-wgpu-runtimes.ps1 on Windows first."
    assert_elf_identity "${library}" "${rid}"
    assert_exports "${library}" wgpuCreateInstance wgpuGetVersion
  done
}

stage_rid_outputs() {
  local stage_root="$1"
  local rid="$2"
  local ktx_destination="${REPOSITORY_ROOT}/artifacts/ktx/${KTX_VERSION}/release/runtimes/${rid}/native"
  local uhdr_destination="${REPOSITORY_ROOT}/artifacts/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes/${rid}/native"
  mkdir -p -- "${ktx_destination}" "${uhdr_destination}"
  cp -- "${stage_root}/ktx/${KTX_VERSION}/release/runtimes/${rid}/native/libktx.so" \
    "${ktx_destination}/libktx.so"
  cp -- "${stage_root}/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes/${rid}/native/libuhdr.so" \
    "${uhdr_destination}/libuhdr.so"
  cp -- "${stage_root}/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes/${rid}/native/libturbojpeg.so" \
    "${uhdr_destination}/libturbojpeg.so"
}

assert_staged_rid() {
  local rid="$1"
  local ktx_library="${REPOSITORY_ROOT}/artifacts/ktx/${KTX_VERSION}/release/runtimes/${rid}/native/libktx.so"
  local uhdr_library="${REPOSITORY_ROOT}/artifacts/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes/${rid}/native/libuhdr.so"
  local turbo_library="${REPOSITORY_ROOT}/artifacts/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes/${rid}/native/libturbojpeg.so"
  [[ -f "${ktx_library}" && -f "${uhdr_library}" && -f "${turbo_library}" ]] || return 1
  assert_elf_identity "${ktx_library}" "${rid}"
  assert_elf_identity "${uhdr_library}" "${rid}"
  assert_elf_identity "${turbo_library}" "${rid}"
  assert_exports "${ktx_library}" "${KTX_EXPORTS[@]}"
  assert_exports "${uhdr_library}" "${UHDR_EXPORTS[@]}"
  assert_exports "${turbo_library}" "${TURBOJPEG_EXPORTS[@]}"
  assert_no_shared_cpp "${ktx_library}"
  assert_no_shared_cpp "${uhdr_library}"
  assert_no_shared_cpp "${turbo_library}"
}

finalize_outputs() {
  local rid
  for rid in "${RIDS[@]}"; do
    assert_staged_rid "${rid}" || die "The staged Android runtime set is incomplete for ${rid}."
  done

  local sums_path="${REPOSITORY_ROOT}/artifacts/SHA256SUMS.android-native"
  : >"${sums_path}"
  while IFS= read -r -d '' library; do
    sha256sum "${library}" | sed "s#  ${REPOSITORY_ROOT}/#  #" >>"${sums_path}"
  done < <(find \
    "${REPOSITORY_ROOT}/artifacts/wgpu-native/${WGPU_VERSION}/release/runtimes" \
    "${REPOSITORY_ROOT}/artifacts/ktx/${KTX_VERSION}/release/runtimes" \
    "${REPOSITORY_ROOT}/artifacts/ultrahdr/${ULTRAHDR_VERSION}/release/runtimes" \
    -path '*/android-*/native/*.so' -type f -print0 | sort -z)
}

cleanup() {
  if [[ -n "${WORK_ROOT}" && "${WORK_ROOT}" == /tmp/mu3d-android-build.* && -d "${WORK_ROOT}" ]]; then
    rm -rf -- "${WORK_ROOT}"
  fi
}

main() {
  install_build_packages
  install_ndk
  verify_wgpu_runtimes

  WORK_ROOT="$(mktemp -d --tmpdir mu3d-android-build.XXXXXXXX)"
  trap cleanup EXIT
  local source_root="${WORK_ROOT}/sources"
  local build_root="${WORK_ROOT}/build"
  local stage_root="${WORK_ROOT}/stage/artifacts"
  mkdir -p -- "${source_root}" "${build_root}" "${stage_root}"

  log "Checking out exact pinned native source commits"
  checkout_exact_commit \
    https://github.com/KhronosGroup/KTX-Software.git \
    "${KTX_COMMIT}" "${source_root}/ktx" \
    "refs/tags/${KTX_VERSION}:refs/tags/${KTX_VERSION}"
  [[ "$(git -C "${source_root}/ktx" describe --tags --exact-match)" == "${KTX_VERSION}" ]] || \
    die "KTX tag ${KTX_VERSION} is not attached to the pinned source checkout."
  checkout_exact_commit \
    https://github.com/google/libultrahdr.git \
    "${ULTRAHDR_COMMIT}" "${source_root}/ultrahdr"
  checkout_exact_commit \
    https://github.com/libjpeg-turbo/libjpeg-turbo.git \
    "${JPEG_TURBO_COMMIT}" "${source_root}/jpeg-turbo"
  git -C "${source_root}/ktx" apply --check \
    "${REPOSITORY_ROOT}/eng/native/KtxAndroidX86Read.patch"
  git -C "${source_root}/ktx" apply \
    "${REPOSITORY_ROOT}/eng/native/KtxAndroidX86Read.patch"

  local rid
  for rid in "${RIDS[@]}"; do
    if [[ "${FORCE_REBUILD}" != "true" ]] && assert_staged_rid "${rid}"; then
      log "Using already verified staged outputs for ${rid}"
      continue
    fi
    build_ktx "${source_root}/ktx" "${build_root}" "${stage_root}" "${rid}"
    build_codecs \
      "${source_root}/ultrahdr" "${source_root}/jpeg-turbo" \
      "${build_root}" "${stage_root}" "${rid}"
    stage_rid_outputs "${stage_root}" "${rid}"
    assert_staged_rid "${rid}"
  done

  finalize_outputs
  log "All four Android RIDs are built, verified, and staged."
  log "Hashes: ${REPOSITORY_ROOT}/artifacts/SHA256SUMS.android-native"
}

main "$@"
