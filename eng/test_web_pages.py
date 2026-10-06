"""Pages integrity and portable MSBuild print cleanup checks, not target-OS builds."""
import argparse
import base64
from contextlib import redirect_stdout
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]

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

    def add_print_presets(self, publication):
        profiles = {"GRACoL2013_CRPC6.icc": b"first default ICC",
                    "SWOP2013C3_CRPC5.icc": b"second default ICC"}
        files = {**profiles, "NOTICE.md": b"Default profile redistribution notice\n",
                 "catalog.json": json.dumps({"profiles": [
                     {"fileName": name, "sha256": hashlib.sha256(data).hexdigest()}
                     for name, data in profiles.items()]}).encode()}
        source = self.root / "samples/Mu3D.Gallery/Resources/PrintPresets"
        published = publication / "wwwroot/assets/PrintPresets"
        for folder in (source, published):
            folder.mkdir(parents=True, exist_ok=True)
            for name, data in files.items():
                (folder / name).write_bytes(data)
        component = publication / "wwwroot/GallerySource/CmykPrintingExample.cs"
        component.parent.mkdir(parents=True, exist_ok=True)
        component.write_text("// Printing-enabled Gallery source fixture\n")
        return files

    def test_staging_keeps_every_boot_import_and_prunes_only_obsolete_files(self):
        for publication in (self.args.aot, self.args.interpreted):
            defaults = self.add_print_presets(publication)
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
            staged_presets = site / mode / "assets/PrintPresets"
            self.assertEqual({path.name for path in staged_presets.iterdir()}, set(defaults))
            for name, data in defaults.items():
                self.assertEqual((staged_presets / name).read_bytes(), data)
                self.assertEqual((publication / "wwwroot/assets/PrintPresets" / name).read_bytes(), data)
            self.assertEqual((site / mode / "assets/model.glb").read_bytes(), b"original Gallery asset")
        report = json.loads((site / "gallery-deployment.json").read_text())
        self.assertTrue(all(item["bootResources"] == 3 and item["hashedResources"] == 1
                            for item in report["galleries"]))

    def test_missing_default_print_assets_reject_publication_without_injection(self):
        for publication in (self.args.aot, self.args.interpreted):
            for name in ("catalog.json", "NOTICE.md", "GRACoL2013_CRPC6.icc", "SWOP2013C3_CRPC5.icc"):
                with self.subTest(mode=publication.name, missing=name):
                    for mode in (self.args.aot, self.args.interpreted):
                        self.add_print_presets(mode)
                    missing = publication / "wwwroot/assets/PrintPresets" / name
                    missing.unlink()
                    with self.assertRaisesRegex(ValueError, "Missing default print preset"):
                        pages.stage(self.args)
                    self.assertFalse(missing.exists())

    def test_changed_default_print_assets_reject_publication(self):
        for publication in (self.args.aot, self.args.interpreted):
            for name in ("catalog.json", "NOTICE.md", "GRACoL2013_CRPC6.icc", "SWOP2013C3_CRPC5.icc"):
                with self.subTest(mode=publication.name, changed=name):
                    for mode in (self.args.aot, self.args.interpreted):
                        self.add_print_presets(mode)
                    (publication / "wwwroot/assets/PrintPresets" / name).write_bytes(b"changed")
                    with self.assertRaisesRegex(ValueError, "Default print preset differs"):
                        pages.stage(self.args)

    def test_printing_disabled_rejects_default_print_assets(self):
        for publication in (self.args.aot, self.args.interpreted):
            with self.subTest(mode=publication.name):
                self.add_print_presets(publication)
                (publication / "wwwroot/GallerySource/CmykPrintingExample.cs").unlink()
                with self.assertRaisesRegex(ValueError, "Printing-disabled Gallery"):
                    pages.verify_gallery(publication / "wwwroot")

    def test_printing_disabled_stages_without_default_print_assets(self):
        with redirect_stdout(io.StringIO()):
            site = pages.stage(self.args)
        for mode in ("gallery", "gallery-interpreted"):
            self.assertFalse((site / mode / "assets/PrintPresets").exists())

    def run_isolated_target(self, project, target_name, properties):
        dotnet = REPO / "artifacts/toolchain/dotnet/dotnet"
        if not dotnet.is_file():
            dotnet = shutil.which("dotnet")
        if not dotnet:
            self.skipTest("A .NET SDK is required to execute the isolated publish cleanup target")
        path = self.root / "print-cleanup.proj"
        ET.ElementTree(project).write(path, encoding="utf-8", xml_declaration=True)
        # Execute actual cleanup targets without importing application SDKs,
        # restoring, compiling or publishing.
        result = subprocess.run([str(dotnet), "msbuild", str(path), "-nologo", "-v:q", "-nr:false",
                                 f"-t:{target_name}", *[f"-p:{key}={value}" for key, value in properties.items()]],
                                cwd=self.root, text=True, capture_output=True,
                                timeout=30, env={**os.environ, "DOTNET_CLI_USE_MSBUILD_SERVER": "0"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def run_print_cleanup(self, publish_dir, printing):
        project = ET.Element("Project")
        actual = ET.parse(REPO / "samples/Mu3D.Gallery/Web/Mu3D.GalleryApp.Web.csproj")
        target = actual.find("./Target[@Name='RemoveDisabledGalleryPrintAssets']")
        self.assertIsNotNone(target)
        self.assertEqual(target.get("AfterTargets"), "Publish")
        project.append(copy.deepcopy(target))
        self.run_isolated_target(project, "RemoveDisabledGalleryPrintAssets",
                                 {"EnableMu3DPrinting": printing, "PublishDir": publish_dir})

    def test_disabled_publish_cleans_only_old_print_assets_in_reused_output(self):
        for publication in (self.args.aot, self.args.interpreted):
            with self.subTest(mode=publication.name):
                self.add_print_presets(publication)
                web = publication / "wwwroot"
                optional = web / "assets/PrintProfiles/local.icc"
                optional.parent.mkdir(); optional.write_bytes(b"optional ICC")
                for name in ("CmykPrintingPage.xaml", "CmykPrintingProfiles.cs"):
                    (web / "GallerySource" / name).write_bytes(b"old print source")
                retained = web / "GallerySource/ColorRampsPage.xaml"
                retained.write_bytes(b"unrelated source")
                original = {file.relative_to(web): file.read_bytes() for file in web.rglob("*") if file.is_file()}
                publish_dir = str(publication) if publication == self.args.aot else publication.name + "/"
                self.run_print_cleanup(publish_dir, "false")
                self.assertFalse((web / "assets/PrintPresets").exists())
                self.assertFalse(optional.parent.exists())
                self.assertEqual(list((web / "GallerySource").glob("CmykPrinting*")), [])
                for relative, content in original.items():
                    if relative.parts[:2] in (("assets", "PrintPresets"), ("assets", "PrintProfiles")) or relative.name.startswith("CmykPrinting"):
                        continue
                    self.assertEqual((web / relative).read_bytes(), content)
                self.assertTrue((self.root / "samples/Mu3D.Gallery/Resources/PrintPresets/catalog.json").is_file())
                pages.verify_gallery(web)
        with redirect_stdout(io.StringIO()):
            site = pages.stage(self.args)
        self.assertFalse((site / "gallery/assets/PrintPresets").exists())
        self.assertFalse((site / "gallery-interpreted/assets/PrintPresets").exists())

    def test_enabled_publish_preserves_existing_print_assets(self):
        for publication in (self.args.aot, self.args.interpreted):
            with self.subTest(mode=publication.name):
                self.add_print_presets(publication)
                web = publication / "wwwroot"
                optional = web / "assets/PrintProfiles/local.icc"
                optional.parent.mkdir(); optional.write_bytes(b"optional ICC")
                original = {file.relative_to(web): file.read_bytes() for file in web.rglob("*") if file.is_file()}
                self.run_print_cleanup(publication, "true")
                self.assertEqual({file.relative_to(web): file.read_bytes() for file in web.rglob("*") if file.is_file()}, original)

    def add_native_print_assets(self, resources):
        for folder in ("PrintPresets", "PrintProfiles", "Textures"):
            asset = resources / folder / "retained.bin"
            asset.parent.mkdir(parents=True); asset.write_bytes(folder.encode())
        source = resources / "GallerySource"
        source.mkdir()
        for name in ("CmykPrintingPage.xaml", "CmykPrintingPage.xaml.cs", "CmykPrintingExample.cs",
                     "CmykPrintingProfiles.cs", "ColorRampsPage.xaml"):
            (source / name).write_bytes(name.encode())
        (resources / "model.glb").write_bytes(b"unrelated resource")
        return {file.relative_to(resources): file.read_bytes() for file in resources.rglob("*") if file.is_file()}

    def expected_native_print_assets(self, original, printing):
        if printing == "true":
            return original
        return {name: data for name, data in original.items()
                if name.parts[0] not in ("PrintPresets", "PrintProfiles") and
                not (name.parts[0] == "GallerySource" and name.name.startswith("CmykPrinting"))}

    def add_native_copy_guard(self, project, target_name, resources, marker):
        copy_resources = ET.SubElement(project, "Target", Name=target_name)
        group = ET.SubElement(copy_resources, "ItemGroup")
        ET.SubElement(group, "_TestOldPrintSources", Include=str(resources / "GallerySource/CmykPrinting*"))
        ET.SubElement(copy_resources, "Error",
                      Condition="'$(EnableMu3DPrinting)' != 'true' And "
                                "(Exists('$(TestAppResourcesPath)PrintPresets') Or "
                                "Exists('$(TestAppResourcesPath)PrintProfiles') Or '@(_TestOldPrintSources)' != '')",
                      Text="Old print assets reached the native resource copy target")
        ET.SubElement(copy_resources, "WriteLinesToFile", File=str(marker),
                      Lines="copy target reached", Overwrite="true")

    def test_apple_bundle_cleanup_runs_before_copy_with_late_resource_path(self):
        actual = ET.parse(REPO / "samples/Mu3D.Gallery/Mu3D.Gallery.csproj")
        target = actual.find("./Target[@Name='RemoveDisabledApplePrintAssets']")
        self.assertIsNotNone(target)
        self.assertEqual(target.get("BeforeTargets"), "_CopyResourcesToBundle")
        self.assertEqual(target.get("DependsOnTargets"), "_GenerateBundleName")
        for framework in ("net10.0-ios", "net10.0-maccatalyst"):
            for printing in ("false", "true"):
                with self.subTest(framework=framework, printing=printing):
                    bundle_path = "Gallery.app/Contents/Resources" if framework.endswith("maccatalyst") else "Gallery.app"
                    resources = self.root / framework / printing / bundle_path
                    original = self.add_native_print_assets(resources)
                    project = ET.Element("Project")
                    # The bundle path is unavailable at evaluation time. Only the
                    # cleanup target's dependency creates it before RemoveDir runs.
                    generate = ET.SubElement(project, "Target", Name="_GenerateBundleName")
                    group = ET.SubElement(generate, "PropertyGroup")
                    ET.SubElement(group, "_AppResourcesPath").text = "$(TestAppResourcesPath)"
                    project.append(copy.deepcopy(target))
                    marker = resources.parent / "copy-target.txt"
                    self.add_native_copy_guard(project, "_CopyResourcesToBundle", resources, marker)
                    self.run_isolated_target(project, "_CopyResourcesToBundle",
                                             {"TargetFramework": framework, "EnableMu3DPrinting": printing,
                                              "TestAppResourcesPath": str(resources) + "/"})
                    self.assertEqual(marker.read_text().strip(), "copy target reached")
                    self.assertEqual({file.relative_to(resources): file.read_bytes()
                                      for file in resources.rglob("*") if file.is_file()},
                                     self.expected_native_print_assets(original, printing))

    def test_windows_publish_cleanup_before_copy_is_portable_msbuild_only(self):
        # This runs only file tasks in an isolated project on the current host.
        # It does not load Windows/MAUI SDKs or establish a Windows build result.
        actual = ET.parse(REPO / "samples/Mu3D.Gallery/Mu3D.Gallery.csproj")
        target = actual.find("./Target[@Name='RemoveDisabledWindowsPrintAssets']")
        self.assertIsNotNone(target)
        self.assertEqual(target.get("BeforeTargets"), "CopyFilesToPublishDirectory")
        for relative in (False, True):
            for printing in ("false", "true"):
                with self.subTest(relative=relative, printing=printing):
                    resources = self.root / ("Relative" if relative else "Absolute") / printing / "Publish"
                    original = self.add_native_print_assets(resources)
                    publish_dir = resources.relative_to(self.root) if relative else resources
                    project = ET.Element("Project")
                    project.append(copy.deepcopy(target))
                    marker = resources.parent / "copy-target.txt"
                    self.add_native_copy_guard(project, "CopyFilesToPublishDirectory", resources, marker)
                    self.run_isolated_target(project, "CopyFilesToPublishDirectory",
                                             {"TargetFramework": "net10.0-windows10.0.19041.0",
                                              "EnableMu3DPrinting": printing, "PublishDir": publish_dir,
                                              "TestAppResourcesPath": str(resources) + "/"})
                    self.assertEqual(marker.read_text().strip(), "copy target reached")
                    self.assertEqual({file.relative_to(resources): file.read_bytes()
                                      for file in resources.rglob("*") if file.is_file()},
                                     self.expected_native_print_assets(original, printing))

    def test_undeclared_source_print_asset_rejects_publication(self):
        for publication in (self.args.aot, self.args.interpreted):
            self.add_print_presets(publication)
            (publication / "wwwroot/assets/PrintPresets/unlisted.icc").write_bytes(b"undeclared ICC")
        source = self.root / "samples/Mu3D.Gallery/Resources/PrintPresets/unlisted.icc"
        source.write_bytes(b"undeclared ICC")
        with self.assertRaisesRegex(ValueError, "Unexpected sample print preset asset: unlisted.icc"):
            pages.stage(self.args)

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
