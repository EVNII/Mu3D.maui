"""Package maintainer-captured DXIL in the optional renderer; consumers never run this."""
import argparse
import hashlib
import json
import pathlib
import struct
import subprocess
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "src/Mu3D.Rendering.OpenPbr/precompiled/windows"
MAGIC = b"Mu3D.dxil.v1.wgpu29.0.3\0"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def sources():
    files = sorted((ROOT / "src/Mu3D.Rendering.OpenPbr/Shaders").glob("*.wgsl"))
    files += [ROOT / "eng/native/WgpuDx12ShaderCache.rs", ROOT / "eng/prepare-wgpu-shader-cache.py"]
    return {p.relative_to(ROOT).as_posix(): digest(p.read_bytes()) for p in files}


def decode(data):
    if not data.startswith(MAGIC):
        raise ValueError("Unknown capture version")
    offset = len(MAGIC) + 32
    count, = struct.unpack_from("<I", data, offset)
    offset += 4
    args = []
    for _ in range(count):
        length, = struct.unpack_from("<I", data, offset)
        offset += 4
        args.append(data[offset:offset + length].decode("utf-8"))
        offset += length
    length, = struct.unpack_from("<Q", data, offset)
    offset += 8
    if length != len(data) - offset:
        raise ValueError("Truncated capture")
    return args, data[offset:]


def validate_blob(blob):
    if blob[:8] != b"M3DXIL01" or blob[40:44] != b"DXBC" or hashlib.sha256(blob[40:]).digest() != blob[8:40]:
        raise ValueError("Invalid DXIL cache blob")


def compile_feature_variants(capture, compiler):
    """Also cover devices enabling ShaderF16; OpenPBR shader arithmetic remains FP32."""
    work = ROOT / "artifacts/openpbr-offline-f16"
    work.mkdir(parents=True, exist_ok=True)
    for path in sorted(capture.glob("*.input")):
        data = path.read_bytes()
        args, source = decode(data)
        profile = args[args.index("-T") + 1]
        if int(profile.rsplit("_", 1)[1]) < 2:
            continue
        if "-enable-16bit-types" in args:
            args.remove("-enable-16bit-types")
        else:
            args.append("-enable-16bit-types")
        variant = data[:len(MAGIC) + 32] + struct.pack("<I", len(args))
        for arg in args:
            encoded = arg.encode("utf-8")
            variant += struct.pack("<I", len(encoded)) + encoded
        variant += struct.pack("<Q", len(source)) + source
        key = digest(variant)
        target = capture / (key + ".input")
        if target.exists() and target.with_suffix(".bin").exists():
            validate_blob(target.with_suffix(".bin").read_bytes())
            continue
        hlsl, output = work / (key + ".hlsl"), work / (key + ".dxil")
        hlsl.write_bytes(source)
        cli_args = args[1:] if not args[0].startswith("-") else args
        subprocess.run([str(compiler), str(hlsl), *cli_args, "-Fo", str(output)], check=True)
        blob = output.read_bytes()
        encoded = b"M3DXIL01" + hashlib.sha256(blob).digest() + blob
        validate_blob(encoded)
        target.write_bytes(variant)
        target.with_suffix(".bin").write_bytes(encoded)
        print("Offline variant", profile, key, flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", type=pathlib.Path)
    parser.add_argument("--compiler", type=pathlib.Path, action="append", default=[])
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--all-feature-variants", action="store_true", help="Compile both ShaderF16 device-feature states with the pinned x64 DXC")
    args = parser.parse_args()
    manifest_path = OUTPUT / "manifest.json"
    if args.check:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if manifest["sources"] != sources():
            raise ValueError("Precompiled OpenPBR assets are stale; regenerate from the pinned backend")
        if set(manifest["entries"]) != {p.name for p in OUTPUT.glob("*.bin")}:
            raise ValueError("Unlisted or missing precompiled assets")
        for name, entry in manifest["entries"].items():
            blob = (OUTPUT / name).read_bytes()
            validate_blob(blob)
            if digest(blob) != entry["sha256"]:
                raise ValueError(f"Changed precompiled asset: {name}")
        coverage = Counter((e["architecture"], e["profile"][3:], e["shader_f16"]) for e in manifest["entries"].values())
        expected = {(arch, "6_" + str(model), f16): 9 for arch in ("x64", "x86", "arm64")
                    for model in range(9) for f16 in ([False] if model < 2 else [False, True])}
        if coverage != expected:
            raise ValueError("Incomplete model/device-feature/architecture coverage")
        print(f"Verified {len(manifest['entries'])} optional OpenPBR bytecode entries")
        return
    if args.capture is None or not args.compiler:
        parser.error("Generation requires --capture and pinned --compiler paths")
    captured_sources = json.loads((args.capture / "source-manifest.json").read_text(encoding="utf-8"))
    current_sources = {p.name: digest(p.read_bytes()) for p in (ROOT / "src/Mu3D.Rendering.OpenPbr/Shaders").iterdir() if p.suffix in (".wgsl", ".bin")}
    if captured_sources != current_sources:
        raise ValueError("Captures belong to a different embedded renderer build; rebuild and recapture")
    compilers = {p.parent.name: digest(p.read_bytes()) for p in args.compiler}
    if len(compilers) != len(args.compiler):
        raise ValueError("Compiler paths must have distinct architecture parent names")
    pins = json.loads((ROOT / "eng/dxc-assets.json").read_text(encoding="utf-8"))
    for architecture, compiler in compilers.items():
        if pins["files"].get(architecture + "/dxcompiler.dll") != compiler:
            raise ValueError("Compiler differs from the pinned release")
    if args.all_feature_variants:
        compiler = next((p.parent / "dxc.exe" for p in args.compiler if p.parent.name == "x64"), None)
        if compiler is None:
            raise ValueError("Offline f16 variants require the pinned x64 compiler")
        compile_feature_variants(args.capture, compiler)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    entries = {}
    profiles = set()
    for path in sorted(args.capture.glob("*.input")):
        data = path.read_bytes()
        if digest(data) != path.stem:
            raise ValueError(f"Capture key mismatch: {path}")
        compile_args, _ = decode(data)
        profile = compile_args[compile_args.index("-T") + 1]
        profiles.add(profile)
        if "-Zi" in compile_args or "-Od" in compile_args:
            raise ValueError("Do not package debug/unoptimized captures")
        blob = path.with_suffix(".bin").read_bytes()
        validate_blob(blob)
        for architecture, compiler in compilers.items():
            # DXIL describes a GPU program, not host machine code. All three pinned compiler
            # builds use the same source release. Names remain keyed to each actual DLL hash.
            variant = data[:len(MAGIC)] + bytes.fromhex(compiler) + data[len(MAGIC) + 32:]
            name = digest(variant) + ".bin"
            (OUTPUT / name).write_bytes(blob)
            entries[name] = {"sha256": digest(blob), "profile": profile, "architecture": architecture,
                             "shader_f16": "-enable-16bit-types" in compile_args}
    if not entries:
        raise ValueError("No captures found")
    if set(entries) != {p.name for p in OUTPUT.glob("*.bin")}:
        raise ValueError("Output contains older/unowned entries; review before removing them")
    manifest = {"schema": 1, "native": "v29.0.1.1", "hal": "29.0.3", "compiler_release": "1.8.2505.32",
                "compilers": compilers, "sources": sources(), "profiles": sorted(profiles), "entries": entries}
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Packaged {len(entries)} bytecode entries ({sum(p.stat().st_size for p in OUTPUT.glob('*.bin'))} bytes)")


if __name__ == "__main__":
    main()
