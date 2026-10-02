"""Optional independent check; requires the official MaterialX==1.39.4 Python package.

Run the managed test executable first to create bin/Release/net10.0/ExportedOpenPbr.mtlx.
This developer check loads only local trusted reference libraries and test output.
It is not a production dependency or an importer implementation.
"""

from pathlib import Path
import sys

import MaterialX as mx


if mx.__version__ != "1.39.4":
    raise SystemExit(f"Expected MaterialX 1.39.4, found {mx.__version__}")

test_directory = Path(__file__).resolve().parent
library = mx.createDocument()
mx.loadLibraries(mx.getDefaultDataLibraryFolders(), mx.getDefaultDataSearchPath(), library)
for name in ("ND_open_pbr_surface_surfaceshader", "NG_open_pbr_surface_surfaceshader"):
    if library.getChild(name):
        library.removeChild(name)

pinned = mx.createDocument()
mx.readFromXmlFile(pinned, str(test_directory / "Fixtures/open_pbr_surface_1_1_1.mtlx"))
library.importLibrary(pinned)
document = mx.createDocument()
export_path = Path(sys.argv[1]) if len(sys.argv) > 1 else test_directory / "bin/Release/net10.0/ExportedOpenPbr.mtlx"
mx.readFromXmlFile(document, str(export_path))
document.importLibrary(library)
valid, message = document.validate()
surface = document.getNode("FullSurface")
definition = surface.getNodeDef()
if not valid or definition is None or definition.getVersionString() != "1.1.1":
    raise SystemExit(f"Upstream validation failed: {message}")
if len(surface.getInputs()) != 41:
    raise SystemExit("Expected all 41 authored inputs in the exported test document")
print("MaterialX 1.39.4 validation passed: OpenPBR 1.1.1 nodedef, all 41 exported inputs.")

# Independently resolve every generated graph node against the official 1.39.4 stdlib.
graph_document = mx.createDocument()
mx.readFromXmlFile(graph_document, str(test_directory / "bin/Release/net10.0/ExportedGraph.mtlx"))
graph_document.importLibrary(library)
valid, message = graph_document.validate()
if not valid:
    raise SystemExit(f"Upstream graph validation failed: {message}")
for graph in graph_document.getNodeGraphs():
    if graph.getSourceUri():
        continue
    for node in graph.getNodes():
        if node.getNodeDef() is None:
            raise SystemExit(f"No official nodedef resolves {node.getName()}")
print("MaterialX 1.39.4 graph validation passed: node definitions, connections and raw channel conversion.")
