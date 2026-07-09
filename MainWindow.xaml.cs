using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TouhouScalePad.Models;
using TouhouScalePad.Interop;
using TouhouScalePad.Services;
using TouhouScalePad.Settings;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace TouhouScalePad;

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.Brush WaitingBrush = new SolidColorBrush(MediaColor.FromRgb(242, 184, 75));
    private static readonly System.Windows.Media.Brush ActiveBrush = new SolidColorBrush(MediaColor.FromRgb(55, 190, 125));
    private static readonly System.Windows.Media.Brush PausedBrush = new SolidColorBrush(MediaColor.FromRgb(151, 160, 176));

    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private readonly RuntimeCoordinator _runtime;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _trayMonitoringItem;
    private readonly System.Drawing.Icon _applicationIcon;
    private Guid? _editingProfileId;
    private bool _reallyClosing;
    private bool _uiReady;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        _runtime = new RuntimeCoordinator(_settings);
        _runtime.StatusChanged += Runtime_OnStatusChanged;

        _trayMonitoringItem = new Forms.ToolStripMenuItem("監視を一時停止");
        _trayMonitoringItem.Click += (_, _) => Dispatcher.Invoke(ToggleMonitoring);
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("TouhouScalePadを開く", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        trayMenu.Items.Add(_trayMonitoringItem);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("終了", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var associatedIcon = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath)
            : null;
        _applicationIcon = (System.Drawing.Icon)(associatedIcon ?? System.Drawing.SystemIcons.Application).Clone();
        associatedIcon?.Dispose();

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "TouhouScalePad - ゲーム起動待機中",
            Icon = _applicationIcon,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);

        RefreshProfiles();
        RefreshSizePresets();
        SelectPollingInterval(_settings.PollingIntervalMilliseconds);
        SettingsPathTextBlock.Text = $"設定ファイル: {_settingsService.SettingsPath}";
        _uiReady = true;

        if (_settings.MonitoringEnabled) _runtime.Start(); else UpdateMonitoringUi(false);
    }

    private void RefreshProfiles(Guid? selectId = null)
    {
        foreach (var profile in _settings.GameProfiles)
        {
            var size = _settings.SizePresets.FirstOrDefault(item => item.Id == profile.SizePresetId);
            var sizeText = size is null ? "サイズ未設定" : $"{size.Width}×{size.Height}";
            profile.DisplayDetail = $"{sizeText} · D-pad {(profile.DpadMappingEnabled ? "ON" : "OFF")}";
        }
        ProfileListBox.ItemsSource = null;
        ProfileListBox.ItemsSource = _settings.GameProfiles.OrderBy(item => item.GameName).ToList();
        if (selectId is Guid id)
            ProfileListBox.SelectedItem = _settings.GameProfiles.FirstOrDefault(item => item.Id == id);
    }

    private void RefreshSizePresets(Guid? selectId = null)
    {
        var ordered = _settings.SizePresets.OrderBy(item => AspectOrder(item.AspectGroup))
            .ThenBy(item => item.Width).ThenBy(item => item.Height).ToList();
        SizePresetListView.ItemsSource = null;
        SizePresetListView.ItemsSource = ordered;
        ProfileSizeComboBox.ItemsSource = null;
        ProfileSizeComboBox.ItemsSource = ordered;
        if (selectId is Guid id)
            ProfileSizeComboBox.SelectedItem = ordered.FirstOrDefault(item => item.Id == id);
        else if (ProfileSizeComboBox.SelectedIndex < 0)
            ProfileSizeComboBox.SelectedItem = ordered.FirstOrDefault(item => item.Width == 1600 && item.Height == 1200);
    }

    private static int AspectOrder(string group) => group switch { "4:3" => 0, "16:9" => 1, _ => 2 };

    private void ProfileListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileListBox.SelectedItem is not GameProfile profile) return;
        _editingProfileId = profile.Id;
        GameNameTextBox.Text = profile.GameName;
        ExecutablePathTextBox.Text = profile.ExecutablePath;
        ProfileSizeComboBox.SelectedItem = _settings.SizePresets.FirstOrDefault(item => item.Id == profile.SizePresetId);
        DpadMappingCheckBox.IsChecked = profile.DpadMappingEnabled;
        ProfileEnabledCheckBox.IsChecked = profile.IsEnabled;
    }

    private void NewProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProfileListBox.SelectedItem = null;
        _editingProfileId = null;
        GameNameTextBox.Clear();
        ExecutablePathTextBox.Clear();
        DpadMappingCheckBox.IsChecked = true;
        ProfileEnabledCheckBox.IsChecked = true;
        ProfileSizeComboBox.SelectedItem = _settings.SizePresets.FirstOrDefault(item => item.Width == 1600 && item.Height == 1200);
        GameNameTextBox.Focus();
    }

    private void BrowseExecutableButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog { Title = "ゲーム実行ファイルを選択", Filter = "実行ファイル (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        ExecutablePathTextBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(GameNameTextBox.Text))
        {
            var description = FileVersionInfo.GetVersionInfo(dialog.FileName).FileDescription;
            GameNameTextBox.Text = string.IsNullOrWhiteSpace(description)
                ? Path.GetFileNameWithoutExtension(dialog.FileName)
                : description;
        }
    }

    private void PickRunningWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WindowPickerDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedWindow is not RunningWindowInfo selected) return;

        ExecutablePathTextBox.Text = selected.ExecutablePath;
        var description = TryGetFileDescription(selected.ExecutablePath);
        GameNameTextBox.Text = !string.IsNullOrWhiteSpace(description)
            ? description
            : !string.IsNullOrWhiteSpace(selected.WindowTitle)
                ? selected.WindowTitle
                : selected.ProcessName;
        FooterStatusText.Text = $"{selected.WindowTitle} を選択しました";
    }

    private static string? TryGetFileDescription(string executablePath)
    {
        try { return FileVersionInfo.GetVersionInfo(executablePath).FileDescription; }
        catch (Exception exception) when (exception is FileNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var gameName = GameNameTextBox.Text.Trim();
        var executablePath = ExecutablePathTextBox.Text.Trim();
        if (gameName.Length == 0 || executablePath.Length == 0 || !File.Exists(executablePath))
        {
            WpfMessageBox.Show(this, "ゲーム名と、有効なゲーム実行ファイルを指定してください。", "TouhouScalePad");
            return;
        }
        if (ProfileSizeComboBox.SelectedItem is not SizePreset sizePreset)
        {
            WpfMessageBox.Show(this, "拡大サイズを選択してください。", "TouhouScalePad");
            return;
        }

        var processName = Path.GetFileNameWithoutExtension(executablePath);
        var duplicate = _settings.GameProfiles.FirstOrDefault(item => item.Id != _editingProfileId &&
            string.Equals(item.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            WpfMessageBox.Show(this, $"{processName}.exe は「{duplicate.GameName}」ですでに登録されています。", "TouhouScalePad");
            return;
        }

        var profile = _settings.GameProfiles.FirstOrDefault(item => item.Id == _editingProfileId);
        if (profile is null)
        {
            profile = new GameProfile();
            _settings.GameProfiles.Add(profile);
        }
        profile.GameName = gameName;
        profile.ExecutablePath = executablePath;
        profile.ProcessName = processName;
        profile.SizePresetId = sizePreset.Id;
        profile.DpadMappingEnabled = DpadMappingCheckBox.IsChecked == true;
        profile.IsEnabled = ProfileEnabledCheckBox.IsChecked == true;
        _editingProfileId = profile.Id;
        SaveSettings();
        RefreshProfiles(profile.Id);
        _runtime.ReloadProfiles();
        FooterStatusText.Text = $"{profile.GameName} を保存しました";
    }

    private void DeleteProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProfileListBox.SelectedItem is not GameProfile profile) return;
        if (WpfMessageBox.Show(this, $"「{profile.GameName}」を削除しますか？", "TouhouScalePad",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.GameProfiles.Remove(profile);
        SaveSettings();
        NewProfileButton_OnClick(sender, e);
        RefreshProfiles();
        _runtime.ReloadProfiles();
    }

    private void AddSizePresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = PresetNameTextBox.Text.Trim();
        if (!int.TryParse(PresetWidthTextBox.Text, out var width) || !int.TryParse(PresetHeightTextBox.Text, out var height) ||
            width is < 100 or > 16384 || height is < 100 or > 16384)
        {
            WpfMessageBox.Show(this, "幅と高さは100～16384の整数で指定してください。", "TouhouScalePad");
            return;
        }
        if (_settings.SizePresets.Any(item => item.Width == width && item.Height == height))
        {
            WpfMessageBox.Show(this, "同じサイズのプリセットがすでにあります。", "TouhouScalePad");
            return;
        }
        var aspect = (PresetAspectComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "その他";
        var preset = new SizePreset
        {
            Name = name.Length == 0 ? $"{aspect} / {width}×{height}" : name,
            Width = width,
            Height = height,
            AspectGroup = aspect,
            IsBuiltIn = false
        };
        _settings.SizePresets.Add(preset);
        SaveSettings();
        RefreshSizePresets(preset.Id);
        PresetNameTextBox.Clear();
        PresetWidthTextBox.Clear();
        PresetHeightTextBox.Clear();
    }

    private void DeleteSizePresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SizePresetListView.SelectedItem is not SizePreset preset) return;
        if (preset.IsBuiltIn)
        {
            WpfMessageBox.Show(this, "標準プリセットは削除できません。", "TouhouScalePad");
            return;
        }
        if (_settings.GameProfiles.Any(item => item.SizePresetId == preset.Id))
        {
            WpfMessageBox.Show(this, "このサイズを使用しているゲームプロファイルがあります。先にプロファイルを変更してください。", "TouhouScalePad");
            return;
        }
        _settings.SizePresets.Remove(preset);
        SaveSettings();
        RefreshSizePresets();
    }

    private void MonitoringButton_OnClick(object sender, RoutedEventArgs e) => ToggleMonitoring();

    private void ToggleMonitoring()
    {
        if (_runtime.IsMonitoring) _runtime.Stop(); else _runtime.Start();
        _settings.MonitoringEnabled = _runtime.IsMonitoring;
        SaveSettings();
        UpdateMonitoringUi(_runtime.IsMonitoring);
    }

    private void PollingIntervalComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || PollingIntervalComboBox.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var interval)) return;
        _settings.PollingIntervalMilliseconds = interval;
        _runtime.SetPollingInterval(interval);
        SaveSettings();
    }

    private void SelectPollingInterval(int interval)
    {
        foreach (var item in PollingIntervalComboBox.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == interval.ToString()) { PollingIntervalComboBox.SelectedItem = item; return; }
        PollingIntervalComboBox.SelectedIndex = 1;
    }

    private void Runtime_OnStatusChanged(object? sender, RuntimeStatusChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            HeaderStatusText.Text = e.ActiveGameCount > 0 ? $"ゲーム実行中 ({e.ActiveGameCount})" : e.Monitoring ? "ゲーム起動待機中" : "監視停止中";
            FooterStatusText.Text = e.Message;
            StatusIndicator.Fill = e.ActiveGameCount > 0 ? ActiveBrush : e.Monitoring ? WaitingBrush : PausedBrush;
            _trayIcon.Text = e.ActiveGameCount > 0 ? "TouhouScalePad - ゲームへ適用中" : e.Monitoring ? "TouhouScalePad - ゲーム起動待機中" : "TouhouScalePad - 監視停止中";
            UpdateMonitoringUi(e.Monitoring);
        });
    }

    private void UpdateMonitoringUi(bool monitoring)
    {
        MonitoringButton.Content = monitoring ? "監視を一時停止" : "監視を再開";
        _trayMonitoringItem.Text = monitoring ? "監視を一時停止" : "監視を再開";
        if (!monitoring)
        {
            HeaderStatusText.Text = "監視停止中";
            StatusIndicator.Fill = PausedBrush;
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_reallyClosing) return;
        e.Cancel = true;
        Hide();
        _trayIcon.ShowBalloonTip(1500, "TouhouScalePad", "タスクトレイでゲームの起動を監視しています。", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _reallyClosing = true;
        SaveSettings();
        _runtime.StatusChanged -= Runtime_OnStatusChanged;
        _runtime.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _applicationIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void SaveSettings()
    {
        try { _settingsService.Save(_settings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WpfMessageBox.Show(this, $"設定を保存できませんでした。\n{exception.Message}", "TouhouScalePad");
        }
    }
}
