using NetInspector.Desktop;
namespace NetInspector.Collector;
internal static class Program
{
    [STAThread] private static void Main(string[] args) { if (args is ["--maintenance-prefetch"])
        {
            try { var result = NetInspector.Core.Maintenance.CleanPrefetch(CancellationToken.None); Environment.Exit(result.Skipped == 0 ? 0 : 2); }
            catch { Environment.Exit(1); }
            return;
        }
        ApplicationConfiguration.Initialize(); Theme.Initialize(); Application.Run(new InspectorForm(args.FirstOrDefault())); }
}
