# Optional print extension validation

This project is deliberately not in Mu3D.slnx. Official profiles are test inputs only and are not
redistributed. Obtain JapanColor2011Coated, GRACoL2013_CRPC6, SWOP2013C3_CRPC5 PSOcoated_v3 and PSOuncoated_v3_FOGRA52
from https://registry.color.org/profile-registry/ and retain the original bytes and provider terms.
Set MU3D_PRINT_PROFILE_TEST_DIR to that directory.

```sh
dotnet build Mu3D.Printing.slnx -m:1 -nr:false -p:UseSharedCompilation=false
MU3D_PRINT_PROFILE_TEST_DIR=/path/to/profiles dotnet tests/Mu3D.Color.Printing.Tests/bin/Debug/net10.0/Mu3D.Color.Printing.Tests.dll
```

Independent macOS ColorSync comparison, without opening UI:

```sh
clang tests/Mu3D.Color.Printing.Tests/ColorSyncOracle.macos.c -framework ColorSync -framework CoreGraphics -framework CoreFoundation -o /tmp/mu3d-print-oracle
/tmp/mu3d-print-oracle /path/to/profiles/*.icc > /tmp/print-oracle.csv
MU3D_PRINT_PROFILE_TEST_DIR=/path/to/profiles MU3D_PRINT_ORACLE_CSV=/tmp/print-oracle.csv dotnet tests/Mu3D.Color.Printing.Tests/bin/Debug/net10.0/Mu3D.Color.Printing.Tests.dll
```

The oracle uses generic XYZ D50 endpoints to avoid platform canonical RGB profile white-point
normalization affecting the absolute-intent comparison. Each row identifies profile, intent,
direction (0=CMYK to XYZ, 1=XYZ to CMYK), four input slots and four output slots. All four intents
are tested. Separation comparison explicitly enables clipping to match ColorSync's bounded LUT
behavior. This is a numerical regression, not certified soft proof or an instrument measurement.

Observed five-profile baseline: 306 managed checks and 1,280 native oracle patches. Maximum XYZ
component error 0.0044503; maximum ink coverage error 0.0318303 (3.183 percentage points), with
an explicit 0.035 component tolerance. CMM interpolation and mapping are not bit-identical;
this tolerance must not be represented as professional contract-proof accuracy.
