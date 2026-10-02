// Optional maintainer Dawn/Tint check; no application dependency or browser/UI.
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

if (process.argv.length < 3) throw new Error('Usage: node validate-wgsl.mjs /path/to/webgpu/index.js [extra.wgsl ...]');
const { create, globals } = await import(pathToFileURL(resolve(process.argv[2])).href);
Object.assign(globalThis, globals);
const gpu = create(['backend=null']);
const adapter = await gpu.requestAdapter();
if (!adapter) throw new Error('Dawn null backend unavailable');
const device = await adapter.requestDevice();
console.log(JSON.stringify({ backend: 'null', node: process.version, platform: process.platform, arch: process.arch, addon: resolve(process.argv[2]) }));
async function checked(label, action) {
    device.pushErrorScope('validation');
    const result = await action();
    const error = await device.popErrorScope();
    if (error) throw new Error(`${label}: ${error.message}`);
    return result;
}
async function module(label, code) {
    if (/diagnostic\s*\(\s*off\s*,\s*derivative_uniformity/.test(code))
        throw new Error(`${label}: derivative uniformity diagnostics disabled`);
    return checked(label, async () => {
        const result = device.createShaderModule({ label, code });
        const info = await result.getCompilationInfo();
        const failures = info.messages.filter(m => m.type !== 'info');
        if (failures.length) throw new Error(`${label}:\n${failures.map(m => `${m.type} ${m.lineNum}:${m.linePos} ${m.message}`).join('\n')}`);
        console.log(`PASS ${label}`);
        return result;
    });
}
try {
    const shaders = '../../src/Mu3D.Rendering.OpenPbr/Shaders/';
    const fast = await module('fast.wgsl', await readFile(new URL(shaders + 'fast.wgsl', import.meta.url), 'utf8'));
    for (const path of [shaders + 'render.wgsl', shaders + 'visibility.wgsl', './Shaders/reference.wgsl'])
        await module(path.split('/').pop(), await readFile(new URL(path, import.meta.url), 'utf8'));
    for (const path of process.argv.slice(3)) await module(path, await readFile(path, 'utf8'));
    const source = await readFile(new URL('../../src/Mu3D.Rendering.OpenPbr/OpenPbrGpuResources.cs', import.meta.url), 'utf8');
    const vertexSource = source.match(/private const string VertexSource = """\s*\n([\s\S]*?)\n\s*""";/)?.[1];
    if (!vertexSource) throw new Error('Actual OpenPBR fullscreen vertex source not found');
    const vertex = await module('OpenPBR VertexSource', vertexSource);
    // Native GPU checks own explicit host bindings; Dawn independently reflects this shader.
    await checked('OpenPBR Fast pipeline', () =>
        device.createRenderPipelineAsync({ label: 'OpenPBR Fast transport', layout: 'auto',
            vertex: { module: vertex, entryPoint: 'vs_main' },
            fragment: { module: fast, entryPoint: 'main', targets: [{ format: 'rgba32float' }] } }));
    console.log('PASS Fast pipeline (Dawn null, auto layout; no rendering performed)');
    device.destroy();
    process.exit(0);
} catch (error) {
    console.error(error.message);
    device.destroy();
    process.exit(1);
}
