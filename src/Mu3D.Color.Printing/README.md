# Mu3D.Color.Printing

Optional managed ICC CMYK separation, decoding and soft proofing. References only Mu3D.Color;
no native dependencies, automatic registration or bundled third-party profiles. Applications
that do not reference this package do not compile or include its implementation.

Load an ICC v2/v4 CMYK output profile supplied by the printer into CmykProfile, compile a
CmykTransform, then Separate an opaque print-relative LinearRgbaImage or Decode a CmykImage.
Proof simulates ink response and optionally paper white. CMYK values retain the exact profile
and original black channel. Explicit range policy, rendering intent and L*-based BPC are available.

Supports mft1/mft2/mAB/mBA with Lab/XYZ PCS. MPE, DeviceLink, spectral, spot-color, PDF/X and
printer/RIP functionality are outside this package. HDR-to-print tone mapping and alpha
compositing must be selected by the application. Soft-proof diagnostics are not certification.

Repository documentation: docs-public/articles/features/cmyk-printing.md.
Gallery's CMYK + Soft Proof example can be excluded with -p:EnableMu3DPrinting=false.

PrintRgbTransform separates algorithm selection (simple or Blender AgX Rec.2020)
from target RGB space (including Adobe RGB and ProPhoto). AgX is followed by explicit
target-gamut contraction, not another tone curve. No native Adobe/ProPhoto AgX claim.
