using NEM.Contracts;
using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;
using System.Text.Json;

namespace NEM.CLI;

internal static class EpwWeatherExport
{
    public static WeatherDataDTO Create(
        EpwHeader header,
        EpwWeatherSeries weather,
        string sourceFile)
    {
        double windMeasurementHeightMetres = weather.WindSpeed.MeasurementHeightMetres
            ?? throw new InvalidOperationException(
                "Weather export requires a wind-speed trace with a measurement height.");
        FlowSeries solarProductionAtOneMegawattAc = DualAxisSolarPowerCurve.Calculate(
            weather.GlobalHorizontalRadiation,
            weather.DirectNormalRadiation,
            weather.DiffuseHorizontalRadiation,
            weather.DryBulbTemperature,
            weather.SolarZenith,
            Power.FromMegawatts(1));
        FlowSeries windProductionAtOneMegawattInstalled = WindPowerCurve.Calculate(
            weather.WindSpeed,
            Power.FromMegawatts(1));

        return new WeatherDataDTO(
            5,
            sourceFile,
            new WeatherLocation(
                header.City,
                header.Wmo,
                header.Latitude,
                header.Longitude),
            weather.DirectNormalRadiation.Start,
            weather.DirectNormalRadiation.Resolution,
            windMeasurementHeightMetres,
            new WeatherSeriesData(
                ValuesOf(weather.GlobalHorizontalRadiation),
                ValuesOf(weather.DirectNormalRadiation),
                ValuesOf(weather.DiffuseHorizontalRadiation),
                ZenithValuesOf(weather.SolarZenith),
                ValuesOf(weather.DryBulbTemperature),
                ValuesOf(weather.WindSpeed),
                MegawattValuesOf(solarProductionAtOneMegawattAc),
                MegawattValuesOf(windProductionAtOneMegawattInstalled)));
    }

    public static void WriteJson(WeatherDataDTO weatherData, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(weatherData, options));
    }

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
}