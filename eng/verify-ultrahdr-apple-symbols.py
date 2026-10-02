#!/usr/bin/env python3
"""Verify Apple symbol roots against managed imports, and optionally a linked Mach-O."""
import argparse
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--binary", type=pathlib.Path, help="Final linked Apple executable to inspect with nm and dyld_info")
args = parser.parse_args()
roots_path = ROOT / "src/Mu3D.Native.UltraHdr/buildTransitive/Mu3D.Native.UltraHdr.Symbols.props"
roots = ET.parse(roots_path).getroot()
required = set()
for name, item in (("UltraHdrNative", "_Mu3DUltraHdrNativeSymbol"), ("TurboJpegNative", "_Mu3DTurboJpegNativeSymbol")):
    source = (ROOT / f"src/Mu3D.Native.UltraHdr/Interop/{name}.cs").read_text()
    imports = set(re.findall(r"\[LibraryImport\(LibraryName\)\]\s+internal static partial \S+ (\w+)\(", source))
    # Fail closed if a new import uses a declaration this guard does not understand.
    if not imports or len(imports) != source.count("[LibraryImport("):
        raise SystemExit(f"Unrecognized import declaration in {name}; update this guard")
    declared = {s for node in roots.iter(item) for s in node.attrib["Include"].split(";") if s}
    if imports != declared:
        raise SystemExit(f"{name}: unrooted={sorted(imports-declared)}, stale={sorted(declared-imports)}")
    required |= imports
if args.binary:
    result = subprocess.run(["nm", "-gUj", str(args.binary)], check=True, capture_output=True, text=True)
    defined = {s.strip().removeprefix("_") for s in result.stdout.splitlines()}
    if missing := required - defined:
        raise SystemExit(f"Missing linked native exports: {', '.join(sorted(missing))}")
    exports = subprocess.run(["xcrun", "dyld_info", "-exports", str(args.binary)], check=True, capture_output=True, text=True)
    dynamic = {s.removeprefix("_") for s in re.findall(r"\s(_\w+)(?:\s|$)", exports.stdout)}
    if missing := required - dynamic:
        raise SystemExit(f"Missing dynamic lookup exports: {', '.join(sorted(missing))}")
print(f"Verified {len(required)} managed imports and Apple roots" + (" in final executable." if args.binary else "."))
