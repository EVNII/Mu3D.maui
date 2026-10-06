using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Distinguishes physical color from untransformed material data.</summary>
public enum OpenPbrNodeType
{
    /// <summary>One numeric component, replicated internally.</summary>
    Float,
    /// <summary>Three linear ACEScg color components.</summary>
    Color3,
    /// <summary>Two numeric components, such as UV coordinates.</summary>
    Vector2,
    /// <summary>Three numeric components, such as normals or channel distances.</summary>
    Vector3,
    /// <summary>A uniform boolean value.</summary>
    Boolean,
    /// <summary>Four raw numeric channels, including packed opacity.</summary>
    Vector4,
}

/// <summary>Identifies the bounded, portable material graph operations.</summary>
public enum OpenPbrNodeOperation
{
    /// <summary>A typed constant.</summary>
    Constant,
    /// <summary>Mesh UV set zero or one.</summary>
    Texcoord,
    /// <summary>A level-zero texture sample.</summary>
    Image,
    /// <summary>Componentwise sum.</summary>
    Add,
    /// <summary>Componentwise product, optionally with a scalar multiplier.</summary>
    Multiply,
    /// <summary>Linear interpolation of background to foreground.</summary>
    Mix,
    /// <summary>Explicit componentwise clamp.</summary>
    Clamp,
    /// <summary>Decodes a tangent-space normal and transforms it to the inherited world frame.</summary>
    NormalMap,
    /// <summary>Extracts one numeric component.</summary>
    Extract,
    /// <summary>Componentwise difference, optionally subtracting a scalar.</summary>
    Subtract = 9,
    /// <summary>Componentwise minimum, optionally against a scalar.</summary>
    Min = 10,
    /// <summary>Componentwise maximum, optionally against a scalar.</summary>
    Max = 11,
    /// <summary>Componentwise absolute value.</summary>
    Abs = 12,
    /// <summary>Componentwise quotient with a normal FP32 denominator domain.</summary>
    Divide = 13,
    /// <summary>Componentwise square root of nonnegative scalar or vector data.</summary>
    Sqrt = 14,
}

/// <summary>An immutable typed expression used by OpenPBR input bindings.</summary>
/// <remarks>Construction validates types and conservative numeric bounds. Graphs execute directly
/// on the GPU at every surface hit, including secondary rays. No shader compiler is invoked for
/// an authored graph. Color arithmetic is scene-linear ACEScg; raw channels never receive a color transform.</remarks>
public sealed class OpenPbrNode
{
    private OpenPbrNode(OpenPbrNodeOperation operation, OpenPbrNodeType type, Vector4 value,
        OpenPbrNode? a = null, OpenPbrNode? b = null, OpenPbrNode? c = null, OpenPbrTexture? texture = null,
        OpenPbrAddressMode u = OpenPbrAddressMode.Periodic, OpenPbrAddressMode v = OpenPbrAddressMode.Periodic,
        OpenPbrTextureFilter filter = OpenPbrTextureFilter.Linear)
    {
        Operation = operation; Type = type; Value = value; A = a; B = b; C = c;
        Texture = texture; AddressU = u; AddressV = v; Filter = filter;
        (Minimum, Maximum) = Bounds();
        OpenPbrTexture.RequireFinite(Minimum); OpenPbrTexture.RequireFinite(Maximum);
        IsUniform = operation != OpenPbrNodeOperation.Texcoord && operation != OpenPbrNodeOperation.Image &&
            operation != OpenPbrNodeOperation.NormalMap && (a?.IsUniform ?? true) && (b?.IsUniform ?? true) && (c?.IsUniform ?? true);
    }

    /// <summary>Gets the operation.</summary>
    public OpenPbrNodeOperation Operation { get; }
    /// <summary>Gets the output type.</summary>
    public OpenPbrNodeType Type { get; }
    /// <summary>Gets the constant, UV set index, extraction channel or normal scale.</summary>
    public Vector4 Value { get; }
    /// <summary>Gets the first operand, if present.</summary>
    public OpenPbrNode? A { get; }
    /// <summary>Gets the second operand, if present.</summary>
    public OpenPbrNode? B { get; }
    /// <summary>Gets the third operand, if present.</summary>
    public OpenPbrNode? C { get; }
    /// <summary>Gets the image resource, if present.</summary>
    public OpenPbrTexture? Texture { get; }
    /// <summary>Gets horizontal boundary handling.</summary>
    public OpenPbrAddressMode AddressU { get; }
    /// <summary>Gets vertical boundary handling.</summary>
    public OpenPbrAddressMode AddressV { get; }
    /// <summary>Gets image filtering.</summary>
    public OpenPbrTextureFilter Filter { get; }
    /// <summary>Gets conservative output minima used for physical-domain validation.</summary>
    public Vector4 Minimum { get; }
    /// <summary>Gets conservative output maxima used for physical-domain validation.</summary>
    public Vector4 Maximum { get; }
    /// <summary>Gets whether evaluation is independent of the mesh and texture coordinates.</summary>
    public bool IsUniform { get; }

    /// <summary>Creates a finite scalar constant.</summary>
    public static OpenPbrNode Float(float value) => new(OpenPbrNodeOperation.Constant, OpenPbrNodeType.Float, new(value));
    /// <summary>Creates a uniform boolean constant.</summary>
    public static OpenPbrNode Boolean(bool value) => new(OpenPbrNodeOperation.Constant, OpenPbrNodeType.Boolean, new(value ? 1 : 0));
    /// <summary>Creates numeric two-component data.</summary>
    public static OpenPbrNode Vector2(Vector2 value) => new(OpenPbrNodeOperation.Constant, OpenPbrNodeType.Vector2, new(value, 0, 0));
    /// <summary>Creates numeric three-component data.</summary>
    public static OpenPbrNode Vector3(Vector3 value) => new(OpenPbrNodeOperation.Constant, OpenPbrNodeType.Vector3, new(value, 0));
    /// <summary>Creates numeric four-component data.</summary>
    public static OpenPbrNode Vector4(Vector4 value) => new(OpenPbrNodeOperation.Constant, OpenPbrNodeType.Vector4, value);
    /// <summary>Creates a color constant, converting explicitly tagged linear RGB to ACEScg.</summary>
    public static OpenPbrNode Color(LinearRgba value)
    {
        if (value.Alpha != 1) throw new ArgumentException("A color3 input requires alpha one.", nameof(value));
        LinearRgba c = StandardLinearRgbConverter.Convert(value, StandardColorSpaces.AcesCg);
        return new(OpenPbrNodeOperation.Constant, OpenPbrNodeType.Color3,
            value.Red == value.Green && value.Green == value.Blue ? new(value.Red, value.Green, value.Blue, 0) : new(c.Red, c.Green, c.Blue, 0));
    }
    /// <summary>Reads UV set zero or one, with the MaterialX bottom-left convention.</summary>
    public static OpenPbrNode Texcoord(int index = 0) => index is 0 or 1
        ? new(OpenPbrNodeOperation.Texcoord, OpenPbrNodeType.Vector2, new(index, 0, 0, 0))
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Samples a color or data texture using explicit coordinates and boundary handling.</summary>
    /// <remarks>Scalar images read the red channel. Use Extract on a data vector image for packed channels.</remarks>
    public static OpenPbrNode Image(OpenPbrTexture texture, OpenPbrNodeType type, OpenPbrNode? texcoord = null,
        OpenPbrAddressMode addressU = OpenPbrAddressMode.Periodic, OpenPbrAddressMode addressV = OpenPbrAddressMode.Periodic,
        OpenPbrTextureFilter filter = OpenPbrTextureFilter.Linear)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!Enum.IsDefined(type) || type == OpenPbrNodeType.Boolean || texture.IsColor != (type == OpenPbrNodeType.Color3))
            throw new ArgumentException("Image color/data identity must match the node type.", nameof(type));
        if (!Enum.IsDefined(addressU) || !Enum.IsDefined(addressV) || !Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        texcoord ??= Texcoord(); Require(texcoord, OpenPbrNodeType.Vector2);
        return new(OpenPbrNodeOperation.Image, type, default, texcoord, texture: texture, u: addressU, v: addressV, filter: filter);
    }
    /// <summary>Adds identically typed numeric operands.</summary>
    public static OpenPbrNode Add(OpenPbrNode a, OpenPbrNode b) { Same(a, b); return new(OpenPbrNodeOperation.Add, a.Type, default, a, b); }
    /// <summary>Multiplies like types, or a numeric first operand by a scalar second operand.</summary>
    public static OpenPbrNode Multiply(OpenPbrNode a, OpenPbrNode b)
    {
        Numeric(a); Numeric(b); if (a.Type != b.Type && b.Type != OpenPbrNodeType.Float) throw new ArgumentException("Incompatible multiply types.");
        return new(OpenPbrNodeOperation.Multiply, a.Type, default, a, b);
    }
    /// <summary>Subtracts like numeric types, or a scalar second operand from a numeric first operand.</summary>
    /// <param name="a">The numeric first operand, whose type is retained.</param>
    /// <param name="b">A matching numeric operand or a scalar broadcast to every component.</param>
    /// <returns>The componentwise difference with finite conservative bounds.</returns>
    /// <remarks>Color operands remain scene-linear ACEScg. No clipping or color mapping is implicit.</remarks>
    public static OpenPbrNode Subtract(OpenPbrNode a, OpenPbrNode b) => Binary(OpenPbrNodeOperation.Subtract, a, b);
    /// <summary>Selects the minimum of like numeric types, or a numeric first operand and a scalar second operand.</summary>
    /// <param name="a">The numeric first operand, whose type is retained.</param>
    /// <param name="b">A matching numeric operand or a scalar broadcast to every component.</param>
    /// <returns>The componentwise minimum with finite conservative bounds.</returns>
    /// <remarks>Color operands remain scene-linear ACEScg. No clipping or color mapping is implicit.</remarks>
    public static OpenPbrNode Min(OpenPbrNode a, OpenPbrNode b) => Binary(OpenPbrNodeOperation.Min, a, b);
    /// <summary>Selects the maximum of like numeric types, or a numeric first operand and a scalar second operand.</summary>
    /// <param name="a">The numeric first operand, whose type is retained.</param>
    /// <param name="b">A matching numeric operand or a scalar broadcast to every component.</param>
    /// <returns>The componentwise maximum with finite conservative bounds.</returns>
    /// <remarks>Color operands remain scene-linear ACEScg. No clipping or color mapping is implicit.</remarks>
    public static OpenPbrNode Max(OpenPbrNode a, OpenPbrNode b) => Binary(OpenPbrNodeOperation.Max, a, b);
    /// <summary>Takes the absolute value of every component of a numeric operand.</summary>
    /// <param name="input">The numeric operand, whose type is retained.</param>
    /// <returns>The componentwise absolute value with finite conservative bounds.</returns>
    /// <remarks>Color operands remain scene-linear ACEScg. No clipping or color mapping is implicit.</remarks>
    public static OpenPbrNode Abs(OpenPbrNode input)
    {
        Numeric(input);
        return new(OpenPbrNodeOperation.Abs, input.Type, default, input);
    }
    /// <summary>Divides like numeric types, or a numeric first operand by a scalar second operand.</summary>
    /// <param name="a">The numeric numerator, whose type is retained.</param>
    /// <param name="b">A matching numeric denominator or a scalar broadcast to every component.</param>
    /// <returns>The componentwise quotient with finite conservative bounds.</returns>
    /// <remarks>
    /// Each semantic denominator interval must stay strictly positive or strictly negative and
    /// its endpoint nearest zero must be a normal FP32 value. Zero-crossing and subnormal domains
    /// are rejected without adding an epsilon, avoiding tiny divisors that may be flushed to zero
    /// by a GPU. Scalar results are replicated internally; unused vector components are zero.
    /// Color arithmetic remains scene-linear ACEScg, without implicit clipping or color mapping.
    /// </remarks>
    /// <exception cref="ArgumentException">The operands are boolean or have incompatible numeric types.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A semantic denominator interval includes zero or subnormal values, or the quotient bounds are not finite.</exception>
    public static OpenPbrNode Divide(OpenPbrNode a, OpenPbrNode b)
    {
        Numeric(a); Numeric(b);
        if (a.Type != b.Type && b.Type != OpenPbrNodeType.Float) throw new ArgumentException("Incompatible Divide types.");
        for (int channel = 0; channel < SemanticChannels(b.Type); channel++)
        {
            float low = b.Minimum[channel], high = b.Maximum[channel];
            if (low <= 0 && high >= 0 || !float.IsNormal(low > 0 ? low : high))
                throw new ArgumentOutOfRangeException(nameof(b), "Denominator intervals must exclude zero and contain only normal FP32 values.");
        }
        return new(OpenPbrNodeOperation.Divide, a.Type, default, a, b);
    }
    /// <summary>Takes the square root of nonnegative scalar or vector data.</summary>
    /// <param name="input">A Float, Vector2, Vector3 or Vector4 operand, whose type is retained.</param>
    /// <returns>The componentwise square root with finite conservative bounds.</returns>
    /// <remarks>
    /// Every semantic component must have a nonnegative conservative lower bound. No absolute
    /// value, epsilon or clamp is implicit. Scalar results are replicated internally; unused
    /// vector components are zero. Color3 and Boolean operands are not supported.
    /// </remarks>
    /// <exception cref="ArgumentException">The operand is Color3 or Boolean.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A semantic component has a negative conservative lower bound.</exception>
    public static OpenPbrNode Sqrt(OpenPbrNode input)
    {
        Numeric(input);
        if (input.Type == OpenPbrNodeType.Color3) throw new ArgumentException("Square roots require scalar or vector data.", nameof(input));
        for (int channel = 0; channel < SemanticChannels(input.Type); channel++)
            if (input.Minimum[channel] < 0)
                throw new ArgumentOutOfRangeException(nameof(input), "Square roots require nonnegative semantic component bounds.");
        return new(OpenPbrNodeOperation.Sqrt, input.Type, default, input);
    }
    /// <summary>Interpolates background to foreground using a scalar fraction in [0,1].</summary>
    public static OpenPbrNode Mix(OpenPbrNode background, OpenPbrNode foreground, OpenPbrNode amount)
    {
        Same(background, foreground); Require(amount, OpenPbrNodeType.Float);
        if (amount.Minimum.X < 0 || amount.Maximum.X > 1) throw new ArgumentOutOfRangeException(nameof(amount));
        return new(OpenPbrNodeOperation.Mix, background.Type, default, background, foreground, amount);
    }
    /// <summary>Clamps components to explicit constant bounds; no color mapping is implicit.</summary>
    public static OpenPbrNode Clamp(OpenPbrNode input, float low = 0, float high = 1)
    {
        Numeric(input); if (!float.IsFinite(low) || !float.IsFinite(high) || low > high) throw new ArgumentOutOfRangeException(nameof(low));
        return new(OpenPbrNodeOperation.Clamp, input.Type, new(low, high, 0, 0), input);
    }
    /// <summary>Decodes [0,1] raw normal data; X/Y use the scale and Z remains unscaled.</summary>
    /// <remarks>Uses the inherited mesh normal and tangent with its handedness. A zero decoded
    /// vector falls back to the inherited normal. Only tangent-space normal maps are supported.</remarks>
    public static OpenPbrNode NormalMap(OpenPbrNode input, float scale = 1)
    {
        Require(input, OpenPbrNodeType.Vector3);
        for (int channel = 0; channel < 3; channel++)
            if (input.Minimum[channel] < 0 || input.Maximum[channel] > 1)
                throw new ArgumentOutOfRangeException(nameof(input), "Encoded tangent normals require [0,1] data; clamp explicitly if needed.");
        if (!float.IsFinite(scale) || scale < 0 || scale > 1_000_000) throw new ArgumentOutOfRangeException(nameof(scale));
        return new(OpenPbrNodeOperation.NormalMap, OpenPbrNodeType.Vector3, new(scale, 0, 0, 0), input);
    }
    /// <summary>Extracts a zero-based channel as a scalar.</summary>
    public static OpenPbrNode Extract(OpenPbrNode input, int channel)
    {
        Numeric(input);
        int count = input.Type == OpenPbrNodeType.Vector4 ? 4 : input.Type == OpenPbrNodeType.Vector2 ? 2 : input.Type == OpenPbrNodeType.Float ? 1 : 3;
        if (channel < 0 || channel >= count) throw new ArgumentOutOfRangeException(nameof(channel));
        return new(OpenPbrNodeOperation.Extract, OpenPbrNodeType.Float, new(channel, 0, 0, 0), input);
    }

    private static void Require(OpenPbrNode node, OpenPbrNodeType type)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Type != type) throw new ArgumentException($"Expected {type}, received {node.Type}.");
    }
    private static void Numeric(OpenPbrNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Type == OpenPbrNodeType.Boolean) throw new ArgumentException("Boolean nodes are uniform constants.");
    }
    private static void Same(OpenPbrNode a, OpenPbrNode b) { Numeric(a); Require(b, a.Type); }
    private static OpenPbrNode Binary(OpenPbrNodeOperation operation, OpenPbrNode a, OpenPbrNode b)
    {
        Numeric(a); Numeric(b);
        if (a.Type != b.Type && b.Type != OpenPbrNodeType.Float) throw new ArgumentException($"Incompatible {operation} types.");
        return new(operation, a.Type, default, a, b);
    }
    private static int SemanticChannels(OpenPbrNodeType type) => type switch
    {
        OpenPbrNodeType.Float => 1,
        OpenPbrNodeType.Vector2 => 2,
        OpenPbrNodeType.Color3 or OpenPbrNodeType.Vector3 => 3,
        OpenPbrNodeType.Vector4 => 4,
        _ => throw new ArgumentException("Expected a numeric node type.", nameof(type)),
    };
    private (Vector4, Vector4) DivideBounds()
    {
        Vector4 low = default, high = default;
        for (int channel = 0; channel < SemanticChannels(Type); channel++)
        {
            int denominatorChannel = B!.Type == OpenPbrNodeType.Float ? 0 : channel;
            float denominatorLow = B.Minimum[denominatorChannel], denominatorHigh = B.Maximum[denominatorChannel];
            float aa = A!.Minimum[channel] / denominatorLow, ab = A.Minimum[channel] / denominatorHigh,
                ba = A.Maximum[channel] / denominatorLow, bb = A.Maximum[channel] / denominatorHigh;
            low[channel] = MathF.Min(MathF.Min(aa, ab), MathF.Min(ba, bb));
            high[channel] = MathF.Max(MathF.Max(aa, ab), MathF.Max(ba, bb));
        }
        return Type == OpenPbrNodeType.Float ? (new(low.X), new(high.X)) : (low, high);
    }
    private (Vector4, Vector4) SqrtBounds()
    {
        Vector4 low = default, high = default;
        for (int channel = 0; channel < SemanticChannels(Type); channel++)
        {
            low[channel] = MathF.Sqrt(A!.Minimum[channel]);
            high[channel] = MathF.Sqrt(A.Maximum[channel]);
        }
        return Type == OpenPbrNodeType.Float ? (new(low.X), new(high.X)) : (low, high);
    }
    private (Vector4, Vector4) Bounds()
    {
        switch (Operation)
        {
            case OpenPbrNodeOperation.Constant: return (Value, Value);
            case OpenPbrNodeOperation.Texcoord: return (new(-1e20f, -1e20f, 0, 0), new(1e20f, 1e20f, 0, 0));
            case OpenPbrNodeOperation.Image:
                return Type == OpenPbrNodeType.Float ? (new(Texture!.Minimum.X), new(Texture!.Maximum.X)) : (Texture!.Minimum, Texture!.Maximum);
            case OpenPbrNodeOperation.Add: return (A!.Minimum + B!.Minimum, A.Maximum + B.Maximum);
            case OpenPbrNodeOperation.Multiply:
                Vector4 aa = A!.Minimum * B!.Minimum, ab = A.Minimum * B.Maximum,
                    ba = A.Maximum * B.Minimum, bb = A.Maximum * B.Maximum;
                return (System.Numerics.Vector4.Min(System.Numerics.Vector4.Min(aa, ab), System.Numerics.Vector4.Min(ba, bb)),
                    System.Numerics.Vector4.Max(System.Numerics.Vector4.Max(aa, ab), System.Numerics.Vector4.Max(ba, bb)));
            case OpenPbrNodeOperation.Mix: return (System.Numerics.Vector4.Min(A!.Minimum, B!.Minimum), System.Numerics.Vector4.Max(A.Maximum, B.Maximum));
            case OpenPbrNodeOperation.Clamp: return (System.Numerics.Vector4.Clamp(A!.Minimum, new(Value.X), new(Value.Y)), System.Numerics.Vector4.Clamp(A.Maximum, new(Value.X), new(Value.Y)));
            case OpenPbrNodeOperation.NormalMap: return (new(-1, -1, -1, 0), new(1, 1, 1, 0));
            case OpenPbrNodeOperation.Extract: return (new(A!.Minimum[(int)Value.X]), new(A.Maximum[(int)Value.X]));
            case OpenPbrNodeOperation.Subtract: return (A!.Minimum - B!.Maximum, A.Maximum - B.Minimum);
            case OpenPbrNodeOperation.Min:
                return (System.Numerics.Vector4.Min(A!.Minimum, B!.Minimum), System.Numerics.Vector4.Min(A.Maximum, B.Maximum));
            case OpenPbrNodeOperation.Max:
                return (System.Numerics.Vector4.Max(A!.Minimum, B!.Minimum), System.Numerics.Vector4.Max(A.Maximum, B.Maximum));
            case OpenPbrNodeOperation.Abs:
                Vector4 absoluteMinimum = System.Numerics.Vector4.Abs(A!.Minimum), absoluteMaximum = System.Numerics.Vector4.Abs(A.Maximum);
                Vector4 minimum = System.Numerics.Vector4.Min(absoluteMinimum, absoluteMaximum);
                for (int channel = 0; channel < 4; channel++)
                    if (A.Minimum[channel] <= 0 && A.Maximum[channel] >= 0) minimum[channel] = 0;
                return (minimum, System.Numerics.Vector4.Max(absoluteMinimum, absoluteMaximum));
            case OpenPbrNodeOperation.Divide: return DivideBounds();
            case OpenPbrNodeOperation.Sqrt: return SqrtBounds();
            default: throw new InvalidOperationException();
        }
    }
}
