using System.Text.Json;
using Mu3D.Color;

using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Reference/ocio-2.5.2.json")));
var count = 0; var maxError = 0f; var failures = new List<string>();
foreach (var preset in Enum.GetValues<ColorViewPreset>())
foreach (var space in new[]{StandardColorSpaces.LinearSrgb,StandardColorSpaces.AcesCg})
{
    var name = preset + (space == StandardColorSpaces.AcesCg ? "AcesCg":"LinearSrgb");
    var view = new ColorViewTransform(preset,space);
    var max = 0f; var fail = 0;
    foreach (var test in reference.RootElement.GetProperty(name).EnumerateArray())
    {
        var rgb = test.GetProperty("input").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var expected = test.GetProperty("output").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var result = view.Transform(new(rgb[0],rgb[1],rgb[2],.37f,space));
        var actual = new[]{result.Red,result.Green,result.Blue};
        for(var i=0;i<3;i++)
        {
            var error = MathF.Abs(actual[i]-expected[i]); max=MathF.Max(max,error); maxError=MathF.Max(maxError,error);count++;
            if(error > 1e-4f + 4e-5f*MathF.Abs(expected[i]))
            { fail++; if(fail<5)failures.Add($"{name} [{string.Join(',',rgb)}] channel{i}: {actual[i]:G9} vs {expected[i]:G9}, error{error:G9}"); }
        }
        if(result.Alpha != .37f || result.ColorSpace != StandardColorSpaces.LinearSrgb)throw new Exception("Output signal/alpha contract failed");count++;
    }
    Console.WriteLine($"{name}: maxAbsError={max:G9}, failures={fail}");
}
foreach(var preset in Enum.GetValues<ColorViewPreset>())
{
    var view = new ColorViewTransform(preset,StandardColorSpaces.AcesCg,referenceWhiteNits:200);
    var normal = new ColorViewTransform(preset,StandardColorSpaces.AcesCg);
    var exposed = new ColorViewTransform(preset,StandardColorSpaces.AcesCg,exposureStops:1);
    var c = new LinearRgba(.18f,.3f,2f,.63f,StandardColorSpaces.AcesCg);
    var v=view.Transform(c);var n=normal.Transform(c);var e=exposed.Transform(c);var n2=normal.Transform(new(c.Red*2,c.Green*2,c.Blue*2,c.Alpha,c.ColorSpace));
    if(v.Red != n.Red/2 || v.Green != n.Green/2 || v.Blue != n.Blue/2 || e!=n2)throw new Exception("Exposure/normalization order failed");count+=4;
    var peak=normal.Transform(new(1024,1024,1024,1,c.ColorSpace));
    if(view.IsHdr && peak.Red<9.9f)throw new Exception("HDR highlights collapsed");count++;
    if(view.Program.WgslSource.Contains("read_write")||view.Program.Table.Length==0)throw new Exception("Unexpected GPU resource contract");count++;
}
// Boundary values remain finite at both legal exposure/reference-white extremes.
foreach(var preset in Enum.GetValues<ColorViewPreset>())
foreach(var space in new[]{StandardColorSpaces.LinearSrgb,StandardColorSpaces.AcesCg})
foreach(var exposure in new[]{-32f,0f,32f})
foreach(var white in new[]{1f,100f,10000f})
{
    var view = new ColorViewTransform(preset,space,exposure,white);
    foreach(var values in new[]{new[]{float.Epsilon,0f,0f},new[]{-1e20f,-1e20f,-1e20f},new[]{1e20f,1e20f,1e20f},new[]{1e20f,0f,0f},new[]{0f,1e20f,0f},new[]{0f,0f,1e20f},new[]{-.001f,.0001f,.002f}})
    {
        var v=view.Transform(new(values[0],values[1],values[2],.45f,space));
        if(!float.IsFinite(v.Red)||!float.IsFinite(v.Green)||!float.IsFinite(v.Blue)||v.Alpha!=.45f)throw new Exception("Boundary finite/alpha failure");
        count+=4;
    }
}
void Reject<T>(Action action) where T:Exception
{
    try { action(); } catch(T) { count++;return; } throw new Exception("Missing validation: "+typeof(T).Name);
}
Reject<ArgumentOutOfRangeException>(()=>new ColorViewTransform((ColorViewPreset)99,StandardColorSpaces.LinearSrgb));
_ = new ColorViewTransform(ColorViewPreset.AgXSdr, StandardColorSpaces.LinearRec2020);
Reject<ArgumentNullException>(()=>new ColorViewTransform(ColorViewPreset.AgXSdr,null!));
foreach(var x in new[]{float.NaN,float.PositiveInfinity,-32.001f,32.001f})
    Reject<ArgumentOutOfRangeException>(()=>new ColorViewTransform(ColorViewPreset.AgXSdr,StandardColorSpaces.LinearSrgb,exposureStops:x));
foreach(var x in new[]{float.NaN,float.PositiveInfinity,.99f,10000.1f})
    Reject<ArgumentOutOfRangeException>(()=>new ColorViewTransform(ColorViewPreset.AgXSdr,StandardColorSpaces.LinearSrgb,referenceWhiteNits:x));
var identityTag = new ColorViewTransform(ColorViewPreset.AgXSdr,StandardColorSpaces.LinearSrgb);
Reject<ArgumentException>(()=>identityTag.Transform(new(.18f,.18f,.18f,1,StandardColorSpaces.AcesCg)));
Reject<ArgumentOutOfRangeException>(()=>new ColorViewTransform(ColorViewPreset.AgXSdr,StandardColorSpaces.LinearSrgb,32).Transform(new(float.MaxValue,0,0,1,StandardColorSpaces.LinearSrgb)));
foreach(var preset in Enum.GetValues<ColorViewPreset>())
foreach(var space in new[]{StandardColorSpaces.LinearSrgb,StandardColorSpaces.AcesCg})
{
    var view = new ColorViewTransform(preset,space);
    foreach(var red in new[]{-1e30f,1e30f})
    foreach(var green in new[]{-1e30f,1e30f})
    foreach(var blue in new[]{-1e30f,1e30f})
    {
        var v=view.Transform(new(red,green,blue,.72f,space));
        if(!float.IsFinite(v.Red)||!float.IsFinite(v.Green)||!float.IsFinite(v.Blue)||v.Alpha!=.72f)throw new Exception("Maximum-domain matrix safety failed");count+=4;
    }
    Reject<ArgumentOutOfRangeException>(()=>view.Transform(new(float.MaxValue,-float.MaxValue,float.MaxValue,1,space)));
    Reject<ArgumentOutOfRangeException>(()=>view.Transform(new(1.01e30f,0,0,1,space)));
    Reject<ArgumentOutOfRangeException>(()=>new ColorViewTransform(preset,space,1).Transform(new(1e30f,0,0,1,space)));
}
Console.WriteLine($"Official OCIO reference: {count} checks, maximum absolute error {maxError:G9}");
if(failures.Count>0)throw new Exception(string.Join(Environment.NewLine,failures));
