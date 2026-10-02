"""Check transitive project boundaries and optional package contents without launching UI."""
import argparse
import pathlib
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
OPTIONAL = {"Mu3D.Rendering.OpenPbr", "Mu3D.Formats.MaterialX"}


def closure(path, seen=None):
    seen = set() if seen is None else seen
    path = path.resolve()
    if path in seen:
        return seen
    seen.add(path)
    for item in ET.parse(path).iter("ProjectReference"):
        closure(path.parent / item.attrib["Include"], seen)
    return seen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=pathlib.Path)
    args = parser.parse_args()
    for name in ["Mu3D.Maui", "Mu3D.Maui.Toolkit", "Mu3D.Core", "Mu3D.Graphics", "Mu3D.Native.Wgpu"]:
        dependencies = closure(ROOT / "src" / name / (name + ".csproj"))
        if any(path.stem in OPTIONAL for path in dependencies):
            raise RuntimeError(f"{name} transitively requires optional OpenPBR/MaterialX")
        for path in dependencies:
            for item in ET.parse(path).getroot().iter():
                value = item.attrib.get("Include", "")
                if "Rendering.OpenPbr" in value or "openpbr/precompiled" in value.lower():
                    raise RuntimeError(f"Optional payload leaked into {path}: {value}")
    if args.package:
        with zipfile.ZipFile(args.package) as package:
            names = package.namelist()
            if not any(pathlib.PurePosixPath(n).parent.as_posix() == "precompiled/windows" and n.endswith(".bin") for n in names):
                raise RuntimeError("Optional package is missing precompiled assets")
            if "buildTransitive/Mu3D.Rendering.OpenPbr.targets" not in names:
                raise RuntimeError("Optional package is missing asset deployment target")
            spec = ET.fromstring(package.read(next(n for n in names if n.endswith(".nuspec"))))
            deps = [e.attrib.get("id", "") for e in spec.iter() if e.tag.endswith("dependency")]
            if "Mu3D.Native.Wgpu" in deps or "Mu3D.Formats.MaterialX" in deps:
                raise RuntimeError("Optional renderer acquired a backend or file-import dependency")
    print("Verified: base Mu3D has no transitive OpenPBR renderer, MaterialX or shader payload dependency")


if __name__ == "__main__":
    main()
