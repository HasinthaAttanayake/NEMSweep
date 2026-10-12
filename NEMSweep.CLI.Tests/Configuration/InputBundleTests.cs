using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.CLI.Configuration;

namespace NEMSweep.CLI.Tests.Configuration;

public sealed class InputBundleTests
{
    [Fact]
    public void Load_OneRootEpwFile_UsesItForBothRoles()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "weather.epw");

        InputBundle bundle = fixture.Load();

        bundle.WeatherByRegion["NSW1"].SolarEpwPath.Should().Be(bundle.WeatherByRegion["NSW1"].WindEpwPath);
    }

    [Fact]
    public void Load_SolarAndWindFolders_AssignsRolesByFolder()
    {
        using var fixture = new BundleFixture();
        fixture.AddRoleWeather("NSW1", "solar.epw", "wind.epw");

        InputBundle bundle = fixture.Load();

        bundle.WeatherByRegion["NSW1"].SolarEpwPath.Should().EndWith(Path.Combine("solar", "solar.epw"));
        bundle.WeatherByRegion["NSW1"].WindEpwPath.Should().EndWith(Path.Combine("wind", "wind.epw"));
    }

    [Fact]
    public void Load_TwoRootEpwFiles_ThrowsFormatException()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "one.epw", "two.epw");

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*weather/NSW1*");
    }

    [Fact]
    public void Load_RootFileAndRoleFolders_ThrowsFormatException()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "root.epw");
        fixture.AddRoleWeather("NSW1", "solar.epw", "wind.epw");

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*weather/NSW1*");
    }

    [Fact]
    public void Load_EmptyRoleFolder_ThrowsFormatExceptionWithRelativePath()
    {
        using var fixture = new BundleFixture();
        fixture.AddRoleWeather("NSW1", "solar.epw", "wind.epw");
        File.Delete(Path.Combine(fixture.RootPath, "weather", "NSW1", "solar", "solar.epw"));

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*weather/NSW1/solar*");
    }

    [Fact]
    public void Load_MissingDeclaredWeatherFolder_ThrowsFormatExceptionWithRelativePath()
    {
        using var fixture = new BundleFixture();
        fixture.ManifestRegions = ["NSW1", "QLD1"];
        fixture.AddRootWeather("NSW1", "weather.epw");

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*weather/QLD1*");
    }

    [Fact]
    public void Load_UndeclaredWeatherFolder_IsIgnored()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "weather.epw");
        fixture.AddRootWeather("QLD1", "ignored.epw");

        InputBundle bundle = fixture.Load();

        bundle.WeatherByRegion.Keys.Should().ContainSingle().Which.Should().Be("NSW1");
    }

    [Fact]
    public void Load_ReferenceZip_IsExcluded()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "weather.epw");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "demand", "operational-demand-hh", "reference"));
        File.WriteAllBytes(Path.Combine(fixture.RootPath, "demand", "operational-demand-hh", "reference", "ignored.zip"), []);

        InputBundle bundle = fixture.Load();

        bundle.DemandArchivePaths.Should().ContainSingle().Which.Should().EndWith("demand.zip");
    }

    [Fact]
    public void Load_UnknownRegion_ThrowsFormatExceptionNamingManifest()
    {
        using var fixture = new BundleFixture { ManifestRegions = ["NARNIA9"] };

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*manifest.json*");
    }

    [Fact]
    public void Load_RootFolderNameMismatch_ReturnsWarning()
    {
        using var fixture = new BundleFixture { RootFolderName = "different-name" };
        fixture.AddRootWeather("NSW1", "weather.epw");

        InputBundle bundle = fixture.Load();

        bundle.Warnings.Should().ContainSingle().Which.Should().Contain("bundleId");
    }

    [Fact]
    public void Load_UnsupportedSchema_ThrowsFormatExceptionNamingManifest()
    {
        using var fixture = new BundleFixture { SchemaVersion = 3 };

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*manifest.json*");
    }

    [Theory]
    [InlineData("bundleId", "   ")]
    [InlineData("name", "")]
    [InlineData("period", "null")]
    [InlineData("regions", "[]")]
    public void Load_MissingOrBlankManifestField_ThrowsFormatExceptionNamingManifest(string field, string value)
    {
        using var fixture = new BundleFixture();
        fixture.ManifestOverrides[field] = value;

        var act = () => fixture.Load();
        act.Should().Throw<FormatException>().WithMessage("*manifest.json*");
    }

    [Fact]
    public void Load_PeriodBoundsOnDifferentOffsets_ThrowsFormatExceptionNamingManifest()
    {
        using var fixture = new BundleFixture();
        fixture.ManifestOverrides["period"] =
            """{"start":"2026-01-01T00:00:00+10:00","end":"2026-01-01T22:00:00+08:00"}""";

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>()
            .WithMessage("*manifest.json*same market-time offset*");
    }

    [Fact]
    public void Load_PeriodOffsetOutsideTheUsableRange_ThrowsFormatExceptionNamingManifest()
    {
        using var fixture = new BundleFixture();
        fixture.ManifestOverrides["period"] =
            """{"start":"2026-01-01T00:00:00+08:37","end":"2026-01-02T00:00:00+08:37"}""";

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>()
            .WithMessage("*manifest.json*not a usable market-time offset*");
    }

    [Fact]
    public void Load_PeriodOnANonNemOffset_IsAccepted()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "weather.epw");
        fixture.ManifestOverrides["period"] =
            """{"start":"2026-01-01T00:00:00+08:00","end":"2026-01-02T00:00:00+08:00"}""";

        InputBundle bundle = fixture.Load();

        bundle.Manifest.Period.Start.Offset.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void Load_V2Manifest_PreservesFreeFormRegionIdAndUsesWeatherDirectoryWithoutGeneration()
    {
        using var fixture = new BundleFixture
        {
            SchemaVersion = 2,
            ManifestRegions = ["Northern Grid / Zone"],
        };
        fixture.ManifestWeatherDirectories["Northern Grid / Zone"] = "north-zone";
        fixture.AddRootWeather("Northern Grid / Zone", "weather.epw");
        Directory.Delete(Path.Combine(fixture.RootPath, "generation"), recursive: true);

        InputBundle bundle = fixture.Load();

        bundle.RegionIds.Should().ContainSingle().Which.Should().Be("Northern Grid / Zone");
        bundle.WeatherByRegion.Keys.Should().ContainSingle().Which.Should().Be("Northern Grid / Zone");
        bundle.WeatherByRegion["Northern Grid / Zone"].SolarEpwPath
            .Should().Contain(Path.Combine("weather", "north-zone"));
        bundle.GenerationInformationPath.Should().BeNull();
        bundle.Manifest.Regions[0].WeatherDirectory.Should().Be("north-zone");
    }

    [Fact]
    public void Load_V2Manifest_AcceptsRootSchemaHintButRejectsUnknownFields()
    {
        using var fixture = new BundleFixture { SchemaVersion = 2 };
        fixture.AddRootWeather("NSW1", "weather.epw");
        fixture.ManifestOverrides["$schema"] = "https://example.test/input-bundle.schema.json";
        fixture.ManifestOverrides["futureField"] = "unexpected";

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>()
            .WithMessage("*schema version 2*manifest.json*check the $schema hint*");
    }

    [Fact]
    public void Load_V1Manifest_RemainsPermissiveForUnknownFields()
    {
        using var fixture = new BundleFixture();
        fixture.AddRootWeather("NSW1", "weather.epw");
        fixture.ManifestOverrides["futureField"] = "ignored";

        InputBundle bundle = fixture.Load();

        bundle.RegionIds.Should().ContainSingle().Which.Should().Be("NSW1");
        bundle.GenerationInformationPath.Should().NotBeNull();
    }

    [Theory]
    [InlineData("..", "safe directory name")]
    [InlineData("../outside", "safe directory name")]
    [InlineData("folder/name", "safe directory name")]
    [InlineData("CON", "reserved Windows name")]
    [InlineData("LPT1.txt", "reserved Windows name")]
    [InlineData("station*", "safe directory name")]
    [InlineData("station?", "safe directory name")]
    [InlineData("station\"", "safe directory name")]
    [InlineData("station<", "safe directory name")]
    [InlineData("station>", "safe directory name")]
    [InlineData("station|", "safe directory name")]
    public void Load_V2Manifest_RejectsUnsafeWeatherDirectory(string directory, string errorDetail)
    {
        using var fixture = new BundleFixture { SchemaVersion = 2 };
        fixture.ManifestWeatherDirectories["NSW1"] = directory;

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>().WithMessage($"*weatherDirectory*{errorDetail}*");
    }

    [Fact]
    public void Load_V2Manifest_RejectsCaseInsensitiveWeatherDirectoryCollisions()
    {
        using var fixture = new BundleFixture
        {
            SchemaVersion = 2,
            ManifestRegions = ["north", "south"],
        };
        fixture.ManifestWeatherDirectories["north"] = "Weather";
        fixture.ManifestWeatherDirectories["south"] = "weather";

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>().WithMessage("*colliding weatherDirectory*");
    }

    [Fact]
    public void Load_V2Manifest_RejectsCaseInsensitiveRegionIdCollisions()
    {
        using var fixture = new BundleFixture
        {
            SchemaVersion = 2,
            ManifestRegions = ["north", "NORTH"],
        };

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>().WithMessage("*duplicate region*");
    }

    [Theory]
    [InlineData(" north")]
    [InlineData("north ")]
    [InlineData("\tnorth")]
    [InlineData("north\n")]
    public void Load_V2Manifest_RejectsSurroundingRegionIdWhitespace(string regionId)
    {
        using var fixture = new BundleFixture
        {
            SchemaVersion = 2,
            ManifestRegions = [regionId],
        };

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>().WithMessage("*surrounding whitespace*trims region IDs*");
    }

    [Theory]
    [InlineData("x", 101)]
    [InlineData("\u03b1", 51)]
    public void Load_V2Manifest_RejectsUnpublishableRegionBeforeDiscoveringSources(string character, int count)
    {
        using var fixture = new BundleFixture
        {
            SchemaVersion = 2,
            ManifestRegions = [string.Concat(Enumerable.Repeat(character, count))],
        };

        var act = () => fixture.Load();

        act.Should().Throw<FormatException>()
            .WithMessage("*manifest.json*cannot be published safely*100 UTF-8 bytes*");
    }

    private sealed class BundleFixture : IDisposable
    {
        public BundleFixture()
        {
            RootFolderName = $"bundle-{Guid.NewGuid():N}";
            RootPath = Path.Combine(Path.GetTempPath(), RootFolderName);
            Directory.CreateDirectory(Path.Combine(RootPath, "demand", "operational-demand-hh"));
            Directory.CreateDirectory(Path.Combine(RootPath, "generation", "generation-information"));
            File.WriteAllBytes(Path.Combine(RootPath, "demand", "operational-demand-hh", "demand.zip"), []);
            File.WriteAllBytes(Path.Combine(RootPath, "generation", "generation-information", "generation.xlsx"), []);
        }

        public string RootPath { get; }
        public string RootFolderName { get; init; }
        public int SchemaVersion { get; init; } = 1;
        public string[] ManifestRegions { get; set; } = ["NSW1"];
        public Dictionary<string, string> ManifestWeatherDirectories { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ManifestOverrides { get; } = new(StringComparer.Ordinal);

        public void AddRootWeather(string region, params string[] fileNames)
        {
            string path = Path.Combine(RootPath, "weather", WeatherDirectoryFor(region));
            Directory.CreateDirectory(path);
            foreach (string fileName in fileNames)
            {
                File.WriteAllBytes(Path.Combine(path, fileName), []);
            }
        }

        public void AddRoleWeather(string region, string solarFileName, string windFileName)
        {
            string regionPath = Path.Combine(RootPath, "weather", WeatherDirectoryFor(region));
            Directory.CreateDirectory(Path.Combine(regionPath, "solar"));
            Directory.CreateDirectory(Path.Combine(regionPath, "wind"));
            File.WriteAllBytes(Path.Combine(regionPath, "solar", solarFileName), []);
            File.WriteAllBytes(Path.Combine(regionPath, "wind", windFileName), []);
        }

        public InputBundle Load()
        {
            var manifest = new Dictionary<string, object?>
            {
                ["schemaVersion"] = SchemaVersion,
                ["bundleId"] = RootFolderName,
                ["name"] = "Test bundle",
                ["period"] = new { start = "2026-01-01T00:00:00+10:00", end = "2026-01-02T00:00:00+10:00" },
                ["regions"] = SchemaVersion == 1
                    ? ManifestRegions
                    : ManifestRegions.Select(regionId => new
                    {
                        regionId,
                        weatherDirectory = WeatherDirectoryFor(regionId),
                    }).ToArray(),
            };
            foreach ((string key, string value) in ManifestOverrides)
            {
                manifest[key] = value == "null" || value == "[]" || value.StartsWith('{')
                    ? JsonSerializer.Deserialize<JsonElement>(value)
                    : value;
            }

            File.WriteAllText(Path.Combine(RootPath, "manifest.json"), JsonSerializer.Serialize(manifest));
            return InputBundle.Load(RootPath);
        }

        private string WeatherDirectoryFor(string regionId) =>
            ManifestWeatherDirectories.TryGetValue(regionId, out string? directory)
                ? directory
                : regionId;

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}