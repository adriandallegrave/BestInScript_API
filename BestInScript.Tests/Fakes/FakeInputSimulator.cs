using BestInScript.API.Services;

namespace BestInScript.Tests.Fakes;

/// <summary>Records key events in order instead of sending real input.</summary>
public sealed class FakeInputSimulator : IInputSimulator
{
    public List<(string Kind, string Key)> Events { get; } = [];

    public void KeyPress(string key) => Events.Add(("press", key));
    public void KeyDown(string key) => Events.Add(("down", key));
    public void KeyUp(string key) => Events.Add(("up", key));
}
