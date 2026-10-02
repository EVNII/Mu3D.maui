// .NET 10.0.12 bundles Clang 19 with Emscripten 3.1.56. The compiler emits the
// invocation-aware SjLj ABI, while compiler_rt still supplies the older helpers.
// ABI reference: LLVM llvmorg-19.1.0 WebAssemblyLowerEmscriptenEHSjLj.cpp and
// Emscripten 3.1.64 system/lib/compiler-rt/emscripten_setjmp.c (MIT / NCSA).
// Native builds never include this sample-owned compatibility boundary.
// Copyright 2020 The Emscripten Authors. All rights reserved.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to
// do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#include <assert.h>
#include <setjmp.h>
#include <stdint.h>

typedef struct { void* invocation; uint32_t label; } Mu3DJumpTarget;
_Static_assert(sizeof(jmp_buf) >= sizeof(Mu3DJumpTarget), "SjLj buffer must retain its invocation identity");

void __wasm_setjmp(void* environment, uint32_t label, void* invocation)
{
    assert(label && invocation);
    Mu3DJumpTarget* target = environment;
    target->invocation = invocation;
    target->label = label;
}

uint32_t __wasm_setjmp_test(void* environment, void* invocation)
{
    assert(invocation);
    const Mu3DJumpTarget* target = environment;
    assert(target->label);
    return target->invocation == invocation ? target->label : 0;
}
