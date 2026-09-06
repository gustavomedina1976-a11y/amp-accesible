namespace GDMAmpAccessible;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string? startupNamPath = null;
        bool importOnly = false;

        if (args.Length > 0)
        {
            if (string.Equals(args[0], "--import-nam", StringComparison.OrdinalIgnoreCase))
            {
                importOnly = true;
                if (args.Length > 1) startupNamPath = args[1];
            }
            else
            {
                startupNamPath = args[0];
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startupNamPath, importOnly));
    }
}
