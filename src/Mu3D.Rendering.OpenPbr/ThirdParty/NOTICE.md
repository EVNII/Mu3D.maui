# OpenPBR third-party notices

The generated WGSL shaders and lookup data derive from Adobe's OpenPBR BSDF implementation,
Copyright 2026 Adobe, licensed under Apache License 2.0. The complete license accompanies this
notice as `Adobe-LICENSE`.

Source: https://github.com/adobe/openpbr-bsdf/tree/c91aad1d1ce1693e803f039d7c92c2965c4eb013

The fuzz lookup table reproduces the LTC sheen work by Tizian Zeltner, Brent Burley and
Matt Jen-Yuan Chiang under Apache License 2.0, as attributed in upstream's
`impl/data/openpbr_ltc_array.h`:
https://github.com/tizian/ltc-sheen/blob/master/pbrt-v3/src/materials/sheenltc.cpp

Mu3D preserves the upstream implementation and generates WGSL at maintainer time with pinned
Slang 2026.14. Mu3D provides scene integration and replaces texture lookup hooks with a readonly
FP32 buffer and explicit interpolation. Energy values preserve upstream FP32 normalization;
LTC coefficients are unchanged. All upstream feature switches remain enabled, including dispersion,
translucency, metal, coat and fuzz. The upstream default coat/fuzz reciprocity setting is retained.

Numerical agreement with the pinned implementation is distinct from complete physical conformance.
The upstream approximations and unsupported thin-film/thin-walled transmission combination remain
documented in the renderer's README and numerical reference tests.
