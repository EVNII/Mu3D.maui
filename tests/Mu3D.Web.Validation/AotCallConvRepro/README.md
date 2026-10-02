# Mono WASM AOT calling-convention reproduction

This non-packable, one-function class library isolates a build-time failure in the installed
.NET 10.0.401 / Mono 10.0.12 macOS ARM64-to-wasm32 toolchain. No Mu3D assemblies, renderer,
browser or native WebGPU library are loaded. The function is compiled, never executed.

Run from the repository root after installing the repository's pinned WASM tools:

```sh
python3 eng/repro-web-aot-callconv.py
```

The script builds the same declaration three ways and invokes `mono-aot-cross` directly with
the runtime's complete CoreLib/library search path. It writes logs, intermediate bitcode and
`results.json` under `artifacts/validation/web-wasm/aot-callconv-repro/`.

Observed on 2026-09-30:

| Declaration | Compiler result |
| --- | --- |
| `LibraryImport` + `UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])` | Abort, `sgen-alloc.c:409, *p == NULL` |
| `LibraryImport` with the default convention | Success, LLVM bitcode written |
| `DllImport(CallingConvention = CallingConvention.Cdecl)` | Success, LLVM bitcode written |

The negative case must match that diagnostic; an unrelated failure does not count as a successful
reproduction. Both positive controls must exit successfully and produce fresh bitcode. The script
does not download or patch the SDK. Its expected negative result may change when the toolchain is
upgraded; inspect that change rather than automatically preserving the workaround.

In the original Render assembly, LLDB identified `WgpuNative.wgpuAdapterAddRef` as the first
managed-to-native wrapper being generated. The abort stack goes through custom-attribute value
loading, managed array allocation and SGen allocation. This isolates the trigger; it does not
establish the underlying memory-corruption cause inside Mono. The pack records commit
`95017c711e6afc1085133d440e42b4bd78155701`; its corresponding public source URL was unavailable
when checked. No matching upstream issue has been confirmed or filed.

The Render validation suite uses the successful explicit-Cdecl form through
`../BrowserWgpuBindings.targets`. Only its intermediate source copy changes. The committed native
bindings retain their original declarations and both Render benchmark flavors use the same bridge.
This is not a new public backend or a project-wide calling-convention policy.
