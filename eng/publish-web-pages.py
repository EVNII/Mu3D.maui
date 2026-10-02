#!/usr/bin/env python3
"""Stage reviewed Gallery/DocFX output; --deploy publishes it from an isolated gh-pages worktree."""
import argparse
import base64
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]


def run(command, **kwargs):
    return subprocess.run(command, check=True, text=True, **kwargs)


def git(*args, cwd=ROOT):
    return run(["git", *args], cwd=cwd, stdout=subprocess.PIPE).stdout.strip()


def verify_tree(root):
    total = 0
    for path in root.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"Published content cannot contain symlinks: {path}")
        if path.is_file() and path.stat().st_size >= 100 * 1024 * 1024:
            raise ValueError(f"File exceeds the GitHub Git payload limit: {path}")
        if path.is_file():
            total += path.stat().st_size
    if total > 1024 ** 3:
        raise ValueError("The static site exceeds GitHub Pages' 1 GiB limit")
    return total


def verify_gallery(web):
    verify_tree(web)
    script = (web / "_framework/dotnet.js").read_text()
    match = re.search(r"/\*json-start\*/(.*?)/\*json-end\*/", script, re.S)
    if not match:
        raise ValueError("Missing .NET 10 inline boot configuration")
    boot = json.loads(match.group(1))
    if boot["mainAssemblyName"] != "Mu3D.GalleryApp.Web":
        raise ValueError("Only the actual Gallery publication can be staged")
    resources = []

    def walk(value):
        if isinstance(value, dict):
            if "name" in value and "hash" in value:
                resources.append(value)
            for child in value.values():
                walk(child)
        elif isinstance(value, list):
            for child in value:
                walk(child)

    walk(boot["resources"])
    for item in resources:
        file = (web / "_framework" / item["name"]).resolve()
        if not file.is_relative_to(web.resolve()):
            raise ValueError("Boot resource is outside its publication")
        actual = "sha256-" + base64.b64encode(hashlib.sha256(file.read_bytes()).digest()).decode()
        if actual != item["hash"]:
            raise ValueError(f"Boot resource mismatch: {item['name']}")
    raw = ROOT / "samples/GalleryApp/Resources/Raw"
    for original in raw.rglob("*"):
        if original.is_file() and original.read_bytes() != (web / "assets" / original.relative_to(raw)).read_bytes():
            raise ValueError(f"Gallery asset differs from the actual sample: {original.name}")
    return {item["name"] for item in resources}


def stage(args):
    site = args.output.resolve()
    if site == ROOT / "artifacts" or not site.is_relative_to(ROOT / "artifacts"):
        raise ValueError("Output must be a dedicated directory below this checkout's artifacts")
    if not re.fullmatch(r"/(?:[A-Za-z0-9._-]+/)*", args.base_path) or any(
            part in (".", "..") for part in args.base_path.split("/")):
        raise ValueError("Base path must begin and end with / and contain only path segments")
    inputs = [("gallery", args.aot), ("gallery-interpreted", args.interpreted)]
    for _, path in inputs:
        if path and (site == path.resolve() or site.is_relative_to(path.resolve()) or path.resolve().is_relative_to(site)):
            raise ValueError("Staging must not overlap publication inputs")
    if args.docs:
        docs = args.docs.resolve()
        if site == docs or site.is_relative_to(docs) or docs.is_relative_to(site):
            raise ValueError("Staging must not overlap public documentation inputs")
        if docs != ROOT / "artifacts/docs/_site" or not (docs / "v0.1/index.html").is_file():
            raise ValueError("Docs must be the validated public DocFX output")
        verify_tree(docs)
    if site.exists():
        shutil.rmtree(site)
    site.mkdir(parents=True)
    if args.docs:
        shutil.copytree(args.docs, site, dirs_exist_ok=True)
    bases = []
    reports = []
    for name, publication in inputs:
        if publication is None:
            continue
        web = publication.resolve() / "wwwroot"
        resources = verify_gallery(web)
        destination = site / name
        shutil.copytree(web, destination)
        # Incremental publishes retain obsolete fingerprinted files. The validated
        # boot configuration selects this release; prune only the staged copy.
        framework = destination / "_framework"
        retained = resources | {"dotnet.js", "blazor.webassembly.js"}
        for file in framework.iterdir():
            if file.is_file() and file.name not in retained:
                file.unlink()
        base = args.base_path + name + "/"
        host = destination / "index.html"
        content = host.read_text()
        if content.count('<base href="/">') != 1 or "mu3d-route" not in content:
            raise ValueError("Publish the current Gallery host with its static-route bootstrap first")
        host.write_text(content.replace('<base href="/">', f'<base href="{html.escape(base, quote=True)}">'))
        bases.append(base)
        reports.append({"path": base, "hashedResources": len(resources)})
    if not bases:
        raise ValueError("Specify at least one complete Gallery publication")
    # The root 404 document is the one GitHub Pages uses for unmatched deep routes.
    (site / "404.html").write_text('''<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>Mu3D — Page not found</title><script>
const bases = ''' + json.dumps(bases) + ''';
const base = bases.find(value => location.pathname.startsWith(value));
if (base) location.replace(base + '?mu3d-route=' + encodeURIComponent(location.pathname + location.search + location.hash));
</script></head><body><h1>Page not found</h1><a href="''' + html.escape(args.base_path) + '''">Mu3D Gallery</a></body></html>''')
    links = ''.join(f'<li><a href="{html.escape(base)}">Gallery {"(AOT)" if i == 0 and args.aot else "(interpreted)"}</a></li>' for i, base in enumerate(bases))
    links += '<li><a href="v0.1/">Documentation</a></li>'
    (site / "index.html").write_text('<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Mu3D Gallery</title></head><body><h1>Mu3D Gallery</h1><ul>' + links + '</ul></body></html>')
    (site / ".nojekyll").touch()
    (site / "gallery-deployment.json").write_text(json.dumps({"galleries": reports}, indent=2) + "\n")
    total = verify_tree(site)
    print(json.dumps({"site": str(site), "bytes": total, "galleries": reports}, indent=2), flush=True)
    return site


def deploy(args, site):
    endpoint = f"repos/{args.repository}/pages"
    result = subprocess.run(["gh", "api", endpoint], text=True, capture_output=True)
    if result.returncode:
        if '"status":"404"' not in result.stdout and '"status": "404"' not in result.stdout:
            raise RuntimeError(result.stderr or result.stdout)
        if not args.enable_pages:
            raise ValueError("Pages is not configured; run --enable-pages once with an administrator's gh login")
        existing = None
    else:
        existing = json.loads(result.stdout)
        if existing.get("build_type", "legacy") != "legacy" or existing["source"] != {"branch": "gh-pages", "path": "/"}:
            raise ValueError("Existing Pages source is different; preserve it and review its configuration first")
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9._-]*", args.remote):
        raise ValueError("Select an existing named Git remote")
    for option in ((), ("--push",)):
        urls = git("remote", "get-url", *option, "--all", args.remote).splitlines()
        repository = re.fullmatch(r"(?:git@github.com:|https://github.com/)([^/]+/[^/]+?)(?:\.git)?", urls[0]) if len(urls) == 1 else None
        if not repository or repository.group(1).lower() != args.repository.lower():
            raise ValueError("Deployment repository must match the sole fetch and push URLs of the selected remote")
    branch = git("ls-remote", "--heads", args.remote, "gh-pages")
    temporary_branch = "codex/pages-" + uuid.uuid4().hex
    with tempfile.TemporaryDirectory(prefix="mu3d-pages-") as directory:
        checkout = Path(directory) / "site"
        if branch:
            git("fetch", "--no-tags", args.remote, "gh-pages")
            git("worktree", "add", "--detach", str(checkout), "FETCH_HEAD")
        else:
            git("worktree", "add", "--orphan", "-b", temporary_branch, str(checkout))
        try:
            # Replace only the two owned Gallery trees; retain existing docs, versions and CNAME.
            for name in ("gallery", "gallery-interpreted"):
                if (site / name).is_dir() and (checkout / name).exists():
                    shutil.rmtree(checkout / name)
            shutil.copytree(site, checkout, dirs_exist_ok=True)
            verify_tree(checkout)
            git("add", "--all", cwd=checkout)
            if git("status", "--porcelain", cwd=checkout):
                run(["git", "-c", "user.name=Mu3D Pages", "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
                     "commit", "--quiet", "-m", "deploy: publish Web Gallery and public documentation [skip ci]"], cwd=checkout)
            git("push", args.remote, "HEAD:refs/heads/gh-pages", cwd=checkout)
            print("Published gh-pages commit " + git("rev-parse", "HEAD", cwd=checkout), flush=True)
        finally:
            git("worktree", "remove", "--force", str(checkout))
            if not branch:
                git("branch", "-D", temporary_branch)
    if existing is None:
        configuration = {"build_type": "legacy", "source": {"branch": "gh-pages", "path": "/"}}
        created = subprocess.run(["gh", "api", "--method", "POST", endpoint, "--input", "-"],
                                 input=json.dumps(configuration), text=True, capture_output=True)
        if created.returncode:
            # An interrupted/empty response can still have created Pages. Recover
            # only after a fresh read confirms exactly the requested configuration.
            confirmed = subprocess.run(["gh", "api", endpoint], text=True, capture_output=True)
            if confirmed.returncode or any(json.loads(confirmed.stdout).get(key) != value
                                           for key, value in configuration.items()):
                raise RuntimeError(created.stderr or created.stdout)
    # GITHUB_TOKEN pushes do not automatically start a Pages build; request it explicitly.
    run(["gh", "api", "--method", "POST", endpoint + "/builds"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--aot", type=Path, required=True)
    parser.add_argument("--interpreted", type=Path, required=True)
    parser.add_argument("--docs", type=Path)
    parser.add_argument("--base-path", default="/Mu3D.maui/")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/pages-site")
    parser.add_argument("--repository", default="EVNII/Mu3D.maui")
    parser.add_argument("--remote", default="origin", help="Named remote for the public Pages repository")
    parser.add_argument("--deploy", action="store_true", help="Explicitly authorize gh-pages commit/push and Pages build request")
    parser.add_argument("--enable-pages", action="store_true", help="Explicitly enable branch-based Pages if no site exists")
    args = parser.parse_args()
    site = stage(args)
    if args.deploy:
        deploy(args, site)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error))
