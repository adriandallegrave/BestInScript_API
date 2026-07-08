using BestInScript.API.Engine;

namespace BestInScript.Tests.Fakes;

/// <summary>Records every RunAsync invocation; the returned task never completes.</summary>
public sealed class FakeScriptRunner : IScriptRunner
{
    public List<ScriptEntry> Started { get; } = [];

    /// <summary>The cancellation token handed to each RunAsync, in order — lets tests
    /// assert a run was cancelled (e.g. on stop / ClearAll).</summary>
    public List<CancellationToken> Tokens { get; } = [];

    public Task RunAsync(ScriptEntry entry, CancellationToken ct)
    {
        Started.Add(entry);
        Tokens.Add(ct);
        return new TaskCompletionSource().Task;
    }
}
