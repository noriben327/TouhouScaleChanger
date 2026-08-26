using Microsoft.Win32;

namespace TouhouScaleChanger.Services;

public sealed class StartupRegistrationService
{
    public const string AutoStartArgument = "--autostart";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TouhouScaleChanger";

    public void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windowsの自動起動設定を開けませんでした。");

        if (enabled)
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("実行ファイルの場所を取得できませんでした。");

            runKey.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\" {AutoStartArgument}";
}
