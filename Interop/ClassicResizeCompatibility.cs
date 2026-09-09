using System.Runtime.InteropServices;

namespace TouhouScaleChanger.Interop;

/// <summary>Per-session fallback for the Steam Classic games' interactive sizing protocol.</summary>
public sealed class ClassicResizeCompatibility
{
    private readonly bool _newClassic;
    private nint _window;
    private Rect _renderBaseline;
    private int _lastWidth;
    private int _lastHeight;

    private ClassicResizeCompatibility(bool newClassic) => _newClassic = newClassic;

    public static ClassicResizeCompatibility? ForProcess(string processName) =>
        processName.ToLowerInvariant() switch
        {
            "th06c" => new(false),
            "th06nc" => new(true),
            _ => null
        };

    public bool TryResize(nint window, int x, int y, int outerWidth, int outerHeight,
        int clientWidth, int clientHeight)
    {
        if (!GetWindowRect(window, out var before) || !GetClientRect(window, out var client)) return false;
        // Keep the original render baseline across repeated profile changes. A game-side
        // resolution change (or recreated HWND) starts a new baseline.
        if (_window != window || client.Right != _lastWidth || client.Bottom != _lastHeight)
            _renderBaseline = before;
        _window = window;

        var target = new Rect { Left = x, Top = y, Right = x + outerWidth, Bottom = y + outerHeight };
        if (!Send(window, 0x0231)) return false; // WM_ENTERSIZEMOVE
        bool resized;
        bool exited;
        try
        {
            resized = SendSizing(window, ref target) &&
                SetWindowPos(window, 0, x, y, outerWidth, outerHeight, 0x0434);
        }
        finally
        {
            exited = Send(window, 0x0232); // Always leave the interactive sizing state.
        }
        if (!resized || !exited || !Send(window, 0x0005, (nint)((clientHeight << 16) | clientWidth))) return false;

        // New Classic scales its rendered viewport on WM_SIZING in addition to the
        // presentation stretch. Restore the original render extent, not the HWND size.
        if (_newClassic)
        {
            var baseline = _renderBaseline;
            if (!SendSizing(window, ref baseline)) return false;
        }
        if (!GetClientRect(window, out client) || client.Right != clientWidth || client.Bottom != clientHeight)
            return false;
        _lastWidth = clientWidth;
        _lastHeight = clientHeight;
        return true;
    }

    private static bool Send(nint window, uint message, nint parameter = 0) =>
        SendMessageTimeout(window, message, 0, parameter, 0x0002, 500, out _) != 0;

    private static bool SendSizing(nint window, ref Rect rect) =>
        SendSizingTimeout(window, 0x0214, 8, ref rect, 0x0002, 500, out _) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendSizingTimeout(nint window, uint message, nuint wParam, ref Rect lParam,
        uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
