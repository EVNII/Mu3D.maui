"""Real independent-process DX12 cache acceptance. Requires the patched Windows test host."""
import argparse
import json
import os
import pathlib
import re
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    work = pathlib.Path(tempfile.mkdtemp(prefix="cache-check-", dir=args.output)).resolve()
    environment = {k: v for k, v in os.environ.items() if not k.startswith("MU3D_SHADER_CACHE") and k != "MU3D_DX12_SHADER_MODEL"}
    environment["MU3D_SHADER_CACHE_DIR"] = str(work / "writable")
    results = {}

    def run(name, env, startup=False):
        result = subprocess.run([str(args.host.resolve()), "--native", "--dx12", "--cache-log",
                                 "--startup-only" if startup else "--cache-warmup-only"],
                                env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8")
        (args.output / (name + ".log")).write_text(result.stdout, encoding="utf-8")
        if result.returncode:
            raise RuntimeError(f"{name} failed; see its log")
        results[name] = {"compiles": result.stdout.count("Mu3D DXIL compile "),
                         "bundle_hits": result.stdout.count("Mu3D DXIL cache bundle hit "),
                         "disk_hits": result.stdout.count("Mu3D DXIL cache disk hit "),
                         "timings_ms": re.findall(r"Cache preparation (\w+): ([\d.]+) ms", result.stdout)}
        print(name, results[name], flush=True)
        return result.stdout

    text = run("bundled-empty-user-cache", environment)
    assert results["bundled-empty-user-cache"]["compiles"] == 0
    assert results["bundled-empty-user-cache"]["bundle_hits"] == 9
    assert not (work / "writable").exists(), "Bundled hits should not populate a disk cache"
    keys = re.findall(r"cache bundle hit ([0-9a-f]{64})", text)
    bundle = args.host.resolve().parent / "Mu3D/ShaderCache"
    victim = min((bundle / (key + ".bin") for key in keys), key=lambda p: p.stat().st_size)
    broken = work / "corrupted-seeds"
    shutil.copytree(bundle, broken)
    data = bytearray((broken / victim.name).read_bytes())
    data[-1] ^= 1
    (broken / victim.name).write_bytes(data)
    environment["MU3D_SHADER_CACHE_SEEDS"] = str(broken)
    run("corrupt-seed-fallback", environment)
    assert results["corrupt-seed-fallback"]["compiles"] == 1
    run("repaired-from-disk", environment)
    assert results["repaired-from-disk"]["compiles"] == 0
    assert results["repaired-from-disk"]["disk_hits"] == 1
    disk_entry = work / "writable" / victim.name
    disk_entry.write_bytes(b"truncated")
    run("corrupt-disk-fallback", environment)
    assert results["corrupt-disk-fallback"]["compiles"] == 1
    blocker = work / "not-a-directory"
    blocker.write_text("cache cannot be written here", encoding="utf-8")
    environment["MU3D_SHADER_CACHE_DIR"] = str(blocker)
    run("unwritable-cache-fallback", environment)
    assert results["unwritable-cache-fallback"]["compiles"] == 1
    environment["MU3D_SHADER_CACHE_SEEDS"] = str(work / "absent-seeds")
    environment["MU3D_SHADER_CACHE_DIR"] = str(work / "cold")
    run("cold-process", environment)
    assert results["cold-process"]["compiles"] == 9
    run("second-independent-process", environment)
    assert results["second-independent-process"]["disk_hits"] == 9
    assert results["second-independent-process"]["compiles"] == 0
    environment["MU3D_SHADER_CACHE_DISABLE"] = "1"
    run("disabled", environment, startup=True)
    assert results["disabled"]["compiles"] == results["disabled"]["disk_hits"] == results["disabled"]["bundle_hits"] == 0
    (args.output / "acceptance.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print("All 8 cache acceptance scenarios passed; driver caches were not cleared.", flush=True)


if __name__ == "__main__":
    main()
