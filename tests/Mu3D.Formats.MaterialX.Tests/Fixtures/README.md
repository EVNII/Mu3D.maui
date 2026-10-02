# Pinned OpenPBR reference fixture

- File: `open_pbr_surface_1_1_1.mtlx` (unmodified upstream file).
- Project: Academy Software Foundation OpenPBR.
- Tag: `v1.1.1`.
- Source: https://raw.githubusercontent.com/AcademySoftwareFoundation/OpenPBR/v1.1.1/reference/open_pbr_surface.mtlx
- SHA-256: `c15674aaa82ef4bf0a27388b3b3a8f0f171982f53df6ebbceed679ce63699f51`.
- License: Apache-2.0, copied unmodified as `OpenPBR-LICENSE` from
  https://raw.githubusercontent.com/AcademySoftwareFoundation/OpenPBR/v1.1.1/LICENSE.
- Copyright: Contributors to the OpenPBR Project.

The test executable checks the exact source hash, definition version, complete 41-input inventory,
types and defaults. It constructs test instances from the definition rather than importing the
definition's shader graph: general MaterialX graphs are explicitly unsupported by this adapter.
This fixture is test-only and is not included in the production NuGet package.
