using BestInScript.API.Services;

namespace BestInScript.Tests;

public class ResolveVkTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("M", 0x4D)]
    [InlineData("Z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("NUMPAD0", 0x60)]
    [InlineData("NUMPAD9", 0x69)]
    [InlineData("SPACE", 0x20)]
    [InlineData("TAB", 0x09)]
    public void KnownKeys_MapToVirtualKeys(string key, int expectedVk)
        => Assert.Equal((ushort)expectedVk, InputSimulatorService.ResolveVk(key));

    [Theory]
    [InlineData("CTRL", 0xA2)]
    [InlineData("CONTROL", 0xA2)]
    [InlineData("LCTRL", 0xA2)]
    [InlineData("RCTRL", 0xA3)]
    [InlineData("SHIFT", 0xA0)]
    [InlineData("LSHIFT", 0xA0)]
    [InlineData("RSHIFT", 0xA1)]
    [InlineData("ALT", 0xA4)]
    [InlineData("RALT", 0xA5)]
    [InlineData("ESC", 0x1B)]
    [InlineData("ESCAPE", 0x1B)]
    [InlineData("ENTER", 0x0D)]
    [InlineData("RETURN", 0x0D)]
    [InlineData("PGUP", 0x21)]
    [InlineData("PAGEUP", 0x21)]
    [InlineData("PGDN", 0x22)]
    [InlineData("PAGEDOWN", 0x22)]
    [InlineData("DEL", 0x2E)]
    [InlineData("DELETE", 0x2E)]
    [InlineData("INS", 0x2D)]
    [InlineData("INSERT", 0x2D)]
    [InlineData("PAUSE", 0x13)]
    [InlineData("BREAK", 0x13)]
    public void Aliases_ResolveToSameVk(string alias, int expectedVk)
        => Assert.Equal((ushort)expectedVk, InputSimulatorService.ResolveVk(alias));

    [Theory]
    [InlineData("a", 0x41)]
    [InlineData(" a ", 0x41)]
    [InlineData("ctrl", 0xA2)]
    [InlineData(" F1 ", 0x70)]
    public void IsCaseInsensitive_AndTrims(string key, int expectedVk)
        => Assert.Equal((ushort)expectedVk, InputSimulatorService.ResolveVk(key));

    [Theory]
    [InlineData("Mouse1")]
    [InlineData("Mouse2")]
    [InlineData("Mouse3")]
    [InlineData("Mouse4")]
    [InlineData("Mouse5")]
    public void MouseNames_ReturnZero(string key)
        => Assert.Equal(0, InputSimulatorService.ResolveVk(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F13")]
    [InlineData("FOO")]
    public void UnknownOrEmpty_ReturnsZero(string? key)
        => Assert.Equal(0, InputSimulatorService.ResolveVk(key!));
}
