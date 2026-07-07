using BestInScript.API.Services;

namespace BestInScript.Tests;

public class ColorDistanceTests
{
    [Fact]
    public void IdenticalColors_ZeroDistance()
        => Assert.Equal(0.0, ScreenColorService.Distance(new[] { 120, 45, 200 }, new[] { 120, 45, 200 }));

    [Fact]
    public void BlackToWhite_IsRoot3Times255()
        => Assert.Equal(255 * Math.Sqrt(3), ScreenColorService.Distance(new[] { 0, 0, 0 }, new[] { 255, 255, 255 }), 6);

    [Fact]
    public void KnownTriple_345_IsFive()
        => Assert.Equal(5.0, ScreenColorService.Distance(new[] { 3, 4, 0 }, new[] { 0, 0, 0 }), 6);

    [Fact]
    public void NullOrShortArrays_ReturnMaxValue()
    {
        Assert.Equal(double.MaxValue, ScreenColorService.Distance(null!, new[] { 0, 0, 0 }));
        Assert.Equal(double.MaxValue, ScreenColorService.Distance(new[] { 0, 0, 0 }, null!));
        Assert.Equal(double.MaxValue, ScreenColorService.Distance(new[] { 0, 0 }, new[] { 0, 0, 0 }));
        Assert.Equal(double.MaxValue, ScreenColorService.Distance(new[] { 0, 0, 0 }, new[] { 0 }));
    }
}
