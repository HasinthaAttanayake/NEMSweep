using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Grid;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;

namespace NEM.Model.Tests.Documentation;

/// <summary>
/// Guards the published assumptions register against the code it describes.
///
/// `docs/assumptions/index.md` states the constants a reader cannot change from a scenario, and it
/// is the page a reader is asked to trust before quoting any figure. A register that can drift from
/// the model is worse than no register, so the values are asserted here rather than maintained by
/// discipline. Changing a constant below without changing the page fails this test, and vice versa.
/// </summary>
public sealed class ModelAssumptionsTests
{
    private const string RegisterPath = "docs/assumptions/index.md";
    private const string BeginMarker = "<!-- assumption-values:begin -->";
    private const string EndMarker = "<!-- assumption-values:end -->";

    private static readonly IReadOnlyDictionary<string, double> CodeValues =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["reliability.defaultTargetUsePercentage"] = StorageSizingOptions.DefaultTargetUsePercentage,
            ["sizing.minimumPowerMw"] = StorageSizingOptions.MinimumPowerMw,
            ["sizing.minimumEnergyMwh"] = StorageSizingOptions.MinimumEnergyMwh,
            ["sizing.defaultMaximumPasses"] = StorageSizingOptions.DefaultMaximumPasses,
            ["transfer.lossFactorPerHop"] = InterRegionalTransfer.LossFactorPerHop,
            ["storage.pumpedHydroSeedFraction"] = StorageSeedPolicy.PumpedHydroSeedFraction,
            ["storage.defaultSeedFraction"] = StorageSeedPolicy.DefaultSeedFraction,
            ["hydro.reserveFraction"] = HydroReservationState.ReserveFraction,
            ["solar.groundAlbedo"] = GlobalTiltedIrradiationSeries.GroundAlbedo,
            ["solar.systemFactor"] = DualAxisSolarPowerCurve.SystemFactor,
            ["solar.standardTestIrradianceWattsPerSquareMetre"] =
                DualAxisSolarPowerCurve.StandardTestIrradiance.WattsPerSquareMetre,
            ["solar.cellTemperatureRiseAboveDryBulbCelsius"] =
                DualAxisSolarPowerCurve.CellTemperatureRiseAboveDryBulbDegreesCelsius,
            ["solar.referenceCellTemperatureCelsius"] =
                DualAxisSolarPowerCurve.ReferenceCellTemperatureDegreesCelsius,
            ["solar.temperatureCoefficientPerCelsius"] =
                DualAxisSolarPowerCurve.TemperatureCoefficientPerDegreeCelsius,
            ["wind.referenceTurbineCapacityMw"] = WindPowerCurve.ReferenceTurbineCapacity.Megawatts,
            ["wind.referenceAirDensityKilogramsPerCubicMetre"] =
                WindPowerCurve.ReferenceAirDensityKilogramsPerCubicMetre,
            ["wind.hubHeightMetres"] = WindPowerCurveSettings.Default.HubHeightMetres,
            ["wind.shearExponent"] = WindPowerCurveSettings.Default.ShearExponent,
            ["wind.cutInSpeedMetresPerSecond"] = WindPowerCurve.CutInWindSpeedMetresPerSecond,
            ["wind.ratedSpeedMetresPerSecond"] = WindPowerCurve.RatedWindSpeedMetresPerSecond,
            ["wind.cutOutSpeedMetresPerSecond"] =
                WindPowerCurveSettings.Default.CutOutWindSpeedMetresPerSecond,
        };

    [Fact]
    public void PublishedRegister_StatesEveryModelConstantThisTestKnowsAbout()
    {
        IReadOnlyDictionary<string, double> published = ReadPublishedValues();

        published.Keys.Should().BeEquivalentTo(
            CodeValues.Keys,
            "the assumptions register and this test must cover exactly the same constants");
    }

    [Fact]
    public void PublishedRegister_MatchesTheConstantsInTheModel()
    {
        IReadOnlyDictionary<string, double> published = ReadPublishedValues();

        foreach ((string key, double codeValue) in CodeValues)
        {
            published.Should().ContainKey(key);
            published[key].Should().BeApproximately(
                codeValue,
                1e-12,
                $"{RegisterPath} publishes '{key}' as {published[key]} but the model uses {codeValue}");
        }
    }

    private static IReadOnlyDictionary<string, double> ReadPublishedValues()
    {
        string markdown = File.ReadAllText(RegisterFullPath());
        int begin = markdown.IndexOf(BeginMarker, StringComparison.Ordinal);
        int end = markdown.IndexOf(EndMarker, StringComparison.Ordinal);
        begin.Should().BeGreaterThanOrEqualTo(0, $"{RegisterPath} must contain {BeginMarker}");
        end.Should().BeGreaterThan(begin, $"{RegisterPath} must contain {EndMarker} after the begin marker");

        string block = markdown[(begin + BeginMarker.Length)..end];
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
            block,
            @"^\|\s*`(?<key>[^`]+)`\s*\|\s*(?<value>[-+0-9.eE]+)\s*\|\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            values.Add(
                match.Groups["key"].Value,
                double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture));
        }

        values.Should().NotBeEmpty($"{RegisterPath} must publish a table of keyed assumption values");
        return values;
    }

    private static string RegisterFullPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, RegisterPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {RegisterPath} from {AppContext.BaseDirectory}.");
    }
}
