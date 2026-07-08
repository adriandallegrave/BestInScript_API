using BestInScript.API.Services;

namespace BestInScript.Tests.Fakes;

/// <summary>
/// Returns scripted colors (queue first, then <see cref="Fixed"/> forever).
/// A null entry simulates an unreadable screen.
/// </summary>
public sealed class FakeScreenSampler : IScreenSampler
{
    public Queue<(int R, int G, int B)?> Colors { get; } = new();
    public (int R, int G, int B)? Fixed { get; set; }

    public (int R, int G, int B)? ColorAt(int x, int y) => Next();
    public (int R, int G, int B)? ColorAtAveraged(int x, int y, int radius) => Next();
    public (int X, int Y)? CursorPosition() => (0, 0);

    private (int R, int G, int B)? Next() => Colors.Count > 0 ? Colors.Dequeue() : Fixed;
}
