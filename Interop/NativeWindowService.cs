using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using TouhouScaleChanger.Diagnostics;

namespace TouhouScaleChanger.Interop;

public sealed class NativeWindowService
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint SwpNoActivate = 0x0010;
    private const int SwRestore = 9;
    private const uint MonitorDefaultToNearest = 2;
    private const uint GaRoot = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public uint GetProcessId(nint window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    public nint GetForegroundWindowHandle() => GetForegroundWindow();

    public nint GetRootWindow(nint window) => window == nint.Zero ? nint.Zero : GetAncestor(window, GaRoot);

    public bool TryFindMainWindow(int processId, out nint result)
    {
        var bestWindow = nint.Zero;
        long bestArea = -1;
        EnumWindows((window, _) =>
        {
            try
            {
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId != processId || !IsWindowVisible(window) || GetWindow(window, 4) != nint.Zero)
                    return true;
                if (!GetClientRect(window, out var rect)) return true;
                var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    bestWindow = window;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("メインウィンドウ探索中に例外が発生しました。", exception);
            }
            return true;
        }, nint.Zero);
        result = bestWindow;
        return result != nint.Zero;
    }

    public bool IsRootWindow(nint window) => GetAncestor(window, GaRoot) == window;

    public IReadOnlyList<RunningWindowInfo> GetSelectableWindows(int excludedProcessId)
    {
        var windows = new List<RunningWindowInfo>();
        EnumWindows((window, _) =>
        {
            try
            {
                if (!IsWindowVisible(window) || GetWindow(window, 4) != nint.Zero) return true;
                GetWindowThreadProcessId(window, out var processId);
                if (processId == 0 || processId == excludedProcessId) return true;

                var titleLength = GetWindowTextLength(window);
                if (titleLength <= 0) return true;
                var titleBuffer = new StringBuilder(titleLength + 1);
                if (GetWindowText(window, titleBuffer, titleBuffer.Capacity) <= 0) return true;

                var executablePath = TryGetProcessPath(processId);
                var processName = executablePath.Length > 0
                    ? Path.GetFileNameWithoutExtension(executablePath)
                    : TryGetProcessName(unchecked((int)processId));
                windows.Add(new RunningWindowInfo(window, unchecked((int)processId), titleBuffer.ToString(),
                    processName, executablePath));
            }
            catch (Exception exception)
            {
                AppLog.Error("ウィンドウ列挙中に例外が発生しました。", exception);
            }
            return true;
        }, nint.Zero);

        return windows.OrderBy(item => item.WindowTitle, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public bool TryGetClientSize(nint window, out int width, out int height)
    {
        if (GetClientRect(window, out var rect))
        {
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            return true;
        }
        width = 0;
        height = 0;
        return false;
    }

    public bool ResizeClientArea(nint window, int clientWidth, int clientHeight, bool centerWindow = true)
    {
        if (window == nint.Zero || clientWidth <= 0 || clientHeight <= 0) return false;
        if (!GetClientRect(window, out var currentClient)) return false;
        if (currentClient.Right - currentClient.Left == clientWidth && currentClient.Bottom - currentClient.Top == clientHeight)
            return true;

        var style = GetWindowLongPtr(window, GwlStyle);
        var exStyle = GetWindowLongPtr(window, GwlExStyle);
        var outer = new Rect { Right = clientWidth, Bottom = clientHeight };
        var dpi = GetDpiForWindow(window);
        if (!AdjustWindowRectExForDpi(ref outer, unchecked((uint)style.ToInt64()), false,
                unchecked((uint)exStyle.ToInt64()), dpi == 0 ? 96u : dpi))
            return false;

        var width = outer.Right - outer.Left;
        var height = outer.Bottom - outer.Top;
        if (!GetWindowRect(window, out var currentWindow)) return false;
        var x = currentWindow.Left;
        var y = currentWindow.Top;
        if (centerWindow)
        {
            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(monitor, ref info);
            x = info.Work.Left + Math.Max(0, (info.Work.Right - info.Work.Left - width) / 2);
            y = info.Work.Top + Math.Max(0, (info.Work.Bottom - info.Work.Top - height) / 2);
        }

        ShowWindow(window, SwRestore);
        return SetWindowPos(window, nint.Zero, x, y, width, height, SwpNoActivate);
    }

    private static string TryGetProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero) return string.Empty;
        try
        {
            var capacity = 32768u;
            var buffer = new StringBuilder((int)capacity);
            return QueryFullProcessImageName(process, 0, buffer, ref capacity) ? buffer.ToString() : string.Empty;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string TryGetProcessName(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private delegate bool EnumWindowsDelegate(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsDelegate callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}

public sealed record RunningWindowInfo(nint WindowHandle, int ProcessId, string WindowTitle,
    string ProcessName, string ExecutablePath)
{
    public string PathDisplay => ExecutablePath.Length > 0
        ? ExecutablePath
        : "取得できません（管理者権限が必要な可能性があります）";

    public bool CanSelect => ExecutablePath.Length > 0;
}
