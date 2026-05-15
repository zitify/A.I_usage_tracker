using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AIUsageTracker.Converters;

/// <summary>
/// "good"/"warn"/"high"/"bad" 상태 토큰을 20% 투명도 배경 SolidColorBrush 로 변환.
/// 상태 배지의 pill 배경에 사용.
/// </summary>
public class StatusTokenToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var token = value as string ?? "good";
        var color = token switch
        {
            "good" => System.Windows.Media.Color.FromArgb(0x33, 0x4A, 0xDE, 0x80),
            "warn" => System.Windows.Media.Color.FromArgb(0x33, 0xFA, 0xCC, 0x15),
            "high" => System.Windows.Media.Color.FromArgb(0x33, 0xFB, 0x92, 0x3C),
            "bad"  => System.Windows.Media.Color.FromArgb(0x33, 0xF8, 0x71, 0x71),
            _       => System.Windows.Media.Color.FromArgb(0x33, 0x88, 0x88, 0x88),
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// "good"/"warn"/"high"/"bad" 상태 토큰을 불투명 전경 SolidColorBrush 로 변환.
/// 상태 배지 텍스트 색에 사용.
/// </summary>
public class StatusTokenToFgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var token = value as string ?? "good";
        var color = token switch
        {
            "good" => System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80),
            "warn" => System.Windows.Media.Color.FromRgb(0xFA, 0xCC, 0x15),
            "high" => System.Windows.Media.Color.FromRgb(0xFB, 0x92, 0x3C),
            "bad"  => System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71),
            _       => System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88),
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
