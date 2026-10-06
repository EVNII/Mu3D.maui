# Optional print extension validation

This project is deliberately not in Mu3D.slnx. By default, numerical checks use the two checked-in
Gallery presets in `samples/Mu3D.Gallery/Resources/PrintPresets`: GRACoL2013_CRPC6 and
SWOP2013C3_CRPC5. Their source and redistribution terms are retained in that directory's
`catalog.json` and `NOTICE.md`.

The test project source-links the actual shared Gallery catalog loader. It checks the two exact
default profiles and byte preservation, missing catalog/ICC, changed ICC bytes, invalid ICC with
a matching catalog hash, empty catalog and cancellation before any asset read. These checks always
use the checked-in defaults. The existing four-intent numerical tests then run on the selected
profile directory.

```sh
dotnet build Mu3D.Printing.slnx -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tests/Mu3D.Color.Printing.Tests/bin/Debug/net10.0/Mu3D.Color.Printing.Tests.dll
```

To extend numerical coverage, set `MU3D_PRINT_PROFILE_TEST_DIR` to an explicit directory of
supplied `.icc` files. For the prior five-region set, obtain JapanColor2011Coated,
GRACoL2013_CRPC6, SWOP2013C3_CRPC5, PSOcoated_v3 and PSOuncoated_v3_FOGRA52 from the
[ICC registry](https://registry.color.org/profile-registry/) and retain the original provider terms.
The supplied files are optional test fixtures; setting the environment variable does not add
them to Gallery's default catalog or publish them.

```sh
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

Historical supplied-five-profile baseline: 306 managed checks and 1,280 native oracle patches. Maximum XYZ
component error 0.0044503; maximum ink coverage error 0.0318303 (3.183 percentage points), with
an explicit 0.035 component tolerance. CMM interpolation and mapping are not bit-identical;
this tolerance must not be represented as professional contract-proof accuracy.
