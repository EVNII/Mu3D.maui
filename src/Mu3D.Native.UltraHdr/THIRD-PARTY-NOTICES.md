# Third-party notices for Mu3D.Native.UltraHdr

The JPEG-only native binaries in this package are built from:

- Google libultrahdr v2.0.1, commit `a532a8c1418890cff7ce1e90cbe59ba6ebc6fa6d`, under the MIT
  License and Apache License 2.0.
- libjpeg-turbo 3.1.0, commit `20ade4dea9589515a69793e447a6c6220b464535`, under the Independent
  JPEG Group license and the Modified BSD 3-Clause License.

This software is based in part on the work of the Independent JPEG Group.

The package includes the applicable complete license texts. The default native feature set is built
with `UHDR_ENABLE_HEIF=OFF` and contains no libheif binary. A separately built HEIF-enabled feature
set must add and review the notices for every library actually linked into that distribution before
publication.

Sources and provenance are available at:

- <https://github.com/google/libultrahdr/tree/a532a8c1418890cff7ce1e90cbe59ba6ebc6fa6d>
- <https://github.com/libjpeg-turbo/libjpeg-turbo/tree/20ade4dea9589515a69793e447a6c6220b464535>
