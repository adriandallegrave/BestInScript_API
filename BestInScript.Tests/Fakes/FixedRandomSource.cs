using BestInScript.API.Engine;

namespace BestInScript.Tests.Fakes;

/// <summary>Returns a scripted sequence of values; the last value repeats.</summary>
public sealed class FixedRandomSource(params double[] values) : IRandomSource
{
    private int _i;

    public double NextDouble() => values[Math.Min(_i++, values.Length - 1)];
}
