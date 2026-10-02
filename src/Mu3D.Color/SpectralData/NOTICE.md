# CIE 2015 observer datasets

The four CSV/JSON files in this directory are unchanged copies downloaded on 2026-09-18
from the International Commission on Illumination (CIE). The numerical tables are embedded
in Mu3D.Color and are also distributed here with their original metadata. They are separately
licensed under **Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)**:
https://creativecommons.org/licenses/by-sa/4.0/ (legal text:
https://creativecommons.org/licenses/by-sa/4.0/legalcode).
Attribution, source links and this license apply to these datasets, independently of the
license for Mu3D's original source code. Adapted versions of these datasets must retain
attribution and be distributed under the same license as required by CC BY-SA 4.0.
No CIE endorsement is implied. The data and Mu3D are provided without warranty.

## Attribution and original publications

- CIE 2015, *CIE cone-fundamental-based spectral tristimulus values for 2 degree field size*,
  International Commission on Illumination (CIE), Vienna, AT.
  DOI: https://doi.org/10.25039/CIE.DS.548rw69q.
  CIE 170-2:2015, Table 10.7a, 390–830 nm in 1 nm steps.
  https://cie.co.at/datatable/cie-cone-fundamental-based-spectral-tristimulus-values-2-degree-field-size
- CIE 2015, *CIE cone-fundamental-based spectral tristimulus values for 10° field size*,
  International Commission on Illumination (CIE), Vienna, AT.
  DOI: https://doi.org/10.25039/CIE.DS.dm6qiig7.
  CIE 170-2:2015, Table 10.8a, 390–830 nm in 1 nm steps.
  https://www.cie.co.at/datatable/cie-cone-fundamental-based-spectral-tristimulus-values-10-field-size

Download base URL: https://files.cie.co.at/Publications-datasets/

## SHA256 pins

| File | SHA256 |
| --- | --- |
| CIE_cfb_stv_2deg.csv | a10751ec8aecdb023f16ba079557e7fb794806884fe3da982dd63da285f872a9 |
| CIE_cfb_stv_2deg.csv_metadata.json | 24eb42e00e0b6dc3f6b60fcaae1bd5ea2d9195e6da28ff69b801cf475c80cb7a |
| CIE_cfb_stv_10deg.csv | 9019a35f8f51215e245f818e87d4251147d1925a8fbe9a49944fe7f011f16e38 |
| CIE_cfb_stv_10deg.csv_metadata.json | 7551a07c87485f7b8728b5be59160d4570b818d6437fc26a13d1e8be71d97cad |

The CSV hashes match the hashes supplied in the official CIE metadata. Loading verifies the
CSV hashes. Mu3D stores curve values as FP32 and uses piecewise-linear interpolation when
integrating; it does not implement the continuous physiological formulas indicated by the
metadata's `useRelatedFormula` interpolation field. Original tables are not modified.
The data metadata do not specify an observer age. Consequently the standard observer API
reports no age; custom observer age/field metadata require actual supplied matching curves.
These curves do not silently replace CIE 1931 colorimetry in existing ICC or RGB transforms.
