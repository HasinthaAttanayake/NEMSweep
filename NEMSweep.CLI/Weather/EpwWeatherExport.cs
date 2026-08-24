using NEMSweep.Contracts;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Model.Generation.Solar;
using NEMSweep.Model.Generation.Wind;
using NEMSweep.Model.Series;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.CLI.Weather;

internal static class EpwWeatherExport
{
    public static WeatherDataDTO Create(
        string regionId,
        EpwHeader solarHeader,
        RegionalResourceProfile solarWeather,
        string solarSourceFile,
        EpwHeader windHeader,
        RegionalResourceProfile windWeather,
        string windSourceFile)
    {
        ValidateInputs(regionId, solarSourceFile, solarWeather, windSourceFile, windWeather);
        double windMeasurementHeightMetres = windWeather.WindSpeed.MeasurementHeightMetres
            ?? throw new InvalidOperationException(
                "Weather export requires a wind-speed trace with a measurement height.");
        FlowSeries solarProductionAtOneMegawattAc = DualAxisSolarPowerCurve.Calculate(
            solarWeather.GlobalHorizontalRadiation,
            solarWeather.DirectNormalRadiation,
            solarWeather.DiffuseHorizontalRadiation,
            solarWeather.DryBulbTemperature,
            solarWeather.SolarZenith,
            Power.FromMegawatts(1));
        FlowSeries windProductionAtOneMegawattInstalled = WindPowerCurve.Calculate(
            windWeather.WindSpeed,
            Power.FromMegawatts(1));

        return new WeatherDataDTO(
            ArtifactSchemaVersions.Weather,
            regionId,
            solarWeather.DirectNormalRadiation.Start,
            solarWeather.DirectNormalRadiation.Resolution,
            new SolarWeatherData(
                solarSourceFile,
                LocationOf(solarHeader),
                ValuesOf(solarWeather.GlobalHorizontalRadiation),
                ValuesOf(solarWeather.DirectNormalRadiation),
                ValuesOf(solarWeather.DiffuseHorizontalRadiation),
                ZenithValuesOf(solarWeather.SolarZenith),
                ValuesOf(solarWeather.DryBulbTemperature),
                MegawattValuesOf(solarProductionAtOneMegawattAc)),
            new WindWeatherData(
                windSourceFile,
                LocationOf(windHeader),
                ValuesOf(windWeather.WindSpeed),
                windMeasurementHeightMetres,
                MegawattValuesOf(windProductionAtOneMegawattInstalled)));
    }

    public static void WriteJson(WeatherDataDTO weatherData, string path)
        => JsonFile.Write(weatherData, path);

    private static double[] ValuesOf(TraceSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index];
        }

        return values;
    }

    private static double[] ZenithValuesOf(SolarZenithSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index].Degrees;
        }

        return values;
    }

    private static double[] MegawattValuesOf(FlowSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index].Megawatts;
        }

        return values;
    }

    private static WeatherLocation LocationOf(EpwHeader header) =>
        new(header.City, header.Wmo, header.Latitude, header.Longitude);

    private static void ValidateInputs(
        string regionId,
        string solarSourceFile,
        RegionalResourceProfile solarWeather,
        string windSourceFile,
        RegionalResourceProfile windWeather)
    {
        int solarLength = solarWeather.DirectNormalRadiation.Length;
        int windLength = windWeather.WindSpeed.Length;
        bool validLength = solarLength == windLength;
        bool validShape = solarLength is 8760 or 8784 && windLength is 8760 or 8784;
        bool aligned = solarWeather.DirectNormalRadiation.Start == windWeather.WindSpeed.Start
            && solarWeather.DirectNormalRadiation.Resolution == windWeather.WindSpeed.Resolution;
        if (!validLength || !validShape || !aligned)
        {
            throw new FormatException(
                $"Weather inputs for region '{regionId}' are incompatible: "
                + $"solar source '{solarSourceFile}' and wind source '{windSourceFile}' "
                + "must have matching start, resolution, and 8760 or 8784 intervals.");
        }
    }
}