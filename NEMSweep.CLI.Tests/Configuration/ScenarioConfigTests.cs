using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEMSweep.CLI.Configuration;

namespace NEMSweep.CLI.Tests.Configuration;

public sealed class ScenarioConfigTests
{
    [Fact]
    public void Load_RequiresCurrentSchemaVersion()
    {
        var act = () => Load(config => config["schemaVersion"] = 1);

        act.Should().Throw<FormatException>()
            .WithMessage("*found 1*expected 6*");
    }

    [Fact]
    public void Load_RejectsUnknownProperty()
    {
        var act = () => Load(config => config["nameplateMw"] = 1);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Load_RejectsTopLevelDemandFile()
    {
        var act = () => Load(config => config["demandFile"] = "demand.json");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Load_RejectsMissingRegionalDemandFileWithRegionName()
    {
        var act = () => Load(config => Region(config)["demandFile"] = " ");

        act.Should().Throw<FormatException>().WithMessage("*NSW1*demandFile*blank*");
    }

    [Fact]
    public void Load_UsesIndependentInputsForEachRegion()
    {
        ScenarioSettings scenario = Load(config =>
        {
            JsonObject secondRegion = Region(config).DeepClone().AsObject();
            secondRegion["regionId"] = "VIC1";
            secondRegion["demandFile"] = "demand-vic1.json";
            secondRegion["weatherFile"] = "weather-vic1.json";
            config["regions"] = new JsonArray(Region(config).DeepClone(), secondRegion);
        });

        scenario.Regions.Select(region => (region.RegionId, region.DemandFile, region.WeatherFile))
            .Should().Equal(
                ("NSW1", "demand.json", "weather.json"),
                ("VIC1", "demand-vic1.json", "weather-vic1.json"));
    }

    [Fact]
    public void Load_DefaultsRegionalDataCentreNameplateToZero()
    {
        ScenarioSettings scenario = Load(_ => { });

        scenario.Regions.Single().DataCentreNameplateMw.Should().Be(0);
    }

    [Fact]
    public void Load_AcceptsOptionalProvenance()
    {
        ScenarioSettings scenario = Load(config => config["provenance"] = new JsonObject { ["sweepId"] = "test" });

        scenario.Provenance!["sweepId"]!.GetValue<string>().Should().Be("test");
    }

    [Fact]
    public void Load_AcceptsAnInterconnectorBetweenScenarioRegions()
    {
        ScenarioSettings scenario = Load(config =>
        {
            AddVicRegion(config);
            config["interconnectors"] = new JsonArray(Interconnector("NSW1", "VIC1"));
        });

        ScenarioInterconnectorSettings interconnector = scenario.Interconnectors!
            .Should().ContainSingle().Subject;
        interconnector.FromRegionId.Should().Be("NSW1");
        interconnector.ToRegionId.Should().Be("VIC1");
        interconnector.CapacityMw.Should().Be(1_000);
        interconnector.RouteLengthKm.Should().Be(500);
        interconnector.TechnicalLifeYears.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Load_RejectsInterconnectorWithNonPositiveRouteLength(double routeLengthKm)
    {
        var act = () => Load(config =>
        {
            AddVicRegion(config);
            JsonObject interconnector = Interconnector("NSW1", "VIC1");
            interconnector["routeLengthKm"] = routeLengthKm;
            config["interconnectors"] = new JsonArray(interconnector);
        });

        act.Should().Throw<FormatException>()
            .WithMessage("*interconnectors*routeLengthKm*positive*");
    }

    [Fact]
    public void Load_RejectsInterconnectorWhoseEndpointIsNotInScenarioRegions()
    {
        var act = () => Load(config =>
            config["interconnectors"] = new JsonArray(Interconnector("NSW1", "QLD1")));

        act.Should().Throw<FormatException>()
            .WithMessage("*interconnectors*regions*must belong*");
    }

    [Fact]
    public void Load_RejectsInterconnectorWithZeroTechnicalLife()
    {
        var act = () => Load(config =>
        {
            AddVicRegion(config);
            JsonObject interconnector = Interconnector("NSW1", "VIC1");
            interconnector["technicalLifeYears"] = 0;
            config["interconnectors"] = new JsonArray(interconnector);
        });

        act.Should().Throw<FormatException>()
            .WithMessage("*interconnectors*technicalLifeYears*nonzero*");
    }

    [Fact]
    public void Load_AllowsReciprocalInterconnectorDirections()
    {
        ScenarioSettings scenario = Load(config =>
        {
            AddVicRegion(config);
            config["interconnectors"] = new JsonArray(
                Interconnector("NSW1", "VIC1"),
                Interconnector("VIC1", "NSW1"));
        });

        scenario.Interconnectors!.Select(link => (link.FromRegionId, link.ToRegionId)).Should()
            .Equal(("NSW1", "VIC1"), ("VIC1", "NSW1"));
    }

    [Fact]
    public void Load_RejectsDuplicateInterconnectorDirectionsIgnoringCasing()
    {
        var act = () => Load(config =>
        {
            AddVicRegion(config);
            config["interconnectors"] = new JsonArray(
                Interconnector("NSW1", "VIC1"),
                Interconnector("nsw1", "vic1"));
        });

        act.Should().Throw<FormatException>()
            .WithMessage("*interconnectors*duplicate direction*");
    }

    [Fact]
    public void Load_RejectsInterconnectorWithoutRequiredCapacity()
    {
        var act = () => Load(config =>
        {
            AddVicRegion(config);
            JsonObject interconnector = Interconnector("NSW1", "VIC1");
            interconnector.Remove("capacityMw");
            config["interconnectors"] = new JsonArray(interconnector);
        });

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Load_RejectsUnknownRegion()
    {
        var act = () => Load(config => Region(config)["regionId"] = "NARNIA9");

        act.Should().Throw<FormatException>().WithMessage("*region*NARNIA9*");
    }

    [Fact]
    public void Load_RejectsDuplicateRegion()
    {
        var act = () => Load(config => config["regions"] = new JsonArray(Region(config).DeepClone(), Region(config).DeepClone()));

        act.Should().Throw<FormatException>().WithMessage("*region*distinct*");
    }

    [Fact]
    public void Load_RejectsBlankGeneratingTechnology()
    {
        var act = () => Load(config => GeneratingFleet(config)["technology"] = " ");

        act.Should().Throw<FormatException>().WithMessage("*region*technology*blank*");
    }

    [Fact]
    public void Load_RejectsNegativeNameplateCapacity()
    {
        var act = () => Load(config => GeneratingFleet(config)["nameplateCapacityMw"] = -1);

        act.Should().Throw<FormatException>().WithMessage("*NSW1*Coal*nameplateCapacityMw*");
    }

    [Fact]
    public void Load_RejectsRoundTripEfficiencyOutsideRange()
    {
        var act = () => Load(config => StorageProfile(config)["roundTripEfficiency"] = 1.1);

        act.Should().Throw<FormatException>().WithMessage("*NSW1*Battery*roundTripEfficiency*");
    }

    [Fact]
    public void Load_RejectsNegativeHeatRate()
    {
        var act = () => Load(config => GeneratingProfile(config)["heatRateGjPerMwh"] = -1);

        act.Should().Throw<FormatException>().WithMessage("*NSW1*Coal*heatRateGjPerMwh*");
    }

    [Fact]
    public void Load_RejectsCapacityFactorOutsideRange()
    {
        var act = () => Load(config => GeneratingFleet(config)["monthlyCapacityFactors"] = new JsonArray(
            new JsonObject { ["month"] = "2026-01-01", ["capacityFactor"] = 0 }));

        act.Should().Throw<FormatException>().WithMessage("*NSW1*Coal*capacityFactor*");
    }

    [Fact]
    public void Load_RejectsNegativeCost()
    {
        var act = () => Load(config => GeneratingCosts(config)["fuelPriceAudPerGj"] = -1);

        act.Should().Throw<FormatException>().WithMessage("*NSW1*Coal*fuelPriceAudPerGj*");
    }

    [Fact]
    public void Load_RejectsTargetUsePercentageOutsideRange()
    {
        var act = () => Load(config => config["storageSizing"]!["targetUsePercentage"] = 100.1);

        act.Should().Throw<FormatException>().WithMessage("*targetUsePercentage*");
    }

    [Fact]
    public void Load_RejectsNegativeEmissionsIntensity()
    {
        var act = () => Load(config =>
            GeneratingProfile(config)["emissionsIntensityTonnesPerMwh"] = -0.1);

        act.Should().Throw<FormatException>()
            .WithMessage("*NSW1*Coal*emissionsIntensityTonnesPerMwh*");
    }

    /// <summary>
    /// A real file written for the previous schema is missing what this one requires, so the
    /// version has to be checked before deserialisation or the reader is told about a property
    /// rather than about the version their whole file predates.
    /// </summary>
    [Fact]
    public void Load_ReportsTheVersionForAPreviousSchemaFileMissingANewlyRequiredField()
    {
        var act = () => Load(config =>
        {
            config["schemaVersion"] = 5;
            GeneratingProfile(config).Remove("emissionsIntensityTonnesPerMwh");
        });

        act.Should().Throw<FormatException>()
            .WithMessage("*found 5*expected 6*");
    }

    [Fact]
    public void Load_RejectsAGeneratingProfileWithoutAnEmissionsIntensity()
    {
        var act = () => Load(config =>
            GeneratingProfile(config).Remove("emissionsIntensityTonnesPerMwh"));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Load_RejectsImplausibleCostBasisYear()
    {
        var act = () => Load(config => config["costBasis"]!["year"] = 1900);

        act.Should().Throw<FormatException>().WithMessage("*costBasis.year*1900*2000*2100*");
    }

    private static ScenarioSettings Load(Action<JsonObject> mutate)
    {
        JsonObject config = JsonNode.Parse("""
        {
                    "schemaVersion": 6,
          "id": "test",
          "name": "Test",
          "costBasis": { "year": 2026, "realDiscountRate": 0.07 },
          "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 },
          "regions": [{
            "regionId": "NSW1",
                        "demandFile": "demand.json",
                        "weatherFile": "weather.json",
            "generatingFleets": [{
              "technology": "Coal", "nameplateCapacityMw": 100,
              "costParameters": { "capitalCostAudPerMw": 1, "fixedOperatingCostAudPerMwYear": 1, "variableOperatingCostAudPerMwh": 1, "fuelPriceAudPerGj": 1 },
              "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 }
            }],
            "storageFleets": [{
              "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0,
              "costParameters": { "powerCapitalCostAudPerMw": 1, "energyCapitalCostAudPerMwh": 1, "fixedOperatingCostAudPerMwYear": 1 },
              "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 }
            }]
          }]
        }
        """)!.AsObject();
        mutate(config);
        string path = Path.Combine(Path.GetTempPath(), $"scenario-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, config.ToJsonString());
            return ScenarioConfig.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AddVicRegion(JsonObject config)
    {
        JsonObject vic = Region(config).DeepClone().AsObject();
        vic["regionId"] = "VIC1";
        vic["demandFile"] = "demand-vic.json";
        vic["weatherFile"] = "weather-vic.json";
        config["regions"] = new JsonArray(Region(config).DeepClone(), vic);
    }

    private static JsonObject Interconnector(string fromRegionId, string toRegionId) => new()
    {
        ["fromRegionId"] = fromRegionId,
        ["toRegionId"] = toRegionId,
        ["capacityMw"] = 1_000,
        ["routeLengthKm"] = 500,
        ["capitalCostAudPerKmPerMw"] = 1_000,
        ["fixedOperatingCostAudPerKmPerMwYear"] = 10,
        ["technicalLifeYears"] = 50,
    };

    private static JsonObject Region(JsonObject config) => config["regions"]![0]!.AsObject();
    private static JsonObject GeneratingFleet(JsonObject config) => Region(config)["generatingFleets"]![0]!.AsObject();
    private static JsonObject GeneratingCosts(JsonObject config) => GeneratingFleet(config)["costParameters"]!.AsObject();
    private static JsonObject GeneratingProfile(JsonObject config) => GeneratingFleet(config)["technologyProfile"]!.AsObject();
    private static JsonObject StorageProfile(JsonObject config) => Region(config)["storageFleets"]![0]!["technologyProfile"]!.AsObject();
}
