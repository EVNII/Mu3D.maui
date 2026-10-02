namespace Mu3D.Maui.Toolkit.Diagnostics;

internal sealed class FrameStatisticsHistory
{
    private readonly double[] values;
    private int nextIndex;

    public FrameStatisticsHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        values = new double[capacity];
    }

    public int Capacity => values.Length;

    public int Count { get; private set; }

    public double Minimum => GetRange().Minimum;

    public double Maximum => GetRange().Maximum;

    public double this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            int firstIndex = (nextIndex - Count + values.Length) % values.Length;
            return values[(firstIndex + index) % values.Length];
        }
    }

    public void Add(double value)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        values[nextIndex] = value;
        nextIndex = (nextIndex + 1) % values.Length;
        if (Count < values.Length)
        {
            Count++;
        }
    }

    public void Clear()
    {
        nextIndex = 0;
        Count = 0;
    }

    private (double Minimum, double Maximum) GetRange()
    {
        if (Count == 0)
        {
            return (0d, 0d);
        }
        double minimum = double.MaxValue;
        double maximum = 0d;
        for (int index = 0; index < Count; index++)
        {
            double value = this[index];
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return (minimum, maximum);
    }
}
