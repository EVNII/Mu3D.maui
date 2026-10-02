using System.Numerics;
using Mu3D.Color;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
void Near(float actual, double expected, double tolerance, string message) =>
    Check(float.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{message}: {actual:R} != {expected:R}");
void Near3(Vector3 actual, Vector3 expected, float tolerance, string message)
{ Near(actual.X, expected.X, tolerance, message + " X"); Near(actual.Y, expected.Y, tolerance, message + " Y"); Near(actual.Z, expected.Z, tolerance, message + " Z"); }
void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new InvalidOperationException(message);
}

// Independent published transfer-function anchors, not comparisons to a second call path.
Near(HdrTransferFunctions.EncodePqNits(100), 0.5080784215, 6e-8, "PQ 100 nits");
Near(HdrTransferFunctions.EncodePqNits(1000), 0.7518270962, 6e-8, "PQ 1000 nits");
Near(HdrTransferFunctions.EncodePqNits(10000), 1, 0, "PQ peak");
Near(HdrTransferFunctions.EncodePqNits(0), 7.3095590258e-7, 1e-13, "PQ mathematical black");
Near(HdrTransferFunctions.DecodePqNits(0), 0, 0, "PQ code zero");
Near(HdrTransferFunctions.DecodePqNits(1), 10000, 0, "PQ peak inverse");
for (int i = 0; i <= 100; i++)
{
    float value = MathF.Pow(10, -4 + i * 0.08f);
    Near(HdrTransferFunctions.DecodePqNits(HdrTransferFunctions.EncodePqNits(value)), value,
        Math.Max(2e-9, value * 7e-7), "PQ logarithmic roundtrip");
}
Near(HdrTransferFunctions.EncodeHlgScene(1f / 12), 0.5, 1e-7, "HLG breakpoint");
Near(HdrTransferFunctions.DecodeHlgScene(0.5f), 1.0 / 12, 1e-8, "HLG inverse breakpoint");
Vector3 neutral75 = HdrTransferFunctions.DecodeHlgToLinearRec2020(new(0.75f), 1, 1000, 1.2f);
Near(neutral75.X, 203.152145938, 0.0001, "HLG 75% neutral luminance");
Vector3 blue = HdrTransferFunctions.DecodeHlgToLinearRec2020(Vector3.UnitZ, 1, 1000, 1.2f);
Near(blue.Z, 1000 * Math.Pow(0.0593, 0.2), 0.0002, "HLG saturated blue luminance-coupled OOTF");
Check(blue.Z < 600, "HLG is not independent per-channel gamma");
Near3(HdrTransferFunctions.DecodeHlgToLinearRec2020(Vector3.Zero, 203, 1000, 0.8f), Vector3.Zero, 0, "HLG black with gamma below one");
for (int i = 0; i <= 40; i++)
{
    Vector3 encoded = new(i / 40f, (40 - i) / 40f, 0.5f);
    Vector3 linear = HdrTransferFunctions.DecodeHlgToLinearRec2020(encoded, 203, 1000, 1.2f);
    Near3(HdrTransferFunctions.EncodeHlgFromLinearRec2020(linear, 203, 1000, 1.2f), encoded, 2e-7f, "HLG colored roundtrip");
}
Throws<ArgumentOutOfRangeException>(() => HdrTransferFunctions.EncodePqNits(-1), "PQ negative rejection");
Throws<ArgumentOutOfRangeException>(() => HdrTransferFunctions.EncodePqNits(10001), "PQ range rejection");
Throws<ArgumentOutOfRangeException>(() => HdrTransferFunctions.DecodePqNits(float.NaN), "PQ nonfinite rejection");
Throws<ArgumentOutOfRangeException>(() => HdrTransferFunctions.DecodeHlgToLinearRec2020(Vector3.One, 0, 1000, 1.2f), "HLG requires reference white");
Throws<ArgumentOutOfRangeException>(() => HdrTransferFunctions.EncodeHlgFromLinearRec2020(new(0, 0, 1000), 1, 1000, 1.2f), "HLG extended blue signal explicitly rejected");

// Official data anchors and sums test the embedded reference tables, not an invented observer.
SpectralObserver two = Cie2015Observers.TwoDegree, ten = Cie2015Observers.TenDegree;
Check(two.IsStandard && ten.IsStandard && two.ObserverAgeYears is null && ten.ObserverAgeYears is null, "Fixed standard metadata");
Check(two.MatchingFunctions.Length == 441 && ten.MatchingFunctions.Length == 441, "Official grid length");
Check(two.DataSha256 == "a10751ec8aecdb023f16ba079557e7fb794806884fe3da982dd63da285f872a9", "2 degree official hash");
Check(ten.DataSha256 == "9019a35f8f51215e245f818e87d4251147d1925a8fbe9a49944fe7f011f16e38", "10 degree official hash");
Near3(two.MatchingFunctions[119], new(0.01190224f, 0.5011430f, 0.1268279f), 0, "CIE official 509 nm 2 degree row");
Near3(ten.MatchingFunctions[119], new(0.03199910f, 0.6096388f, 0.09682266f), 0, "CIE official 509 nm 10 degree row");
float[] equal = Enumerable.Repeat(1f, 441).ToArray();
SpectralPowerDistribution equalEnergy = new(390, 1, equal, SpectralPowerUnit.RelativePerNanometre, "Equal energy test");
Vector3 sumTwo = SpectralIntegrator.Integrate(equalEnergy, two).Xyz;
Vector3 sumTen = SpectralIntegrator.Integrate(equalEnergy, ten).Xyz;
// Official column sums minus half of each endpoint for the piecewise-linear integral.
Near(sumTwo.X, 113.042318330084 - (0.003769647 + 0.000001762465) / 2, 1e-5, "CIE 2 integral X");
Near(sumTwo.Y, 113.0423145713364 - (0.0004146161 + 0.000000705386) / 2, 1e-5, "CIE 2 integral Y");
Near(sumTwo.Z, 113.04231479170 - 0.0184726 / 2, 1e-5, "CIE 2 integral Z");
Near(sumTen.X, 118.518090897436 - (0.00295242 + 0.000001579199) / 2, 1e-5, "CIE 10 integral X");
Near(sumTen.Y, 118.5180915321789 - (0.0004076779 + 0.000000634538) / 2, 1e-5, "CIE 10 integral Y");
Near(sumTen.Z, 118.518095303296 - 0.01318752 / 2, 1e-5, "CIE 10 integral Z");
equal[0] = 500;
Near(equalEnergy.Samples[0], 1, 0, "SPD defensive copy");
Vector3[] customCurves = [new(0, 1, 1), new(1, 1, 0)];
SpectralObserver custom = new("Analytic ramp", 4, 42, "Synthetic test curves", 400, 10, customCurves);
SpectralPowerDistribution ramp = new(400, 5, [0, 0.5f, 1], SpectralPowerUnit.RelativePerNanometre, "Analytic ramp");
Near3(SpectralIntegrator.Integrate(ramp, custom).Xyz, new(10f / 3, 5, 10f / 6), 1e-6f, "Exact product of interpolants on unequal grids");
customCurves[1] = Vector3.Zero;
Near(custom.MatchingFunctions[1].X, 1, 0, "Observer defensive copy");
Check(custom.ObserverAgeYears == 42 && !custom.IsStandard, "Custom age metadata preserves supplied curve identity");
SpectralPowerDistribution partial = new(402, 1, [1, 1], SpectralPowerUnit.RelativePerNanometre, "Truncated measurement");
Throws<ArgumentException>(() => SpectralIntegrator.Integrate(partial, custom), "Incomplete spectrum explicit rejection");
Near3(SpectralIntegrator.Integrate(partial, custom, SpectralCoveragePolicy.ZeroOutsideSpectrum).Xyz,
    new(0.25f, 1, 0.75f), 1e-7f, "Explicit zero outside truncated spectrum");
Throws<ArgumentOutOfRangeException>(() => new SpectralPowerDistribution(400, 1, [-1, 1], SpectralPowerUnit.RelativePerNanometre, "invalid"), "Negative SPD rejected");

// A measured three-primary model constrains an otherwise nonunique observer conversion.
SpectralPowerDistribution Primary(float center) => new(390, 1,
    Enumerable.Range(390, 441).Select(w => MathF.Max(0, 1 - MathF.Abs(w - center) / 10)).ToArray(),
    SpectralPowerUnit.WattsPerSquareMetreSteradianNanometre, $"Synthetic triangular primary at {center} nm");
SpectralRgbDisplayModel display = new(Primary(630), Primary(530), Primary(450), "Narrowband test display");
Vector3 drive = new(0.7f, 0.4f, 0.2f);
Vector3 sourceXyz = display.Evaluate(drive, two).Xyz;
Vector3 destinationXyz = display.Evaluate(drive, ten).Xyz;
Near3(Vector3.Transform(sourceXyz, display.GetObserverConversionMatrix(two, ten)), destinationXyz, 3e-6f, "Measured-primary observer conversion");
Check(Vector3.Distance(sourceXyz, destinationXyz) > 0.1f, "CIE observers meaningfully differ");
Near3(Vector3.Transform(sourceXyz, display.GetObserverConversionMatrix(two, two)), sourceXyz, 3e-6f, "Observer identity conversion");
SpectralRgbDisplayModel degenerate = new(display.Red, display.Red, display.Red, "Dependent primaries");
Throws<ArgumentException>(() => degenerate.GetObserverConversionMatrix(two, ten), "Dependent primary responses rejected");
foreach (float spectralScale in new[] { 1e-15f, 1e15f })
{
    SpectralPowerDistribution Scaled(SpectralPowerDistribution original) => new(original.FirstWavelengthNanometres,
        original.WavelengthStepNanometres, original.Samples.ToArray().Select(v => v * spectralScale).ToArray(), original.Unit, "Rescaled calibration test");
    SpectralRgbDisplayModel scaled = new(Scaled(display.Red), Scaled(display.Green), Scaled(display.Blue), "Rescaled primaries");
    Near3(Vector3.Transform(sourceXyz, scaled.GetObserverConversionMatrix(two, ten)), destinationXyz, 4e-6f,
        "Observer conversion is invariant under physical SPD unit scale");
}

// A synthetic RGB ICC device has independently specified forward/inverse matrices and paper white.
IccProfile proof = IccFixtures.ProofProfile();
IccProfile monitor = IccFixtures.DisplayProfile(1);
IccSoftProofOptions options = new();
IccSoftProofTransform softProof = new(proof, options, monitor);
LinearRgba source = StandardLinearRgbConverter.FromXyzD50(0.9642f * 0.5f, 0.5f, 0.8249f * 0.5f, 0.37f, StandardColorSpaces.LinearSrgb);
LinearRgba savedSource = source;
IccSoftProofDisplayResult result = softProof.TransformToDisplay(source);
Near(result.Color.Red, 0.4, 1e-5, "Absolute proof paper X");
Near(result.Color.Green, 0.375, 1e-5, "Absolute proof paper Y");
Near(result.Color.Blue, 0.275, 1e-5, "Absolute proof paper Z");
Near(result.Color.Alpha, 0.37, 1e-7, "Proof alpha preserved");
Check(ReferenceEquals(result.Color.Profile, monitor), "Display ICC identity retained");
Check(source == savedSource && !result.ProofWasClipped && !result.DisplayWasClipped, "Source preserved and clipping flags accurate");
IccSoftProofLinearResult linearProof = softProof.TransformToLinear(source, StandardColorSpaces.LinearDisplayP3);
LinearRgba expectedProof = StandardLinearRgbConverter.FromXyzD50(0.4f, 0.375f, 0.275f, 0.37f, StandardColorSpaces.LinearDisplayP3);
Near3(new(linearProof.Color.Red, linearProof.Color.Green, linearProof.Color.Blue), new(expectedProof.Red, expectedProof.Green, expectedProof.Blue), 2e-5f, "Linear destination proof");
Check(ReferenceEquals(linearProof.Color.ColorSpace, StandardColorSpaces.LinearDisplayP3), "Linear destination tagged");
IccSoftProofTransform perceptual = new(proof, options with { ProofIntent = IccRenderingIntent.Perceptual }, monitor);
LinearRgba quarter = StandardLinearRgbConverter.FromXyzD50(0.9642f * 0.25f, 0.25f, 0.8249f * 0.25f, 1, StandardColorSpaces.LinearSrgb);
Near(perceptual.TransformToDisplay(quarter).Color.Green, 0.375, 2e-5, "Proof output intent selected independently of absolute decode");
IccSoftProofTransform compensated = new(proof, options with { ProofBlackPointCompensation = new(0, 20) }, monitor);
LinearRgba black = new(0, 0, 0, 1, StandardColorSpaces.LinearSrgb);
Near(compensated.TransformToDisplay(black).Color.Green, 0.75 * Math.Pow(36.0 / 116, 3), 1e-6, "BPC maps working black before absolute proof simulation");
LinearRgba over = new(4, 4, 4, 1, StandardColorSpaces.LinearSrgb);
Throws<InvalidOperationException>(() => softProof.TransformToDisplay(over), "Out-of-range proof RGB rejected");
IccSoftProofTransform clippedProof = new(proof, options with { ProofRangePolicy = IccSoftProofRangePolicy.Clamp }, monitor);
IccSoftProofDisplayResult clipped = clippedProof.TransformToDisplay(over);
Check(clipped.ProofWasClipped && !clipped.DisplayWasClipped, "Explicit proof clipping flagged");
Near(clipped.Color.Green, 0.75, 2e-5, "Clipped proof reaches actual paper white");
IccProfile brightDisplay = IccFixtures.DisplayProfile(4);
Throws<InvalidOperationException>(() => new IccSoftProofTransform(proof, options, brightDisplay).TransformToDisplay(source), "Out-of-range display RGB rejected");
IccSoftProofDisplayResult clippedDisplay = new IccSoftProofTransform(proof, options with { DisplayRangePolicy = IccSoftProofRangePolicy.Clamp }, brightDisplay).TransformToDisplay(source);
Check(!clippedDisplay.ProofWasClipped && clippedDisplay.DisplayWasClipped, "Explicit display clipping flagged");
Near(clippedDisplay.Color.Green, 1, 0, "Display clamp");
Throws<ArgumentException>(() => new IccSoftProofTransform(proof, options with { ProofIntent = IccRenderingIntent.IccAbsoluteColorimetric, ProofBlackPointCompensation = new(0, 20) }), "Absolute proof intent rejects BPC");
Throws<ArgumentOutOfRangeException>(() => new IccSoftProofTransform(proof, options with { DisplayIntent = (IccRenderingIntent)99 }, monitor), "Invalid display intent rejected");
Throws<InvalidOperationException>(() => new IccSoftProofTransform(proof, options).TransformToDisplay(source), "Missing display profile rejected explicitly");
Console.WriteLine($"PASS: {checks} HDR, spectral observer, measured-primary and ICC soft-proof checks.");
