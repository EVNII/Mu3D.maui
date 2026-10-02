using System.Numerics;
using Mu3D.Color;
using Mu3D.Color.Printing;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
void Throws<T>(Action a) where T:Exception {try{a();}catch(T){checks++;return;}throw new Exception($"Expected {typeof(T).Name}");}
// Mapping properties independent of third-party ICC tables.
float previous = -1;
for (int i = 0; i <= 100; i++)
{
 float value = i * .16f;
 var mapped = HdrPrintMapper.ToAdobeRgb(new(value,value,value,.4f,StandardColorSpaces.LinearAdobeRgb));
 Check(mapped.Red >= previous && Math.Abs(mapped.Red-mapped.Green)<1e-6f && Math.Abs(mapped.Red-mapped.Blue)<1e-6f,"neutral monotonic HDR ramp");
 Check(mapped.Alpha == .4f && mapped.Red is >=0 and <=1,"bounded result and alpha");
 previous = mapped.Red;
}
var green = HdrPrintMapper.ToAdobeRgb(new(0,1,0,1,StandardColorSpaces.LinearAdobeRgb));
Check(StandardLinearRgbConverter.Convert(green,StandardColorSpaces.LinearSrgb).Red < -.01f,"Adobe green survives beyond sRGB gamut");
var random = new Random(42);
foreach(var space in new[] { StandardColorSpaces.LinearSrgb, StandardColorSpaces.LinearAdobeRgb, StandardColorSpaces.LinearRec2020, StandardColorSpaces.AcesCg })
for(int i=0;i<100;i++)
{
 var mapped=HdrPrintMapper.ToAdobeRgb(new((float)random.NextDouble()*16-2,(float)random.NextDouble()*16-2,(float)random.NextDouble()*16-2,1,space),i%9-4);
 Check(mapped.Red is >=0 and <=1 && mapped.Green is >=0 and <=1 && mapped.Blue is >=0 and <=1,"HDR gamut bound");
}
Check(HdrPrintMapper.ToAdobeRgb(new(1,1,1,1,StandardColorSpaces.LinearAdobeRgb),1).Red >
      HdrPrintMapper.ToAdobeRgb(new(1,1,1,1,StandardColorSpaces.LinearAdobeRgb)).Red,"exposure raises luminance");
Throws<ArgumentOutOfRangeException>(()=>HdrPrintMapper.ToAdobeRgb(green,float.NaN));
Throws<ArgumentOutOfRangeException>(()=>HdrPrintMapper.ToAdobeRgb(green,21));
// Both rendering algorithms and independently selected print gamuts.
foreach(var mapping in Enum.GetValues<PrintToneMapping>())
foreach(var destination in new[] { StandardColorSpaces.LinearAdobeRgb, StandardColorSpaces.LinearProPhotoRgb })
{
 var renderer = new PrintRgbTransform(StandardColorSpaces.LinearRec2020,destination,mapping);
 float last = -1;
 for(int i=0;i<100;i++)
 {
  float level = i*.16f;
  var gray = renderer.Transform(new(level,level,level,.3f,StandardColorSpaces.LinearRec2020));
  Check(gray.Red+1e-5f >= last,"print gray monotonic");
  Check(Math.Abs(gray.Red-gray.Green)<.002f && Math.Abs(gray.Red-gray.Blue)<.002f,"print gray neutral");
  last=gray.Red;
  var c=renderer.Transform(new((float)random.NextDouble()*30-1,(float)random.NextDouble()*30-1,(float)random.NextDouble()*30-1,.3f,StandardColorSpaces.LinearRec2020));
  Check(c.Red is >=0 and <=1 && c.Green is >=0 and <=1 && c.Blue is >=0 and <=1 && c.Alpha==.3f && c.ColorSpace==destination,"AgX/target bounds and tags");
 }
 var exposed = new PrintRgbTransform(StandardColorSpaces.LinearRec2020,destination,mapping,1);
 Check(exposed.Transform(new(.2f,.4f,.6f,1,StandardColorSpaces.LinearRec2020))==
       renderer.Transform(new(.4f,.8f,1.2f,1,StandardColorSpaces.LinearRec2020)),"exposure applied once");
 Throws<ArgumentException>(()=>renderer.Transform(green));
}
var printAgx=new PrintRgbTransform(StandardColorSpaces.LinearRec2020,StandardColorSpaces.LinearProPhotoRgb);
var officialAgx=new ColorViewTransform(ColorViewPreset.AgXSdrRec2020,StandardColorSpaces.LinearRec2020);
var mid=new LinearRgba(.18f,.18f,.18f,1,StandardColorSpaces.LinearRec2020);
var expectedMid=StandardLinearRgbConverter.Convert(officialAgx.Transform(mid),StandardColorSpaces.LinearProPhotoRgb);
Check(Math.Abs(printAgx.Transform(mid).Red-expectedMid.Red)<1e-5f,"no second tone curve after AgX");
bool retainsWide=false;
for(int i=1;i<=30;i++)
{
 var c=printAgx.Transform(new(0,i*.1f,0,1,StandardColorSpaces.LinearRec2020));
 var srgb=StandardLinearRgbConverter.Convert(c,StandardColorSpaces.LinearSrgb);
 retainsWide |= srgb.Red<-.001f || srgb.Blue<-.001f;
}
Check(retainsWide,"AgX print retains colors outside sRGB");
Throws<ArgumentOutOfRangeException>(()=>new PrintRgbTransform(StandardColorSpaces.LinearSrgb,StandardColorSpaces.LinearAdobeRgb,(PrintToneMapping)99));
string? directory=Environment.GetEnvironmentVariable("MU3D_PRINT_PROFILE_TEST_DIR");
Throws<InvalidDataException>(()=>new CmykProfile(new byte[132]));
if(directory is null) throw new Exception("Set MU3D_PRINT_PROFILE_TEST_DIR to official print profiles (see README). They are never bundled.");
foreach(string file in Directory.GetFiles(directory,"*.icc"))
{
 var bytes=File.ReadAllBytes(file);var profile=new CmykProfile(bytes);Check(profile.ToArray().SequenceEqual(bytes),"ICC preservation");
 byte original=bytes[36];bytes[36]=0;Check(profile.ToArray()[36]==original,"copy ownership");
 var patches=new[]{Vector4.Zero,new Vector4(0,0,0,1),new Vector4(1,0,0,0),new Vector4(.2f,.3f,.4f,.1f),Vector4.One};
 var image=new CmykImage(5,1,patches,profile);patches[0]=Vector4.One;Check(image.Pixels[0]==Vector4.Zero,"CMYK image ownership");
 Check(image.FindExcessInk(350).SequenceEqual(new[]{4}),"ink diagnostics preserve black");
 Throws<ArgumentOutOfRangeException>(()=>new CmykColor(-.1f,0,0,0,profile));
 Throws<ArgumentOutOfRangeException>(()=>new CmykImage(uint.MaxValue,uint.MaxValue,[],profile));
 for(int intent=0;intent<4;intent++)
 {
  var transform=new CmykTransform(profile,new(){Intent=(IccRenderingIntent)intent});
  foreach(var p in image.Pixels)
  {
   var c=new CmykColor(p.X,p.Y,p.Z,p.W,profile);var rgb=transform.Decode(c,StandardColorSpaces.LinearSrgb);
   Check(float.IsFinite(rgb.Red)&&rgb.Alpha==1,"finite opaque decode");
   Check(c.Channels==p,"decode preserves separation");
  }
  var separated=transform.Separate(new LinearRgba(.2f,.3f,.4f,1,StandardColorSpaces.LinearSrgb));
  Check(separated.TotalInkPercent is >=0 and <=400,"bounded separation");
 }
 byte[] malformed=profile.ToArray();
 System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(malformed.AsSpan(136),int.MaxValue);
 Throws<InvalidDataException>(()=>new CmykProfile(malformed));
 byte[] unsupported=profile.ToArray();int tagCount=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(unsupported.AsSpan(128));
 for(int i=0;i<tagCount;i++)
 {
  int row=132+i*12;
  if(System.Text.Encoding.ASCII.GetString(unsupported,row,4)!="A2B1")continue;
  System.Text.Encoding.ASCII.GetBytes("zzzz").CopyTo(unsupported,row);
 }
 Throws<NotSupportedException>(()=>new CmykTransform(new CmykProfile(unsupported)));
 var relative=new CmykTransform(profile);
 byte[] distinct=profile.ToArray();distinct[80]^=1;
 var foreign=new CmykColor(0,0,0,1,new CmykProfile(distinct));
 Throws<ArgumentException>(()=>relative.Decode(foreign,StandardColorSpaces.LinearSrgb));
 var proof=relative.Proof(new LinearRgba(.2f,.3f,.4f,1,StandardColorSpaces.LinearSrgb),StandardColorSpaces.LinearSrgb);
 Check(float.IsFinite(proof.DeltaE76),"proof diagnostic");
 var proofWithoutPaper=relative.Proof(proof.Separation,StandardColorSpaces.LinearSrgb,false);
 Check(proofWithoutPaper!=proof.Preview,"paper white is explicit");
 Throws<InvalidOperationException>(()=>relative.Separate(new LinearRgba(2,1,1,1,StandardColorSpaces.LinearSrgb)));
 Throws<ArgumentException>(()=>relative.Separate(new LinearRgba(.2f,.3f,.4f,.5f,StandardColorSpaces.LinearSrgb)));
 var clipped=new CmykTransform(profile,new(){RangePolicy=PrintRangePolicy.Clip});
 Check(clipped.Separate(new LinearRgba(2,-1,.2f,1,StandardColorSpaces.LinearSrgb)).TotalInkPercent>=0,"explicit clipping");
 var compensated=new CmykTransform(profile,new(){BlackPointCompensation=new(0,10)});
 Check(compensated.Separate(new LinearRgba(.2f,.3f,.4f,1,StandardColorSpaces.LinearSrgb))!=proof.Separation,"BPC affects separation");
 Throws<NotSupportedException>(()=>new CmykTransform(profile,new(){Intent=IccRenderingIntent.IccAbsoluteColorimetric,BlackPointCompensation=new(0,10)}));
 Check(image.FindExcessInk(400).Count==0,"400 percent cannot exceed legal CMYK");
 Check(image.FindExcessInk(0).Count==4,"zero threshold marks every nonzero separation");
 foreach(var destination in new[] { StandardColorSpaces.LinearAdobeRgb, StandardColorSpaces.LinearProPhotoRgb })
 {
  var mapped=new PrintRgbTransform(StandardColorSpaces.LinearRec2020,destination).Transform(new(8,2,.5f,1,StandardColorSpaces.LinearRec2020));
  Check(relative.Separate(mapped).TotalInkPercent is >=0 and <=400,"AgX wide RGB ICC separation");
 }
 var hdrMapped=HdrPrintMapper.ToAdobeRgb(new(8,2,.5f,1,StandardColorSpaces.LinearAdobeRgb));
 Check(relative.Separate(hdrMapped).TotalInkPercent is >=0 and <=400,"HDR mapped Adobe RGB accepted without RGB clipping");
 Check(relative.Decode(image,StandardColorSpaces.AcesCg).Pixels.Count==5,"bulk decode");
 Console.WriteLine($"{Path.GetFileName(file)}: all four intents, image/ICC preservation, proof, BPC, range/alpha policies passed.");
}
Console.WriteLine($"Printing: {checks} checks passed.");
if(Environment.GetEnvironmentVariable("MU3D_PRINT_ORACLE_CSV") is string oracle)
{
 var transforms=new Dictionary<string,CmykTransform>();float maxDecode=0,maxEncode=0;int compared=0;
 foreach(string line in File.ReadLines(oracle))
 {
  var fields=line.Split(',');string key=fields[0]+fields[1];int intent=int.Parse(fields[1]),direction=int.Parse(fields[2]);
  if(!transforms.TryGetValue(key,out var t))
  { t=new(new CmykProfile(File.ReadAllBytes(Path.Combine(directory,fields[0]))),new(){Intent=(IccRenderingIntent)intent,RangePolicy=PrintRangePolicy.Clip});transforms.Add(key,t); }
  var v=fields.Skip(3).Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
  float[] actual;
  if(direction==0){var c=t.Decode(new CmykColor(v[0],v[1],v[2],v[3],t.Profile),StandardColorSpaces.LinearSrgb);var xyz=StandardLinearRgbConverter.ToXyzD50(c);actual=[xyz.X,xyz.Y,xyz.Z];}
  else {var c=t.Separate(StandardLinearRgbConverter.FromXyzD50(v[0],v[1],v[2],1,StandardColorSpaces.LinearSrgb)).Channels;actual=[c.X,c.Y,c.Z,c.W];}
  float error=actual.Select((x,i)=>MathF.Abs(x-v[i+4])).Max();
  if(direction==0)maxDecode=Math.Max(maxDecode,error);else maxEncode=Math.Max(maxEncode,error);
  Check(error<.035f,$"ColorSync difference {key} direction {direction}: {error}; input {string.Join(',',v.Take(4))}, actual {string.Join(',',actual)}, expected {string.Join(',',v.Skip(4))}, white {t.Profile.MediaWhite}");compared++;
 }
 Check(compared>=256,"Oracle CSV must contain real comparison rows.");
 Console.WriteLine($"ColorSync: {compared} patches, max XYZ error {maxDecode}, max ink error {maxEncode}.");
}
