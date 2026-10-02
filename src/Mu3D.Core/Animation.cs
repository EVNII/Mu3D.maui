using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Controls interpolation between adjacent transform animation keys.</summary>
public enum AnimationInterpolation
{
    /// <summary>Holds the preceding key value until the next key.</summary>
    Step,

    /// <summary>Linearly interpolates vectors and spherically interpolates rotations.</summary>
    Linear,

    /// <summary>
    /// Uses cubic Hermite interpolation with explicitly supplied per-key incoming and outgoing
    /// derivatives. Rotation results are normalized after component-wise interpolation.
    /// </summary>
    CubicSpline,
}

/// <summary>Controls how clip time outside its duration is resolved.</summary>
public enum AnimationWrapMode
{
    /// <summary>Clamps time to the first or final key.</summary>
    Clamp,

    /// <summary>Repeats positive or negative time across the clip duration.</summary>
    Loop,
}

/// <summary>Identifies a Vector3 transform property animated by a track.</summary>
public enum Vector3AnimationTarget
{
    /// <summary>Animates local translation.</summary>
    Position,

    /// <summary>Animates local scale.</summary>
    Scale,
}

/// <summary>Base class for one immutable FP32 transform animation track.</summary>
public abstract class AnimationTrack
{
    /// <summary>Initializes a track.</summary>
    protected AnimationTrack(SceneNode target, AnimationInterpolation interpolation)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(interpolation))
        {
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        }
        Target = target;
        Interpolation = interpolation;
    }

    /// <summary>Gets the target scene node.</summary>
    public SceneNode Target { get; }

    /// <summary>Gets interpolation between adjacent keys.</summary>
    public AnimationInterpolation Interpolation { get; }

    /// <summary>Gets the final key time in seconds.</summary>
    public abstract float Duration { get; }

    /// <summary>Samples the track at a finite time and writes its target transform.</summary>
    public abstract void Apply(float timeSeconds);

    /// <summary>Copies and validates strictly increasing non-negative key times.</summary>
    protected static float[] CopyTimes(IEnumerable<float> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        float[] result = [.. times];
        if (result.Length == 0)
        {
            throw new ArgumentException("An animation track requires at least one key.", nameof(times));
        }
        float previous = -1f;
        foreach (float time in result)
        {
            if (!float.IsFinite(time) || time < 0f || time <= previous)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(times),
                    "Animation key times must be finite, non-negative and strictly increasing.");
            }
            previous = time;
        }
        return result;
    }

    /// <summary>Locates the preceding key and normalized interpolation amount.</summary>
    protected static (int Lower, int Upper, float Amount) Locate(float[] times, float timeSeconds)
    {
        if (!float.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }
        if (timeSeconds <= times[0])
        {
            return (0, 0, 0f);
        }
        int final = times.Length - 1;
        if (timeSeconds >= times[final])
        {
            return (final, final, 0f);
        }
        int upper = Array.BinarySearch(times, timeSeconds);
        if (upper >= 0)
        {
            return (upper, upper, 0f);
        }
        upper = ~upper;
        int lower = upper - 1;
        float amount = (timeSeconds - times[lower]) / (times[upper] - times[lower]);
        return (lower, upper, amount);
    }
}

/// <summary>Animates one node's local position or scale with immutable FP32 keys.</summary>
public sealed class Vector3AnimationTrack : AnimationTrack
{
    private float[] times = [];
    private Vector3[] values = [];
    private readonly Vector3[]? inTangents;
    private readonly Vector3[]? outTangents;

    /// <summary>Initializes a position or scale track and copies its keys.</summary>
    public Vector3AnimationTrack(
        SceneNode target,
        Vector3AnimationTarget property,
        IEnumerable<float> times,
        IEnumerable<Vector3> values,
        AnimationInterpolation interpolation = AnimationInterpolation.Linear)
        : base(target, interpolation)
    {
        if (interpolation == AnimationInterpolation.CubicSpline)
        {
            throw new ArgumentException(
                "Cubic-spline tracks require explicit incoming and outgoing tangents.",
                nameof(interpolation));
        }
        Initialize(property, times, values);
    }

    /// <summary>
    /// Initializes a cubic-Hermite position or scale track and copies its values and derivatives.
    /// Tangents are derivatives per second and therefore are scaled by each key interval at sample
    /// time, matching the glTF cubic-spline contract.
    /// </summary>
    public Vector3AnimationTrack(
        SceneNode target,
        Vector3AnimationTarget property,
        IEnumerable<float> times,
        IEnumerable<Vector3> values,
        IEnumerable<Vector3> inTangents,
        IEnumerable<Vector3> outTangents)
        : base(target, AnimationInterpolation.CubicSpline)
    {
        Initialize(property, times, values);
        this.inTangents = CopyFiniteVectors(inTangents, this.times.Length, nameof(inTangents));
        this.outTangents = CopyFiniteVectors(outTangents, this.times.Length, nameof(outTangents));
        InTangents = Array.AsReadOnly(this.inTangents);
        OutTangents = Array.AsReadOnly(this.outTangents);
    }

    private void Initialize(
        Vector3AnimationTarget property,
        IEnumerable<float> times,
        IEnumerable<Vector3> values)
    {
        if (!Enum.IsDefined(property))
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }
        this.times = CopyTimes(times);
        ArgumentNullException.ThrowIfNull(values);
        this.values = [.. values];
        if (this.values.Length != this.times.Length)
        {
            throw new ArgumentException("Animation values must match the key-time count.", nameof(values));
        }
        foreach (Vector3 value in this.values)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(values), "Animation values must be finite.");
            }
        }
        Property = property;
        Times = new ReadOnlyCollection<float>(this.times);
        Values = new ReadOnlyCollection<Vector3>(this.values);
    }

    /// <summary>Gets the animated transform property.</summary>
    public Vector3AnimationTarget Property { get; private set; }

    /// <summary>Gets the immutable key times.</summary>
    public IReadOnlyList<float> Times { get; private set; } = Array.Empty<float>();

    /// <summary>Gets the immutable key values.</summary>
    public IReadOnlyList<Vector3> Values { get; private set; } = Array.Empty<Vector3>();

    /// <summary>
    /// Gets immutable incoming derivatives for a cubic-spline track, or <see langword="null"/> for
    /// Step and Linear tracks.
    /// </summary>
    public IReadOnlyList<Vector3>? InTangents { get; }

    /// <summary>
    /// Gets immutable outgoing derivatives for a cubic-spline track, or <see langword="null"/> for
    /// Step and Linear tracks.
    /// </summary>
    public IReadOnlyList<Vector3>? OutTangents { get; }

    /// <inheritdoc />
    public override float Duration => times[^1];

    /// <inheritdoc />
    public override void Apply(float timeSeconds)
    {
        Vector3 value = Sample(timeSeconds);
        if (Property == Vector3AnimationTarget.Position)
        {
            Target.Transform.Position = value;
        }
        else
        {
            Target.Transform.Scale = value;
        }
    }

    internal Vector3 Sample(float timeSeconds)
    {
        (int lower, int upper, float amount) = Locate(times, timeSeconds);
        if (lower == upper || Interpolation == AnimationInterpolation.Step)
        {
            return values[lower];
        }
        if (Interpolation == AnimationInterpolation.Linear)
        {
            return Vector3.Lerp(values[lower], values[upper], amount);
        }
        float interval = times[upper] - times[lower];
        return CubicHermite(
            values[lower],
            outTangents![lower] * interval,
            values[upper],
            inTangents![upper] * interval,
            amount);
    }

    private static Vector3[] CopyFiniteVectors(
        IEnumerable<Vector3> source,
        int expectedCount,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source);
        Vector3[] result = [.. source];
        if (result.Length != expectedCount)
        {
            throw new ArgumentException("Animation tangents must match the key-time count.", parameterName);
        }
        if (result.Any(static value =>
            !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Animation tangents must be finite.");
        }
        return result;
    }

    private static Vector3 CubicHermite(
        Vector3 start,
        Vector3 startTangent,
        Vector3 end,
        Vector3 endTangent,
        float amount)
    {
        float squared = amount * amount;
        float cubed = squared * amount;
        return ((2f * cubed - 3f * squared + 1f) * start) +
            ((cubed - 2f * squared + amount) * startTangent) +
            ((-2f * cubed + 3f * squared) * end) +
            ((cubed - squared) * endTangent);
    }
}

/// <summary>Animates one node's local rotation with normalized FP32 quaternion keys.</summary>
public sealed class QuaternionAnimationTrack : AnimationTrack
{
    private float[] times = [];
    private Quaternion[] values = [];
    private readonly Vector4[]? inTangents;
    private readonly Vector4[]? outTangents;

    /// <summary>Initializes a rotation track and copies its keys.</summary>
    public QuaternionAnimationTrack(
        SceneNode target,
        IEnumerable<float> times,
        IEnumerable<Quaternion> values,
        AnimationInterpolation interpolation = AnimationInterpolation.Linear)
        : base(target, interpolation)
    {
        if (interpolation == AnimationInterpolation.CubicSpline)
        {
            throw new ArgumentException(
                "Cubic-spline tracks require explicit incoming and outgoing tangents.",
                nameof(interpolation));
        }
        Initialize(times, values);
    }

    /// <summary>
    /// Initializes a component-wise cubic-Hermite rotation track. Tangents are quaternion-component
    /// derivatives per second and are not themselves normalized.
    /// </summary>
    public QuaternionAnimationTrack(
        SceneNode target,
        IEnumerable<float> times,
        IEnumerable<Quaternion> values,
        IEnumerable<Vector4> inTangents,
        IEnumerable<Vector4> outTangents)
        : base(target, AnimationInterpolation.CubicSpline)
    {
        Initialize(times, values);
        this.inTangents = CopyFiniteVectors(inTangents, this.times.Length, nameof(inTangents));
        this.outTangents = CopyFiniteVectors(outTangents, this.times.Length, nameof(outTangents));
        InTangents = Array.AsReadOnly(this.inTangents);
        OutTangents = Array.AsReadOnly(this.outTangents);
    }

    private void Initialize(IEnumerable<float> times, IEnumerable<Quaternion> values)
    {
        this.times = CopyTimes(times);
        ArgumentNullException.ThrowIfNull(values);
        this.values = [.. values];
        if (this.values.Length != this.times.Length)
        {
            throw new ArgumentException("Animation values must match the key-time count.", nameof(values));
        }
        for (int index = 0; index < this.values.Length; index++)
        {
            Quaternion value = this.values[index];
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z) || !float.IsFinite(value.W) ||
                value.LengthSquared() <= float.Epsilon)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(values),
                    "Rotation keys must be finite non-zero quaternions.");
            }
            this.values[index] = Quaternion.Normalize(value);
        }
        Times = new ReadOnlyCollection<float>(this.times);
        Values = new ReadOnlyCollection<Quaternion>(this.values);
    }

    /// <summary>Gets the immutable key times.</summary>
    public IReadOnlyList<float> Times { get; private set; } = Array.Empty<float>();

    /// <summary>Gets normalized immutable rotation keys.</summary>
    public IReadOnlyList<Quaternion> Values { get; private set; } = Array.Empty<Quaternion>();

    /// <summary>Gets cubic incoming component derivatives, or null for Step and Linear tracks.</summary>
    public IReadOnlyList<Vector4>? InTangents { get; }

    /// <summary>Gets cubic outgoing component derivatives, or null for Step and Linear tracks.</summary>
    public IReadOnlyList<Vector4>? OutTangents { get; }

    /// <inheritdoc />
    public override float Duration => times[^1];

    /// <inheritdoc />
    public override void Apply(float timeSeconds)
    {
        Target.Transform.Rotation = Sample(timeSeconds);
    }

    internal Quaternion Sample(float timeSeconds)
    {
        (int lower, int upper, float amount) = Locate(times, timeSeconds);
        Quaternion value;
        if (lower == upper || Interpolation == AnimationInterpolation.Step)
        {
            value = values[lower];
        }
        else if (Interpolation == AnimationInterpolation.Linear)
        {
            value = Quaternion.Slerp(values[lower], values[upper], amount);
        }
        else
        {
            float interval = times[upper] - times[lower];
            Vector4 start = new(values[lower].X, values[lower].Y, values[lower].Z, values[lower].W);
            Vector4 end = new(values[upper].X, values[upper].Y, values[upper].Z, values[upper].W);
            Vector4 sampled = CubicHermite(
                start,
                outTangents![lower] * interval,
                end,
                inTangents![upper] * interval,
                amount);
            value = new Quaternion(sampled.X, sampled.Y, sampled.Z, sampled.W);
            if (value.LengthSquared() <= float.Epsilon)
            {
                throw new InvalidOperationException("Cubic rotation interpolation produced a zero quaternion.");
            }
        }
        return Quaternion.Normalize(value);
    }

    private static Vector4[] CopyFiniteVectors(
        IEnumerable<Vector4> source,
        int expectedCount,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source);
        Vector4[] result = [.. source];
        if (result.Length != expectedCount)
        {
            throw new ArgumentException("Animation tangents must match the key-time count.", parameterName);
        }
        if (result.Any(static value =>
            !float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W)))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Animation tangents must be finite.");
        }
        return result;
    }

    private static Vector4 CubicHermite(
        Vector4 start,
        Vector4 startTangent,
        Vector4 end,
        Vector4 endTangent,
        float amount)
    {
        float squared = amount * amount;
        float cubed = squared * amount;
        return ((2f * cubed - 3f * squared + 1f) * start) +
            ((cubed - 2f * squared + amount) * startTangent) +
            ((-2f * cubed + 3f * squared) * end) +
            ((cubed - squared) * endTangent);
    }
}

/// <summary>Animates all morph-target weights on one mesh with immutable FP32 keys.</summary>
public sealed class MorphWeightAnimationTrack : AnimationTrack
{
    private float[] times = [];
    private float[][] values = [];
    private readonly float[][]? inTangents;
    private readonly float[][]? outTangents;

    /// <summary>Initializes a morph-weight track and copies every key vector.</summary>
    public MorphWeightAnimationTrack(
        Mesh target,
        IEnumerable<float> times,
        IEnumerable<IReadOnlyList<float>> values,
        AnimationInterpolation interpolation = AnimationInterpolation.Linear)
        : base(target, interpolation)
    {
        if (interpolation == AnimationInterpolation.CubicSpline)
        {
            throw new ArgumentException(
                "Cubic-spline tracks require explicit incoming and outgoing tangents.",
                nameof(interpolation));
        }
        Initialize(target, times, values);
    }

    /// <summary>
    /// Initializes a cubic-Hermite morph-weight track. Every value and tangent vector must match the
    /// mesh's morph-target count; tangents are derivatives per second.
    /// </summary>
    public MorphWeightAnimationTrack(
        Mesh target,
        IEnumerable<float> times,
        IEnumerable<IReadOnlyList<float>> values,
        IEnumerable<IReadOnlyList<float>> inTangents,
        IEnumerable<IReadOnlyList<float>> outTangents)
        : base(target, AnimationInterpolation.CubicSpline)
    {
        Initialize(target, times, values);
        this.inTangents = CopyFiniteKeys(
            inTangents,
            this.times.Length,
            target.Geometry.MorphTargets.Count,
            nameof(inTangents));
        this.outTangents = CopyFiniteKeys(
            outTangents,
            this.times.Length,
            target.Geometry.MorphTargets.Count,
            nameof(outTangents));
        InTangents = AsReadOnlyKeys(this.inTangents);
        OutTangents = AsReadOnlyKeys(this.outTangents);
    }

    private void Initialize(
        Mesh target,
        IEnumerable<float> times,
        IEnumerable<IReadOnlyList<float>> values)
    {
        if (target.Geometry.MorphTargets.Count == 0)
        {
            throw new ArgumentException("A morph-weight track requires morph targets.", nameof(target));
        }
        this.times = CopyTimes(times);
        ArgumentNullException.ThrowIfNull(values);
        this.values = values.Select(static value => value?.ToArray() ??
            throw new ArgumentException("Morph animation values cannot contain null.", nameof(values))).ToArray();
        if (this.values.Length != this.times.Length)
        {
            throw new ArgumentException("Morph values must match the key-time count.", nameof(values));
        }
        foreach (float[] key in this.values)
        {
            if (key.Length != target.Geometry.MorphTargets.Count)
            {
                throw new ArgumentException("Every morph key must match the target count.", nameof(values));
            }
            if (key.Any(static weight => !float.IsFinite(weight)))
            {
                throw new ArgumentOutOfRangeException(nameof(values), "Morph animation weights must be finite.");
            }
        }
        Times = new ReadOnlyCollection<float>(this.times);
        Values = new ReadOnlyCollection<IReadOnlyList<float>>(
            this.values.Select(static value => (IReadOnlyList<float>)Array.AsReadOnly(value)).ToArray());
    }

    /// <summary>Gets the target mesh.</summary>
    public new Mesh Target => (Mesh)base.Target;

    /// <summary>Gets the immutable key times.</summary>
    public IReadOnlyList<float> Times { get; private set; } = Array.Empty<float>();

    /// <summary>Gets immutable morph-weight vectors.</summary>
    public IReadOnlyList<IReadOnlyList<float>> Values { get; private set; } =
        Array.Empty<IReadOnlyList<float>>();

    /// <summary>Gets cubic incoming weight derivatives, or null for Step and Linear tracks.</summary>
    public IReadOnlyList<IReadOnlyList<float>>? InTangents { get; }

    /// <summary>Gets cubic outgoing weight derivatives, or null for Step and Linear tracks.</summary>
    public IReadOnlyList<IReadOnlyList<float>>? OutTangents { get; }

    /// <inheritdoc />
    public override float Duration => times[^1];

    /// <inheritdoc />
    public override void Apply(float timeSeconds)
    {
        Span<float> sampled = stackalloc float[Target.Geometry.MorphTargets.Count];
        Sample(timeSeconds, sampled);
        Target.SetMorphWeights(sampled);
    }

    internal void Sample(float timeSeconds, Span<float> destination)
    {
        if (destination.Length != Target.Geometry.MorphTargets.Count)
        {
            throw new ArgumentException("Morph sample destination has the wrong length.", nameof(destination));
        }
        (int lower, int upper, float amount) = Locate(times, timeSeconds);
        float squared = amount * amount;
        float cubed = squared * amount;
        float interval = lower == upper ? 0f : times[upper] - times[lower];
        for (int index = 0; index < destination.Length; index++)
        {
            if (lower == upper || Interpolation == AnimationInterpolation.Step)
            {
                destination[index] = values[lower][index];
            }
            else if (Interpolation == AnimationInterpolation.Linear)
            {
                destination[index] =
                    values[lower][index] + ((values[upper][index] - values[lower][index]) * amount);
            }
            else
            {
                destination[index] =
                    ((2f * cubed - 3f * squared + 1f) * values[lower][index]) +
                    ((cubed - 2f * squared + amount) * outTangents![lower][index] * interval) +
                    ((-2f * cubed + 3f * squared) * values[upper][index]) +
                    ((cubed - squared) * inTangents![upper][index] * interval);
            }
        }
    }

    private static float[][] CopyFiniteKeys(
        IEnumerable<IReadOnlyList<float>> source,
        int expectedKeyCount,
        int expectedValueCount,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source);
        float[][] result = source.Select(value => value?.ToArray() ??
            throw new ArgumentException("Morph tangent vectors cannot contain null.", parameterName)).ToArray();
        if (result.Length != expectedKeyCount || result.Any(value => value.Length != expectedValueCount))
        {
            throw new ArgumentException(
                "Morph tangent vectors must match the key and target counts.",
                parameterName);
        }
        if (result.Any(static value => value.Any(static component => !float.IsFinite(component))))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Morph tangents must be finite.");
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<float>> AsReadOnlyKeys(float[][] source) =>
        new ReadOnlyCollection<IReadOnlyList<float>>(
            source.Select(static value => (IReadOnlyList<float>)Array.AsReadOnly(value)).ToArray());
}

/// <summary>Supplies one weighted clip to an <see cref="AnimationMixer"/>.</summary>
public sealed class AnimationLayer
{
    private float time;
    private float weight = 1f;
    private AnimationWrapMode wrapMode = AnimationWrapMode.Loop;

    /// <summary>Initializes an enabled animation layer.</summary>
    public AnimationLayer(AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Clip = clip;
    }

    /// <summary>Gets the immutable clip sampled by this layer.</summary>
    public AnimationClip Clip { get; }

    /// <summary>Gets or sets whether this layer contributes.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the finite layer time in seconds.</summary>
    public float Time
    {
        get => time;
        set
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            time = value;
        }
    }

    /// <summary>Gets or sets the finite non-negative blend weight.</summary>
    public float Weight
    {
        get => weight;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            weight = value;
        }
    }

    /// <summary>Gets or sets time wrapping for this layer.</summary>
    public AnimationWrapMode WrapMode
    {
        get => wrapMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            wrapMode = value;
        }
    }

    internal float ResolvedTime => Clip.ResolveTime(time, wrapMode);
}

/// <summary>
/// Blends enabled override layers against captured local TRS base poses without accumulating drift.
/// </summary>
public sealed class AnimationMixer
{
    private readonly AnimationLayer[] layers;
    private readonly Dictionary<SceneNode, TransformPose> basePoses;

    /// <summary>Initializes a mixer, copies its layers and captures every targeted node's base pose.</summary>
    public AnimationMixer(IEnumerable<AnimationLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        this.layers = [.. layers];
        if (this.layers.Length == 0 || this.layers.Any(static layer => layer is null))
        {
            throw new ArgumentException("An animation mixer requires non-null layers.", nameof(layers));
        }
        Layers = new ReadOnlyCollection<AnimationLayer>(this.layers);
        basePoses = new Dictionary<SceneNode, TransformPose>(ReferenceEqualityComparer.Instance);
        foreach (AnimationLayer layer in this.layers)
        {
            foreach (AnimationTrack track in layer.Clip.Tracks)
            {
                SceneNode node = track.Target;
                basePoses.TryAdd(
                    node,
                    new TransformPose(
                        node.Transform.Position,
                        node.Transform.Rotation,
                        node.Transform.Scale));
            }
        }
    }

    /// <summary>Gets the ordered mutable layer states.</summary>
    public IReadOnlyList<AnimationLayer> Layers { get; }

    /// <summary>Replaces captured base poses with the targets' current local transforms.</summary>
    public void CaptureBasePose()
    {
        foreach (SceneNode node in basePoses.Keys)
        {
            basePoses[node] = new TransformPose(
                node.Transform.Position,
                node.Transform.Rotation,
                node.Transform.Scale);
        }
    }

    /// <summary>Samples and blends every enabled non-zero layer into its targeted local transforms.</summary>
    public void Apply()
    {
        foreach ((SceneNode node, TransformPose pose) in basePoses)
        {
            node.Transform.Position = BlendVector(node, Vector3AnimationTarget.Position, pose.Position);
            node.Transform.Scale = BlendVector(node, Vector3AnimationTarget.Scale, pose.Scale);
            node.Transform.Rotation = BlendRotation(node, pose.Rotation);
        }
    }

    private Vector3 BlendVector(SceneNode node, Vector3AnimationTarget property, Vector3 baseValue)
    {
        Vector3 weightedSum = Vector3.Zero;
        float total = 0f;
        foreach (AnimationLayer layer in layers)
        {
            if (!layer.IsEnabled || layer.Weight <= 0f)
            {
                continue;
            }
            foreach (AnimationTrack candidate in layer.Clip.Tracks)
            {
                if (candidate is not Vector3AnimationTrack track)
                {
                    continue;
                }
                if (ReferenceEquals(track.Target, node) && track.Property == property)
                {
                    weightedSum += track.Sample(layer.ResolvedTime) * layer.Weight;
                    total += layer.Weight;
                }
            }
        }
        if (total <= 0f)
        {
            return baseValue;
        }
        float scale = total > 1f ? 1f / total : 1f;
        return baseValue * Math.Max(0f, 1f - total) + weightedSum * scale;
    }

    private Quaternion BlendRotation(SceneNode node, Quaternion baseValue)
    {
        Quaternion reference = Quaternion.Normalize(baseValue);
        Vector4 weightedSum = Vector4.Zero;
        float total = 0f;
        foreach (AnimationLayer layer in layers)
        {
            if (!layer.IsEnabled || layer.Weight <= 0f)
            {
                continue;
            }
            foreach (AnimationTrack candidate in layer.Clip.Tracks)
            {
                if (candidate is not QuaternionAnimationTrack track)
                {
                    continue;
                }
                if (ReferenceEquals(track.Target, node))
                {
                    Quaternion value = track.Sample(layer.ResolvedTime);
                    Quaternion aligned = Quaternion.Dot(reference, value) < 0f
                        ? new Quaternion(-value.X, -value.Y, -value.Z, -value.W)
                        : value;
                    weightedSum += ToVector(aligned) * layer.Weight;
                    total += layer.Weight;
                }
            }
        }
        if (total <= 0f)
        {
            return reference;
        }
        float scale = total > 1f ? 1f / total : 1f;
        Vector4 sum = ToVector(reference) * Math.Max(0f, 1f - total) + weightedSum * scale;
        Quaternion result = new(sum.X, sum.Y, sum.Z, sum.W);
        return result.LengthSquared() > float.Epsilon ? Quaternion.Normalize(result) : baseValue;
    }

    private static Vector4 ToVector(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private readonly record struct TransformPose(Vector3 Position, Quaternion Rotation, Vector3 Scale);
}

/// <summary>Groups immutable transform tracks under one named timeline.</summary>
public sealed class AnimationClip
{
    /// <summary>Initializes a clip and copies its tracks.</summary>
    public AnimationClip(IEnumerable<AnimationTrack> tracks, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        AnimationTrack[] copied = [.. tracks];
        if (copied.Length == 0 || copied.Any(static track => track is null))
        {
            throw new ArgumentException("An animation clip requires non-null tracks.", nameof(tracks));
        }
        Tracks = new ReadOnlyCollection<AnimationTrack>(copied);
        Duration = copied.Max(static track => track.Duration);
        Name = name;
    }

    /// <summary>Gets the optional clip name.</summary>
    public string? Name { get; }

    /// <summary>Gets immutable clip tracks.</summary>
    public IReadOnlyList<AnimationTrack> Tracks { get; }

    /// <summary>Gets the clip duration in seconds.</summary>
    public float Duration { get; }

    /// <summary>Samples every track and writes target local transforms.</summary>
    public void Apply(float timeSeconds, AnimationWrapMode wrapMode = AnimationWrapMode.Clamp)
    {
        float resolved = ResolveTime(timeSeconds, wrapMode);
        foreach (AnimationTrack track in Tracks)
        {
            track.Apply(resolved);
        }
    }

    internal float ResolveTime(float timeSeconds, AnimationWrapMode wrapMode)
    {
        if (!float.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }
        if (!Enum.IsDefined(wrapMode))
        {
            throw new ArgumentOutOfRangeException(nameof(wrapMode));
        }
        return wrapMode == AnimationWrapMode.Clamp || Duration <= 0f
            ? Math.Clamp(timeSeconds, 0f, Duration)
            : ((timeSeconds % Duration) + Duration) % Duration;
    }
}
