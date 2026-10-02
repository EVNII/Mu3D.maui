# Khronos glTF conformance samples

These unmodified GLB files come from the official
[`KhronosGroup/glTF-Sample-Assets`](https://github.com/KhronosGroup/glTF-Sample-Assets)
repository. The adjacent `*.LICENSE.md` files preserve source attribution and license terms; most
are the original model READMEs.

| Asset | Purpose | SHA-256 |
| --- | --- | --- |
| `BoxInterleaved.glb` | GLB, hierarchy, matrix transform, interleaved POSITION/NORMAL accessors | `b2ae631f118f1d13f829cdf9d9dc0fe7cb582de20b8c51d17f81f77a1cbf290c` |
| `AnimatedMorphCube.glb` | Two morph targets and weight animation | `214ee56160a50dbf22543a1d66dbf860986e87f0efac3d89feac1359d0e6aeab` |
| `Fox.glb` | Embedded PNG, UVs, normalized joint indices, skin and three animation clips | `d97044e701822bac5a62696459b27d7b375aada5de8574ed4362edbba94771f7` |
| `AlphaBlendModeTest.glb` | Multiple meshes/materials, base/normal/ORM textures, OPAQUE/BLEND/MASK | `37c3577d143071b42dd46e9d942b157837eb25c6340112171d7faecaa987b14e` |
| `InterpolationTest.glb` | STEP/LINEAR/CUBICSPLINE transforms and a 4-bit indexed PNG label | `a86eb331b4a083715e75fe19a1f747eac5692d5b9ff120f1eaa457c23ba72bca` |
| `NormalTangentMirrorTest.glb` | Authored tangents and mirrored-UV handedness under a normal map | `31ce5f3c873fc55531a17ccacf001e3d1d482a508695aa826fa79cc196e0ce78` |
| `NormalTangentTest.glb` | Derivative tangent-frame reconstruction under a normal map | `5ac0932355ae1ea05a7485eeddcc3f1fbe56c678e00b519075feced80e9e9d6a` |
| `TextureTransformTest/TextureTransformTest.gltf` | External GLTF/BIN/PNG plus `KHR_texture_transform` offset, rotation and scale | `c22c8c6c96c0ea4bcbb9b47ea245a093c5ef59acc5fd425effa4c00da4cdf164` |
| `TextureSettingsTest.glb` | Per-material clamp, repeat and mirrored-repeat sampler modes plus double-sided state | `44013a0602cbbdcaf703905ed47cc6b6fb49f067d7de0e1a0a568a5bbfa9d769` |
| `BoomBox.glb` | Core glTF BaseColor/Normal/ORM plus an emissive texture and factor | `f8b918445ebdd006768232205a62f5182d2208ca57f84c6ccc084943c0bc8f15` |
| `EmissiveStrengthTest.glb` | Isolated `KHR_materials_emissive_strength` progression from 1x through 16x | `074898ec4c3636230faffe0cacac7403c3f6135db61f943da28ffaa88ca2a39f` |
| `AnisotropyBarnLampKtx/AnisotropyBarnLamp.gltf` | External GLTF/BIN/KTX2, core PBR fallback and reported optional material extensions | `71a528dfc329c6df9f3651d2fdbaf4ea5ee5e8dab668596f126bc7c2425c4543` |
| `SheenTestGrid.glb` | Required `KHR_materials_sheen`, varying sheen color and roughness over a sphere grid | `b3d82dde0ae6b93bef1a8085ec05540b21139f9917bfe771135c5aed9ba1c101` |
| `IridescenceSuzanne.glb` | Required `KHR_materials_iridescence`, dielectric and metallic thin-film response | `866762602d60a0942b7608bfabf4e8686a29033636224f4b55c5e56abe8abf8f` |
| `SpecularTest.glb` | Required `KHR_materials_specular` factor/color and texture forms over dielectric F0/F90 | `cf789c68c3ab4b74877da3c5992c612c213f9c901ed4fafbf9be362f434e48d9` |
| `DiffuseTransmissionPlant.glb` | Release-candidate `KHR_materials_diffuse_transmission` with thin double-sided leaves and authored transmission color | `1748be8847e24bdd5cb6c5ac2bfd0afe8f11efc4f85091148550555e47c7ee83` |
| `UnlitTest.glb` | Required `KHR_materials_unlit`; every visible face must retain uniform authored base color | `e07b68c6fd9fbf73d372c610a681b89d6b013bdd1a506e46a411df82210a2481` |
| `TransmissionRoughnessTest.glb` | `KHR_materials_transmission` + IOR + volume; IOR 1 stays sharp while higher rows expose rough-refraction blur | `43ace8f8dfe0b6148dc4668e6a768c8853ba37cf1f0fe7de76eaf0ea8e94bb73` |
| `ClearCoatTest.glb` | Isolated required `KHR_materials_clearcoat` factor, roughness and normal-map gate | `c3a1cbe318cd043b937130af4eb83ec2ea0b03613387b1b7d769dfab4ac15948` |
| `IORTestGrid.glb` | Isolated dielectric `KHR_materials_ior` reflection/refraction grid | `863cf24d0e48892ec830a7c712e4eb8bf5c0fd6cc2ae2f34d213b216f0bd6c12` |
| `TransmissionTest.glb` | Isolated required `KHR_materials_transmission` factor and texture gate | `dd9732dae5517f8605ad4324d78b077b969c3e8357c056280d0a4e4b67797d15` |
| `GlassHurricaneCandleHolder.glb` | Holistic `KHR_materials_volume` thickness and colored attenuation gate | `add23791ee6f5f550b94a21232a323720c92c20d280f4f7d19cf954234f0b7b8` |
| `DispersionTest.glb` | Required `KHR_materials_dispersion` over authored dispersion and IOR grids | `bbac38386632b5a7a27289737963314c2f92095074dfdf0e8c0c1c25270b3e37` |
| `MaterialsVariantsShoe.glb` | Runtime `KHR_materials_variants` switching while geometry remains shared | `e1d7cb190382111e5a5b37b51e9a7f007f7eb2ab1b6185e0188e8d0a0d1265a7` |

The `AnisotropyBarnLampKtx` gate also preserves these unmodified official companion files:

- `AnisotropyBarnLamp.bin`: `50f196f7613c3cf40886c0058c8f4a51f6118d5654879f41bb2fcdb5ba989871`
- `AnisotropyBarnLamp_basecolor.ktx2`: `9887c98d487f2415f9221f37c14f185edfded7c9438e83ce75a753ae27a4ae53`
- `AnisotropyBarnLamp_normalbump.ktx2`: `c04db9be21ca5f2d57c86854b9608c240e13b47e7d7329b696b62a79b262f839`
- `AnisotropyBarnLamp_occlusionroughnessmetal.ktx2`: `73a1ba6dfa0fbf1b1edc158b119623acbff894d62ce5ac3edaa20bedfe812383`
- `AnisotropyBarnLamp_anisotropy.ktx2`: `4f2ee6ad27b50eb1497c73369d42154234c23d06292d4163cb61c0e65a02c68a`
- `LICENSE.md`: `e134777852bb33f44780ccd91976c1616542f35633e15286cdeec6bb77e0f97f`

The `TextureTransformTest` directory preserves the official external BIN and five PNG files:

- `TextureTransformTest.bin`: `d0cdd23f2fa0996a0db99d4932fb9912a51a34e27c8bf832d072dc856ee9a7af`
- `Arrow.png`: `5df5b251fed0ac306cf859a30b59a4953d152483a4bed2b84e5bba1c667a1e64`
- `Correct.png`: `3a14b12635ebbcee3ea427fbdb4d20da73e4740ec7a51683d700a2d6b4b7861a`
- `Error.png`: `8d1032ef535a5c379bf78f9f565d3180ee3fc2d1cfef6892dee6e567728b619c`
- `NotSupported.png`: `1fbdeca7e5c105d677a39578e4bab82b89c8aa9f406ee681ede05635ea0f8c74`
- `UV.png`: `ac37ff52fe06a4c8c35ad4e1e7e8da039dfa8afc9b629d40a44ca8b8cfe9d03d`

The adjacent attribution files preserve each model's license. `BoomBox` is CC0;
`TextureSettingsTest.glb` and `EmissiveStrengthTest.glb` retain their adjacent CC BY 4.0
attribution. The latter attribution file hash is
`b7e1522c4a62df162340850d8d7527af1a242c41555932a6c7669eee2f189d58`.

The official normal, ORM and anisotropy KTX2 files declare `BT709 + LINEAR` in their DFD. The
`KHR_texture_basisu` specification requires `UNSPECIFIED + LINEAR` for non-color data, so strict
adapter mode rejects them. This Gallery gate enables the adapter's explicit legacy compatibility
policy for these exact hash-pinned files; it still performs no transfer or primary conversion on
numeric data channels and does not count this as a conformance pass.

The suite is intentionally progressive. `BoxInterleaved` is the first physical import gate. The
other assets remain fail-closed until their corresponding texture, skin, animation, morph and
texture-alpha import slices are implemented; they must never appear to pass with data omitted.
