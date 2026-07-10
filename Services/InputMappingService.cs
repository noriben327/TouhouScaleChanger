using System.ComponentModel;
using System.Runtime.InteropServices;
using TouhouScaleChanger.Core;
using TouhouScaleChanger.Diagnostics;

namespace TouhouScaleChanger.Services;

public sealed class InputMappingService : IDisposable
{
    private readonly IControllerSource _controllerSource;
    private readonly IKeyboardOutput _keyboardOutput;
    private readonly Func<bool> _isInputAllowed;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cancellation;
    private Thread? _thread;
    private int _pollingIntervalMilliseconds;
    private bool _disposed;

    public InputMappingService(IControllerSource controllerSource, IKeyboardOutput keyboardOutput,
        Func<bool> isInputAllowed, int pollingIntervalMilliseconds)
    {
        _controllerSource = controllerSource;
        _keyboardOutput = keyboardOutput;
        _isInputAllowed = isInputAllowed;
        PollingIntervalMilliseconds = pollingIntervalMilliseconds;
    }

    public event EventHandler<MappingStatusChangedEventArgs>? StatusChanged;
    public bool IsRunning { get; private set; }

    public int PollingIntervalMilliseconds
    {
        get => Volatile.Read(ref _pollingIntervalMilliseconds);
        set
        {
            if (value is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _pollingIntervalMilliseconds, value);
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lifecycleLock)
        {
            if (IsRunning) return;
            _cancellation = new CancellationTokenSource();
            _thread = new Thread(() => PollLoop(_cancellation.Token))
            {
                IsBackground = true,
                Name = "TouhouScaleChanger D-pad polling",
                Priority = ThreadPriority.AboveNormal
            };
            IsRunning = true;
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lifecycleLock)
        {
            if (!IsRunning) return;
            _cancellation?.Cancel();
            thread = _thread;
            IsRunning = false;
        }

        if (thread is not null && thread != Thread.CurrentThread) thread.Join();
        lock (_lifecycleLock)
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _thread = null;
        }
        StatusChanged?.Invoke(this, new MappingStatusChangedEventArgs(false, null, null));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private void PollLoop(CancellationToken token)
    {
        var previous = DpadButtons.None;
        var connected = false;
        var controllerIndex = -1;
        TimeBeginPeriod(1);
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_isInputAllowed())
                    {
                        if (previous != DpadButtons.None) _keyboardOutput.ReleaseAll(previous);
                        previous = DpadButtons.None;
                    }
                    else if (_controllerSource.TryGetState(out var state))
                    {
                        if (connected && controllerIndex != state.ControllerIndex)
                        {
                            _keyboardOutput.ReleaseAll(previous);
                            previous = DpadButtons.None;
                        }
                        _keyboardOutput.ApplyTransition(previous, state.Buttons);
                        previous = state.Buttons;
                        if (!connected || controllerIndex != state.ControllerIndex)
                            StatusChanged?.Invoke(this, new MappingStatusChangedEventArgs(true, state.ControllerIndex, null));
                        connected = true;
                        controllerIndex = state.ControllerIndex;
                    }
                    else if (connected)
                    {
                        _keyboardOutput.ReleaseAll(previous);
                        previous = DpadButtons.None;
                        connected = false;
                        controllerIndex = -1;
                        StatusChanged?.Invoke(this, new MappingStatusChangedEventArgs(false, null, null));
                    }
                }
                catch (Exception exception)
                {
                    // Runs on a background thread: any escaping exception terminates the whole
                    // process. Controller/HID drivers can throw a range of types (Win32,
                    // DllNotFound, SEH/AccessViolation-wrapped, etc.), so catch all, recover,
                    // and keep polling instead of crashing.
                    AppLog.Error("D-padポーリング中に例外が発生しました。", exception);
                    try { _keyboardOutput.ReleaseAll(previous); } catch (Exception releaseException) { AppLog.Error("キー解放に失敗しました。", releaseException); }
                    previous = DpadButtons.None;
                    connected = false;
                    StatusChanged?.Invoke(this, new MappingStatusChangedEventArgs(false, null, exception.Message));
                }

                if (token.WaitHandle.WaitOne(PollingIntervalMilliseconds)) break;
            }
        }
        finally
        {
            try { _keyboardOutput.ReleaseAll(previous); } catch (Win32Exception) { }
            TimeEndPeriod(1);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint periodMilliseconds);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint periodMilliseconds);
}

public sealed class MappingStatusChangedEventArgs(bool isConnected, int? controllerIndex, string? errorMessage) : EventArgs
{
    public bool IsConnected { get; } = isConnected;
    public int? ControllerIndex { get; } = controllerIndex;
    public string? ErrorMessage { get; } = errorMessage;
}
