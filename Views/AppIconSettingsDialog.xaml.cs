using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIUsageTracker.Services;

namespace AIUsageTracker.Views;

public partial class AppIconSettingsDialog : Window
{
    private readonly StorageService _storage;
    private string? _selectedConfigValue;

    public AppIconSettingsDialog(StorageService storage)
    {
        InitializeComponent();
        _storage = storage;
        _selectedConfigValue = storage.Settings.AppIconPath;
        UpdateSelectionUI();
    }

    private void UpdateSelectionUI()
    {
        // 빌트인 카드 테두리 — 선택된 것만 강조
        ChoiceA.BorderBrush = (System.Windows.Media.Brush)FindResource(
            _selectedConfigValue?.Equals("builtin:A", System.StringComparison.OrdinalIgnoreCase) == true
                ? "AccentBrush" : "BorderBrushBase");
        ChoiceB.BorderBrush = (System.Windows.Media.Brush)FindResource(
            _selectedConfigValue?.Equals("builtin:B", System.StringComparison.OrdinalIgnoreCase) == true
                ? "AccentBrush" : "BorderBrushBase");
        ChoiceC.BorderBrush = (System.Windows.Media.Brush)FindResource(
            _selectedConfigValue?.Equals("builtin:C", System.StringComparison.OrdinalIgnoreCase) == true
                ? "AccentBrush" : "BorderBrushBase");

        // 사용자 파일 경로
        if (!string.IsNullOrEmpty(_selectedConfigValue) &&
            !_selectedConfigValue.StartsWith("builtin:", System.StringComparison.OrdinalIgnoreCase))
        {
            CustomPathBox.Text = _selectedConfigValue;
        }
        else
        {
            CustomPathBox.Text = "";
        }
    }

    private void Choice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string tag)
        {
            _selectedConfigValue = tag;
            UpdateSelectionUI();
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "앱 아이콘 파일 선택",
            Filter = "이미지 파일 (*.ico;*.png;*.jpg;*.jpeg)|*.ico;*.png;*.jpg;*.jpeg|모든 파일 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedConfigValue = dlg.FileName;
            UpdateSelectionUI();
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _selectedConfigValue = null;
        UpdateSelectionUI();
        StatusLabel.Text = "기본 아이콘으로 복원 — '적용'을 눌러 저장하세요";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        // 사용자 파일을 골랐는데 잘못된 경로면 경고
        if (!string.IsNullOrEmpty(_selectedConfigValue) &&
            !_selectedConfigValue.StartsWith("builtin:", System.StringComparison.OrdinalIgnoreCase) &&
            !System.IO.File.Exists(_selectedConfigValue))
        {
            System.Windows.MessageBox.Show(this, $"파일을 찾을 수 없습니다:\n{_selectedConfigValue}",
                "앱 아이콘 설정", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _storage.Settings.AppIconPath = _selectedConfigValue;
        _storage.SaveSettings(_storage.Settings);
        IconHelper.ApplyToApp(_selectedConfigValue);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
