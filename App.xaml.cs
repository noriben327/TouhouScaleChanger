using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TouhouScaleChanger.Diagnostics;

namespace TouhouScaleChanger;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private int _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstanceMutex = new Mutex(true, "Local\\TouhouScaleChanger.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("TouhouScaleChangerはすでに起動しています。", "TouhouScaleChanger");
            Shutdown();
            return;
        }

        AppLog.Info($"起動しました (PID {Environment.ProcessId})");
        base.OnStartup(e);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A UI-thread exception (e.g. surfacing from a WinEvent callback) would otherwise
        // tear down the window while leaving the process alive as a tray zombie. Log it,
        // mark it handled, and terminate cleanly so the single-instance mutex is released.
        AppLog.Error("UIスレッドで未処理例外が発生しました。", e.Exception);
        e.Handled = true;
        FatalShutdown();
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("未処理例外が発生しました。", e.ExceptionObject as Exception);
        FatalShutdown();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("観測されていないタスク例外が発生しました。", e.Exception);
        e.SetObserved();
    }

    private void FatalShutdown()
    {
        // Guard against re-entrancy: multiple handlers may fire for a single failure.
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        try { _mainWindow?.ForceCleanup(); }
        catch (Exception exception) { AppLog.Error("終了処理中に例外が発生しました。", exception); }
        finally
        {
            ReleaseMutex();
            AppLog.Info("致命的エラーによりプロセスを終了します。");
            // Environment.Exit guarantees the process (and its background threads /
            // NotifyIcon / timeBeginPeriod state) is fully torn down, leaving no zombie.
            Environment.Exit(1);
        }
    }

    private void ReleaseMutex()
    {
        try
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
        catch (Exception exception) { AppLog.Error("mutex解放中に例外が発生しました。", exception); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseMutex();
        base.OnExit(e);
    }
}
