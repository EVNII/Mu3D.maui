using System.Collections.ObjectModel;
using System.Numerics;
namespace Mu3D.Color.Printing;

/// <summary>Opaque device CMYK components in [0,1] bound to the exact separation profile.</summary>
public sealed record CmykColor
{
    /// <summary>Constructs ink coverages; black is an ink channel, never alpha.</summary>
    public CmykColor(float cyan,float magenta,float yellow,float black,CmykProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Channels=new(cyan,magenta,yellow,black);Validate(Channels);Profile=profile;
    }
    /// <summary>Gets C, M, Y, K coverages respectively. The fourth channel is not opacity.</summary>
    public Vector4 Channels {get;}
    /// <summary>Gets the profile that interprets these separation numbers.</summary>
    public CmykProfile Profile {get;}
    /// <summary>Gets total area coverage as a percentage from 0 through 400.</summary>
    public float TotalInkPercent => 100*(Channels.X+Channels.Y+Channels.Z+Channels.W);
    internal static void Validate(Vector4 v)
    {
        if(!float.IsFinite(v.X)||!float.IsFinite(v.Y)||!float.IsFinite(v.Z)||!float.IsFinite(v.W)||
            v.X is <0 or >1||v.Y is <0 or >1||v.Z is <0 or >1||v.W is <0 or >1)
            throw new ArgumentOutOfRangeException(nameof(v),"CMYK coverages must be finite and in [0,1].");
    }
}

/// <summary>Immutable row-major opaque CMYK image retaining its original profile and black separation.</summary>
public sealed class CmykImage
{
    /// <summary>Copies CMYK pixels and validates dimensions, coverages and mandatory profile.</summary>
    public CmykImage(uint width,uint height,IEnumerable<Vector4> pixels,CmykProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(pixels);ArgumentNullException.ThrowIfNull(profile);
        ulong size=(ulong)width*height;if(size>int.MaxValue)throw new ArgumentOutOfRangeException(nameof(width));
        Vector4[] copy=pixels.ToArray();if((ulong)copy.Length!=size)throw new ArgumentException("Pixel count differs from dimensions.",nameof(pixels));
        foreach(var v in copy) CmykColor.Validate(v);
        Width=width;Height=height;Pixels=new ReadOnlyCollection<Vector4>(copy);Profile=profile;
    }
    /// <summary>Gets pixel width.</summary>
    public uint Width {get;}
    /// <summary>Gets pixel height.</summary>
    public uint Height {get;}
    /// <summary>Gets immutable C, M, Y, K values. There is no alpha channel.</summary>
    public IReadOnlyList<Vector4> Pixels {get;}
    /// <summary>Gets the exact original separation profile.</summary>
    public CmykProfile Profile {get;}
    /// <summary>Finds pixels exceeding an application-selected total ink limit, without modifying ink channels.</summary>
    public IReadOnlyList<int> FindExcessInk(float maximumPercent)
    {
        if(!float.IsFinite(maximumPercent)||maximumPercent is <0 or >400)throw new ArgumentOutOfRangeException(nameof(maximumPercent));
        return Pixels.Select((v,i)=>(v,i)).Where(p=>100*(p.v.X+p.v.Y+p.v.Z+p.v.W)>maximumPercent).Select(p=>p.i).ToArray();
    }
}
