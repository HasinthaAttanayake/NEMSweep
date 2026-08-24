using NEMSweep.CLI.Application;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            RepositoryPaths paths = RepositoryPaths.Discover(AppContext.BaseDirectory);
            return new CommandRouter(
                paths,
                AppContext.BaseDirectory,
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