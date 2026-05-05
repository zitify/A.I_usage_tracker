using System.IO;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;
using DrawingBitmap = System.Drawing.Bitmap;

namespace AIUsageTracker.Services;

// ════════════════════════════════════════════════════════════════
//  IconHelper — 사용자 선택 앱 아이콘 로드/적용
//  AppSettings.AppIconPath 가:
//    null/빈 문자열  → 기본 Assets/icon.ico
//    'builtin:A/B/C' → 번들된 빌트인 PNG 컨셉 (Assets/icons/icon-{A|B|C}-*.png)
//    그 외(절대 경로) → 사용자 파일
//  Window·작업표시줄·트레이 세 곳을 한 번에 갱신.
//  주의: .exe 의 PE 리소스 아이콘(탐색기·시작메뉴)은 빌드 시 고정 → 변경 불가.
// ════════════════════════════════════════════════════════════════
public static class IconHelper
{
    public const string DefaultIconPackUri = "pack://application:,,,/Assets/icon.ico";

    /// <summary>설정값을 실제 사용 가능한 절대 파일 경로로 해석.
    /// builtin:A/B/C → 앱 디렉터리의 Assets/icons/icon-{X}-*.png 로 매핑.
    /// 빈 값/잘못된 값이면 null 반환 → 기본 아이콘 사용.</summary>
    public static string? ResolvePath(string? configValue)
    {
        if (string.IsNullOrWhiteSpace(configValue)) return null;

        if (configValue.StartsWith("builtin:", System.StringComparison.OrdinalIgnoreCase))
        {
            var key = configValue.Substring(8).ToUpperInvariant();
            var fileName = key switch
            {
                "A" => "icon-A-paw-chart.png",
                "B" => "icon-B-quota-ring.png",
                "C" => "icon-C-dual-corgi.png",
                _   => null
            };
            if (fileName == null) return null;
            var path = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Assets", "icons", fileName);
            return File.Exists(path) ? path : null;
        }

        return File.Exists(configValue) ? configValue : null;
    }

    /// <summary>Window.Icon 으로 쓸 ImageSource. path null 이면 기본 ico.</summary>
    public static BitmapImage LoadWindowIcon(string? path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (path != null && File.Exists(path))
            bmp.UriSource = new System.Uri(path, System.UriKind.Absolute);
        else
            bmp.UriSource = new System.Uri(DefaultIconPackUri);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>NotifyIcon.Icon 용 System.Drawing.Icon. PNG 면 비트맵 → HICON 으로 변환.</summary>
    public static DrawingIcon LoadTrayIcon(string? path)
    {
        if (path == null || !File.Exists(path))
        {
            var resource = System.Windows.Application.GetResourceStream(new System.Uri(DefaultIconPackUri));
            if (resource != null)
            {
                using var s = resource.Stream;
                return new DrawingIcon(s);
            }
            return System.Drawing.SystemIcons.Application;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ico")
        {
            using var fs = File.OpenRead(path);
            return new DrawingIcon(fs);
        }

        // PNG/JPG 등 → Bitmap → HICON. 트레이는 작은 크기 (16/32) 라 1024 에서 다운샘플링 OK.
        using var bmp = new DrawingBitmap(path);
        var hicon = bmp.GetHicon();
        // GetHicon 핸들 소유권은 Icon 이 가져가므로 별도 DestroyIcon 호출 불필요(앱 종료까지 보유).
        return DrawingIcon.FromHandle(hicon);
    }

    /// <summary>현재 실행 중인 앱의 모든 아이콘 표시 위치를 갱신 (Window + 트레이).
    /// <para>탐색기·시작메뉴 .exe 아이콘은 변경되지 않음 — 빌드 시 PE 리소스 박힘.</para></summary>
    public static void ApplyToApp(string? configValue)
    {
        var path = ResolvePath(configValue);

        try
        {
            var window = System.Windows.Application.Current?.MainWindow;
            if (window != null) window.Icon = LoadWindowIcon(path);
        }
        catch (System.Exception ex) { Logger.Warn("Apply window icon failed", ex); }

        try
        {
            if (System.Windows.Application.Current is App app)
                app.UpdateTrayIcon(LoadTrayIcon(path));
        }
        catch (System.Exception ex) { Logger.Warn("Apply tray icon failed", ex); }
    }
}
