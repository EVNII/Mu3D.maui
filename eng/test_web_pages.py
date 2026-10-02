"""Regression checks for the Pages publisher's .NET boot dependency boundary."""
import argparse
import base64
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("pages", Path(__file__).with_name("publish-web-pages.py"))
pages = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pages)


class BootDependencyChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        root_patch = patch.object(pages, "ROOT", self.root)
        root_patch.start()
        self.addCleanup(root_patch.stop)
        asset = self.root / "samples/Mu3D.Gallery/Resources/Raw/model.glb"
        asset.parent.mkdir(parents=True)
        asset.write_bytes(b"original Gallery asset")
        self.args = argparse.Namespace(aot=self.publication("Aot"), interpreted=self.publication("Interpreted"),
                                       docs=None, output=self.root / "artifacts/site", base_path="/Mu3D.maui/")

    def publication(self, mode):
        publish = self.root / mode
        web = publish / "wwwroot"
        framework = web / "_framework"
        framework.mkdir(parents=True)
        wasm = b"\0asm\1\0\0\0"
        (framework / "dotnet.native.current.wasm").write_bytes(wasm)
        # JS imports intentionally have no hash, matching the actual .NET 10 output.
        boot = {"mainAssemblyName": "Mu3D.GalleryApp.Web", "resources": {
            "jsModuleNative": [{"name": "dotnet.native.current.js"}],
            "jsModuleRuntime": [{"name": "dotnet.runtime.current.js"}],
            "wasmNative": [{"name": "dotnet.native.current.wasm", "hash": "sha256-" +
                            base64.b64encode(hashlib.sha256(wasm).digest()).decode()}]}}
        (framework / "dotnet.js").write_text("/*json-start*/" + json.dumps(boot) + "/*json-end*/")
        for name in ("dotnet.native.current.js", "dotnet.runtime.current.js", "blazor.webassembly.js",
                     "dotnet.native.obsolete.js", "dotnet.native.obsolete.wasm"):
            (framework / name).write_text("export const fixture = true;")
        (web / "index.html").write_text('<base href="/">mu3d-route')
        (web / "assets").mkdir()
        (web / "assets/model.glb").write_bytes(b"original Gallery asset")
        return publish

    def test_staging_keeps_every_boot_import_and_prunes_only_obsolete_files(self):
        for publication in (self.args.aot, self.args.interpreted):
            profile = publication / "wwwroot/assets/PrintProfiles/local.icc"
            profile.parent.mkdir()
            profile.write_bytes(b"supplied local profile")
        with redirect_stdout(io.StringIO()):
            site = pages.stage(self.args)
        for mode, publication in (("gallery", self.args.aot), ("gallery-interpreted", self.args.interpreted)):
            framework = site / mode / "_framework"
            self.assertEqual({file.name for file in framework.iterdir()}, {
                "dotnet.js", "blazor.webassembly.js", "dotnet.native.current.js",
                "dotnet.runtime.current.js", "dotnet.native.current.wasm"})
            self.assertEqual((framework / "dotnet.runtime.current.js").read_bytes(),
                             (self.args.aot / "wwwroot/_framework/dotnet.runtime.current.js").read_bytes())
            self.assertEqual((publication / "wwwroot/assets/PrintProfiles/local.icc").read_bytes(),
                             b"supplied local profile")
            self.assertFalse((site / mode / "assets/PrintProfiles").exists())
            self.assertEqual((site / mode / "assets/model.glb").read_bytes(), b"original Gallery asset")
        report = json.loads((site / "gallery-deployment.json").read_text())
        self.assertTrue(all(item["bootResources"] == 3 and item["hashedResources"] == 1
                            for item in report["galleries"]))

    def test_missing_unhashed_runtime_import_rejects_publication(self):
        web = self.args.aot / "wwwroot"
        (web / "_framework/dotnet.runtime.current.js").unlink()
        with self.assertRaisesRegex(ValueError, "Missing boot resource: dotnet.runtime.current.js"):
            pages.verify_gallery(web)

    def test_hashed_runtime_integrity_still_rejects_changed_bytes(self):
        web = self.args.aot / "wwwroot"
        (web / "_framework/dotnet.native.current.wasm").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "Boot resource mismatch"):
            pages.verify_gallery(web)

    def test_missing_blazor_loader_rejects_publication(self):
        web = self.args.aot / "wwwroot"
        (web / "_framework/blazor.webassembly.js").unlink()
        with self.assertRaisesRegex(ValueError, "Missing Gallery loader: blazor.webassembly.js"):
            pages.verify_gallery(web)


if __name__ == "__main__":
    unittest.main()
