#!/usr/bin/env python3
"""Serve the published Web Gallery and WASM probes on loopback."""
import argparse
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

repo = Path(__file__).resolve().parent.parent
published = repo / "artifacts/web-wasm"
runner = repo / "tests/Mu3D.Web.Validation/wwwroot"
suites = ("Gallery", "GalleryAot", "Render", "Smoke", "Creative", "Graphics", "HostContracts", "HostContractsAot", "SmokeAot", "WgpuAbi", "Emdawn", "RenderPerf", "RenderAot")
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--port", type=int, default=8765)
args = parser.parse_args()
def is_published(suite):
    return (published / suite / "wwwroot/_framework/dotnet.js").is_file()


if not any(is_published(suite) for suite in suites):
    parser.error("Publish a Web validation suite before starting the server (see its README).")


class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map,
                      ".mjs": "text/javascript", ".wasm": "application/wasm"}

    def send_head(self):
        path = urlsplit(self.path).path
        if path == "/" or path.strip("/") in suites and not path.endswith("/"):
            if path == "/":
                default = next(suite for suite in suites if is_published(suite))
                location = f"/{default}/"
            else:
                location = path + "/"
                if urlsplit(self.path).query:
                    location += "?" + urlsplit(self.path).query
            self.send_response(302)
            self.send_header("Location", location)
            self.end_headers()
            return None
        return super().send_head()

    def translate_path(self, path):
        parts = unquote(urlsplit(path).path).strip("/").split("/", 1)
        suite = parts[0]
        if suite not in suites:
            return str(published / ".missing-validation-route")
        base = (published / suite / "wwwroot").resolve()
        if not (base / "_framework/dotnet.js").is_file():
            return str(published / ".missing-validation-build")
        relative = parts[1] if len(parts) == 2 else ""
        if suite == "Render" and relative in {"hdr-diagnostic.html", "hdr-diagnostic.mjs"}:
            return str(runner / relative)
        if suite not in {"RenderPerf", "RenderAot", "Gallery", "GalleryAot"} and relative in {"", "index.html", "main.mjs", "render-host.mjs", "viewer-host.mjs"}:
            return str(runner / (relative or "index.html"))
        candidate = (base / relative).resolve()
        if not candidate.is_relative_to(base):
            return str(published / ".missing-validation-route")
        if suite in {"Gallery", "GalleryAot"} and not candidate.is_file() and "." not in Path(relative).name:
            return str(base / "index.html")
        return str(candidate)

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


print(f"Mu3D browser validation: http://127.0.0.1:{args.port}/", flush=True)
try:
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
except KeyboardInterrupt:
    pass
