using BestInScript.API.Engine;

namespace BestInScript.Tests.Fakes;

/// <summary>Records every RunAsync invocation; the returned task never completes.</summary>
public sealed class FakeScriptRunner : IScriptRunner
{
    public List<ScriptEntry> Started { get; } = [];

    public Task RunAsync(ScriptEntry entry, CancellationToken ct)
    {
        Started.Add(entry);
        return new TaskCompletionSource().Task;
    }
}
