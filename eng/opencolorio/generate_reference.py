#!/usr/bin/env python3
"""Generate direct-C++ OCIO goldens, independently of the Mu3D C wrapper and .NET marshalling."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import subprocess

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--oracle", required=True, type=Path)
parser.add_argument("--check", action="store_true")
args = parser.parse_args()
fixture = ROOT / "tests/Mu3D.OpenColorIO.Tests/reference.ocio"
output = fixture.with_name("oracle.json")
manifest = json.loads((ROOT / "eng/opencolorio/manifest.json").read_text())
random.seed(2512)
inputs = [[0, 0, 0], [.18, .18, .18], [1, 1, 1], [-.2, .25, 5], [16, 2, .01]]
inputs += [[random.uniform(-.1, 8) for _ in range(3)] for _ in range(27)]
builtin = "cg-config-v4.0.0_aces-v2.0_ocio-v2.5"
definitions = [
    ("matrix", "fixture", ["Linear", "Scaled"]),
    ("encoding", "fixture", ["Linear", "Encoded"]),
    ("aces-ap1-ap0", builtin, ["ACEScg", "ACES2065-1"]),
    ("aces2-display", builtin, ["ACEScg", "sRGB - Display", "ACES 2.0 - SDR 100 nits (Rec.709)"]),
]
cases = []
for name, config, parameters in definitions:
    path = str(fixture) if config == "fixture" else "ocio://" + config
    raw = subprocess.run([str(args.oracle.resolve()), path, *parameters],
        input="".join(" ".join(map(str, value)) + "\n" for value in inputs),
        text=True, capture_output=True, check=True).stdout
    values = [[float(value) for value in line.split()] for line in raw.splitlines()]
    if len(values) != len(inputs): raise RuntimeError("Unexpected C++ oracle result length.")
    cases.append({"name": name, "configuration": config, "parameters": parameters, "outputs": values})
result = {"openColorIO": manifest["openColorIO"], "fixtureSha256": hashlib.sha256(fixture.read_bytes()).hexdigest(),
    "optimization": "OPTIMIZATION_LOSSLESS", "absoluteTolerance": 2e-6, "relativeTolerance": 2e-6,
    "inputs": inputs, "cases": cases}
text = json.dumps(result, indent=2, allow_nan=False) + "\n"
if args.check:
    if output.read_text() != text: raise RuntimeError("C++ OpenColorIO oracle fixture is stale.")
else: output.write_text(text)
print(f"Verified {len(inputs)*len(cases)*3} independent native reference components.")
