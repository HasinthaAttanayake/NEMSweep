using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Services;

namespace NEMSweep.Web.Tests.Services;

public sealed class RegionArtifactResolverTests
{
    [Fact]
    public void TryResolveWeatherPaths_ResolvesOneWeatherPathPerRegionInOrdinalOrder()
    {
        var dataSourcesByRegion = new Dictionary<string, DispatchSourcesDTO>
        {
            ["VIC1"] = Sources("weather-vic1.json"),
            ["NSW1"] = Sources("weather-nsw1.json"),
        };

        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1", "VIC1"],
            dataSourcesByRegion,
            out IReadOnlyList<string> regionIds,
            out IReadOnlyDictionary<string, string> weatherPathsByRegion,
            out string? validationMessage);

        resolved.Should().BeTrue();
        validationMessage.Should().BeNull();
        regionIds.Should().Equal("NSW1", "VIC1");
        weatherPathsByRegion["NSW1"].Should().Be("data/weather-nsw1.json");
        weatherPathsByRegion["VIC1"].Should().Be("data/weather-vic1.json");
    }

    [Fact]
    public void TryResolveWeatherPaths_NormalisesAFileNameAlreadyPrefixedWithData()
    {
        var dataSourcesByRegion = new Dictionary<string, DispatchSourcesDTO>
        {
            ["NSW1"] = Sources("data/weather-nsw1.json"),
        };

        RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1"],
            dataSourcesByRegion,
            out _,
            out IReadOnlyDictionary<string, string> weatherPathsByRegion,
            out _);

        weatherPathsByRegion["NSW1"].Should().Be("data/weather-nsw1.json");
    }

    [Fact]
    public void TryResolveWeatherPaths_RejectsEmptyRegionList()
    {
        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            [],
            new Dictionary<string, DispatchSourcesDTO>(),
            out _,
            out _,
            out string? validationMessage);

        resolved.Should().BeFalse();
        validationMessage.Should().Contain("do not define any regions");
    }

    [Fact]
    public void TryResolveWeatherPaths_RejectsNullDataSources()
    {
        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1"],
            null,
            out _,
            out _,
            out string? validationMessage);

        resolved.Should().BeFalse();
        validationMessage.Should().Contain("regional data sources");
    }

    [Fact]
    public void TryResolveWeatherPaths_SkipsARegionMissingFromDataSourcesAndResolvesTheRest()
    {
        var dataSourcesByRegion = new Dictionary<string, DispatchSourcesDTO>
        {
            ["NSW1"] = Sources("weather-nsw1.json"),
        };

        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1", "QLD1"],
            dataSourcesByRegion,
            out IReadOnlyList<string> regionIds,
            out IReadOnlyDictionary<string, string> weatherPathsByRegion,
            out string? validationMessage);

        resolved.Should().BeTrue();
        validationMessage.Should().BeNull();
        regionIds.Should().Equal("NSW1");
        weatherPathsByRegion.Should().ContainKey("NSW1");
        weatherPathsByRegion.Should().NotContainKey("QLD1");
    }

    [Fact]
    public void TryResolveWeatherPaths_RejectsWhenNoRegionResolves()
    {
        var dataSourcesByRegion = new Dictionary<string, DispatchSourcesDTO>();

        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1", "QLD1"],
            dataSourcesByRegion,
            out _,
            out _,
            out string? validationMessage);

        resolved.Should().BeFalse();
        validationMessage.Should().Contain("None of the system's regions");
    }

    [Fact]
    public void TryResolveWeatherPaths_RejectsACaseVariantDuplicateRegion()
    {
        var dataSourcesByRegion = new Dictionary<string, DispatchSourcesDTO>
        {
            ["NSW1"] = Sources("weather-nsw1.json"),
            ["nsw1"] = Sources("weather-nsw1.json"),
        };

        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1", "nsw1"],
            dataSourcesByRegion,
            out _,
            out _,
            out string? validationMessage);

        resolved.Should().BeFalse();
        validationMessage.Should().Contain("more than once");
    }

    [Fact]
    public void TryResolveWeatherPaths_RejectsAPathTraversalFileName()
    {
        var dataSourcesByRegion = new Dictionary<string, DispatchSourcesDTO>
        {
            ["NSW1"] = Sources("../secrets.json"),
        };

        bool resolved = RegionArtifactResolver.TryResolveWeatherPaths(
            ["NSW1"],
            dataSourcesByRegion,
            out _,
            out _,
            out string? validationMessage);

        resolved.Should().BeFalse();
        validationMessage.Should().NotBeNullOrWhiteSpace();
    }

    private static DispatchSourcesDTO Sources(string weatherFileName) => new(
        new DispatchInputArtifactDTO("demand-nsw1.json", ArtifactSchemaVersions.OperationalDemand, "demand-hash"),
        new DispatchInputArtifactDTO(weatherFileName, ArtifactSchemaVersions.Weather, "weather-hash"),
        new WeatherBasisDTO(
            WeatherBasisKind.TypicalMeteorologicalYear,
            new WeatherSiteDTO("solar.epw", "Solar site"),
            new WeatherSiteDTO("wind.epw", "Wind site"),
            "Typical meteorological year."),
        []);
}
