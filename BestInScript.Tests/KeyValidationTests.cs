using BestInScript.API.Services;

namespace BestInScript.Tests;

public class KeyValidationTests
{
    [Theory]
    [InlineData("Mouse1")]
    [InlineData("Mouse2")]
    [InlineData("Mouse3")]
    [InlineData("Mouse4")]
    [InlineData("Mouse5")]
    [InlineData("mouse1")]
    public void IsValidKey_AcceptsMouseButtons(string key)
        => Assert.True(InputSimulatorService.IsValidKey(key));

    [Theory]
    [InlineData("Mouse1")]
    [InlineData("Mouse2")]
    [InlineData("Mouse3")]
    [InlineData("Mouse4")]
    [InlineData("Mouse5")]
    public void IsValidTriggerKey_RejectsAllMouseButtons(string key)
        => Assert.False(InputSimulatorService.IsValidTriggerKey(key));

    [Theory]
    [InlineData("A")]
    [InlineData("F5")]
    [InlineData("NUMPAD3")]
    [InlineData("CTRL")]
    [InlineData("SPACE")]
    public void IsValidTriggerKey_AcceptsKeyboardKeys(string key)
        => Assert.True(InputSimulatorService.IsValidTriggerKey(key));

    [Theory]
    [InlineData("")]
    [InlineData("F13")]
    [InlineData("FOO")]
    public void InvalidNames_FailBothChecks(string key)
    {
        Assert.False(InputSimulatorService.IsValidKey(key));
        Assert.False(InputSimulatorService.IsValidTriggerKey(key));
    }
}
