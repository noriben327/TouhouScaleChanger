using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouhouScalePad.Interop;

namespace TouhouScalePad;

public partial class WindowPickerDialog : Window
{
    private readonly NativeWindowService _windows = new();

    public WindowPickerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshWindows();
    }

    public RunningWindowInfo? SelectedWindow { get; private set; }

    private void RefreshWindows()
    {
        var items = _windows.GetSelectableWindows(Environment.ProcessId);
        WindowListView.ItemsSource = items;
        CountTextBlock.Text = $"{items.Count} 個のウィンドウ";
        SelectButton.IsEnabled = false;
    }

    private void WindowListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled = WindowListView.SelectedItem is RunningWindowInfo { CanSelect: true };
    }

    private void WindowListView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WindowListView.SelectedItem is RunningWindowInfo { CanSelect: true }) ConfirmSelection();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e) => RefreshWindows();

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectButton_OnClick(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (WindowListView.SelectedItem is not RunningWindowInfo { CanSelect: true } selected) return;
        SelectedWindow = selected;
        DialogResult = true;
        Close();
    }
}
