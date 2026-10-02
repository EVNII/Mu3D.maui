// Actual CPU codec/import workload; excludes browser/GPU/display acceptance.
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { writeFile } from 'node:fs/promises';
import { performance } from 'node:perf_hooks';

const [suite, output] = process.argv.slice(2);
if (!suite || !output) throw new Error('Usage: node measure-memory.mjs <published-suite> <report.json>');
const started = performance.now();
const { dotnet } = await import(pathToFileURL(resolve(suite, 'wwwroot/_framework/dotnet.js')));
const samples = [], cycles = [];
let runtime, cycle = 0;
function snapshot(label, managedBytes = null) {
  samples.push({ cycle, label, managedBytes, elapsedMs: performance.now() - started,
    wasmBytes: runtime?.localHeapViewU8().byteLength ?? null, ...process.memoryUsage() });
}
runtime = await dotnet.withModuleConfig({ print: line => {
  if (/^Retired|^Model /.test(line)) snapshot(line);
  console.log(line);
} }).create();
const startupMs = performance.now() - started;
const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
snapshot('runtime-ready');
for (cycle = 1; cycle <= 2; cycle++) {
  const mainStarted = performance.now();
  const code = await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
  const mainMs = performance.now() - mainStarted;
  if (code !== 0) throw new Error(`Codec contracts returned ${code}`);
  // Unwind the entire managed Main and its conservative AOT stack before measuring.
  await new Promise(resolve => setTimeout(resolve, 0));
  const report = JSON.parse(exports.Mu3D.Web.Validation.CodecMemory.CollectAndReport());
  snapshot('retired-main', report.managedBytes);
  cycles.push({ cycle, mainMs, ...report, wasmBytes: runtime.localHeapViewU8().byteLength });
  console.log(`Memory cycle ${cycle}: ${mainMs.toFixed(1)} ms; managed ${report.managedBytes} bytes; WASM ${runtime.localHeapViewU8().byteLength} bytes.`);
}
if (cycles[1].retainedArrayBytes > cycles[0].retainedArrayBytes)
  throw new Error('Retired decoded storage grew across identical cycles.');
if (cycles[1].wasmBytes > cycles[0].wasmBytes)
  throw new Error('WASM capacity grew across identical cycles.');
await writeFile(output, JSON.stringify({ suite, node: process.version, startupMs, cycles, samples }, null, 2) + '\n');
