using MostreamTV.Host.UI;

namespace MostreamTV.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => string.Equals(a, "--wmp-player", StringComparison.OrdinalIgnoreCase)))
        {
            var initialFile = ParseFlagValue(args, "--file");
            Application.Run(new WmpPlayerForm(initialFile));
            return;
        }

        Application.Run(new MainForm());
    }

    private static string? ParseFlagValue(string[] args, string flagName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flagName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
