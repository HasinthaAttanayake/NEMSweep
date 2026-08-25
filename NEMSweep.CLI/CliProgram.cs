using NEMSweep.CLI.Application;

namespace NEMSweep.CLI;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return new CommandRouter(
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Console.Out,
                Console.Error).Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CLI startup failed: {exception.Message}");
            return 1;
        }
    }
}
