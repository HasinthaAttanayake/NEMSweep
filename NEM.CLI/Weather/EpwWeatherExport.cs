using NEM.Contracts;
using NEM.CLI.Infrastructure;
using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.CLI.Weather;

internal static class EpwWeatherExport
{
    public static WeatherDataDTO Create(
        EpwHeader header,
        RegionalResourceProfile weather,
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
}