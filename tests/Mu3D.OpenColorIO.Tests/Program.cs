using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.Native.OpenColorIO;

int checks = 0;
if (args.Contains("--expect-missing", StringComparer.Ordinal))
{
    try { OcioConfiguration.LoadBuiltin("cg-config-v4.0.0_aces-v2.0_ocio-v2.5"); }
    catch (DllNotFoundException error) when (error.Message.Contains("runtime is missing", StringComparison.Ordinal))
    { Console.WriteLine("OpenColorIO missing-runtime guard passed."); return; }
    throw new InvalidOperationException("The isolated process should reject a missing pinned native runtime.");
}
string fixture = Path.Combine(AppContext.BaseDirectory, "reference.ocio");
Environment.SetEnvironmentVariable("OCIO", "/no/implicit/config/may/be/selected.ocio");
using OcioConfiguration config = OcioConfiguration.LoadFile(fixture);
Expect(OcioConfiguration.Version == "2.5.2", "pinned runtime version");
Expect(config.ColorSpaces.SequenceEqual(["Linear", "Scaled", "Encoded"]), "colorspace enumeration");
Expect(config.Displays.SequenceEqual(["Test"]), "display enumeration");
Expect(config.GetViews("Test").SequenceEqual(["Plain", "WithLook"]), "view enumeration");
Expect(config.Looks.SequenceEqual(["Half"]), "Look enumeration");
Expect(config.GetDisplayViewOutputColorSpace("Test", "Plain") == "Encoded", "explicit encoded output identity");
using OcioProcessor scaled = config.CreateColorSpaceProcessor("Linear", "Scaled");
Expect(scaled.SourceIdentifier == "Linear" && scaled.OutputIdentifier == "Scaled", "processor identifiers");
Expect(scaled.ConfigurationCacheId == config.CacheId && scaled.CacheId.Length > 0, "complete cache identities");
Near(scaled.ApplyRgb(new(-.2f, .25f, 5)), new(-.4f, .5f, 10), 1e-6f, "signed HDR matrix processing");
Vector4[] rgba = [new(-1, 2, 4, .125f), new(.1f, .2f, .3f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)))];
scaled.ApplyRgba(rgba);
Near(new(rgba[0].X, rgba[0].Y, rgba[0].Z), new(-2, 4, 8), 1e-6f, "batch RGB processing");
Expect(rgba[0].W == .125f && BitConverter.SingleToInt32Bits(rgba[1].W) == unchecked((int)0x80000000), "exact alpha preservation including negative zero");
using OcioProcessor plain = config.CreateDisplayViewProcessor("Linear", "Test", "Plain");
Near(plain.ApplyRgb(new(.25f, 1, 4)), new(.5f, 1, 2), 2e-6f, "display output remains encoded");
using OcioProcessor looked = config.CreateDisplayViewProcessor("Linear", "Test", "WithLook");
Near(looked.ApplyRgb(new(.5f, 2, 8)), new(.5f, 1, 2), 2e-6f, "configured Look before display view");
using OcioProcessor bypass = config.CreateDisplayViewProcessor("Linear", "Test", "WithLook", "");
Near(bypass.ApplyRgb(new(.25f, 1, 4)), new(.5f, 1, 2), 2e-6f, "explicit empty Look bypass");
using OcioProcessor overrideLook = config.CreateDisplayViewProcessor("Linear", "Test", "Plain", "Half");
Near(overrideLook.ApplyRgb(new(.5f, 2, 8)), new(.5f, 1, 2), 2e-6f, "explicit Look override");
Expect(looked.CacheId != bypass.CacheId, "Look changes alter processor cache identity");
using OcioProcessor decoded = config.CreateDisplayViewToLinearProcessor("Linear", "Test", "Plain", "Encoded", "Linear");
Near(decoded.ApplyRgb(new(.25f, 1, 4)), new(.25f, 1, 4), 2e-6f, "explicit output decoding");
ILinearRgbTransform linear = decoded.AsLinearTransform(StandardColorSpaces.AcesCg, StandardColorSpaces.AcesCg);
LinearRgbLut3D baked = LinearRgbLut3D.Bake(9, Vector3.Zero, new(8), linear.SourceSpace, linear.DestinationSpace,
    linear.Transform, ColorLutRangePolicy.Clamp);
LinearRgba offGrid = baked.Transform(new(.37f, 1.28f, 5.91f, .25f, StandardColorSpaces.AcesCg));
Near(new(offGrid.Red, offGrid.Green, offGrid.Blue), new(.37f, 1.28f, 5.91f), 4e-6f, "portable LUT bake and off-grid interpolation");
LinearRgba transformed = linear.Transform(new(.25f, 1, 4, .3f, StandardColorSpaces.AcesCg));
Expect(transformed.Alpha == .3f && transformed.ColorSpace == StandardColorSpaces.AcesCg, "explicit verified metadata and alpha through linear adapter");
Reject<ArgumentException>(() => linear.Transform(new(1, 1, 1, 1, StandardColorSpaces.LinearSrgb)), "mismatched input metadata");
Reject<InvalidOperationException>(() => config.CreateDisplayViewToLinearProcessor("Linear", "Test", "Plain", "Scaled", "Linear"), "wrong view output colorspace");
Reject<InvalidOperationException>(() => config.CreateColorSpaceProcessor("missing", "Linear"), "unknown colorspace reports native error");
Reject<InvalidOperationException>(() => OcioConfiguration.LoadString("not: [valid"), "invalid config reports native error");
Reject<ArgumentException>(() => OcioConfiguration.LoadBuiltin("abc\0def"), "embedded NUL is rejected");
Vector4[] invalid = [new(1, 2, 3, 1), new(float.NaN, 0, 0, 1)];
Reject<ArgumentOutOfRangeException>(() => scaled.ApplyRgba(invalid), "batch validation precedes mutation");
Expect(invalid[0] == new Vector4(1, 2, 3, 1), "invalid batch leaves earlier pixels unchanged");
using OcioConfiguration textConfig = OcioConfiguration.LoadString(File.ReadAllText(fixture));
using OcioProcessor independent = textConfig.CreateColorSpaceProcessor("Linear", "Scaled");
textConfig.Dispose();
Near(independent.ApplyRgb(Vector3.One), new(2), 1e-6f, "processor independently owns compiled transform after config disposal");
Reject<ObjectDisposedException>(() => textConfig.CreateColorSpaceProcessor("Linear", "Scaled"), "configuration disposal");
independent.Dispose();
Reject<ObjectDisposedException>(() => independent.ApplyRgb(Vector3.One), "processor disposal");
IReadOnlyList<string> builtins = OcioConfiguration.GetBuiltinConfigurations();
Expect(builtins.Count > 0, "built-in config registry");
string aces2 = builtins.First(name => name.Contains("aces-v2.0", StringComparison.Ordinal));
using OcioConfiguration builtin = OcioConfiguration.LoadBuiltin(aces2);
Expect(builtin.ColorSpaces.Contains("ACEScg"), "real ACES2 builtin loads and enumerates");
using OcioProcessor aces = builtin.CreateColorSpaceProcessor("ACEScg", "ACES2065-1");
Vector3 neutral = aces.ApplyRgb(new(.18f));
Near(neutral, new(.18f), 2e-6f, "ACES AP1/AP0 neutral reference");
Reject<InvalidOperationException>(() => builtin.CreateDisplayViewToLinearProcessor("ACEScg", "sRGB - Display",
    "ACES 2.0 - SDR 100 nits (Rec.709)", "sRGB - Display", "ACEScg"), "output decoding cannot silently cross display/scene reference spaces");
using JsonDocument oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "oracle.json")));
JsonElement document = oracle.RootElement;
Expect(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).Equals(document.GetProperty("fixtureSha256").GetString(),
    StringComparison.OrdinalIgnoreCase), "golden configuration hash");
Expect(document.GetProperty("openColorIO").GetProperty("version").GetString() == OcioConfiguration.Version, "golden runtime version");
foreach (JsonElement item in document.GetProperty("cases").EnumerateArray())
{
    string configuration = item.GetProperty("configuration").GetString()!;
    using OcioConfiguration reference = configuration == "fixture" ? OcioConfiguration.LoadFile(fixture) : OcioConfiguration.LoadBuiltin(configuration);
    string[] parameters = item.GetProperty("parameters").EnumerateArray().Select(p => p.GetString()!).ToArray();
    using OcioProcessor processor = parameters.Length == 2 ? reference.CreateColorSpaceProcessor(parameters[0], parameters[1]) :
        reference.CreateDisplayViewProcessor(parameters[0], parameters[1], parameters[2]);
    int index = 0;
    foreach (JsonElement input in document.GetProperty("inputs").EnumerateArray())
    {
        Vector3 actual = processor.ApplyRgb(new(input[0].GetSingle(), input[1].GetSingle(), input[2].GetSingle()));
        JsonElement expected = item.GetProperty("outputs")[index++];
        for (int channel = 0; channel < 3; channel++)
        {
            float value = expected[channel].GetSingle();
            Expect(MathF.Abs(actual[channel] - value) <= 2e-6f + 2e-6f * MathF.Abs(value),
                $"independent direct-C++ {item.GetProperty("name").GetString()} sample {index} channel {channel}");
        }
    }
}
Console.WriteLine($"OpenColorIO {OcioConfiguration.Version} native CPU checks passed: {checks}; built-in {aces2}.");

void Expect(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; }
void Near(Vector3 actual, Vector3 expected, float tolerance, string label)
{
    Expect(float.IsFinite(actual.X) && float.IsFinite(actual.Y) && float.IsFinite(actual.Z) &&
        Vector3.Abs(actual - expected).Length() <= tolerance, $"{label}: {actual} versus {expected}");
}
void Reject<T>(Action action, string label) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new InvalidOperationException(label);
}
