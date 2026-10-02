// Exact scalar/vector semantics used by the maintained OCIO shader-to-C# export.
using System.Numerics;
namespace Mu3D.Color;
internal abstract class ColorViewEvaluator(Vector4[] table)
{
    protected readonly Vector4[] Table = table;
    internal abstract float4 Evaluate(float4 value);
    protected static int clamp(int v, int lo, int hi) => Math.Clamp(v, lo, hi);
    protected static float clamp(float v, float lo, float hi) => Math.Clamp(v, lo, hi);
    protected static float abs(float v) => MathF.Abs(v);
    protected static float floor(float v) => MathF.Floor(v);
    protected static float sqrt(float v) => MathF.Sqrt(v);
    protected static float log2(float v) => MathF.Log2(v);
    protected static float log(float v) => MathF.Log(v);
    protected static float cos(float v) => MathF.Cos(v);
    protected static float sin(float v) => MathF.Sin(v);
    protected static float sign(float v) => MathF.Sign(v);
    protected static float atan2(float y,float x) => MathF.Atan2(y,x);
    protected static float min(float a,float b) => MathF.Min(a,b);
    protected static float max(float a,float b) => MathF.Max(a,b);
    protected static float pow(float a,float b) => MathF.Pow(a,b);
    protected static float lerp(float a,float b,float t) => a + (b-a)*t;
    protected static float2 abs(float2 v) => new(abs(v.x),abs(v.y));
    protected static float2 floor(float2 v) => new(floor(v.x),floor(v.y));
    protected static float2 sqrt(float2 v) => new(sqrt(v.x),sqrt(v.y));
    protected static float2 log2(float2 v) => new(log2(v.x),log2(v.y));
    protected static float2 sign(float2 v) => new(sign(v.x),sign(v.y));
    protected static float2 min(float2 a,float2 b) => new(min(a.x,b.x),min(a.y,b.y));
    protected static float2 max(float2 a,float2 b) => new(max(a.x,b.x),max(a.y,b.y));
    protected static float2 pow(float2 a,float2 b) => new(pow(a.x,b.x),pow(a.y,b.y));
    protected static float2 lerp(float2 a,float2 b,float t) => a+(b-a)*t;
    protected static float dot(float2 a,float2 b) => a.x*b.x + a.y*b.y;
    protected static float3 abs(float3 v) => new(abs(v.x),abs(v.y),abs(v.z));
    protected static float3 floor(float3 v) => new(floor(v.x),floor(v.y),floor(v.z));
    protected static float3 sqrt(float3 v) => new(sqrt(v.x),sqrt(v.y),sqrt(v.z));
    protected static float3 log2(float3 v) => new(log2(v.x),log2(v.y),log2(v.z));
    protected static float3 sign(float3 v) => new(sign(v.x),sign(v.y),sign(v.z));
    protected static float3 min(float3 a,float3 b) => new(min(a.x,b.x),min(a.y,b.y),min(a.z,b.z));
    protected static float3 max(float3 a,float3 b) => new(max(a.x,b.x),max(a.y,b.y),max(a.z,b.z));
    protected static float3 pow(float3 a,float3 b) => new(pow(a.x,b.x),pow(a.y,b.y),pow(a.z,b.z));
    protected static float3 lerp(float3 a,float3 b,float t) => a+(b-a)*t;
    protected static float dot(float3 a,float3 b) => a.x*b.x + a.y*b.y + a.z*b.z;
    protected static float3 mul(float3 a,float3x3 b) => new(a.x*b.m00 + a.y*b.m10 + a.z*b.m20,a.x*b.m01 + a.y*b.m11 + a.z*b.m21,a.x*b.m02 + a.y*b.m12 + a.z*b.m22);
    protected static float4 abs(float4 v) => new(abs(v.x),abs(v.y),abs(v.z),abs(v.w));
    protected static float4 floor(float4 v) => new(floor(v.x),floor(v.y),floor(v.z),floor(v.w));
    protected static float4 sqrt(float4 v) => new(sqrt(v.x),sqrt(v.y),sqrt(v.z),sqrt(v.w));
    protected static float4 log2(float4 v) => new(log2(v.x),log2(v.y),log2(v.z),log2(v.w));
    protected static float4 sign(float4 v) => new(sign(v.x),sign(v.y),sign(v.z),sign(v.w));
    protected static float4 min(float4 a,float4 b) => new(min(a.x,b.x),min(a.y,b.y),min(a.z,b.z),min(a.w,b.w));
    protected static float4 max(float4 a,float4 b) => new(max(a.x,b.x),max(a.y,b.y),max(a.z,b.z),max(a.w,b.w));
    protected static float4 pow(float4 a,float4 b) => new(pow(a.x,b.x),pow(a.y,b.y),pow(a.z,b.z),pow(a.w,b.w));
    protected static float4 lerp(float4 a,float4 b,float t) => a+(b-a)*t;
    protected static float dot(float4 a,float4 b) => a.x*b.x + a.y*b.y + a.z*b.z + a.w*b.w;
    protected static float4 mul(float4 a,float4x4 b) => new(a.x*b.m00 + a.y*b.m10 + a.z*b.m20 + a.w*b.m30,a.x*b.m01 + a.y*b.m11 + a.z*b.m21 + a.w*b.m31,a.x*b.m02 + a.y*b.m12 + a.z*b.m22 + a.w*b.m32,a.x*b.m03 + a.y*b.m13 + a.z*b.m23 + a.w*b.m33);
}
internal struct float2(float x,float y)
{
 public float x = x;
 public float y = y;
 public float r { readonly get => x; set => x=value; }
 public float g { readonly get => y; set => y=value; }
 public static float2 operator +(float2 a,float2 b) => new(a.x+b.x,a.y+b.y);
 public static float2 operator +(float2 a,float b) => new(a.x+b,a.y+b);
 public static float2 operator +(float a,float2 b) => new(a+b.x,a+b.y);
 public static float2 operator -(float2 a,float2 b) => new(a.x-b.x,a.y-b.y);
 public static float2 operator -(float2 a,float b) => new(a.x-b,a.y-b);
 public static float2 operator -(float a,float2 b) => new(a-b.x,a-b.y);
 public static float2 operator *(float2 a,float2 b) => new(a.x*b.x,a.y*b.y);
 public static float2 operator *(float2 a,float b) => new(a.x*b,a.y*b);
 public static float2 operator *(float a,float2 b) => new(a*b.x,a*b.y);
 public static float2 operator /(float2 a,float2 b) => new(a.x/b.x,a.y/b.y);
 public static float2 operator /(float2 a,float b) => new(a.x/b,a.y/b);
 public static float2 operator /(float a,float2 b) => new(a/b.x,a/b.y);
}
internal struct float3(float x,float y,float z)
{
 public float x = x;
 public float y = y;
 public float z = z;
 public float r { readonly get => x; set => x=value; }
 public float g { readonly get => y; set => y=value; }
 public float b { readonly get => z; set => z=value; }
 public float3 rgb { readonly get => new(x,y,z); set { x=value.x;y=value.y;z=value.z; } }
 public readonly float2 rg => new(x,y);
 public readonly float3 zyx => new(z,y,x);
 public static float3 operator +(float3 a,float3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static float3 operator +(float3 a,float b) => new(a.x+b,a.y+b,a.z+b);
 public static float3 operator +(float a,float3 b) => new(a+b.x,a+b.y,a+b.z);
 public static float3 operator -(float3 a,float3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static float3 operator -(float3 a,float b) => new(a.x-b,a.y-b,a.z-b);
 public static float3 operator -(float a,float3 b) => new(a-b.x,a-b.y,a-b.z);
 public static float3 operator *(float3 a,float3 b) => new(a.x*b.x,a.y*b.y,a.z*b.z);
 public static float3 operator *(float3 a,float b) => new(a.x*b,a.y*b,a.z*b);
 public static float3 operator *(float a,float3 b) => new(a*b.x,a*b.y,a*b.z);
 public static float3 operator /(float3 a,float3 b) => new(a.x/b.x,a.y/b.y,a.z/b.z);
 public static float3 operator /(float3 a,float b) => new(a.x/b,a.y/b,a.z/b);
 public static float3 operator /(float a,float3 b) => new(a/b.x,a/b.y,a/b.z);
}
internal struct float4(float x,float y,float z,float w)
{
 public readonly float this[int index] => index switch { 0 => x, 1 => y, 2 => z, 3 => w, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
 public float x = x;
 public float y = y;
 public float z = z;
 public float w = w;
 public float r { readonly get => x; set => x=value; }
 public float g { readonly get => y; set => y=value; }
 public float b { readonly get => z; set => z=value; }
 public float a { readonly get => w; set => w=value; }
 public float3 rgb { readonly get => new(x,y,z); set { x=value.x;y=value.y;z=value.z; } }
 public readonly float2 rg => new(x,y);
 public readonly float3 zyx => new(z,y,x);
 public static float4 operator +(float4 a,float4 b) => new(a.x+b.x,a.y+b.y,a.z+b.z,a.w+b.w);
 public static float4 operator +(float4 a,float b) => new(a.x+b,a.y+b,a.z+b,a.w+b);
 public static float4 operator +(float a,float4 b) => new(a+b.x,a+b.y,a+b.z,a+b.w);
 public static float4 operator -(float4 a,float4 b) => new(a.x-b.x,a.y-b.y,a.z-b.z,a.w-b.w);
 public static float4 operator -(float4 a,float b) => new(a.x-b,a.y-b,a.z-b,a.w-b);
 public static float4 operator -(float a,float4 b) => new(a-b.x,a-b.y,a-b.z,a-b.w);
 public static float4 operator *(float4 a,float4 b) => new(a.x*b.x,a.y*b.y,a.z*b.z,a.w*b.w);
 public static float4 operator *(float4 a,float b) => new(a.x*b,a.y*b,a.z*b,a.w*b);
 public static float4 operator *(float a,float4 b) => new(a*b.x,a*b.y,a*b.z,a*b.w);
 public static float4 operator /(float4 a,float4 b) => new(a.x/b.x,a.y/b.y,a.z/b.z,a.w/b.w);
 public static float4 operator /(float4 a,float b) => new(a.x/b,a.y/b,a.z/b,a.w/b);
 public static float4 operator /(float a,float4 b) => new(a/b.x,a/b.y,a/b.z,a/b.w);
 public static implicit operator float4(Vector4 v) => new(v.X,v.Y,v.Z,v.W);
}
internal readonly struct float3x3(float m00,float m01,float m02,float m10,float m11,float m12,float m20,float m21,float m22)
{
 public readonly float m00 = m00;
 public readonly float m01 = m01;
 public readonly float m02 = m02;
 public readonly float m10 = m10;
 public readonly float m11 = m11;
 public readonly float m12 = m12;
 public readonly float m20 = m20;
 public readonly float m21 = m21;
 public readonly float m22 = m22;
}
internal readonly struct float4x4(float m00,float m01,float m02,float m03,float m10,float m11,float m12,float m13,float m20,float m21,float m22,float m23,float m30,float m31,float m32,float m33)
{
 public readonly float m00 = m00;
 public readonly float m01 = m01;
 public readonly float m02 = m02;
 public readonly float m03 = m03;
 public readonly float m10 = m10;
 public readonly float m11 = m11;
 public readonly float m12 = m12;
 public readonly float m13 = m13;
 public readonly float m20 = m20;
 public readonly float m21 = m21;
 public readonly float m22 = m22;
 public readonly float m23 = m23;
 public readonly float m30 = m30;
 public readonly float m31 = m31;
 public readonly float m32 = m32;
 public readonly float m33 = m33;
}
