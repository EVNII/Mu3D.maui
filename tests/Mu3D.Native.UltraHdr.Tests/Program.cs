using System.Numerics;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Native.UltraHdr;
using Mu3D.SceneGraph;

List<string> failures = [];
UltraHdrJpegEncoder encoder = new();

ExpectThrows<ArgumentOutOfRangeException>(
    () => encoder.Encode(
        CreateImage(new Vector4(1f, 1f, 1f, 1f)),
        new UltraHdrJpegEncodeOptions { BaseQuality = 101 }),
    "base quality range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => encoder.Encode(
        CreateImage(new Vector4(1f, 1f, 1f, 1f)),
        new UltraHdrJpegEncodeOptions { GainMapQuality = -1 }),
    "gain-map quality range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => encoder.Encode(
        CreateImage(new Vector4(1f, 1f, 1f, 1f)),
        new UltraHdrJpegEncodeOptions { GainMapScaleFactor = 129 }),
    "gain-map scale range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => encoder.Encode(
        CreateImage(new Vector4(1f, 1f, 1f, 1f)),
        new UltraHdrJpegEncodeOptions { TargetDisplayPeakBrightnessNits = 100f }),
    "target peak range",
    failures);
ExpectThrows<InvalidOperationException>(
    () => encoder.Encode(CreateImage(new Vector4(1f, 1f, 1f, 0.5f))),
    "transparent JPEG rejection",
    failures);
ExpectThrows<InvalidOperationException>(
    () => encoder.Encode(CreateImage(new Vector4(-0.01f, 1f, 1f, 1f))),
    "negative component rejection",
    failures);
ExpectThrows<InvalidOperationException>(
    () => encoder.Encode(
        CreateImage(new Vector4(2f, 1f, 1f, 1f)),
        new UltraHdrJpegEncodeOptions { TargetDisplayPeakBrightnessNits = 203f }),
    "target below content peak rejection",
    failures);
ExpectThrows<NotSupportedException>(
    () => encoder.Encode(
        new LinearRgbaImage(
            1,
            1,
            [new Vector4(1f, 1f, 1f, 1f)],
            new TestColorSpace())),
    "unknown color-space rejection",
    failures);

LinearRgbaImage metricReference = new(
    2,
    1,
    [new Vector4(0f, 0f, 0f, 1f), new Vector4(2f, 1f, 0.5f, 1f)],
    StandardColorSpaces.LinearDisplayP3);
LinearRgbImageComparison exactMetrics = LinearRgbImageComparer.Compare(
    metricReference,
    metricReference);
Expect(
    exactMetrics.PixelCount == 2 &&
    exactMetrics.MeanAbsoluteRgbError == 0 &&
    exactMetrics.RootMeanSquareRgbError == 0 &&
    exactMetrics.PeakRelativeRgbError == 0 &&
    double.IsPositiveInfinity(exactMetrics.PeakSignalToNoiseRatioDecibels) &&
    exactMetrics.MeanDeltaE2000 == 0,
    "linear RGB image exact comparison metrics",
    failures);

LinearRgbaImage metricActual = new(
    2,
    1,
    [new Vector4(0f, 0f, 0f, 1f), new Vector4(1.5f, 1.25f, 0.5f, 1f)],
    StandardColorSpaces.LinearDisplayP3);
LinearRgbImageComparison errorMetrics = LinearRgbImageComparer.Compare(
    metricReference,
    metricActual);
double expectedRmse = Math.Sqrt(0.3125 / 6);
Expect(
    Math.Abs(errorMetrics.MeanAbsoluteRgbError - 0.125) <= 0.000001 &&
    Math.Abs(errorMetrics.RootMeanSquareRgbError - expectedRmse) <= 0.000001 &&
    errorMetrics.MaximumAbsoluteRgbError == 0.5 &&
    errorMetrics.ReferencePeakMagnitude == 2 &&
    errorMetrics.ActualPeakMagnitude == 1.5 &&
    errorMetrics.PeakRelativeRgbError == 0.25 &&
    Math.Abs(
        errorMetrics.PeakSignalToNoiseRatioDecibels -
        20 * Math.Log10(2 / expectedRmse)) <= 0.000001 &&
    errorMetrics.MeanDeltaE2000 > 0 &&
    errorMetrics.MaximumDeltaE2000 >= errorMetrics.MeanDeltaE2000,
    "linear RGB image error and perceptual metrics",
    failures);

LinearRgba equivalentP3 = new(
    0.2f,
    0.4f,
    0.6f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
LinearRgba equivalentSrgb = StandardLinearRgbConverter.Convert(
    equivalentP3,
    StandardColorSpaces.LinearSrgb);
LinearRgbImageComparison convertedMetrics = LinearRgbImageComparer.Compare(
    new LinearRgbaImage(
        1,
        1,
        [new Vector4(equivalentP3.Red, equivalentP3.Green, equivalentP3.Blue, 1f)],
        StandardColorSpaces.LinearDisplayP3),
    new LinearRgbaImage(
        1,
        1,
        [new Vector4(equivalentSrgb.Red, equivalentSrgb.Green, equivalentSrgb.Blue, 1f)],
        StandardColorSpaces.LinearSrgb));
Expect(
    convertedMetrics.MaximumAbsoluteRgbError <= 0.000001 &&
    convertedMetrics.MaximumDeltaE2000 <= 0.0001,
    "linear RGB image cross-space comparison",
    failures);

(float canonicalX1, float canonicalY1, float canonicalZ1) = LabToXyzD50ForTest(
    50f,
    2.6772f,
    -79.7751f);
(float canonicalX2, float canonicalY2, float canonicalZ2) = LabToXyzD50ForTest(
    50f,
    0f,
    -82.7485f);
LinearRgba canonicalFirst = StandardLinearRgbConverter.FromXyzD50(
    canonicalX1,
    canonicalY1,
    canonicalZ1,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba canonicalSecond = StandardLinearRgbConverter.FromXyzD50(
    canonicalX2,
    canonicalY2,
    canonicalZ2,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgbImageComparison canonicalDeltaE = LinearRgbImageComparer.Compare(
    new LinearRgbaImage(
        1,
        1,
        [new Vector4(canonicalFirst.Red, canonicalFirst.Green, canonicalFirst.Blue, 1f)],
        StandardColorSpaces.LinearProPhotoRgb),
    new LinearRgbaImage(
        1,
        1,
        [new Vector4(canonicalSecond.Red, canonicalSecond.Green, canonicalSecond.Blue, 1f)],
        StandardColorSpaces.LinearProPhotoRgb));
Expect(
    Math.Abs(canonicalDeltaE.MeanDeltaE2000 - 2.0425) <= 0.0002,
    "CIEDE2000 canonical Sharma pair",
    failures);

ExpectThrows<ArgumentException>(
    () => LinearRgbImageComparer.Compare(
        metricReference,
        new LinearRgbaImage(
            1,
            1,
            [Vector4.One],
            StandardColorSpaces.LinearDisplayP3)),
    "linear RGB image metric dimension mismatch",
    failures);

JpegDocumentEncoder documentEncoder = new();
IccProfile documentOutputProfile = new(
    CreateLutProfile("B2D1", "XYZ ", CreateScaledXyzMatrixMpe(2f)));
ExpectThrows<ArgumentOutOfRangeException>(
    () => documentEncoder.Encode(
        CreateImage(Vector4.One),
        documentOutputProfile,
        new JpegDocumentEncodeOptions { Quality = 0 }),
    "ordinary JPEG document quality range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => documentEncoder.Encode(
        CreateImage(Vector4.One),
        documentOutputProfile,
        new JpegDocumentEncodeOptions
        {
            ChromaSubsampling = (JpegChromaSubsampling)99,
        }),
    "ordinary JPEG document chroma policy validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => documentEncoder.Encode(
        CreateImage(Vector4.One),
        documentOutputProfile,
        new JpegDocumentEncodeOptions
        {
            RangePolicy = (JpegEncodingRangePolicy)99,
        }),
    "ordinary JPEG document range policy validation",
    failures);
ExpectThrows<InvalidOperationException>(
    () => documentEncoder.Encode(
        CreateImage(new Vector4(0.25f, 0.25f, 0.25f, 0.5f)),
        documentOutputProfile),
    "ordinary JPEG document transparency rejection",
    failures);
LinearRgba overRangeDocumentColor = StandardLinearRgbConverter.FromXyzD50(
    0.75f,
    0.75f,
    0.75f,
    1f,
    StandardColorSpaces.LinearSrgb);
ExpectThrows<InvalidOperationException>(
    () => documentEncoder.Encode(
        CreateImage(new Vector4(
            overRangeDocumentColor.Red,
            overRangeDocumentColor.Green,
            overRangeDocumentColor.Blue,
            1f)),
        documentOutputProfile),
    "ordinary JPEG document implicit clipping rejection",
    failures);
ExpectThrows<NotSupportedException>(
    () => documentEncoder.Encode(
        CreateImage(Vector4.One),
        new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateIdentityXyzLut16()))),
    "ordinary JPEG document missing output transform rejection",
    failures);

byte[] profileBytes = CreateTestIccProfile(70000, majorVersion: 4);
IccProfile profile = new(profileBytes);
Expect(
    profile.MajorVersion == 4 &&
    profile.MinorVersion == 3 &&
    profile.ProfileClass == "mntr" &&
    profile.DataColorSpace == "RGB " &&
    profile.ProfileConnectionSpace == "XYZ " &&
    profile.Fingerprint.Length == 64 &&
    profile.ToArray().SequenceEqual(profileBytes),
    "ICC v4 header identity and defensive payload",
    failures);
byte[] minimalJpeg = [0xff, 0xd8, 0xff, 0xd9];
byte[] profiledJpeg = JpegIccProfileCodec.Insert(minimalJpeg, profile);
IccProfile? reconstructed = new JpegImageDecoder().ReadEmbeddedIccProfile(
    profiledJpeg,
    "image/jpeg");
Expect(
    reconstructed is not null && reconstructed.Fingerprint == profile.Fingerprint &&
    reconstructed.ToArray().SequenceEqual(profileBytes),
    "multi-segment JPEG ICC profile round trip",
    failures);
ExpectThrows<NotSupportedException>(
    () => new IccProfile(CreateTestIccProfile(128, majorVersion: 5)),
    "unsupported ICC version rejection",
    failures);
byte[] incompleteProfiledJpeg = (byte[])profiledJpeg.Clone();
incompleteProfiledJpeg[17] = 3;
ExpectThrows<InvalidDataException>(
    () => new JpegImageDecoder().ReadEmbeddedIccProfile(
        incompleteProfiledJpeg,
        "image/jpeg"),
    "incomplete JPEG ICC chunk rejection",
    failures);

IccProfile gammaProfile = new(CreateMatrixTrcIcc(CreateGammaCurve(2f)));
IccMatrixTrcTransform gammaTransform = new(gammaProfile);
LinearRgba gammaActual = gammaTransform.TransformEncodedRgb(
    0.5f,
    0.25f,
    0.75f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba gammaExpected = StandardLinearRgbConverter.FromXyzD50(
    0.25f,
    0.0625f,
    0.5625f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(gammaActual, gammaExpected, 0.0001f, "ICC curveType gamma transform", failures);

IccMatrixTrcTransform sampledTransform = new(
    new IccProfile(CreateMatrixTrcIcc(CreateSampledCurve([0f, 0.25f, 1f]))));
LinearRgba sampledActual = sampledTransform.TransformEncodedRgb(
    0.5f,
    0.5f,
    0.5f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba sampledExpected = StandardLinearRgbConverter.FromXyzD50(
    0.25f,
    0.25f,
    0.25f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(sampledActual, sampledExpected, 0.0001f, "ICC sampled curve transform", failures);

IccMatrixTrcTransform srgbParametricTransform = new(
    new IccProfile(CreateMatrixTrcIcc(CreateSrgbParametricCurve())));
const float encodedMidpoint = 0.5f;
float decodedMidpoint = MathF.Pow(
    (encodedMidpoint + 0.055f) / 1.055f,
    2.4f);
LinearRgba parametricActual = srgbParametricTransform.TransformEncodedRgb(
    encodedMidpoint,
    encodedMidpoint,
    encodedMidpoint,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba parametricExpected = StandardLinearRgbConverter.FromXyzD50(
    decodedMidpoint,
    decodedMidpoint,
    decodedMidpoint,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(
    parametricActual,
    parametricExpected,
    0.0002f,
    "ICC parametricCurveType sRGB transform",
    failures);

byte[] missingTagProfile = CreateMatrixTrcIcc(CreateGammaCurve(1f));
Encoding.ASCII.GetBytes("xxxx").CopyTo(missingTagProfile, 132 + 3 * 12);
ExpectThrows<NotSupportedException>(
    () => new IccMatrixTrcTransform(new IccProfile(missingTagProfile)),
    "ICC missing matrix/TRC tag rejection",
    failures);
byte[] labPcsProfile = CreateMatrixTrcIcc(CreateGammaCurve(1f));
Encoding.ASCII.GetBytes("Lab ").CopyTo(labPcsProfile, 20);
ExpectThrows<NotSupportedException>(
    () => new IccMatrixTrcTransform(new IccProfile(labPcsProfile)),
    "ICC Lab PCS matrix/TRC rejection",
    failures);

IccRgbToLinearTransform lut16Transform = new(
    new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateIdentityXyzLut16())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba lut16Actual = lut16Transform.TransformEncodedRgb(
    0.2f,
    0.4f,
    0.6f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
LinearRgba lut16Expected = StandardLinearRgbConverter.FromXyzD50(
    0.2f,
    0.4f,
    0.6f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
ExpectColorNear(
    lut16Actual,
    lut16Expected,
    0.0001f,
    "ICC lut16Type XYZ tetrahedral transform",
    failures);
Vector3[] tetrahedralOrderings =
[
    new(0.6f, 0.4f, 0.2f),
    new(0.6f, 0.2f, 0.4f),
    new(0.4f, 0.6f, 0.2f),
    new(0.2f, 0.6f, 0.4f),
    new(0.4f, 0.2f, 0.6f),
];
foreach (Vector3 ordering in tetrahedralOrderings)
{
    LinearRgba actual = lut16Transform.TransformEncodedRgb(
        ordering.X,
        ordering.Y,
        ordering.Z,
        1f,
        StandardColorSpaces.LinearDisplayP3);
    LinearRgba expected = StandardLinearRgbConverter.FromXyzD50(
        ordering.X,
        ordering.Y,
        ordering.Z,
        1f,
        StandardColorSpaces.LinearDisplayP3);
    ExpectColorNear(
        actual,
        expected,
        0.0001f,
        $"ICC tetrahedral ordering {ordering}",
        failures);
}

IccRgbToLinearTransform lut8LabTransform = new(
    new IccProfile(CreateLutProfile("A2B0", "Lab ", CreateWhiteLabLut8())),
    IccRenderingIntent.Perceptual);
LinearRgba lut8White = lut8LabTransform.TransformEncodedRgb(
    1f,
    1f,
    1f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba d50White = StandardLinearRgbConverter.FromXyzD50(
    0.9642f,
    1f,
    0.8249f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(lut8White, d50White, 0.0002f, "ICC lut8Type Lab transform", failures);

ExpectThrows<NotSupportedException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateLutProfile("A2B0", "XYZ ", CreateWhiteLabLut8())),
        IccRenderingIntent.Perceptual),
    "ambiguous ICC lut8Type XYZ rejection",
    failures);
ExpectThrows<NotSupportedException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateUnsupportedMpe())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "ICC v4 multi-process element fail-closed boundary",
    failures);
ExpectThrows<NotSupportedException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateLutProfile("A2B0", "XYZ ", CreateIdentityXyzLut16())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "missing requested ICC rendering intent rejection",
    failures);
ExpectThrows<InvalidDataException>(
    () => new IccRgbToLinearTransform(
        gammaProfile,
        IccRenderingIntent.IccAbsoluteColorimetric),
    "ICC absolute intent without D2B3 or media white rejection",
    failures);

IccRgbToLinearTransform absoluteMpeTransform = new(
    new IccProfile(CreateLutProfile("D2B3", "XYZ ", CreateScaledXyzMatrixMpe(2f))),
    IccRenderingIntent.IccAbsoluteColorimetric);
LinearRgba absoluteMpeActual = absoluteMpeTransform.TransformEncodedRgb(
    0.75f,
    0.75f,
    0.75f,
    1f,
    StandardColorSpaces.LinearRec2020);
LinearRgba absoluteMpeExpected = StandardLinearRgbConverter.FromXyzD50(
    1.5f,
    1.5f,
    1.5f,
    1f,
    StandardColorSpaces.LinearRec2020);
ExpectColorNear(
    absoluteMpeActual,
    absoluteMpeExpected,
    0.0002f,
    "ICC D2B3 direct absolute PCS and unclipped headroom",
    failures);
if (absoluteMpeTransform.RenderingIntent != IccRenderingIntent.IccAbsoluteColorimetric)
{
    failures.Add("ICC D2B3 transform did not preserve the selected absolute rendering intent.");
}

IccRgbToLinearTransform synthesizedAbsoluteTransform = new(
    new IccProfile(CreateTaggedProfile(
        "XYZ ",
        ("D2B1", CreateScaledXyzMatrixMpe(2f)),
        ("wtpt", CreateXyzTagData(0.4821f, 0.25f, 0.618675f)))),
    IccRenderingIntent.IccAbsoluteColorimetric);
LinearRgba synthesizedAbsoluteActual = synthesizedAbsoluteTransform.TransformEncodedRgb(
    0.8f,
    0.8f,
    0.8f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
LinearRgba synthesizedAbsoluteExpected = StandardLinearRgbConverter.FromXyzD50(
    0.8f,
    0.4f,
    1.2f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
ExpectColorNear(
    synthesizedAbsoluteActual,
    synthesizedAbsoluteExpected,
    0.0002f,
    "ICC absolute synthesis from D2B1 and asymmetric media white",
    failures);

IccRgbToLinearTransform unsupportedAbsoluteMpeFallback = new(
    new IccProfile(CreateTaggedProfile(
        "XYZ ",
        ("D2B3", CreateUnknownElementMpe()),
        ("A2B1", CreateIdentityXyzLut16()),
        ("wtpt", CreateXyzTagData(0.4821f, 0.5f, 0.41245f)))),
    IccRenderingIntent.IccAbsoluteColorimetric);
LinearRgba unsupportedAbsoluteFallbackActual =
    unsupportedAbsoluteMpeFallback.TransformEncodedRgb(
        0.5f,
        0.5f,
        0.5f,
        1f,
        StandardColorSpaces.LinearProPhotoRgb);
LinearRgba unsupportedAbsoluteFallbackExpected = StandardLinearRgbConverter.FromXyzD50(
    0.25f,
    0.25f,
    0.25f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(
    unsupportedAbsoluteFallbackActual,
    unsupportedAbsoluteFallbackExpected,
    0.0002f,
    "unsupported ICC D2B3 falls back to A2B1 plus media-white synthesis",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateTaggedProfile(
            "XYZ ",
            ("A2B1", CreateIdentityXyzLut16()),
            ("wtpt", new byte[20]))),
        IccRenderingIntent.IccAbsoluteColorimetric),
    "malformed ICC media white rejection",
    failures);

IccProfile outputProfile = new(
    CreateLutProfile("B2D1", "XYZ ", CreateScaledXyzMatrixMpe(2f)));
IccLinearToRgbTransform outputTransform = new(
    outputProfile,
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba outputSource = StandardLinearRgbConverter.FromXyzD50(
    0.75f,
    0.25f,
    0.125f,
    0.4f,
    StandardColorSpaces.LinearRec2020);
IccEncodedRgba encodedOutput = outputTransform.Transform(outputSource);
Expect(
    MathF.Abs(encodedOutput.Red - 1.5f) <= 0.0002f &&
    MathF.Abs(encodedOutput.Green - 0.5f) <= 0.0002f &&
    MathF.Abs(encodedOutput.Blue - 0.25f) <= 0.0002f &&
    MathF.Abs(encodedOutput.Alpha - 0.4f) <= 0.0002f &&
    ReferenceEquals(encodedOutput.Profile, outputProfile) &&
    outputTransform.RenderingIntent == IccRenderingIntent.MediaRelativeColorimetric,
    "ICC B2D1 PCSXYZ output transform and unclipped device headroom",
    failures);

IccProfile fourChannelOutputProfile = new(
    CreateLutProfile("B2D1", "XYZ ", CreateFourChannelIntermediateMpe()));
IccEncodedRgba fourChannelEncodedOutput = new IccLinearToRgbTransform(
    fourChannelOutputProfile).Transform(
        StandardLinearRgbConverter.FromXyzD50(
            0.2f,
            0.4f,
            0.6f,
            1f,
            StandardColorSpaces.LinearProPhotoRgb));
Expect(
    MathF.Abs(fourChannelEncodedOutput.Red - 0.24f) <= 0.0002f &&
    MathF.Abs(fourChannelEncodedOutput.Green - 0.48f) <= 0.0002f &&
    MathF.Abs(fourChannelEncodedOutput.Blue - 0.72f) <= 0.0002f,
    "ICC B2D four-channel matrix curve CLUT intermediate chain",
    failures);

IccProfile labOutputProfile = new(
    CreateLutProfile("B2D1", "Lab ", CreateLabOutputMatrixMpe()));
IccEncodedRgba labEncodedOutput = new IccLinearToRgbTransform(labOutputProfile).Transform(
    StandardLinearRgbConverter.FromXyzD50(
        0.9642f,
        1f,
        0.8249f,
        1f,
        StandardColorSpaces.LinearProPhotoRgb));
Expect(
    MathF.Abs(labEncodedOutput.Red - 1f) <= 0.0002f &&
    MathF.Abs(labEncodedOutput.Green - 0.5f) <= 0.0002f &&
    MathF.Abs(labEncodedOutput.Blue - 0.5f) <= 0.0002f,
    "ICC B2D1 PCSLAB input encoding",
    failures);

ExpectThrows<NotSupportedException>(
    () => new IccLinearToRgbTransform(
        new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateIdentityXyzLut16()))),
    "ICC output without B2D or implemented B2A rejection",
    failures);
ExpectThrows<InvalidDataException>(
    () => new IccLinearToRgbTransform(
        outputProfile,
        IccRenderingIntent.IccAbsoluteColorimetric),
    "ICC absolute output without B2D3 or media white rejection",
    failures);
ExpectThrows<NotSupportedException>(
    () => new IccLinearToRgbTransform(
        new IccProfile(CreateLutProfile("B2D1", "XYZ ", CreateUnknownElementMpe()))),
    "unsupported ICC B2D without B2A fallback rejection",
    failures);

IccProfile legacyOutputProfile = new(
    CreateLutProfile("B2A1", "XYZ ", CreateIdentityOutputXyzLut16()));
IccEncodedRgba legacyEncodedOutput = new IccLinearToRgbTransform(legacyOutputProfile).Transform(
    StandardLinearRgbConverter.FromXyzD50(
        0.8f,
        0.4f,
        0.2f,
        0.6f,
        StandardColorSpaces.LinearDisplayP3));
Expect(
    MathF.Abs(legacyEncodedOutput.Red - 0.4f) <= 0.0002f &&
    MathF.Abs(legacyEncodedOutput.Green - 0.2f) <= 0.0002f &&
    MathF.Abs(legacyEncodedOutput.Blue - 0.1f) <= 0.0002f &&
    MathF.Abs(legacyEncodedOutput.Alpha - 0.6f) <= 0.0002f,
    "ICC legacy B2A1 lut16 PCSXYZ encoding",
    failures);

IccProfile matrixLegacyOutputProfile = new(
    CreateLutProfile("B2A1", "XYZ ", CreateMatrixOutputXyzLut16()));
IccEncodedRgba matrixLegacyEncodedOutput = new IccLinearToRgbTransform(
    matrixLegacyOutputProfile).Transform(
        StandardLinearRgbConverter.FromXyzD50(
            0.8f,
            0.4f,
            0.2f,
            1f,
            StandardColorSpaces.LinearDisplayP3));
Expect(
    MathF.Abs(matrixLegacyEncodedOutput.Red - 0.2f) <= 0.0002f &&
    MathF.Abs(matrixLegacyEncodedOutput.Green - 0.4f) <= 0.0002f &&
    MathF.Abs(matrixLegacyEncodedOutput.Blue - 0.05f) <= 0.0002f,
    "ICC legacy B2A1 lut16 non-identity PCSXYZ input matrix",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateMatrixOutputXyzLut16())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "ICC legacy A2B non-identity matrix rejection",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccLinearToRgbTransform(
        new IccProfile(CreateLutProfile("B2A1", "Lab ", CreateMatrixOutputXyzLut16())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "ICC legacy B2A Lab non-identity matrix rejection",
    failures);

IccProfile modernOutputProfile = new(
    CreateLutProfile("B2A1", "XYZ ", CreateBOnlyXyzMba()));
IccEncodedRgba modernEncodedOutput = new IccLinearToRgbTransform(modernOutputProfile).Transform(
    StandardLinearRgbConverter.FromXyzD50(
        0.5f,
        0.5f,
        0.5f,
        1f,
        StandardColorSpaces.LinearRec2020));
float normalizedHalfXyz = 0.5f / (65535f / 32768f);
float squaredNormalizedHalfXyz = normalizedHalfXyz * normalizedHalfXyz;
Expect(
    MathF.Abs(modernEncodedOutput.Red - squaredNormalizedHalfXyz) <= 0.0002f &&
    MathF.Abs(modernEncodedOutput.Green - squaredNormalizedHalfXyz) <= 0.0002f &&
    MathF.Abs(modernEncodedOutput.Blue - squaredNormalizedHalfXyz) <= 0.0002f,
    "ICC v4 B2A1 mBA B-curve ordering",
    failures);

IccProfile fullModernOutputProfile = new(
    CreateLutProfile("B2A1", "XYZ ", CreateFullXyzMba()));
IccEncodedRgba fullModernEncodedOutput =
    new IccLinearToRgbTransform(fullModernOutputProfile).Transform(
        StandardLinearRgbConverter.FromXyzD50(
            0.5f,
            0.5f,
            0.5f,
            1f,
            StandardColorSpaces.LinearRec2020));
Expect(
    MathF.Abs(fullModernEncodedOutput.Red - 0.015625f) <= 0.0002f &&
    MathF.Abs(fullModernEncodedOutput.Green - 0.015625f) <= 0.0002f &&
    MathF.Abs(fullModernEncodedOutput.Blue - 0.015625f) <= 0.0002f,
    "ICC v4 full mBA B-matrix-M-CLUT-A ordering",
    failures);

IccProfile unsupportedB2dFallbackProfile = new(
    CreateTaggedProfile(
        "XYZ ",
        ("B2D1", CreateUnknownElementMpe()),
        ("B2A1", CreateIdentityOutputXyzLut16())));
IccEncodedRgba unsupportedB2dFallback =
    new IccLinearToRgbTransform(unsupportedB2dFallbackProfile).Transform(
        StandardLinearRgbConverter.FromXyzD50(
            0.6f,
            0.6f,
            0.6f,
            1f,
            StandardColorSpaces.LinearProPhotoRgb));
Expect(
    MathF.Abs(unsupportedB2dFallback.Red - 0.3f) <= 0.0002f &&
    MathF.Abs(unsupportedB2dFallback.Green - 0.3f) <= 0.0002f &&
    MathF.Abs(unsupportedB2dFallback.Blue - 0.3f) <= 0.0002f,
    "unsupported ICC B2D falls back to matching B2A",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccLinearToRgbTransform(
        new IccProfile(CreateDualLutProfile(
            "XYZ ",
            "B2D1",
            CreateMalformedMpe(),
            "B2A1",
            CreateIdentityOutputXyzLut16()))),
    "malformed ICC B2D rejects without B2A fallback",
    failures);

IccProfile directAbsoluteOutputProfile = new(
    CreateLutProfile("B2D3", "XYZ ", CreateScaledXyzMatrixMpe(2f)));
IccLinearToRgbTransform directAbsoluteOutputTransform = new(
    directAbsoluteOutputProfile,
    IccRenderingIntent.IccAbsoluteColorimetric);
IccEncodedRgba directAbsoluteOutput = directAbsoluteOutputTransform.Transform(
    StandardLinearRgbConverter.FromXyzD50(
        0.75f,
        0.5f,
        0.25f,
        1f,
        StandardColorSpaces.LinearRec2020));
Expect(
    MathF.Abs(directAbsoluteOutput.Red - 1.5f) <= 0.0002f &&
    MathF.Abs(directAbsoluteOutput.Green - 1f) <= 0.0002f &&
    MathF.Abs(directAbsoluteOutput.Blue - 0.5f) <= 0.0002f &&
    directAbsoluteOutputTransform.RenderingIntent == IccRenderingIntent.IccAbsoluteColorimetric,
    "ICC direct B2D3 absolute output and headroom",
    failures);

IccProfile synthesizedAbsoluteOutputProfile = new(
    CreateTaggedProfile(
        "XYZ ",
        ("B2D1", CreateScaledXyzMatrixMpe(1f)),
        ("wtpt", CreateXyzTagData(0.4821f, 0.25f, 0.618675f))));
IccEncodedRgba synthesizedAbsoluteOutput = new IccLinearToRgbTransform(
    synthesizedAbsoluteOutputProfile,
    IccRenderingIntent.IccAbsoluteColorimetric).Transform(
        StandardLinearRgbConverter.FromXyzD50(
            0.4f,
            0.2f,
            0.6f,
            1f,
            StandardColorSpaces.LinearDisplayP3));
Expect(
    MathF.Abs(synthesizedAbsoluteOutput.Red - 0.8f) <= 0.0002f &&
    MathF.Abs(synthesizedAbsoluteOutput.Green - 0.8f) <= 0.0002f &&
    MathF.Abs(synthesizedAbsoluteOutput.Blue - 0.8f) <= 0.0002f,
    "ICC absolute output synthesis through media-relative B2D1",
    failures);

IccProfile unsupportedAbsoluteOutputFallbackProfile = new(
    CreateTaggedProfile(
        "XYZ ",
        ("B2D3", CreateUnknownElementMpe()),
        ("B2A1", CreateIdentityOutputXyzLut16()),
        ("wtpt", CreateXyzTagData(0.4821f, 0.5f, 0.41245f))));
IccEncodedRgba unsupportedAbsoluteOutputFallback = new IccLinearToRgbTransform(
    unsupportedAbsoluteOutputFallbackProfile,
    IccRenderingIntent.IccAbsoluteColorimetric).Transform(
        StandardLinearRgbConverter.FromXyzD50(
            0.4f,
            0.4f,
            0.4f,
            1f,
            StandardColorSpaces.LinearProPhotoRgb));
Expect(
    MathF.Abs(unsupportedAbsoluteOutputFallback.Red - 0.4f) <= 0.0002f &&
    MathF.Abs(unsupportedAbsoluteOutputFallback.Green - 0.4f) <= 0.0002f &&
    MathF.Abs(unsupportedAbsoluteOutputFallback.Blue - 0.4f) <= 0.0002f,
    "unsupported ICC B2D3 falls back through media white and B2A1",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccLinearToRgbTransform(
        new IccProfile(CreateTaggedProfile(
            "XYZ ",
            ("B2D3", CreateMalformedMpe()),
            ("B2A1", CreateIdentityOutputXyzLut16()),
            ("wtpt", CreateXyzTagData(0.4821f, 0.5f, 0.41245f)))),
        IccRenderingIntent.IccAbsoluteColorimetric),
    "malformed ICC B2D3 rejects without relative fallback",
    failures);

IccProfile linkSourceProfile = new(
    CreateLutProfile("D2B1", "XYZ ", CreateDiagonalXyzMatrixMpe(0.9642f, 1f, 0.8249f)));
IccProfile linkDestinationProfile = new(
    CreateLutProfile("B2D1", "XYZ ", CreateScaledXyzMatrixMpe(1f)));
IccProfileLinkTransform unadjustedLink = new(linkSourceProfile, linkDestinationProfile);
IccEncodedRgba unadjustedLinked = unadjustedLink.TransformEncodedRgb(0.25f, 0.5f, 0.75f, 0.3f);
Expect(
    MathF.Abs(unadjustedLinked.Red - 0.24105f) <= 0.0002f &&
    MathF.Abs(unadjustedLinked.Green - 0.5f) <= 0.0002f &&
    MathF.Abs(unadjustedLinked.Blue - 0.618675f) <= 0.0002f &&
    MathF.Abs(unadjustedLinked.Alpha - 0.3f) <= 0.0002f &&
    ReferenceEquals(unadjustedLink.SourceProfile, linkSourceProfile) &&
    ReferenceEquals(unadjustedLink.DestinationProfile, linkDestinationProfile) &&
    unadjustedLink.BlackPointCompensation is null,
    "ICC dual-profile link without PCS adjustment",
    failures);

IccBlackPointCompensation explicitBpc = new(0f, 20f);
IccProfileLinkTransform compensatedLink = new(
    linkSourceProfile,
    linkDestinationProfile,
    IccRenderingIntent.MediaRelativeColorimetric,
    explicitBpc);
IccEncodedRgba compensatedBlack = compensatedLink.TransformEncodedRgb(0f, 0f, 0f, 1f);
float destinationBlackY = DecodeLabLightness(20f);
Expect(
    MathF.Abs(compensatedBlack.Red - 0.9642f * destinationBlackY) <= 0.0002f &&
    MathF.Abs(compensatedBlack.Green - destinationBlackY) <= 0.0002f &&
    MathF.Abs(compensatedBlack.Blue - 0.8249f * destinationBlackY) <= 0.0002f &&
    compensatedLink.BlackPointCompensation == explicitBpc,
    "ICC BPC maps source black to destination neutral black",
    failures);
IccEncodedRgba compensatedWhite = compensatedLink.TransformEncodedRgb(1f, 1f, 1f, 1f);
Expect(
    MathF.Abs(compensatedWhite.Red - 0.9642f) <= 0.0002f &&
    MathF.Abs(compensatedWhite.Green - 1f) <= 0.0002f &&
    MathF.Abs(compensatedWhite.Blue - 0.8249f) <= 0.0002f,
    "ICC BPC preserves PCS white",
    failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => new IccBlackPointCompensation(-0.01f, 10f),
    "ICC BPC negative source black rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => new IccBlackPointCompensation(0f, 100f),
    "ICC BPC white destination black rejection",
    failures);
ExpectThrows<ArgumentException>(
    () => new IccProfileLinkTransform(
        linkSourceProfile,
        linkDestinationProfile,
        IccRenderingIntent.IccAbsoluteColorimetric,
        explicitBpc),
    "ICC BPC with absolute intent rejection",
    failures);

IccProfile inversePolaritySource = new(
    CreateLutProfile("D2B1", "Lab ", CreateLabLightnessInputMpe(-100f, 100f)));
IccProfile relativeFloorDestination = new(
    CreateDualLutProfile(
        "Lab ",
        "D2B1",
        CreateLabLightnessInputMpe(80f, 20f),
        "B2D1",
        CreateLabLightnessOutputMpe(1f / 80f, -0.25f)));
IccBlackPointCompensation estimatedRelativeBpc = IccBlackPointEstimator.Estimate(
    inversePolaritySource,
    relativeFloorDestination);
Expect(
    MathF.Abs(estimatedRelativeBpc.SourceLightness) <= 0.0002f &&
    MathF.Abs(estimatedRelativeBpc.DestinationLightness - 20f) <= 0.02f,
    "ISO 18619 inverse-polarity source and relative destination straight-ramp estimate",
    failures);
IccProfileLinkTransform automaticBpcLink =
    IccProfileLinkTransform.CreateWithAutomaticBlackPointCompensation(
        inversePolaritySource,
        relativeFloorDestination);
Expect(
    automaticBpcLink.BlackPointCompensation == estimatedRelativeBpc,
    "ICC profile-link automatic BPC factory",
    failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => new IccProfileLinkTransformCache(0),
    "ICC profile-link cache zero-capacity rejection",
    failures);
IccProfileLinkTransformCache linkCache = new(capacity: 2);
IccProfileLinkTransform cachedUnadjusted = linkCache.GetOrCreate(
    linkSourceProfile,
    linkDestinationProfile);
IccProfileLinkTransform cachedUnadjustedAgain = linkCache.GetOrCreate(
    linkSourceProfile,
    linkDestinationProfile);
IccProfileLinkTransform cachedExplicit = linkCache.GetOrCreate(
    linkSourceProfile,
    linkDestinationProfile,
    blackPointCompensation: explicitBpc);
_ = linkCache.GetOrCreate(linkSourceProfile, linkDestinationProfile);
IccProfileLinkTransform cachedAutomatic =
    linkCache.GetOrCreateWithAutomaticBlackPointCompensation(
        inversePolaritySource,
        relativeFloorDestination);
IccProfileLinkTransform cachedExplicitAfterEviction = linkCache.GetOrCreate(
    linkSourceProfile,
    linkDestinationProfile,
    blackPointCompensation: explicitBpc);
Expect(
    ReferenceEquals(cachedUnadjusted, cachedUnadjustedAgain) &&
    !ReferenceEquals(cachedExplicit, cachedExplicitAfterEviction) &&
    cachedAutomatic.BlackPointCompensation == estimatedRelativeBpc &&
    linkCache.Count == linkCache.Capacity,
    "ICC profile-link cache identity, policy separation and LRU eviction",
    failures);

IccProfile equivalentSourceProfile = new(linkSourceProfile.ToArray());
IccProfile equivalentDestinationProfile = new(linkDestinationProfile.ToArray());
IccProfileLinkTransform equivalentPayloadLink = linkCache.GetOrCreate(
    equivalentSourceProfile,
    equivalentDestinationProfile);
Expect(
    !ReferenceEquals(equivalentPayloadLink, cachedUnadjusted) &&
    ReferenceEquals(equivalentPayloadLink.SourceProfile, equivalentSourceProfile) &&
    ReferenceEquals(equivalentPayloadLink.DestinationProfile, equivalentDestinationProfile),
    "ICC profile-link cache preserves exact profile object identity",
    failures);

IccProfileLinkTransformCache concurrentLinkCache = new(capacity: 4);
IccProfileLinkTransform?[] concurrentLinks = new IccProfileLinkTransform?[16];
Parallel.For(
    0,
    concurrentLinks.Length,
    index => concurrentLinks[index] = concurrentLinkCache.GetOrCreate(
        linkSourceProfile,
        linkDestinationProfile));
Expect(
    concurrentLinks.All(item => ReferenceEquals(item, concurrentLinks[0])) &&
    concurrentLinkCache.Count == 1,
    "ICC profile-link cache concurrent single compilation",
    failures);
IccProfileLinkTransformCache failedLinkCache = new(capacity: 2);
ExpectThrows<ArgumentException>(
    () => failedLinkCache.GetOrCreateWithAutomaticBlackPointCompensation(
        linkSourceProfile,
        linkDestinationProfile,
        IccRenderingIntent.IccAbsoluteColorimetric),
    "ICC profile-link cache failed compilation propagation",
    failures);
Expect(
    failedLinkCache.Count == 0,
    "ICC profile-link cache removes failed compilation",
    failures);
linkCache.Clear();
Expect(linkCache.Count == 0, "ICC profile-link cache clear", failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => new IccRgbToLinearTransformCache(0),
    "ICC input-transform cache zero-capacity rejection",
    failures);
IccRgbToLinearTransformCache inputTransformCache = new(capacity: 2);
IccRgbToLinearTransform cachedInput = inputTransformCache.GetOrCreate(linkSourceProfile);
IccRgbToLinearTransform equivalentPayloadInput = inputTransformCache.GetOrCreate(
    equivalentSourceProfile);
Expect(
    ReferenceEquals(cachedInput, equivalentPayloadInput) && inputTransformCache.Count == 1,
    "ICC input-transform cache shares byte-identical profile payloads",
    failures);
inputTransformCache.Clear();
Expect(inputTransformCache.Count == 0, "ICC input-transform cache clear", failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => new IccLinearToRgbTransformCache(0),
    "ICC output-transform cache zero-capacity rejection",
    failures);
IccLinearToRgbTransformCache outputTransformCache = new(capacity: 2);
IccLinearToRgbTransform cachedOutput = outputTransformCache.GetOrCreate(
    linkDestinationProfile);
IccLinearToRgbTransform cachedOutputAgain = outputTransformCache.GetOrCreate(
    linkDestinationProfile);
IccLinearToRgbTransform equivalentPayloadOutput = outputTransformCache.GetOrCreate(
    equivalentDestinationProfile);
Expect(
    ReferenceEquals(cachedOutput, cachedOutputAgain) &&
    !ReferenceEquals(cachedOutput, equivalentPayloadOutput) &&
    ReferenceEquals(equivalentPayloadOutput.Profile, equivalentDestinationProfile) &&
    outputTransformCache.Count == 2,
    "ICC output-transform cache preserves exact target-profile identity",
    failures);
outputTransformCache.Clear();
Expect(outputTransformCache.Count == 0, "ICC output-transform cache clear", failures);

IccProfile perceptualSource = new(
    CreateLutProfile("D2B0", "Lab ", CreateLabLightnessInputMpe(100f, 0f)));
IccProfile perceptualFloorDestination = new(
    CreateTaggedProfile(
        "Lab ",
        ("D2B0", CreateLabLightnessInputMpe(80f, 20f)),
        ("D2B1", CreateLabLightnessInputMpe(80f, 20f)),
        ("B2D0", CreateLabLightnessOutputMpe(1f / 80f, -0.25f))));
IccBlackPointCompensation estimatedPerceptualBpc = IccBlackPointEstimator.Estimate(
    perceptualSource,
    perceptualFloorDestination,
    IccRenderingIntent.Perceptual);
Expect(
    MathF.Abs(estimatedPerceptualBpc.SourceLightness) <= 0.0002f &&
    MathF.Abs(estimatedPerceptualBpc.DestinationLightness - 20f) <= 0.05f,
    "ISO 18619 perceptual destination shadow-curve estimate",
    failures);
IccRgbToLinearTransform relativeCachedInput = inputTransformCache.GetOrCreate(
    perceptualFloorDestination,
    IccRenderingIntent.MediaRelativeColorimetric);
IccRgbToLinearTransform perceptualCachedInput = inputTransformCache.GetOrCreate(
    perceptualFloorDestination,
    IccRenderingIntent.Perceptual);
Expect(
    !ReferenceEquals(relativeCachedInput, perceptualCachedInput) &&
    inputTransformCache.Count == 2,
    "ICC input-transform cache separates rendering intent",
    failures);
ExpectThrows<ArgumentException>(
    () => IccBlackPointEstimator.Estimate(
        linkSourceProfile,
        linkDestinationProfile,
        IccRenderingIntent.IccAbsoluteColorimetric),
    "ISO 18619 absolute-intent rejection",
    failures);

IccRgbToLinearTransform mabTransform = new(
    new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateFullXyzMab())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba mabActual = mabTransform.TransformEncodedRgb(
    0.5f,
    0.5f,
    0.5f,
    1f,
    StandardColorSpaces.LinearRec2020);
LinearRgba mabExpected = StandardLinearRgbConverter.FromXyzD50(
    0.0625f,
    0.0625f,
    0.0625f,
    1f,
    StandardColorSpaces.LinearRec2020);
ExpectColorNear(
    mabActual,
    mabExpected,
    0.0002f,
    "ICC v4 full A-CLUT-M-matrix-B pipeline",
    failures);

IccRgbToLinearTransform mabBOnlyTransform = new(
    new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateBOnlyXyzMab())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba mabBOnlyActual = mabBOnlyTransform.TransformEncodedRgb(
    0.5f,
    0.5f,
    0.5f,
    1f,
    StandardColorSpaces.LinearRec2020);
LinearRgba mabBOnlyExpected = StandardLinearRgbConverter.FromXyzD50(
    0.5f,
    0.5f,
    0.5f,
    1f,
    StandardColorSpaces.LinearRec2020);
ExpectColorNear(
    mabBOnlyActual,
    mabBOnlyExpected,
    0.0002f,
    "ICC v4 B-only unpadded final curve",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateLutProfile("A2B1", "XYZ ", CreateMabWithoutRequiredB())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "ICC lutAToBType missing B curves rejection",
    failures);

IccRgbToLinearTransform mpeTransform = new(
    new IccProfile(CreateDualLutProfile(
        "XYZ ",
        "D2B1",
        CreateFormulaClutMatrixMpe(),
        "A2B1",
        CreateIdentityXyzLut16())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba mpeActual = mpeTransform.TransformEncodedRgb(
    0.75f,
    0.75f,
    0.75f,
    1f,
    StandardColorSpaces.LinearRec2020);
LinearRgba mpeExpected = StandardLinearRgbConverter.FromXyzD50(
    1.125f,
    1.125f,
    1.125f,
    1f,
    StandardColorSpaces.LinearRec2020);
ExpectColorNear(
    mpeActual,
    mpeExpected,
    0.0002f,
    "ICC D2B formula-curve CLUT matrix precedence and unclipped float PCS",
    failures);

IccRgbToLinearTransform sampledMpeTransform = new(
    new IccProfile(CreateLutProfile("D2B1", "XYZ ", CreateSampledCurveMpe())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba sampledMpeActual = sampledMpeTransform.TransformEncodedRgb(
    0.5f,
    0.5f,
    0.5f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba sampledMpeExpected = StandardLinearRgbConverter.FromXyzD50(
    0.2f,
    0.2f,
    0.2f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(
    sampledMpeActual,
    sampledMpeExpected,
    0.0002f,
    "ICC D2B sampled segmented curves",
    failures);

IccRgbToLinearTransform labMpeTransform = new(
    new IccProfile(CreateLutProfile("D2B1", "Lab ", CreateLabMatrixMpe())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba labMpeActual = labMpeTransform.TransformEncodedRgb(
    0.5f,
    0f,
    0f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
float labMidpoint = 66f / 116f;
float labMidpointCube = labMidpoint * labMidpoint * labMidpoint;
LinearRgba labMpeExpected = StandardLinearRgbConverter.FromXyzD50(
    0.9642f * labMidpointCube,
    labMidpointCube,
    0.8249f * labMidpointCube,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(
    labMpeActual,
    labMpeExpected,
    0.0002f,
    "ICC D2B float PCSLAB direct encoding",
    failures);

IccRgbToLinearTransform fourChannelMpeTransform = new(
    new IccProfile(CreateLutProfile("D2B1", "XYZ ", CreateFourChannelIntermediateMpe())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba fourChannelMpeActual = fourChannelMpeTransform.TransformEncodedRgb(
    0.2f,
    0.4f,
    0.6f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
LinearRgba fourChannelMpeExpected = StandardLinearRgbConverter.FromXyzD50(
    0.24f,
    0.48f,
    0.72f,
    1f,
    StandardColorSpaces.LinearProPhotoRgb);
ExpectColorNear(
    fourChannelMpeActual,
    fourChannelMpeExpected,
    0.0002f,
    "ICC D2B four-channel matrix curve CLUT intermediate chain",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateLutProfile("D2B1", "XYZ ", CreateDisconnectedChannelMpe())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "ICC MPE disconnected channel chain rejection",
    failures);

IccRgbToLinearTransform unsupportedMpeFallback = new(
    new IccProfile(CreateDualLutProfile(
        "XYZ ",
        "D2B1",
        CreateUnknownElementMpe(),
        "A2B1",
        CreateIdentityXyzLut16())),
    IccRenderingIntent.MediaRelativeColorimetric);
LinearRgba fallbackActual = unsupportedMpeFallback.TransformEncodedRgb(
    0.2f,
    0.4f,
    0.6f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
LinearRgba fallbackExpected = StandardLinearRgbConverter.FromXyzD50(
    0.2f,
    0.4f,
    0.6f,
    1f,
    StandardColorSpaces.LinearDisplayP3);
ExpectColorNear(
    fallbackActual,
    fallbackExpected,
    0.0002f,
    "unsupported ICC D2B element falls back to A2B",
    failures);

ExpectThrows<InvalidDataException>(
    () => new IccRgbToLinearTransform(
        new IccProfile(CreateDualLutProfile(
            "XYZ ",
            "D2B1",
            CreateMalformedMpe(),
            "A2B1",
            CreateIdentityXyzLut16())),
        IccRenderingIntent.MediaRelativeColorimetric),
    "malformed ICC D2B rejection without fallback",
    failures);

Expect(
    UltraHdrBackendInfo.NativeVersion == "v2.0.1" &&
    UltraHdrBackendInfo.JpegTurboVersion == "3.1.0",
    "packaged native codec version identity",
    failures);
AssemblyMetadataAttribute[] codecMetadata = typeof(UltraHdrBackendInfo).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .ToArray();
string? nativeVersionMetadata = codecMetadata
    .SingleOrDefault(static attribute => attribute.Key == "UltraHdrNativeVersion")?.Value;
string? jpegVersionMetadata = codecMetadata
    .SingleOrDefault(static attribute => attribute.Key == "JpegTurboNativeVersion")?.Value;
string? heifFeatureMetadata = codecMetadata
    .SingleOrDefault(static attribute => attribute.Key == "UltraHdrHeifEnabled")?.Value;
Expect(
    nativeVersionMetadata == UltraHdrBackendInfo.NativeVersion &&
    jpegVersionMetadata == UltraHdrBackendInfo.JpegTurboVersion &&
    bool.TryParse(heifFeatureMetadata, out bool metadataHeifEnabled) &&
    metadataHeifEnabled == UltraHdrBackendInfo.IsHeifEnabled,
    "managed codec identity matches packaged feature metadata",
    failures);
Expect(
    typeof(JpegImageDecoder).Assembly.GetType(
        "Mu3D.Native.UltraHdr.UltraHdrImageDecoder",
        throwOnError: false) is null,
    "public decoders use container-specific names rather than an Ultra HDR container name",
    failures);
ExpectThrows<NotSupportedException>(
    () => new JpegImageDecoder().DecodeColor(new byte[] { 1 }, "image/heif"),
    "JPEG decoder rejects HEIF independently of native feature set",
    failures);
TestFallbackImageDecoder heifFallback = new();
LinearRgbaImage routedFallbackImage = new HeifImageDecoder(heifFallback).DecodeColor(
    new byte[] { 1 },
    "image/application-owned");
NormalizedRgbaDataImage routedFallbackData = new HeifImageDecoder(heifFallback).DecodeData(
    new byte[] { 1 },
    "image/application-owned");
Expect(
    heifFallback.ColorDecodeCount == 1 &&
    heifFallback.DataDecodeCount == 1 &&
    routedFallbackImage.Name == "application fallback" &&
    routedFallbackData.Name == "application fallback",
    "HEIF decoder delegates non-HEIF MIME types to application fallback",
    failures);
ExpectThrows<NotSupportedException>(
    () => new HeifImageDecoder().DecodeColor(
        new byte[] { 1 },
        "image/application-owned"),
    "HEIF decoder requires an explicit fallback for other MIME types",
    failures);
if (!UltraHdrBackendInfo.IsHeifEnabled)
{
    ExpectThrows<NotSupportedException>(
        () => new HeifImageDecoder().DecodeColor(new byte[] { 1 }, "image/heif"),
        "default native feature set rejects HEIF before native loading",
        failures);
}

// Independently runnable ordinary JPEG checks; no Ultra HDR native library is needed.
if (Environment.GetEnvironmentVariable("MU3D_RUN_STANDARD_JPEG_TESTS") == "true")
{
    foreach (var encoding in Enum.GetValues<StandardRgbEncoding>())
    {
        var source = StandardRgbEncodingConverter.DecodeImage(8, 8,
            Enumerable.Repeat(new Vector4(.2f, .4f, .8f, 1), 64), encoding, StandardColorSpaces.AcesCg);
        byte[] jpeg = new JpegDocumentEncoder().Encode(source, encoding,
            new JpegDocumentEncodeOptions { Quality = 100 });
        var standardProfile = new JpegImageDecoder().ReadEmbeddedIccProfile(jpeg, "image/jpeg");
        Expect(standardProfile?.Fingerprint == StandardRgbProfiles.Get(encoding).Fingerprint,
            $"Standard JPEG {encoding} retains complete ICC", failures);
        if (Environment.GetEnvironmentVariable("MU3D_JPEG_TEST_OUTPUT") is string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"{encoding}.jpg"), jpeg);
        }
    }
    Console.WriteLine("Five native standard RGB JPEG exports and ICC readbacks completed.");
}

if (Environment.GetEnvironmentVariable("MU3D_RUN_ULTRAHDR_NATIVE_TESTS") == "true")
{
    JpegImageDecoder jpegDecoder = new();
    Vector4[] nativePixels = Enumerable.Range(0, 64)
        .Select(static index => (index % 4) switch
        {
            0 => new Vector4(0.02f, 0.02f, 0.02f, 1f),
            1 => new Vector4(0.5f, 0.25f, 0.1f, 1f),
            2 => new Vector4(1f, 1f, 1f, 1f),
            _ => new Vector4(2f, 1.25f, 0.5f, 1f),
        })
        .ToArray();
    LinearRgbaImage nativeSource = new(
        8,
        8,
        nativePixels,
        StandardColorSpaces.LinearSrgb,
        "native gain-map fixture");
    byte[] gainMapJpeg = encoder.Encode(nativeSource);
    LinearRgbaImage decodedGainMap = jpegDecoder.DecodeColor(
        gainMapJpeg,
        "image/jpeg",
        "native gain-map result");
    Expect(
        decodedGainMap.Width == nativeSource.Width &&
        decodedGainMap.Height == nativeSource.Height &&
        decodedGainMap.Pixels.Max(static pixel => pixel.X) > 1f,
        "host-native gain-map JPEG round trip retains HDR",
        failures);

    NormalizedRgbaDataImage decodedBase = jpegDecoder.DecodeData(
        gainMapJpeg,
        "image/jpeg",
        "native turbojpeg base result");
    Expect(
        decodedBase.Width == nativeSource.Width && decodedBase.Height == nativeSource.Height,
        "host-native turbojpeg decode path",
        failures);

    byte[] ordinaryJpeg = documentEncoder.Encode(
        CreateImage(new Vector4(0f, 0f, 0f, 1f)),
        documentOutputProfile);
    Expect(
        ordinaryJpeg is [0xff, 0xd8, .., 0xff, 0xd9],
        "host-native turbojpeg encode path",
        failures);
}

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine(
    "Validated gain-map and ordinary ICC JPEG export policy, professional RGB/CIEDE2000 metrics, and cached ICC input/output/profile-link, arbitrary-channel MPE, legacy output matrices, absolute, and ISO 18619 BPC transforms.");
return 0;

static LinearRgbaImage CreateImage(Vector4 pixel) =>
    new(1, 1, [pixel], StandardColorSpaces.LinearSrgb);

static (float X, float Y, float Z) LabToXyzD50ForTest(float l, float a, float b)
{
    float fy = (l + 16f) / 116f;
    float fx = fy + a / 500f;
    float fz = fy - b / 200f;
    return (
        0.9642f * Inverse(fx),
        Inverse(fy),
        0.8249f * Inverse(fz));

    static float Inverse(float value)
    {
        const float epsilon = 216f / 24389f;
        const float kappa = 24389f / 27f;
        float cube = value * value * value;
        return cube > epsilon ? cube : (116f * value - 16f) / kappa;
    }
}

static byte[] CreateTestIccProfile(int length, byte majorVersion)
{
    byte[] profile = new byte[length];
    BinaryPrimitives.WriteUInt32BigEndian(profile, checked((uint)length));
    profile[8] = majorVersion;
    profile[9] = 0x30;
    Encoding.ASCII.GetBytes("mntr").CopyTo(profile, 12);
    Encoding.ASCII.GetBytes("RGB ").CopyTo(profile, 16);
    Encoding.ASCII.GetBytes("XYZ ").CopyTo(profile, 20);
    Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);
    WriteS15Fixed16(profile.AsSpan(68, 4), 0.9642f);
    WriteS15Fixed16(profile.AsSpan(72, 4), 1f);
    WriteS15Fixed16(profile.AsSpan(76, 4), 0.8249f);
    return profile;
}

static byte[] CreateMatrixTrcIcc(byte[] curve)
{
    const int tagCount = 6;
    const int firstTagData = 132 + tagCount * 12;
    const int redXyzOffset = firstTagData;
    const int greenXyzOffset = redXyzOffset + 20;
    const int blueXyzOffset = greenXyzOffset + 20;
    const int curveOffset = blueXyzOffset + 20;
    int profileLength = (curveOffset + curve.Length + 3) & ~3;
    byte[] profile = CreateTestIccProfile(profileLength, majorVersion: 4);
    BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128, 4), tagCount);

    WriteTagEntry(profile, 0, "rXYZ", redXyzOffset, 20);
    WriteTagEntry(profile, 1, "gXYZ", greenXyzOffset, 20);
    WriteTagEntry(profile, 2, "bXYZ", blueXyzOffset, 20);
    WriteTagEntry(profile, 3, "rTRC", curveOffset, curve.Length);
    WriteTagEntry(profile, 4, "gTRC", curveOffset, curve.Length);
    WriteTagEntry(profile, 5, "bTRC", curveOffset, curve.Length);
    WriteXyzTag(profile, redXyzOffset, 1f, 0f, 0f);
    WriteXyzTag(profile, greenXyzOffset, 0f, 1f, 0f);
    WriteXyzTag(profile, blueXyzOffset, 0f, 0f, 1f);
    curve.CopyTo(profile, curveOffset);
    return profile;
}

static byte[] CreateLutProfile(string tagSignature, string pcs, byte[] lut)
{
    const int tagOffset = 144;
    int profileLength = (tagOffset + lut.Length + 3) & ~3;
    byte[] profile = CreateTestIccProfile(profileLength, majorVersion: 4);
    Encoding.ASCII.GetBytes(pcs).CopyTo(profile, 20);
    BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128, 4), 1u);
    WriteTagEntry(profile, 0, tagSignature, tagOffset, lut.Length);
    lut.CopyTo(profile, tagOffset);
    return profile;
}

static byte[] CreateDualLutProfile(
    string pcs,
    string firstSignature,
    byte[] firstLut,
    string secondSignature,
    byte[] secondLut)
{
    const int firstOffset = 156;
    int secondOffset = (firstOffset + firstLut.Length + 3) & ~3;
    int profileLength = (secondOffset + secondLut.Length + 3) & ~3;
    byte[] profile = CreateTestIccProfile(profileLength, majorVersion: 4);
    Encoding.ASCII.GetBytes(pcs).CopyTo(profile, 20);
    BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128, 4), 2u);
    WriteTagEntry(profile, 0, firstSignature, firstOffset, firstLut.Length);
    WriteTagEntry(profile, 1, secondSignature, secondOffset, secondLut.Length);
    firstLut.CopyTo(profile, firstOffset);
    secondLut.CopyTo(profile, secondOffset);
    return profile;
}

static byte[] CreateTaggedProfile(
    string pcs,
    params (string Signature, byte[] Data)[] tags)
{
    int firstTagData = checked(132 + tags.Length * 12);
    int offset = (firstTagData + 3) & ~3;
    int profileLength = offset;
    foreach ((string _, byte[] data) in tags)
    {
        profileLength = checked((profileLength + data.Length + 3) & ~3);
    }
    byte[] profile = CreateTestIccProfile(profileLength, majorVersion: 4);
    Encoding.ASCII.GetBytes(pcs).CopyTo(profile, 20);
    BinaryPrimitives.WriteUInt32BigEndian(
        profile.AsSpan(128, 4),
        checked((uint)tags.Length));
    for (int index = 0; index < tags.Length; index++)
    {
        (string signature, byte[] data) = tags[index];
        WriteTagEntry(profile, index, signature, offset, data.Length);
        data.CopyTo(profile, offset);
        offset = checked((offset + data.Length + 3) & ~3);
    }
    return profile;
}

static byte[] CreateXyzTagData(float x, float y, float z)
{
    byte[] tag = new byte[20];
    WriteXyzTag(tag, 0, x, y, z);
    return tag;
}

static byte[] CreateIdentityXyzLut16()
{
    const int inputValues = 6;
    const int clutValues = 24;
    const int outputValues = 6;
    byte[] lut = new byte[52 + 2 * (inputValues + clutValues + outputValues)];
    Encoding.ASCII.GetBytes("mft2").CopyTo(lut, 0);
    WriteLegacyLutHeader(lut);
    BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(48, 2), 2);
    BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(50, 2), 2);
    int offset = 52;
    for (int channel = 0; channel < 3; channel++)
    {
        WriteUInt16(lut, ref offset, 0);
        WriteUInt16(lut, ref offset, ushort.MaxValue);
    }
    for (int red = 0; red < 2; red++)
    {
        for (int green = 0; green < 2; green++)
        {
            for (int blue = 0; blue < 2; blue++)
            {
                WriteUInt16(lut, ref offset, red == 0 ? (ushort)0 : (ushort)32768);
                WriteUInt16(lut, ref offset, green == 0 ? (ushort)0 : (ushort)32768);
                WriteUInt16(lut, ref offset, blue == 0 ? (ushort)0 : (ushort)32768);
            }
        }
    }
    for (int channel = 0; channel < 3; channel++)
    {
        WriteUInt16(lut, ref offset, 0);
        WriteUInt16(lut, ref offset, ushort.MaxValue);
    }
    return lut;
}

static byte[] CreateIdentityOutputXyzLut16()
{
    byte[] lut = CreateIdentityXyzLut16();
    const int clutOffset = 52 + 12;
    int offset = clutOffset;
    for (int red = 0; red < 2; red++)
    {
        for (int green = 0; green < 2; green++)
        {
            for (int blue = 0; blue < 2; blue++)
            {
                WriteUInt16(lut, ref offset, red == 0 ? (ushort)0 : ushort.MaxValue);
                WriteUInt16(lut, ref offset, green == 0 ? (ushort)0 : ushort.MaxValue);
                WriteUInt16(lut, ref offset, blue == 0 ? (ushort)0 : ushort.MaxValue);
            }
        }
    }
    return lut;
}

static byte[] CreateMatrixOutputXyzLut16()
{
    byte[] lut = CreateIdentityOutputXyzLut16();
    for (int index = 0; index < 9; index++)
    {
        WriteS15Fixed16(lut.AsSpan(12 + index * 4, 4), 0f);
    }
    WriteS15Fixed16(lut.AsSpan(16, 4), 1f);
    WriteS15Fixed16(lut.AsSpan(24, 4), 1f);
    WriteS15Fixed16(lut.AsSpan(44, 4), 0.5f);
    return lut;
}

static byte[] CreateWhiteLabLut8()
{
    byte[] lut = new byte[48 + 3 * 256 + 2 * 2 * 2 * 3 + 3 * 256];
    Encoding.ASCII.GetBytes("mft1").CopyTo(lut, 0);
    WriteLegacyLutHeader(lut);
    int offset = 48;
    for (int channel = 0; channel < 3; channel++)
    {
        for (int value = 0; value < 256; value++)
        {
            lut[offset++] = (byte)value;
        }
    }
    for (int red = 0; red < 2; red++)
    {
        for (int green = 0; green < 2; green++)
        {
            for (int blue = 0; blue < 2; blue++)
            {
                lut[offset++] = red == 0 ? (byte)0 : byte.MaxValue;
                lut[offset++] = 128;
                lut[offset++] = 128;
            }
        }
    }
    for (int channel = 0; channel < 3; channel++)
    {
        for (int value = 0; value < 256; value++)
        {
            lut[offset++] = (byte)value;
        }
    }
    return lut;
}

static byte[] CreateUnsupportedMpe()
{
    byte[] value = new byte[16];
    Encoding.ASCII.GetBytes("mpet").CopyTo(value, 0);
    return value;
}

static byte[] CreateFormulaClutMatrixMpe()
{
    const int curveSetOffset = 40;
    const int curveSetSize = 156;
    const int clutOffset = curveSetOffset + curveSetSize;
    const int clutSize = 124;
    const int matrixOffset = clutOffset + clutSize;
    const int matrixSize = 60;
    byte[] mpe = new byte[matrixOffset + matrixSize];
    WriteMpeHeader(mpe, 3);
    WritePosition(mpe, 16, curveSetOffset, curveSetSize);
    WritePosition(mpe, 24, clutOffset, clutSize);
    WritePosition(mpe, 32, matrixOffset, matrixSize);

    Encoding.ASCII.GetBytes("cvst").CopyTo(mpe, curveSetOffset);
    WriteThreeChannels(mpe, curveSetOffset);
    for (int channel = 0; channel < 3; channel++)
    {
        int curveOffset = 36 + channel * 40;
        WritePosition(mpe, curveSetOffset + 12 + channel * 8, curveOffset, 40);
        WriteSingleFormulaCurve(mpe, curveSetOffset + curveOffset, 2f, 1f, 0f, 0f);
    }

    Encoding.ASCII.GetBytes("clut").CopyTo(mpe, clutOffset);
    WriteThreeChannels(mpe, clutOffset);
    mpe[clutOffset + 12] = 2;
    mpe[clutOffset + 13] = 2;
    mpe[clutOffset + 14] = 2;
    int valueOffset = clutOffset + 28;
    for (int red = 0; red < 2; red++)
    {
        for (int green = 0; green < 2; green++)
        {
            for (int blue = 0; blue < 2; blue++)
            {
                WriteFloat32(mpe.AsSpan(valueOffset, 4), red);
                WriteFloat32(mpe.AsSpan(valueOffset + 4, 4), green);
                WriteFloat32(mpe.AsSpan(valueOffset + 8, 4), blue);
                valueOffset += 12;
            }
        }
    }

    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, matrixOffset);
    WriteThreeChannels(mpe, matrixOffset);
    WriteFloat32(mpe.AsSpan(matrixOffset + 12, 4), 2f);
    WriteFloat32(mpe.AsSpan(matrixOffset + 28, 4), 2f);
    WriteFloat32(mpe.AsSpan(matrixOffset + 44, 4), 2f);
    return mpe;
}

static byte[] CreateSampledCurveMpe()
{
    const int elementOffset = 24;
    const int curveSize = 96;
    const int elementSize = 36 + 3 * curveSize;
    byte[] mpe = new byte[elementOffset + elementSize];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, elementSize);
    Encoding.ASCII.GetBytes("cvst").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    for (int channel = 0; channel < 3; channel++)
    {
        int relativeCurveOffset = 36 + channel * curveSize;
        WritePosition(mpe, elementOffset + 12 + channel * 8, relativeCurveOffset, curveSize);
        int curveOffset = elementOffset + relativeCurveOffset;
        Encoding.ASCII.GetBytes("curf").CopyTo(mpe, curveOffset);
        BinaryPrimitives.WriteUInt16BigEndian(mpe.AsSpan(curveOffset + 8, 2), 3);
        WriteFloat32(mpe.AsSpan(curveOffset + 12, 4), 0f);
        WriteFloat32(mpe.AsSpan(curveOffset + 16, 4), 1f);
        WriteFormulaSegment(mpe, curveOffset + 20, 1f, 1f, 0f, 0f);
        Encoding.ASCII.GetBytes("samf").CopyTo(mpe, curveOffset + 48);
        BinaryPrimitives.WriteUInt32BigEndian(mpe.AsSpan(curveOffset + 56, 4), 2u);
        WriteFloat32(mpe.AsSpan(curveOffset + 60, 4), 0.2f);
        WriteFloat32(mpe.AsSpan(curveOffset + 64, 4), 1f);
        WriteFormulaSegment(mpe, curveOffset + 68, 1f, 1f, 0f, 0f);
    }
    return mpe;
}

static byte[] CreateFourChannelIntermediateMpe()
{
    const int matrixToFourOffset = 48;
    const int matrixToFourSize = 76;
    const int curveSetOffset = matrixToFourOffset + matrixToFourSize;
    const int curveSetSize = 204;
    const int clutOffset = curveSetOffset + curveSetSize;
    const int clutSize = 284;
    const int matrixToThreeOffset = clutOffset + clutSize;
    const int matrixToThreeSize = 72;
    byte[] mpe = new byte[matrixToThreeOffset + matrixToThreeSize];
    WriteMpeHeader(mpe, 4);
    WritePosition(mpe, 16, matrixToFourOffset, matrixToFourSize);
    WritePosition(mpe, 24, curveSetOffset, curveSetSize);
    WritePosition(mpe, 32, clutOffset, clutSize);
    WritePosition(mpe, 40, matrixToThreeOffset, matrixToThreeSize);

    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, matrixToFourOffset);
    WriteChannels(mpe, matrixToFourOffset, 3, 4);
    WriteFloat32(mpe.AsSpan(matrixToFourOffset + 12, 4), 1f);
    WriteFloat32(mpe.AsSpan(matrixToFourOffset + 28, 4), 1f);
    WriteFloat32(mpe.AsSpan(matrixToFourOffset + 44, 4), 1f);
    WriteFloat32(mpe.AsSpan(matrixToFourOffset + 48, 4), 1f / 3f);
    WriteFloat32(mpe.AsSpan(matrixToFourOffset + 52, 4), 1f / 3f);
    WriteFloat32(mpe.AsSpan(matrixToFourOffset + 56, 4), 1f / 3f);

    Encoding.ASCII.GetBytes("cvst").CopyTo(mpe, curveSetOffset);
    WriteChannels(mpe, curveSetOffset, 4, 4);
    for (int channel = 0; channel < 4; channel++)
    {
        int relativeCurveOffset = 44 + channel * 40;
        WritePosition(mpe, curveSetOffset + 12 + channel * 8, relativeCurveOffset, 40);
        WriteSingleFormulaCurve(
            mpe,
            curveSetOffset + relativeCurveOffset,
            1f,
            1f,
            0f,
            0f);
    }

    Encoding.ASCII.GetBytes("clut").CopyTo(mpe, clutOffset);
    WriteChannels(mpe, clutOffset, 4, 4);
    for (int dimension = 0; dimension < 4; dimension++)
    {
        mpe[clutOffset + 12 + dimension] = 2;
    }
    int valueOffset = clutOffset + 28;
    for (int first = 0; first < 2; first++)
    {
        for (int second = 0; second < 2; second++)
        {
            for (int third = 0; third < 2; third++)
            {
                for (int fourth = 0; fourth < 2; fourth++)
                {
                    foreach (int value in new[] { first, second, third, fourth })
                    {
                        WriteFloat32(mpe.AsSpan(valueOffset, 4), value);
                        valueOffset += 4;
                    }
                }
            }
        }
    }

    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, matrixToThreeOffset);
    WriteChannels(mpe, matrixToThreeOffset, 4, 3);
    WriteFloat32(mpe.AsSpan(matrixToThreeOffset + 12, 4), 1f);
    WriteFloat32(mpe.AsSpan(matrixToThreeOffset + 24, 4), 0.1f);
    WriteFloat32(mpe.AsSpan(matrixToThreeOffset + 32, 4), 1f);
    WriteFloat32(mpe.AsSpan(matrixToThreeOffset + 40, 4), 0.2f);
    WriteFloat32(mpe.AsSpan(matrixToThreeOffset + 52, 4), 1f);
    WriteFloat32(mpe.AsSpan(matrixToThreeOffset + 56, 4), 0.3f);
    return mpe;
}

static byte[] CreateDisconnectedChannelMpe()
{
    const int tableEnd = 32;
    byte[] mpe = new byte[tableEnd + 24];
    WriteMpeHeader(mpe, 2);
    WritePosition(mpe, 16, tableEnd, 12);
    WritePosition(mpe, 24, tableEnd + 12, 12);
    Encoding.ASCII.GetBytes("bACS").CopyTo(mpe, tableEnd);
    WriteChannels(mpe, tableEnd, 3, 3);
    Encoding.ASCII.GetBytes("eACS").CopyTo(mpe, tableEnd + 12);
    WriteChannels(mpe, tableEnd + 12, 4, 4);
    return mpe;
}

static byte[] CreateLabMatrixMpe()
{
    const int elementOffset = 24;
    const int elementSize = 60;
    byte[] mpe = new byte[elementOffset + elementSize];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, elementSize);
    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    WriteFloat32(mpe.AsSpan(elementOffset + 12, 4), 100f);
    return mpe;
}

static byte[] CreateLabLightnessInputMpe(float scale, float offset)
{
    const int elementOffset = 24;
    const int elementSize = 60;
    byte[] mpe = new byte[elementOffset + elementSize];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, elementSize);
    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    WriteFloat32(mpe.AsSpan(elementOffset + 12, 4), scale);
    WriteFloat32(mpe.AsSpan(elementOffset + 48, 4), offset);
    return mpe;
}

static byte[] CreateLabLightnessOutputMpe(float scale, float offset)
{
    const int elementOffset = 24;
    const int elementSize = 60;
    byte[] mpe = new byte[elementOffset + elementSize];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, elementSize);
    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    WriteFloat32(mpe.AsSpan(elementOffset + 12, 4), scale);
    WriteFloat32(mpe.AsSpan(elementOffset + 24, 4), scale);
    WriteFloat32(mpe.AsSpan(elementOffset + 36, 4), scale);
    WriteFloat32(mpe.AsSpan(elementOffset + 48, 4), offset);
    WriteFloat32(mpe.AsSpan(elementOffset + 52, 4), offset);
    WriteFloat32(mpe.AsSpan(elementOffset + 56, 4), offset);
    return mpe;
}

static byte[] CreateScaledXyzMatrixMpe(float scale)
{
    const int elementOffset = 24;
    const int elementSize = 60;
    byte[] mpe = new byte[elementOffset + elementSize];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, elementSize);
    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    WriteFloat32(mpe.AsSpan(elementOffset + 12, 4), scale);
    WriteFloat32(mpe.AsSpan(elementOffset + 28, 4), scale);
    WriteFloat32(mpe.AsSpan(elementOffset + 44, 4), scale);
    return mpe;
}

static byte[] CreateDiagonalXyzMatrixMpe(float xScale, float yScale, float zScale)
{
    byte[] mpe = CreateScaledXyzMatrixMpe(0f);
    const int elementOffset = 24;
    WriteFloat32(mpe.AsSpan(elementOffset + 12, 4), xScale);
    WriteFloat32(mpe.AsSpan(elementOffset + 28, 4), yScale);
    WriteFloat32(mpe.AsSpan(elementOffset + 44, 4), zScale);
    return mpe;
}

static byte[] CreateLabOutputMatrixMpe()
{
    const int elementOffset = 24;
    const int elementSize = 60;
    byte[] mpe = new byte[elementOffset + elementSize];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, elementSize);
    Encoding.ASCII.GetBytes("matf").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    WriteFloat32(mpe.AsSpan(elementOffset + 12, 4), 0.01f);
    WriteFloat32(mpe.AsSpan(elementOffset + 28, 4), 0.01f);
    WriteFloat32(mpe.AsSpan(elementOffset + 44, 4), 0.01f);
    WriteFloat32(mpe.AsSpan(elementOffset + 52, 4), 0.5f);
    WriteFloat32(mpe.AsSpan(elementOffset + 56, 4), 0.5f);
    return mpe;
}

static byte[] CreateUnknownElementMpe()
{
    const int elementOffset = 24;
    byte[] mpe = new byte[36];
    WriteMpeHeader(mpe, 1);
    WritePosition(mpe, 16, elementOffset, 12);
    Encoding.ASCII.GetBytes("zzzz").CopyTo(mpe, elementOffset);
    WriteThreeChannels(mpe, elementOffset);
    return mpe;
}

static byte[] CreateMalformedMpe()
{
    byte[] mpe = CreateUnknownElementMpe();
    BinaryPrimitives.WriteUInt32BigEndian(mpe.AsSpan(16, 4), 28u);
    return mpe;
}

static void WriteMpeHeader(byte[] destination, int elementCount)
{
    Encoding.ASCII.GetBytes("mpet").CopyTo(destination, 0);
    WriteThreeChannels(destination, 0);
    BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(12, 4), checked((uint)elementCount));
}

static void WriteThreeChannels(byte[] destination, int offset)
{
    WriteChannels(destination, offset, 3, 3);
}

static void WriteChannels(byte[] destination, int offset, int inputChannels, int outputChannels)
{
    BinaryPrimitives.WriteUInt16BigEndian(
        destination.AsSpan(offset + 8, 2),
        checked((ushort)inputChannels));
    BinaryPrimitives.WriteUInt16BigEndian(
        destination.AsSpan(offset + 10, 2),
        checked((ushort)outputChannels));
}

static void WritePosition(byte[] destination, int offset, int valueOffset, int size)
{
    BinaryPrimitives.WriteUInt32BigEndian(
        destination.AsSpan(offset, 4),
        checked((uint)valueOffset));
    BinaryPrimitives.WriteUInt32BigEndian(
        destination.AsSpan(offset + 4, 4),
        checked((uint)size));
}

static void WriteSingleFormulaCurve(
    byte[] destination,
    int offset,
    float gamma,
    float scale,
    float bias,
    float constant)
{
    Encoding.ASCII.GetBytes("curf").CopyTo(destination, offset);
    BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset + 8, 2), 1);
    WriteFormulaSegment(destination, offset + 12, gamma, scale, bias, constant);
}

static void WriteFormulaSegment(
    byte[] destination,
    int offset,
    float gamma,
    float scale,
    float bias,
    float constant)
{
    Encoding.ASCII.GetBytes("parf").CopyTo(destination, offset);
    WriteFloat32(destination.AsSpan(offset + 12, 4), gamma);
    WriteFloat32(destination.AsSpan(offset + 16, 4), scale);
    WriteFloat32(destination.AsSpan(offset + 20, 4), bias);
    WriteFloat32(destination.AsSpan(offset + 24, 4), constant);
}

static void WriteFloat32(Span<byte> destination, float value) =>
    BinaryPrimitives.WriteInt32BigEndian(destination, BitConverter.SingleToInt32Bits(value));

static byte[] CreateFullXyzMab()
{
    const int bOffset = 32;
    const int matrixOffset = 68;
    const int mOffset = 116;
    const int clutOffset = 164;
    const int aOffset = 256;
    byte[] mab = new byte[304];
    Encoding.ASCII.GetBytes("mAB ").CopyTo(mab, 0);
    mab[8] = 3;
    mab[9] = 3;
    BinaryPrimitives.WriteUInt32BigEndian(mab.AsSpan(12, 4), bOffset);
    BinaryPrimitives.WriteUInt32BigEndian(mab.AsSpan(16, 4), matrixOffset);
    BinaryPrimitives.WriteUInt32BigEndian(mab.AsSpan(20, 4), mOffset);
    BinaryPrimitives.WriteUInt32BigEndian(mab.AsSpan(24, 4), clutOffset);
    BinaryPrimitives.WriteUInt32BigEndian(mab.AsSpan(28, 4), aOffset);

    int offset = bOffset;
    for (int channel = 0; channel < 3; channel++)
    {
        offset = WriteEmbeddedIdentityCurve(mab, offset);
    }
    WriteS15Fixed16(mab.AsSpan(matrixOffset, 4), 2f);
    WriteS15Fixed16(mab.AsSpan(matrixOffset + 16, 4), 2f);
    WriteS15Fixed16(mab.AsSpan(matrixOffset + 32, 4), 2f);
    offset = mOffset;
    for (int channel = 0; channel < 3; channel++)
    {
        offset = WriteEmbeddedGammaCurve(mab, offset, 2f);
    }

    mab[clutOffset] = 2;
    mab[clutOffset + 1] = 3;
    mab[clutOffset + 2] = 2;
    mab[clutOffset + 16] = 2;
    offset = clutOffset + 20;
    for (int red = 0; red < 2; red++)
    {
        for (int green = 0; green < 3; green++)
        {
            for (int blue = 0; blue < 2; blue++)
            {
                WriteUInt16(mab, ref offset, checked((ushort)MathF.Round(red * 32768f)));
                WriteUInt16(mab, ref offset, checked((ushort)MathF.Round(green * 0.5f * 32768f)));
                WriteUInt16(mab, ref offset, checked((ushort)MathF.Round(blue * 32768f)));
            }
        }
    }
    offset = aOffset;
    for (int channel = 0; channel < 3; channel++)
    {
        offset = WriteEmbeddedGammaCurve(mab, offset, 2f);
    }
    return mab;
}

static byte[] CreateFullXyzMba()
{
    byte[] mba = CreateFullXyzMab();
    Encoding.ASCII.GetBytes("mBA ").CopyTo(mba, 0);
    return mba;
}

static byte[] CreateMabWithoutRequiredB()
{
    byte[] mab = new byte[32];
    Encoding.ASCII.GetBytes("mAB ").CopyTo(mab, 0);
    mab[8] = 3;
    mab[9] = 3;
    return mab;
}

static byte[] CreateBOnlyXyzMab()
{
    const int bOffset = 32;
    byte[] mab = new byte[78];
    Encoding.ASCII.GetBytes("mAB ").CopyTo(mab, 0);
    mab[8] = 3;
    mab[9] = 3;
    BinaryPrimitives.WriteUInt32BigEndian(mab.AsSpan(12, 4), bOffset);
    int offset = bOffset;
    for (int channel = 0; channel < 3; channel++)
    {
        offset = WriteEmbeddedGammaCurve(mab, offset, 2f);
    }
    return mab;
}

static byte[] CreateBOnlyXyzMba()
{
    byte[] mba = CreateBOnlyXyzMab();
    Encoding.ASCII.GetBytes("mBA ").CopyTo(mba, 0);
    return mba;
}

static int WriteEmbeddedIdentityCurve(byte[] destination, int offset)
{
    Encoding.ASCII.GetBytes("curv").CopyTo(destination, offset);
    BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset + 8, 4), 0u);
    return offset + 12;
}

static int WriteEmbeddedGammaCurve(byte[] destination, int offset, float gamma)
{
    Encoding.ASCII.GetBytes("curv").CopyTo(destination, offset);
    BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset + 8, 4), 1u);
    BinaryPrimitives.WriteUInt16BigEndian(
        destination.AsSpan(offset + 12, 2),
        checked((ushort)MathF.Round(gamma * 256f)));
    return offset + 16;
}

static void WriteLegacyLutHeader(byte[] lut)
{
    lut[8] = 3;
    lut[9] = 3;
    lut[10] = 2;
    WriteS15Fixed16(lut.AsSpan(12, 4), 1f);
    WriteS15Fixed16(lut.AsSpan(28, 4), 1f);
    WriteS15Fixed16(lut.AsSpan(44, 4), 1f);
}

static void WriteUInt16(byte[] destination, ref int offset, ushort value)
{
    BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset, 2), value);
    offset += 2;
}

static byte[] CreateGammaCurve(float gamma)
{
    byte[] curve = new byte[14];
    Encoding.ASCII.GetBytes("curv").CopyTo(curve, 0);
    BinaryPrimitives.WriteUInt32BigEndian(curve.AsSpan(8, 4), 1u);
    BinaryPrimitives.WriteUInt16BigEndian(
        curve.AsSpan(12, 2),
        checked((ushort)MathF.Round(gamma * 256f)));
    return curve;
}

static byte[] CreateSampledCurve(ReadOnlySpan<float> values)
{
    byte[] curve = new byte[12 + values.Length * 2];
    Encoding.ASCII.GetBytes("curv").CopyTo(curve, 0);
    BinaryPrimitives.WriteUInt32BigEndian(curve.AsSpan(8, 4), checked((uint)values.Length));
    for (int index = 0; index < values.Length; index++)
    {
        BinaryPrimitives.WriteUInt16BigEndian(
            curve.AsSpan(12 + index * 2, 2),
            checked((ushort)MathF.Round(values[index] * 65535f)));
    }
    return curve;
}

static byte[] CreateSrgbParametricCurve()
{
    float[] parameters =
    [
        2.4f,
        1f / 1.055f,
        0.055f / 1.055f,
        1f / 12.92f,
        0.04045f,
        0f,
        0f,
    ];
    byte[] curve = new byte[12 + parameters.Length * 4];
    Encoding.ASCII.GetBytes("para").CopyTo(curve, 0);
    BinaryPrimitives.WriteUInt16BigEndian(curve.AsSpan(8, 2), 4);
    for (int index = 0; index < parameters.Length; index++)
    {
        WriteS15Fixed16(curve.AsSpan(12 + index * 4, 4), parameters[index]);
    }
    return curve;
}

static void WriteTagEntry(
    byte[] profile,
    int index,
    string signature,
    int offset,
    int size)
{
    int entry = 132 + index * 12;
    Encoding.ASCII.GetBytes(signature).CopyTo(profile, entry);
    BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(entry + 4, 4), checked((uint)offset));
    BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(entry + 8, 4), checked((uint)size));
}

static void WriteXyzTag(byte[] profile, int offset, float x, float y, float z)
{
    Encoding.ASCII.GetBytes("XYZ ").CopyTo(profile, offset);
    WriteS15Fixed16(profile.AsSpan(offset + 8, 4), x);
    WriteS15Fixed16(profile.AsSpan(offset + 12, 4), y);
    WriteS15Fixed16(profile.AsSpan(offset + 16, 4), z);
}

static void WriteS15Fixed16(Span<byte> destination, float value) =>
    BinaryPrimitives.WriteInt32BigEndian(destination, checked((int)MathF.Round(value * 65536f)));

static float DecodeLabLightness(float lightness)
{
    const float transition = 8f;
    float transitionValue = MathF.Pow((transition + 16f) / 116f, 3f);
    return lightness <= transition
        ? lightness * transitionValue / transition
        : MathF.Pow((lightness + 16f) / 116f, 3f);
}

static void ExpectColorNear(
    LinearRgba actual,
    LinearRgba expected,
    float tolerance,
    string name,
    ICollection<string> failures)
{
    bool matches =
        MathF.Abs(actual.Red - expected.Red) <= tolerance &&
        MathF.Abs(actual.Green - expected.Green) <= tolerance &&
        MathF.Abs(actual.Blue - expected.Blue) <= tolerance &&
        MathF.Abs(actual.Alpha - expected.Alpha) <= tolerance &&
        actual.ColorSpace == expected.ColorSpace;
    if (!matches)
    {
        failures.Add(
            $"FAILED: {name}; actual=({actual.Red:R}, {actual.Green:R}, {actual.Blue:R}, {actual.Alpha:R}), " +
            $"expected=({expected.Red:R}, {expected.Green:R}, {expected.Blue:R}, {expected.Alpha:R})");
    }
}

static void Expect(bool condition, string name, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add($"FAILED: {name}");
    }
}

static void ExpectThrows<T>(
    Action action,
    string name,
    ICollection<string> failures)
    where T : Exception
{
    try
    {
        action();
        failures.Add($"FAILED: {name} did not throw {typeof(T).Name}");
    }
    catch (T)
    {
    }
    catch (Exception error)
    {
        failures.Add($"FAILED: {name} threw {error.GetType().Name}");
    }
}

sealed record TestColorSpace() : ColorSpaceReference("Test linear space", isLinear: true);

sealed class TestFallbackImageDecoder : IEncodedImageDecoder
{
    public int ColorDecodeCount { get; private set; }

    public int DataDecodeCount { get; private set; }

    public LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        ColorDecodeCount++;
        return new LinearRgbaImage(
            1,
            1,
            [Vector4.One],
            StandardColorSpaces.LinearSrgb,
            "application fallback");
    }

    public NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        DataDecodeCount++;
        return new NormalizedRgbaDataImage(
            1,
            1,
            [Vector4.One],
            "application fallback");
    }
}
