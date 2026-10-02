"""Append exact cache-only dependencies and license texts to the existing upstream report."""
import argparse
import hashlib
import html
import pathlib
import subprocess
import tomllib

ROOT = pathlib.Path(__file__).resolve().parent.parent
BEGIN, END = "<!-- MU3D CACHE LICENSES BEGIN -->", "<!-- MU3D CACHE LICENSES END -->"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-source", type=pathlib.Path, required=True)
    parser.add_argument("--registry", type=pathlib.Path, required=True)
    args = parser.parse_args()
    upstream = tomllib.loads(subprocess.check_output(["git", "-C", str(args.native_source), "show", "6aed50955d934ac36049ba8d002034841633ae02:Cargo.lock"]).decode())
    old = {(p["name"], p["version"]) for p in upstream["package"]}
    lock = (ROOT / "eng/native/WgpuShaderCache.Cargo.lock").read_bytes()
    packages = [p for p in tomllib.loads(lock.decode())["package"] if (p["name"], p["version"]) not in old]
    sections = [BEGIN, '<section id="mu3d-shader-cache"><h2>Mu3D Windows DXIL cache dependencies</h2>',
                '<p>Additional packages in the pinned cache Cargo.lock, SHA-256: ' + hashlib.sha256(lock).hexdigest() + '</p>']
    for package in packages:
        name = package["name"] + "-" + package["version"]
        source = args.registry / name
        meta = tomllib.loads((source / "Cargo.toml").read_text(encoding="utf-8"))["package"]
        files = sorted(source.glob("LICENSE*"))
        if not files or meta.get("license") not in ("MIT", "MIT OR Apache-2.0"):
            raise ValueError("Review missing or unexpected license: " + name)
        sections.append('<h3>' + html.escape(name) + '</h3><p>' + html.escape(meta["license"]) + '</p>')
        for path in files:
            sections.append('<details><summary>' + html.escape(path.name) + '</summary><pre>' + html.escape(path.read_text(encoding="utf-8")) + '</pre></details>')
    sections.extend(['</section>', END])
    report = ROOT / "src/Mu3D.Native.Wgpu.Runtime/licenses/wgpu-native-dependencies.html"
    text = report.read_bytes().decode("utf-8")
    if BEGIN in text:
        start, stop = text.index(BEGIN), text.index(END) + len(END)
        text = text[:start] + "\n".join(sections) + text[stop:]
    else:
        text = text.replace('</body>', "\n".join(sections) + '\n</body>')
    report.write_bytes(text.encode("utf-8"))
    print(f"Recorded full license texts for {len(packages)} added dependencies")


if __name__ == "__main__":
    main()
