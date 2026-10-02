"""Maintainer-only, idempotent patch of isolated pinned native/HAL sources."""
import argparse
import pathlib
import shutil

ROOT = pathlib.Path(__file__).resolve().parent.parent


def replace(path, before, after):
    text = path.read_text(encoding="utf-8")
    if after in text:
        return
    if text.count(before) != 1:
        raise RuntimeError(f"Pinned source drift: {path}")
    path.write_text(text.replace(before, after), encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-source", type=pathlib.Path, required=True)
    parser.add_argument("--hal-source", type=pathlib.Path, required=True)
    args = parser.parse_args()
    native, hal = args.native_source.resolve(), args.hal_source.resolve()
    artifacts = (ROOT / "artifacts").resolve()
    if not native.is_relative_to(artifacts) or not hal.is_relative_to(artifacts):
        raise RuntimeError("Patch only isolated sources beneath repository artifacts")
    if 'version = "29.0.3"' not in (hal / "Cargo.toml").read_text(encoding="utf-8"):
        raise RuntimeError("Expected locked wgpu-hal 29.0.3")
    cargo = native / "Cargo.toml"
    patch = '\n[patch.crates-io]\nwgpu-hal = { path = "' + hal.as_posix() + '" }\n'
    text = cargo.read_text(encoding="utf-8")
    if patch not in text:
        if "[patch.crates-io]" in text:
            raise RuntimeError("Unexpected native Cargo patch table")
        cargo.write_text(text + patch, encoding="utf-8", newline="\n")
    cargo = hal / "Cargo.toml"
    text = cargo.read_text(encoding="utf-8")
    if "[dependencies.sha2]" not in text:
        cargo.write_text(text + '\n[dependencies.sha2]\nversion = "=0.10.9"\n', encoding="utf-8", newline="\n")
    dx12 = hal / "src/dx12"
    shutil.copyfile(ROOT / "eng/native/WgpuDx12ShaderCache.rs", dx12 / "mu3d_shader_cache.rs")
    replace(dx12 / "mod.rs", "mod shader_compilation;", "mod shader_compilation;\nmod mu3d_shader_cache;")
    replace(dx12 / "shader_compilation.rs", "    let buffer = Dxc::DxcBuffer {", """    let cache = super::mu3d_shader_cache::Entry::new(source, &compile_args, compiler);
    if let Some(blob) = cache.as_ref().and_then(|entry| entry.load()) {
        return Ok(crate::dx12::CompiledShader::Precompiled(blob));
    }

    let buffer = Dxc::DxcBuffer {""")
    replace(dx12 / "shader_compilation.rs", "    Ok(crate::dx12::CompiledShader::Dxc(blob))", """    if let Some(cache) = cache {
        let bytes = unsafe { core::slice::from_raw_parts(blob.GetBufferPointer().cast::<u8>(), blob.GetBufferSize()) };
        cache.store(bytes);
    }
    Ok(crate::dx12::CompiledShader::Dxc(blob))""")
    # Maintainer capture may force a LOWER model to generate the actual Naga layout variant.
    # Hardware/compiler ceilings still apply. Unknown strings retain upstream selection.
    replace(dx12 / "adapter.rs", "        let wgt_shader_model = backend_options", """        let mu3d_capture_model = std::env::var("MU3D_DX12_SHADER_MODEL").ok().and_then(|s| match s.as_str() {
            "6_0" => Some(wgt::DxcShaderModel::V6_0), "6_1" => Some(wgt::DxcShaderModel::V6_1),
            "6_2" => Some(wgt::DxcShaderModel::V6_2), "6_3" => Some(wgt::DxcShaderModel::V6_3),
            "6_4" => Some(wgt::DxcShaderModel::V6_4), "6_5" => Some(wgt::DxcShaderModel::V6_5),
            "6_6" => Some(wgt::DxcShaderModel::V6_6), "6_7" => Some(wgt::DxcShaderModel::V6_7),
            "6_8" => Some(wgt::DxcShaderModel::V6_8), _ => None,
        });
        let wgt_shader_model = backend_options""")
    replace(dx12 / "adapter.rs", ".or(compiler_container.max_shader_model());", ".or(mu3d_capture_model)\n            .or(compiler_container.max_shader_model());")
    replace(dx12 / "device.rs", "        let raw: Direct3D12::ID3D12PipelineState =\n            // If stream", "        let mu3d_pso_started = Instant::now();\n        let raw: Direct3D12::ID3D12PipelineState =\n            // If stream")
    replace(dx12 / "device.rs", "        self.counters.render_pipelines.add(1);", """        log::info!("Mu3D PSO {:?} ({:.2} ms)", desc.label, mu3d_pso_started.elapsed().as_secs_f64() * 1000.0);
        self.counters.render_pipelines.add(1);""")
    print("Prepared pinned DX12 bytecode-cache patch; native ABI unchanged.")


if __name__ == "__main__":
    main()
