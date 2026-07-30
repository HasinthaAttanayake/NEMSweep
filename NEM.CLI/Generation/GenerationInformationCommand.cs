using NEM.CLI.Application;
using NEM.Contracts;

namespace NEM.CLI.Generation;

internal static class GenerationInformationCommand
{
    public static int Run(CliContext context, string sourcePath)
    {
        IReadOnlyList<GenerationInformationRow> rows = GenerationInformationParser.Read(sourcePath);
        GenerationInformationDTO result = GenerationInformationExport.Create(sourcePath, rows);
        string outputPath = context.Paths.WebDataPath("generation-information.json");
        GenerationInformationExport.WriteJson(result, outputPath);
        context.Output.WriteLine($"Loaded {rows.Count} generation-information rows.");
        context.Output.WriteLine(
            $"Wrote generation information to: {Path.GetFullPath(outputPath)}");
        return 0;
    }
}