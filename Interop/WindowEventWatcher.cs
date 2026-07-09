using System.Runtime.InteropServices;

namespace TouhouScalePad.Interop;

public sealed class WindowEventWatcher : IDisposable
{
    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;
    private const int ObjidWindow = 0;

    private readonly WinEventDelegate _callback;
    private nint _foregroundHook;
    private nint _showHook;

    public WindowEventWatcher() => _callback = OnWinEvent;

    public event EventHandler<WindowEventArgs>? WindowEvent;

    public void Start()
    {
        if (_foregroundHook != nint.Zero) return;
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, nint.Zero, _callback, 0, 0, WineventOutOfContext);
        _showHook = SetWinEventHook(EventObjectShow, EventObjectShow, nint.Zero, _callback, 0, 0, WineventOutOfContext);
    }

    public void Stop()
    {
        if (_foregroundHook != nint.Zero) UnhookWinEvent(_foregroundHook);
        if (_showHook != nint.Zero) UnhookWinEvent(_showHook);
        _foregroundHook = nint.Zero;
        _showHook = nint.Zero;
    }

    public void Dispose() => Stop();

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint eventTime)
    {
        if (hwnd != nint.Zero && (eventType == EventSystemForeground || objectId == ObjidWindow))
            WindowEvent?.Invoke(this, new WindowEventArgs(hwnd, eventType));
    }

    private delegate void WinEventDelegate(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint eventTime);

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}

public sealed class WindowEventArgs(nint windowHandle, uint eventType) : EventArgs
{
    public nint WindowHandle { get; } = windowHandle;
    public uint EventType { get; } = eventType;
}
