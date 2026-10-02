using System.Numerics;
namespace Mu3D.Color.Printing;

/// <summary>Explicit treatment of source RGB or PCS outside the bounded print transform domain.</summary>
public enum PrintRangePolicy
{
    /// <summary>Rejects extended input. The application must first choose its HDR-to-print mapping.</summary>
    Reject,
    /// <summary>Explicitly clips to the transform domain; this is not perceptual tone mapping.</summary>
    Clip,
}

/// <summary>Application-owned print separation policy. Defaults never silently tone map HDR.</summary>
public sealed record PrintTransformOptions
{
    /// <summary>Gets the exact requested ICC intent; missing intent tables fail rather than substitute.</summary>
    public IccRenderingIntent Intent {get;init;}=IccRenderingIntent.MediaRelativeColorimetric;
    /// <summary>Gets the explicitly selected bounded-domain policy.</summary>
    public PrintRangePolicy RangePolicy {get;init;}=PrintRangePolicy.Reject;
    /// <summary>Gets optional explicit media-relative black-point L* compensation for RGB-to-print conversion.</summary>
    /// <remarks>Only relative colorimetric separation accepts BPC. It is not applied when decoding existing CMYK.</remarks>
    public IccBlackPointCompensation? BlackPointCompensation {get;init;}
}

/// <summary>Compiled managed ICC RGB/CMYK conversion through D50 PCS. Safe for concurrent reads.</summary>
/// <remarks>Supports lut8/lut16 and v4 mAB/mBA. Extended MPE, DeviceLink, spectral and spot-color
/// profiles are not supported. Conversion uses the profile's supplied black generation, not an invented GCR.</remarks>
public sealed class CmykTransform
{
    private readonly IccPipeline toPcs,fromPcs;
    private static readonly Vector3 D50=new(.9642f,1,.8249f);
    /// <summary>Compiles both requested directions once, validating unsupported profile structures eagerly.</summary>
    public CmykTransform(CmykProfile profile,PrintTransformOptions? options=null)
    {
        ArgumentNullException.ThrowIfNull(profile);Profile=profile;Options=options??new();
        if(!Enum.IsDefined(Options.Intent)||!Enum.IsDefined(Options.RangePolicy))throw new ArgumentOutOfRangeException(nameof(options));
        if(Options.BlackPointCompensation!=null && Options.Intent!=IccRenderingIntent.MediaRelativeColorimetric)
            throw new NotSupportedException("Explicit BPC is supported only for relative colorimetric separation.");
        int intent=Options.Intent==IccRenderingIntent.IccAbsoluteColorimetric?1:(int)Options.Intent;
        toPcs=new(profile.Tag($"A2B{intent}"),4,3,profile.ConnectionSpace);
        fromPcs=new(profile.Tag($"B2A{intent}"),3,4,profile.ConnectionSpace);
    }
    /// <summary>Gets the immutable print profile.</summary>
    public CmykProfile Profile {get;}
    /// <summary>Gets the immutable conversion policy.</summary>
    public PrintTransformOptions Options {get;}
    /// <summary>Separates an opaque, print-relative RGB color. HDR mapping and compositing are caller-owned.</summary>
    public CmykColor Separate(LinearRgba source)
    {
        if(source.Alpha!=1)throw new ArgumentException("Composite alpha explicitly before print separation.",nameof(source));
        Vector3 rgb=new(source.Red,source.Green,source.Blue);rgb=Bound(rgb);
        Vector3 xyz=StandardLinearRgbConverter.ToXyzD50(new(rgb.X,rgb.Y,rgb.Z,1,source.ColorSpace));
        if(Options.Intent==IccRenderingIntent.IccAbsoluteColorimetric)xyz=xyz*D50/Profile.MediaWhite;
        if(Options.BlackPointCompensation is {} bpc)
        {
            float s=BlackY(bpc.SourceLightness),d=BlackY(bpc.DestinationLightness),scale=(1-d)/(1-s);
            xyz=xyz*scale+D50*(d-s*scale);
        }
        float[] encoded=fromPcs.EncodePcs(xyz,Profile.ConnectionSpace);
        Vector3 bounded=Bound(new(encoded[0],encoded[1],encoded[2]));
        float[] ink=fromPcs.Run([bounded.X,bounded.Y,bounded.Z]);
        return new(ink[0],ink[1],ink[2],ink[3],Profile);
    }
    /// <summary>Decodes existing CMYK without reseparating or applying separation BPC.</summary>
    public LinearRgba Decode(CmykColor color,StandardRgbColorSpaceReference destination)
    {
        Vector3 xyz=DecodeXyz(color,Options.Intent==IccRenderingIntent.IccAbsoluteColorimetric);
        return StandardLinearRgbConverter.FromXyzD50(xyz.X,xyz.Y,xyz.Z,1,destination);
    }
    /// <summary>Separates a complete opaque image, retaining the selected print profile.</summary>
    public CmykImage Separate(LinearRgbaImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.Width,source.Height,source.Pixels.Select(v=>Separate(new LinearRgba(v.X,v.Y,v.Z,v.W,source.ColorSpace)).Channels),Profile);
    }
    /// <summary>Decodes an immutable CMYK image into a selected linear RGB space.</summary>
    public LinearRgbaImage Decode(CmykImage source,StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(source);CheckProfile(source.Profile);
        return new(source.Width,source.Height,source.Pixels.Select(v=>
        {var c=Decode(new CmykColor(v.X,v.Y,v.Z,v.W,source.Profile),destination);return new Vector4(c.Red,c.Green,c.Blue,1);}),destination);
    }
    /// <summary>Previews existing separations, optionally simulating media white. Original CMYK is unchanged.</summary>
    public LinearRgba Proof(CmykColor color,StandardRgbColorSpaceReference displaySpace,bool simulatePaperWhite=true)
    {
        Vector3 xyz=DecodeXyz(color,simulatePaperWhite);
        return StandardLinearRgbConverter.FromXyzD50(xyz.X,xyz.Y,xyz.Z,1,displaySpace);
    }
    /// <summary>Separates and soft-proofs RGB, reporting reproducibility and display-gamut diagnostics.</summary>
    /// <remarks>The Lab ΔE76 diagnostic is a round-trip error, not a certified gamut boundary or ΔE2000.
    /// Render the result through the calibrated display pipeline without an additional creative view.</remarks>
    public PrintProofResult Proof(LinearRgba source,StandardRgbColorSpaceReference displaySpace,bool simulatePaperWhite=true,float toleranceDeltaE76=2)
    {
        if(!float.IsFinite(toleranceDeltaE76)||toleranceDeltaE76<0)throw new ArgumentOutOfRangeException(nameof(toleranceDeltaE76));
        CmykColor ink=Separate(source);LinearRgba preview=Proof(ink,displaySpace,simulatePaperWhite);
        float error=Vector3.Distance(IccPipeline.Lab(StandardLinearRgbConverter.ToXyzD50(source)),IccPipeline.Lab(DecodeXyz(ink,false)));
        bool displayOutside=preview.Red is <0 or >1||preview.Green is <0 or >1||preview.Blue is <0 or >1;
        return new(ink,preview,error,error>toleranceDeltaE76,displayOutside);
    }
    private Vector3 DecodeXyz(CmykColor color,bool absolute)
    {
        ArgumentNullException.ThrowIfNull(color);CheckProfile(color.Profile);var v=color.Channels;
        Vector3 xyz=toPcs.DecodePcs(toPcs.Run([v.X,v.Y,v.Z,v.W]),Profile.ConnectionSpace);
        return absolute?xyz*Profile.MediaWhite/D50:xyz;
    }
    private void CheckProfile(CmykProfile profile)
    { if(profile.Fingerprint!=Profile.Fingerprint)throw new ArgumentException("CMYK belongs to a different profile; explicit reseparation is required."); }
    private Vector3 Bound(Vector3 v)
    {
        if(!float.IsFinite(v.X)||!float.IsFinite(v.Y)||!float.IsFinite(v.Z))throw new ArgumentOutOfRangeException(nameof(v));
        if(Options.RangePolicy==PrintRangePolicy.Clip)return Vector3.Clamp(v,Vector3.Zero,Vector3.One);
        if(v.X is <0 or >1||v.Y is <0 or >1||v.Z is <0 or >1)throw new InvalidOperationException("Outside print transform domain; choose explicit HDR mapping or clipping.");
        return v;
    }
    private static float BlackY(float l)=>l>8?MathF.Pow((l+16)/116,3):l*27/24389;
}

/// <summary>Print separations, linear display preview and explicitly named proof diagnostics.</summary>
/// <param name="Separation">The profile-bound CMYK values.</param>
/// <param name="Preview">The un-clipped, linear display-space preview.</param>
/// <param name="DeltaE76">Media-relative source/proof Lab distance, before paper-white simulation.</param>
/// <param name="ExceedsTolerance">Whether that round-trip distance exceeds the requested tolerance.</param>
/// <param name="OutsideDisplayGamut">Whether the preview lies outside nominal [0,1] display RGB.</param>
public sealed record PrintProofResult(CmykColor Separation,LinearRgba Preview,float DeltaE76,bool ExceedsTolerance,bool OutsideDisplayGamut);
