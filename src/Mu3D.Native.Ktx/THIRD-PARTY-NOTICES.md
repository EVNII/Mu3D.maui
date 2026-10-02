# Third-party notices for Mu3D.Native.Ktx

The native binaries in this package are built from KhronosGroup/KTX-Software v4.4.2 at
commit `4d6fc70eaf62ad0558e63e8d97eb9766118327a6`. Mu3D builds only the
reader/transcoder target (`ktx_read`) with tools, tests, upload helpers and
`KTX_FEATURE_ETC_UNPACK` disabled.

The disabled ETC-unpack option is significant: `external/etcdec/etcdec.cxx`, which upstream
identifies as subject to a special Ericsson license, is not compiled into these binaries.

The distributed reader/transcoder contains code from these projects:

- KTX-Software and DFD utilities — Copyright Mark Callow, The Khronos Group Inc., Arm Limited,
  Oculus VR, LLC, RasterGrid Kft. and contributors; Apache License 2.0.
- Basis Universal transcoder and its miniz fork — Copyright Binomial LLC and contributors;
  Apache License 2.0.
- Arm ASTC Encoder — Copyright Arm Limited and contributors; Apache License 2.0.
- Zstandard decoder — Copyright Meta Platforms, Inc. and affiliates; BSD 3-Clause license.
- uthash — Copyright 2003–2010 Troy D. Hanson; BSD 1-Clause license.

The complete applicable license texts and the upstream KTX-Software license overview accompany
this notice in the package's `licenses/` directory. Source and additional provenance are available
at <https://github.com/KhronosGroup/KTX-Software/tree/4d6fc70eaf62ad0558e63e8d97eb9766118327a6>.
