namespace BestInScript.API.Engine
{
    /// <summary>
    /// Delay seam for the run loops. Production forwards to
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> so timing is
    /// identical to calling Task.Delay directly; tests substitute a recording
    /// fake that completes immediately.
    /// </summary>
    public interface IDelayScheduler
    {
        Task Delay(TimeSpan duration, CancellationToken ct);
    }

    public sealed class TaskDelayScheduler : IDelayScheduler
    {
        public Task Delay(TimeSpan duration, CancellationToken ct)
            => Task.Delay(duration, ct);
    }
}
