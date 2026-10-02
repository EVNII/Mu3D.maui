using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Mu3D.Color;

/// <summary>Provides pinned CIE 170-2:2015 2° and 10° XYZ color-matching functions.</summary>
/// <remarks>
/// Experimental spectral API. Data are the unchanged CIE 1 nm tables over 390–830 nm under
/// CC BY-SA 4.0; see the package's SpectralData/NOTICE.md. Published age is unspecified in the
/// dataset metadata. These observers do not replace the CIE 1931 observer used by ICC profiles.
/// </remarks>
public static class Cie2015Observers
{
    private static readonly Lazy<SpectralObserver> Two = new(() => Load(2,
        "a10751ec8aecdb023f16ba079557e7fb794806884fe3da982dd63da285f872a9", "10.25039/CIE.DS.548rw69q"));
    private static readonly Lazy<SpectralObserver> Ten = new(() => Load(10,
        "9019a35f8f51215e245f818e87d4251147d1925a8fbe9a49944fe7f011f16e38", "10.25039/CIE.DS.dm6qiig7"));

    /// <summary>Gets the immutable CIE 2015 2° observer, CIE 170-2:2015 Table 10.7a.</summary>
    public static SpectralObserver TwoDegree => Two.Value;
    /// <summary>Gets the immutable CIE 2015 10° observer, CIE 170-2:2015 Table 10.8a.</summary>
    public static SpectralObserver TenDegree => Ten.Value;

    private static SpectralObserver Load(int degrees, string hash, string doi)
    {
        string resource = $"Mu3D.Color.SpectralData.CIE_cfb_stv_{degrees}deg.csv";
        using Stream stream = typeof(Cie2015Observers).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded CIE dataset: {resource}.");
        using MemoryStream data = new();
        stream.CopyTo(data);
        byte[] bytes = data.ToArray();
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(hash, StringComparison.Ordinal))
            throw new InvalidDataException("The embedded CIE data differ from the pinned official dataset.");
        string[] rows = Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length != 441) throw new InvalidDataException("The CIE dataset must contain 441 wavelengths.");
        Vector3[] curves = new Vector3[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            string[] fields = rows[i].Split(',');
            if (fields.Length != 4 || int.Parse(fields[0], CultureInfo.InvariantCulture) != 390 + i)
                throw new InvalidDataException("Unexpected CIE wavelength grid.");
            curves[i] = new(float.Parse(fields[1], CultureInfo.InvariantCulture),
                float.Parse(fields[2], CultureInfo.InvariantCulture), float.Parse(fields[3], CultureInfo.InvariantCulture));
        }
        return new SpectralObserver($"CIE 2015 {degrees} degree", degrees, null, $"https://doi.org/{doi}",
            390, 1, curves, true, hash);
    }
}
