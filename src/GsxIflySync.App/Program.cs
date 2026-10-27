using GsxIflySync.Core;

namespace GsxIflySync.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var baseDir = AppContext.BaseDirectory;
        using var log = new FileLogger(Path.Combine(baseDir, "logs"));
        log.Info($"démarrage GSX iFly Sync {typeof(Program).Assembly.GetName().Version} — {Environment.OSVersion}, .NET {Environment.Version}");

        SyncConfig config;
        var configPath = Path.Combine(baseDir, "config.json");
        try
        {
            config = SyncConfig.LoadOrCreate(configPath);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Error($"config.json illisible ({ex.Message}) : valeurs sûres par défaut, lecture seule");
            config = new SyncConfig();
        }

        log.Info($"configuration : {configPath}\n{config.ToJson()}");

        // Une erreur d'interface ne doit jamais fermer l'outil en silence : elle est journalisée.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Error($"exception non gérée : {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error($"exception fatale : {e.ExceptionObject}");

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(config, log));
        log.Info("arrêt normal");
    }
}
