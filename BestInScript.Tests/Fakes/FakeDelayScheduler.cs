using BestInScript.API.Engine;

namespace BestInScript.Tests.Fakes;

/// <summary>
/// Records requested delays and completes immediately. Optionally cancels a
/// CancellationTokenSource after N delays so loop-based code terminates.
/// </summary>
public sealed class FakeDelayScheduler : IDelayScheduler
{
    public List<TimeSpan> Delays { get; } = [];

    /// <summary>When set, <see cref="Cts"/> is cancelled once this many delays have been requested.</summary>
    public int? CancelAfter { get; set; }
    public CancellationTokenSource? Cts { get; set; }

    public Task Delay(TimeSpan duration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Delays.Add(duration);
        if (CancelAfter is int n && Delays.Count >= n)
            Cts?.Cancel();
        return Task.CompletedTask;
    }
}
