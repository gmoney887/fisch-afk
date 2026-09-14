using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FischMacroCS.Native;
using FischMacroCS.Core;

namespace FischMacroCS;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            args.Handled = true; // Prevent abrupt crash if possible
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] App.OnStartup start\n"); } catch { }

        try
        {
            Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { }

        if (e.Args.Length > 0 && e.Args[0].Equals("--record-cast", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var settings = Settings.Load();
                using var engine = new Core.FishingEngine(settings);
                int ms = engine.ExecuteAutoTuneCast(recordReplication: true);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "record_cast_result.txt"), $"CalibratedMs={ms}\nDir={engine.LastCastReplicationDir}\n");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "record_cast_result.txt"), $"Error={ex}\n");
            }
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] App.OnStartup base.OnStartup completed\n"); } catch { }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            string crashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}\n\n";
            File.AppendAllText(crashPath, msg);
        }
        catch { }
    }
}
