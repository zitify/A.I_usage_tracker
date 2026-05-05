using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using AIUsageTracker.Services;
using AIUsageTracker.Services.Providers;
using AIUsageTracker.Views;

namespace AIUsageTracker;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Global\\AIUsageTracker_SingleInstance";
    private const string ShowEventName            = "Global\\AIUsageTracker_Show";

    private System.Threading.Mutex?          _instanceMutex;
    private System.Threading.EventWaitHandle? _showEvent;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow?                       _mainWindow;
    private GeminiRelayService?               _geminiRelay;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 전역 예외 핸들러 — 로그에 남기고 사용자에게 친절한 에러 메시지 표시.
        // (이전엔 캐치되지 않은 UI/스레드 예외가 조용히 앱을 죽여서 진단 불가능했음)
        DispatcherUnhandledException += (s, ex) =>
        {
            Logger.Error("Unhandled UI exception", ex.Exception);
            try
            {
                System.Windows.MessageBox.Show(
                    $"예기치 못한 오류가 발생했습니다.\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n로그: %APPDATA%\\AI_usage_tracker\\logs\\app.log",
                    "A.I. Usage Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            ex.Handled = true;   // 앱이 죽지 않도록
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            Logger.Error("Unhandled domain exception", ex.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            Logger.Warn("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        _instanceMutex = new System.Threading.Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Signal the running instance to show its window, then exit.
            try
            {
                using var ev = System.Threading.EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        // Listen for show-signals from future second-instance attempts.
        _showEvent = new System.Threading.EventWaitHandle(
            false, System.Threading.EventResetMode.AutoReset, ShowEventName);
        var t = new System.Threading.Thread(() =>
        {
            while (_showEvent != null && _showEvent.WaitOne())
                Dispatcher.Invoke(ShowWin);
        }) { IsBackground = true };
        t.Start();

        var storage = new StorageService();

        if (storage.Settings.Theme == "dog")
        {
            var dogDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Styles/DogTheme.xaml")
            };
            Resources.MergedDictionaries.Add(dogDict);
        }

        var api = new ClaudeApiService();
        var usage = new UsageService(storage, api);
        var geminiProvider = new GeminiProvider();
        var geminiAccounts = new GeminiAccountService(storage, geminiProvider);
        var geminiRelay = new GeminiRelayService(storage, geminiAccounts);
        _geminiRelay = geminiRelay;
        var anthropicProvider = new AnthropicApiProvider();
        var anthropicAccounts = new AnthropicApiAccountService(storage, anthropicProvider);
        var openAiProvider = new OpenAiApiProvider();
        var openAiAccounts = new OpenAiApiAccountService(storage, openAiProvider);
        var codex = new CodexCliService();
        var grokProvider = new GrokApiProvider();
        var grokAccounts = new GrokApiAccountService(storage, grokProvider);
        var grokCli = new GrokCliService();

        _mainWindow = new MainWindow(usage, api, storage, geminiAccounts, geminiProvider,
                                     anthropicAccounts, openAiAccounts, codex,
                                     grokAccounts, grokCli, geminiRelay);
        MainWindow = _mainWindow;

        Logger.Info($"App started (v{UpdateService.CurrentVersion})");

        if (storage.Settings.GeminiRelayAutoStart)
        {
            var port = storage.Settings.ClampedGeminiRelayPort();
            if (!geminiRelay.Start(port, out var relayErr))
                Logger.Warn($"GeminiRelay auto-start failed: {relayErr}");
        }

        SetupTray();
        _mainWindow.Show();

        // 사용자 지정 앱 아이콘 적용 (윈도우/작업표시줄/트레이) — 저장된 설정값 기반
        IconHelper.ApplyToApp(storage.Settings.AppIconPath);
    }

    /// <summary>설정 다이얼로그에서 호출 — 트레이 아이콘 즉시 갱신.
    /// (Window/작업표시줄 갱신은 IconHelper.ApplyToApp 가 동시에 처리)</summary>
    public void UpdateTrayIcon(System.Drawing.Icon icon)
    {
        if (_trayIcon != null) _trayIcon.Icon = icon;
    }

    private void SetupTray()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "A.I. Usage Tracker",
            Icon = SystemIcons.Application
        };

        try
        {
            var res = GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
            if (res != null)
            {
                using var stream = res.Stream;
                _trayIcon.Icon = new Icon(stream);
            }
            else
            {
                var p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
                if (System.IO.File.Exists(p)) _trayIcon.Icon = new Icon(p);
            }
        }
        catch (Exception ex) { Logger.Warn("Tray icon load failed", ex); }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => ShowWin());
        menu.Items.Add("Refresh Now", null, (_, _) => { ShowWin(); _mainWindow?.TriggerRefresh(); });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowWin();

        _tooltipTimer.Tick += (_, _) =>
        {
            if (_trayIcon != null && _mainWindow != null)
                _trayIcon.Text = _mainWindow.GetTrayTooltip();
        };
        _tooltipTimer.Start();
    }

    private void ShowWin()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void Quit()
    {
        _mainWindow?.RealClose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    public static void ShowBalloon(string title, string text)
    {
        if (Current is App app && app._trayIcon != null)
            app._trayIcon.ShowBalloonTip(3000, title, text, System.Windows.Forms.ToolTipIcon.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _geminiRelay?.Stop(); } catch { }
        _geminiRelay = null;

        // Unblock the show-listener thread so it exits cleanly.
        if (_showEvent != null)
        {
            var ev = _showEvent;
            _showEvent = null;
            try { ev.Set(); } catch { }
            try { ev.Dispose(); } catch { }
        }

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
