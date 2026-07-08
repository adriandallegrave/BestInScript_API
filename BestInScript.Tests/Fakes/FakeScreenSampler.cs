using BestInScript.API.Services;

namespace BestInScript.Tests.Fakes;

/// <summary>
/// Returns scripted colors. A coordinate present in <see cref="ByCoord"/> wins;
/// otherwise the queue is drained first, then <see cref="Fixed"/> forever.
/// A null entry simulates an unreadable screen.
/// </summary>
public sealed class FakeScreenSampler : IScreenSampler
{
    public Queue<(int R, int G, int B)?> Colors { get; } = new();
    public (int R, int G, int B)? Fixed { get; set; }

    /// <summary>Colors keyed by exact screen coordinate (takes precedence over the queue).</summary>
    public Dictionary<(int X, int Y), (int R, int G, int B)?> ByCoord { get; } = new();

    /// <summary>Cursor position returned by <see cref="CursorPosition"/>.</summary>
    public (int X, int Y)? Cursor { get; set; } = (0, 0);

    public (int R, int G, int B)? ColorAt(int x, int y) => Lookup(x, y);
    public (int R, int G, int B)? ColorAtAveraged(int x, int y, int radius) => Lookup(x, y);
    public (int X, int Y)? CursorPosition() => Cursor;

    private (int R, int G, int B)? Lookup(int x, int y)
        => ByCoord.TryGetValue((x, y), out var c) ? c
         : Colors.Count > 0 ? Colors.Dequeue()
         : Fixed;
}
