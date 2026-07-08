using BestInScript.API.Persistence;
using Microsoft.Extensions.Configuration;

namespace BestInScript.Tests;

public class DataFilePathResolverTests
{
    private const string Key = "BestInScript:DataFilePath";

    private static IConfiguration Config(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void AbsoluteSpecificPath_WinsVerbatim()
    {
        var config = Config((Key, @"C:\elsewhere\my.json"), ("BestInScript:DataDirectory", @"C:\data"));
        Assert.Equal(@"C:\elsewhere\my.json", DataFilePathResolver.Resolve(config, Key, "scripts.json"));
    }

    [Fact]
    public void RelativeSpecific_CombinesWithDataDirectory()
    {
        var config = Config((Key, "custom.json"), ("BestInScript:DataDirectory", @"C:\data"));
        Assert.Equal(@"C:\data\custom.json", DataFilePathResolver.Resolve(config, Key, "scripts.json"));
    }

    [Fact]
    public void NoSpecific_UsesDefaultFileNameInDataDirectory()
    {
        var config = Config(("BestInScript:DataDirectory", @"C:\data"));
        Assert.Equal(@"C:\data\scripts.json", DataFilePathResolver.Resolve(config, Key, "scripts.json"));
    }

    [Fact]
    public void NoDirectory_FallsBackToBaseDirectory()
    {
        var config = Config();
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scripts.json"));
        Assert.Equal(expected, DataFilePathResolver.Resolve(config, Key, "scripts.json"));
    }
}
