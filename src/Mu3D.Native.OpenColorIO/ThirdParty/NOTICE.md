# Native dependency provenance

The Mu3D-owned wrapper links OpenColorIO 2.5.2 privately, under its BSD-3-Clause license.
Official source: https://github.com/AcademySoftwareFoundation/OpenColorIO/tree/v2.5.2
Commit: `c52966a6677723d5bd2dbef0ccec3fed9cbc3790`.

The pinned host recipe also links Expat 2.7.2, yaml-cpp 0.8.0, pystring 1.1.4, Imath 3.2.1,
zlib 1.3.1 and minizip-ng 4.0.10. Their unmodified license files are included beside this notice.
`eng/opencolorio/manifest.json` records each official archive URL and SHA256. Mu3D changes no
library implementation; temporary build recipes use those verified local sources.

The packaged managed assembly contains no native binary or completed platform runtime matrix.
The local macOS arm64 native artifact is maintainer verification evidence only; applications
must receive a separately prepared compatible runtime. Consumer builds never fetch or compile it.
