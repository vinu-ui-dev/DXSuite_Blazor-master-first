using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using DXSuite.Host.Setup;

namespace DXSuite.Host.Setup.Wizard;

public partial class App : Application
{
    // Written next to the bundle's other logs so failures are diagnosable on a customer box.
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "dxsuite-wizard.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Capture anything that escapes so a startup crash isn't just an opaque 0xE0434352.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            base.OnStartup(e);
            Log($"Wizard starting. Args: {string.Join(' ', e.Args)}");

            // The MSI passes --output <json> and --installdir <INSTALLFOLDER>; otherwise defaults.
            var outputPath = SetupConfig.DefaultPath;
            string? installDir = null;
            for (var i = 0; i < e.Args.Length - 1; i++)
            {
                if (string.Equals(e.Args[i], "--output", StringComparison.OrdinalIgnoreCase))
                    outputPath = e.Args[i + 1];
                else if (string.Equals(e.Args[i], "--installdir", StringComparison.OrdinalIgnoreCase))
                    installDir = e.Args[i + 1];
            }

            // Pre-fill from a prior run if one exists (e.g. a repair/upgrade).
            SetupConfig? existing = null;
            if (File.Exists(outputPath))
            {
                try { existing = SetupConfig.Load(outputPath); } catch { /* start blank */ }
            }

            var cfg = existing ?? new SetupConfig();

            // The MSI owns the install folder; the wizard records it for the headless steps.
            if (!string.IsNullOrWhiteSpace(installDir))
                cfg.Site.InstallDir = installDir;

            var window = new MainWindow(outputPath, cfg);
            window.Show();
            Log("Main window shown.");
        }
        catch (Exception ex)
        {
            Fatal("OnStartup", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Fatal("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Fatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);
    }

    private void Fatal(string source, Exception? ex)
    {
        Log($"FATAL [{source}]: {ex}");
        try
        {
            MessageBox.Show(
                $"DXSuite setup could not start.\n\n{ex?.Message}\n\nDetails written to:\n{LogPath}",
                "DXSuite Setup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* no interactive desktop — the log is the record */ }

        Environment.ExitCode = 1603; // ERROR_INSTALL_FAILURE
        Shutdown(1603);
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* best effort */ }
    }
}
