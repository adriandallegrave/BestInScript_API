namespace BestInScript.API.Engine
{
    /// <summary>
    /// Execution seam between the coordinator (ownership bookkeeping) and the
    /// executor (run loops). Production implementation is
    /// <see cref="ScriptExecutor"/>; ownership tests substitute a fake.
    /// </summary>
    public interface IScriptRunner
    {
        Task RunAsync(ScriptEntry entry, CancellationToken ct);
    }
}
