using System.Diagnostics;
using TouhouScalePad.Interop;
using TouhouScalePad.Models;
using TouhouScalePad.Settings;

namespace TouhouScalePad.Services;

public sealed class RuntimeCoordinator : IDisposable
{
    private readonly AppSettings _settings;
    private readonly WindowEventWatcher _watcher = new();
    private readonly NativeWindowService _windows = new();
    private readonly InputMappingService _mapping;
    private readonly Dictionary<int, ActiveSession> _sessions = [];
    private readonly object _sync = new();
    private GameProfile[] _profileSnapshot = [];
    private Dictionary<Guid, SizePreset> _presetSnapshot = [];
    private bool _monitoring;
    private bool _disposed;

    public RuntimeCoordinator(AppSettings settings)
    {
        _settings = settings;
        _mapping = new InputMappingService(new XInputControllerSource(), new SendInputKeyboardOutput(),
            IsInputAllowed, settings.PollingIntervalMilliseconds);
        _mapping.StatusChanged += OnMappingStatusChanged;
        _watcher.WindowEvent += OnWindowEvent;
    }

    public event EventHandler<RuntimeStatusChangedEventArgs>? StatusChanged;

    public bool IsMonitoring => _monitoring;
    public bool IsMappingRunning => _mapping.IsRunning;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_monitoring) return;
        CaptureConfiguration();
        _monitoring = true;
        _watcher.Start();
        ScanAlreadyRunningGames();
        RaiseStatus("ゲームの起動を待っています");
    }

    public void Stop()
    {
        if (!_monitoring) return;
        _monitoring = false;
        _watcher.Stop();
        _mapping.Stop();

        List<ActiveSession> sessions;
        lock (_sync)
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }
        foreach (var session in sessions) session.Process.Dispose();
        RaiseStatus("監視を一時停止しました");
    }

    public void ReloadProfiles()
    {
        if (!_monitoring) return;
        Stop();
        Start();
    }

    public void SetPollingInterval(int milliseconds)
    {
        _mapping.PollingIntervalMilliseconds = milliseconds;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _watcher.WindowEvent -= OnWindowEvent;
        _mapping.StatusChanged -= OnMappingStatusChanged;
        _watcher.Dispose();
        _mapping.Dispose();
        _disposed = true;
    }

    private void ScanAlreadyRunningGames()
    {
        foreach (var profile in EnabledProfiles())
        {
            if (string.IsNullOrWhiteSpace(profile.ProcessName)) continue;
            Process[] processes;
            try { processes = Process.GetProcessesByName(profile.ProcessName); }
            catch (InvalidOperationException) { continue; }

            foreach (var process in processes)
            {
                if (process.Id == Environment.ProcessId)
                {
                    process.Dispose();
                    continue;
                }
                AttachProcess(process, profile);
                if (_windows.TryFindMainWindow(process.Id, out var window)) ApplyProfile(process.Id, window);
            }
        }
        UpdateMappingState();
    }

    private void OnWindowEvent(object? sender, WindowEventArgs e)
    {
        if (!_monitoring || !_windows.IsRootWindow(e.WindowHandle)) return;
        var processId = unchecked((int)_windows.GetProcessId(e.WindowHandle));
        if (processId <= 0 || processId == Environment.ProcessId) return;

        ActiveSession? existing;
        lock (_sync) _sessions.TryGetValue(processId, out existing);
        if (existing is not null)
        {
            // System menus and game-owned popups also raise foreground/show events from the
            // same process. Always resolve the largest unowned window instead of resizing
            // the event HWND itself; otherwise a title-bar context menu can become game-sized.
            if (_windows.TryFindMainWindow(processId, out var mainWindow) &&
                (existing.WindowHandle == nint.Zero || e.WindowHandle == mainWindow))
                ApplyProfile(processId, mainWindow);
            return;
        }

        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }

        GameProfile? profile;
        try
        {
            profile = EnabledProfiles().FirstOrDefault(item =>
                string.Equals(item.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            process.Dispose();
            return;
        }

        if (profile is null)
        {
            process.Dispose();
            return;
        }

        AttachProcess(process, profile);
        if (_windows.TryFindMainWindow(processId, out var detectedMainWindow))
            ApplyProfile(processId, detectedMainWindow);
        UpdateMappingState();
    }

    private void AttachProcess(Process process, GameProfile profile)
    {
        var processId = process.Id;
        lock (_sync)
        {
            if (_sessions.ContainsKey(processId))
            {
                process.Dispose();
                return;
            }

            var session = new ActiveSession(process, profile);
            _sessions.Add(processId, session);
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnProcessExited(processId);
            }
            catch (InvalidOperationException)
            {
                _sessions.Remove(processId);
                process.Dispose();
                return;
            }
        }
        RaiseStatus($"{profile.GameName} を検出しました");
    }

    private void ApplyProfile(int processId, nint window)
    {
        ActiveSession? session;
        bool centerWindow;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(processId, out session)) return;
            centerWindow = !session.HasAppliedSize || session.WindowHandle != window;
            session.WindowHandle = window;
        }

        if (!_presetSnapshot.TryGetValue(session.Profile.SizePresetId, out var preset)) return;

        if (_windows.ResizeClientArea(window, preset.Width, preset.Height, centerWindow))
        {
            lock (_sync)
            {
                if (_sessions.TryGetValue(processId, out var currentSession) && currentSession.WindowHandle == window)
                    currentSession.HasAppliedSize = true;
            }
            RaiseStatus($"{session.Profile.GameName}: {preset.Width}×{preset.Height} を適用しました");
        }
        else
            RaiseStatus($"{session.Profile.GameName}: ウィンドウサイズを変更できませんでした");
    }

    private void OnProcessExited(int processId)
    {
        ActiveSession? session;
        lock (_sync)
        {
            if (!_sessions.Remove(processId, out session)) return;
        }
        var gameName = session.Profile.GameName;
        session.Process.Dispose();
        UpdateMappingState();
        RaiseStatus($"{gameName} が終了しました。待機処理に戻ります");
    }

    private void UpdateMappingState()
    {
        bool shouldRun;
        lock (_sync) shouldRun = _monitoring && _sessions.Values.Any(item => item.Profile.DpadMappingEnabled);
        if (shouldRun) _mapping.Start(); else _mapping.Stop();
    }

    private bool IsInputAllowed()
    {
        var foreground = _windows.GetRootWindow(_windows.GetForegroundWindowHandle());
        if (foreground == nint.Zero) return false;
        lock (_sync)
        {
            return _sessions.Values.Any(item => item.Profile.DpadMappingEnabled && item.WindowHandle == foreground);
        }
    }

    private IEnumerable<GameProfile> EnabledProfiles() => _profileSnapshot;

    private void CaptureConfiguration()
    {
        _profileSnapshot = _settings.GameProfiles
            .Where(item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.ProcessName))
            .Select(item => new GameProfile
            {
                Id = item.Id,
                GameName = item.GameName,
                ExecutablePath = item.ExecutablePath,
                ProcessName = item.ProcessName,
                SizePresetId = item.SizePresetId,
                DpadMappingEnabled = item.DpadMappingEnabled,
                IsEnabled = item.IsEnabled
            }).ToArray();
        _presetSnapshot = _settings.SizePresets.ToDictionary(item => item.Id, item => new SizePreset
        {
            Id = item.Id,
            Name = item.Name,
            Width = item.Width,
            Height = item.Height,
            AspectGroup = item.AspectGroup,
            IsBuiltIn = item.IsBuiltIn
        });
    }

    private void OnMappingStatusChanged(object? sender, MappingStatusChangedEventArgs e)
    {
        if (e.ErrorMessage is not null) RaiseStatus($"D-padエラー: {e.ErrorMessage}");
        else if (e.IsConnected && e.ControllerIndex is int index) RaiseStatus($"コントローラー {index + 1} のD-pad変換中");
        else RaiseStatus("D-pad変換: コントローラー待機中");
    }

    private void RaiseStatus(string message)
    {
        int count;
        lock (_sync) count = _sessions.Count;
        StatusChanged?.Invoke(this, new RuntimeStatusChangedEventArgs(message, count, _mapping.IsRunning, _monitoring));
    }

    private sealed class ActiveSession(Process process, GameProfile profile)
    {
        public Process Process { get; } = process;
        public GameProfile Profile { get; } = profile;
        public nint WindowHandle { get; set; }
        public bool HasAppliedSize { get; set; }
    }
}

public sealed class RuntimeStatusChangedEventArgs(string message, int activeGameCount, bool mapperRunning, bool monitoring) : EventArgs
{
    public string Message { get; } = message;
    public int ActiveGameCount { get; } = activeGameCount;
    public bool MapperRunning { get; } = mapperRunning;
    public bool Monitoring { get; } = monitoring;
}
