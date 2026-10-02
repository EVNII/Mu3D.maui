# Mu3D.Native.UltraHdr.Runtime.Jpeg

Runtime-only native assets for ordinary JPEG plus Ultra HDR and ISO gain-map JPEG. Applications
normally reference `Mu3D.Native.UltraHdr`, which selects this exact JPEG runtime transitively.

This package intentionally excludes HEIF/HEIC/AVIF support. It contains no Mu3D managed assembly,
symbols, SourceLink data or source code. Native assets pin libultrahdr v2.0.1 and libjpeg-turbo
3.1.0 to the commits recorded in the package notices.
