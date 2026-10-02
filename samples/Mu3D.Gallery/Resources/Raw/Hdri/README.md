# Gallery HDRI asset

`studio_small_02_1k.hdr` is **Studio Small 02** by Greg Zaal, downloaded from
[Poly Haven](https://polyhaven.com/a/studio_small_02) (formerly HDRI Haven).

- License: [CC0](https://polyhaven.com/license)
- Resolution: 1024 x 512 Radiance RGBE
- File size: 1,701,272 bytes
- MD5: `4485d3e4af57d136e7d3ffe0e92b9c85`
- Official download: `https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/studio_small_02_1k.hdr`

The file header declares Radiance RGBE but no `PRIMARIES` metadata. Gallery therefore explicitly
assigns the conventional linear-sRGB/Rec.709 interpretation at load time; it does not infer the
document or display space from the numeric texture. No tone mapping or clipping is baked into the
asset or its decode path.

`artist_workshop_1k.hdr` is **Artist Workshop** by Oliksiy Yakovlyev, downloaded from
[Poly Haven](https://polyhaven.com/a/artist_workshop). It is the environment named by the official
Khronos SpecularTest reference rendering and is used only by that Model Lab gate.

- License: [CC0](https://polyhaven.com/license)
- Resolution: 1024 x 512 Radiance RGBE
- Official download: `https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/artist_workshop_1k.hdr`
- SHA-256: `f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767`
- Color interpretation: explicitly assigned linear sRGB/Rec.709 because Radiance RGBE carries no
  reliable modern color-space identity
