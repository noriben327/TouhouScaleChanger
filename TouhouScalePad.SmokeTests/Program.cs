using System.Diagnostics;
using TouhouScalePad.Models;
using TouhouScalePad.Services;
using TouhouScalePad.Settings;
using Forms = System.Windows.Forms;

namespace TouhouScalePad.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--target", StringComparer.Ordinal)) return RunSmokeTarget();

        try
        {
            VerifyDefaultPresets();
            VerifyResizeKeepsPositionWhenRequested();
            VerifyEventDrivenResize();
            Console.WriteLine("TouhouScalePad smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyDefaultPresets()
    {
        var settings = AppSettings.CreateDefault();
        Assert(settings.SizePresets.Any(item => item.Width == 1600 && item.Height == 1200 && item.AspectGroup == "4:3"),
            "1600x1200 preset is missing.");
        Assert(settings.SizePresets.Any(item => item.Width == 1400 && item.Height == 1050 && item.AspectGroup == "4:3"),
            "1400x1050 preset is missing.");
        Assert(settings.SizePresets.Count(item => item.AspectGroup == "4:3") >= 5,
            "Expected multiple 4:3 presets.");
        Assert(settings.SizePresets.Count(item => item.AspectGroup == "16:9") >= 4,
            "Expected multiple 16:9 presets.");
        Assert(settings.SizePresets.Select(item => item.Id).Distinct().Count() == settings.SizePresets.Count,
            "Preset IDs must be unique.");
    }

    private static void VerifyResizeKeepsPositionWhenRequested()
    {
        using var form = new Forms.Form
        {
            ClientSize = new System.Drawing.Size(320, 240),
            FormBorderStyle = Forms.FormBorderStyle.FixedSingle,
            StartPosition = Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(137, 163)
        };
        form.Show();
        var originalLocation = form.Location;
        var windows = new TouhouScalePad.Interop.NativeWindowService();
        Assert(windows.ResizeClientArea(form.Handle, 700, 500, centerWindow: false),
            "In-place resize failed.");
        Forms.Application.DoEvents();
        Assert(form.Location == originalLocation,
            $"In-place resize moved the window from {originalLocation} to {form.Location}.");
        Assert(form.ClientSize == new System.Drawing.Size(700, 500),
            "In-place resize did not apply the requested client size.");
        form.Close();
    }

    private static void VerifyEventDrivenResize()
    {
        const int targetWidth = 700;
        const int targetHeight = 500;
        var preset = new SizePreset
        {
            Name = "Smoke test",
            Width = targetWidth,
            Height = targetHeight,
            AspectGroup = "その他"
        };
        var settings = new AppSettings
        {
            SizePresets = [preset],
            GameProfiles =
            [
                new GameProfile
                {
                    GameName = "TouhouScalePad smoke target",
                    ProcessName = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "TouhouScalePad.SmokeTests",
                    SizePresetId = preset.Id,
                    DpadMappingEnabled = true,
                    IsEnabled = true
                }
            ]
        };

        using var coordinator = new RuntimeCoordinator(settings);
        coordinator.Start();

        using var target = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            Arguments = "--target",
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not launch the smoke target.");

        var native = new TouhouScalePad.Interop.NativeWindowService();
        var timeout = Stopwatch.StartNew();
        var resized = false;
        while (timeout.Elapsed < TimeSpan.FromSeconds(5) && !target.HasExited)
        {
            target.Refresh();
            var window = target.MainWindowHandle;
            if (window != nint.Zero && native.TryGetClientSize(window, out var width, out var height) &&
                width == targetWidth && height == targetHeight)
            {
                resized = true;
                break;
            }
            Thread.Sleep(25);
        }

        Assert(resized, "The external fixed-window target was not resized by the launch event.");
        Assert(coordinator.IsMonitoring, "Coordinator stopped unexpectedly.");
        Assert(coordinator.IsMappingRunning, "D-pad mapping did not start for the running target.");

        var selectableWindow = native.GetSelectableWindows(Environment.ProcessId)
            .FirstOrDefault(item => item.ProcessId == target.Id);
        Assert(selectableWindow is not null, "The running target was not listed by the window picker.");
        Assert(selectableWindow!.CanSelect && File.Exists(selectableWindow.ExecutablePath),
            "The window picker did not resolve the target executable path.");
        Assert(selectableWindow.WindowTitle.Contains("TouhouScalePad fixed-window", StringComparison.Ordinal),
            "The window picker did not resolve the target window title.");

        timeout.Restart();
        TouhouScalePad.Interop.RunningWindowInfo? popupWindow = null;
        while (timeout.Elapsed < TimeSpan.FromSeconds(3) && popupWindow is null)
        {
            popupWindow = native.GetSelectableWindows(Environment.ProcessId)
                .FirstOrDefault(item => item.ProcessId == target.Id && item.WindowTitle == "TouhouScalePad popup regression target");
            Thread.Sleep(25);
        }
        Assert(popupWindow is not null, "The popup regression target was not created.");
        Assert(native.TryGetClientSize(popupWindow!.WindowHandle, out var popupWidth, out var popupHeight) &&
               popupWidth == 240 && popupHeight == 120,
            $"A transient popup was resized incorrectly: {popupWidth}x{popupHeight}");

        target.CloseMainWindow();
        if (!target.WaitForExit(2000)) target.Kill(true);
        timeout.Restart();
        while (timeout.Elapsed < TimeSpan.FromSeconds(2) && coordinator.IsMappingRunning) Thread.Sleep(10);
        Assert(!coordinator.IsMappingRunning, "D-pad mapping did not stop after the target exited.");
        coordinator.Stop();
        Assert(!coordinator.IsMonitoring, "Coordinator did not stop.");
    }

    private static int RunSmokeTarget()
    {
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);
        using var form = new Forms.Form
        {
            Text = "TouhouScalePad fixed-window smoke target",
            ClientSize = new System.Drawing.Size(320, 240),
            FormBorderStyle = Forms.FormBorderStyle.FixedSingle,
            StartPosition = Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(20, 20)
        };
        Forms.Form? popup = null;
        using var popupTimer = new Forms.Timer { Interval = 500 };
        popupTimer.Tick += (_, _) =>
        {
            popupTimer.Stop();
            popup = new Forms.Form
            {
                Text = "TouhouScalePad popup regression target",
                ClientSize = new System.Drawing.Size(240, 120),
                FormBorderStyle = Forms.FormBorderStyle.FixedToolWindow,
                StartPosition = Forms.FormStartPosition.Manual,
                Location = new System.Drawing.Point(40, 40)
            };
            popup.Show();
        };
        popupTimer.Start();
        Forms.Application.Run(form);
        popup?.Close();
        popup?.Dispose();
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
