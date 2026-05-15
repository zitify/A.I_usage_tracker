using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AIUsageTracker.Models;
using AIUsageTracker.Services;
using AIUsageTracker.Services.Providers;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;

namespace AIUsageTracker.Views;

public partial class MainWindow : Window
{
    private readonly UsageService _usage;
    private readonly ClaudeApiService _api;
    private readonly StorageService _storage;
    private readonly GeminiAccountService _geminiAccounts;
    private readonly GeminiProvider _geminiProvider;
    private readonly AnthropicApiAccountService _anthropicAccounts;
    private readonly OpenAiApiAccountService _openAiAccounts;
    private readonly CodexCliService _codex;
    private readonly GrokApiAccountService _grokAccounts;
    private readonly GrokCliService _grokCli;
    private readonly GeminiRelayService _geminiRelay;
    private readonly UpdateService _update = new();
    private DogAnimationController? _dogAnim;
    private bool _suppressGeminiSelection;
    private bool _suppressAnthropicSelection;
    private bool _suppressOpenAiSelection;
    private bool _suppressGrokSelection;
    private int _anthropicRangeDays = 7;
    private int _openAiRangeDays = 7;
    private int _codexRangeDays = 7;
    private int _grokCliRangeDays = 7;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly Action _onStatusChanged;
    private readonly Action _onUsageUpdated;
    private Action? _onGrokChanged;
    private Action? _onGeminiChanged;
    private Action? _onGeminiRelayStatus;
    private Action<GeminiUsageRecord>? _onGeminiRelayUsage;
    private Action? _onAnthropicChanged;
    private Action? _onOpenAiChanged;
    private Task? _updateCheckTask;
    private bool _reallyClosing;
    private bool _notified;
    private UpdateInfo? _pendingUpdate;
    private DateTimeOffset _lastForcedResetRefresh = DateTimeOffset.MinValue;

    // 7-day Gemini cost trend 의 최근 데이터 캐시 — TrendCanvas 가 리사이즈될 때
    // 데이터 재조회 없이 즉시 다시 그릴 수 있게 보관 (Update7DayTrend 가 갱신).
    private IReadOnlyList<GeminiUsageRecord>? _lastTrendRecords;

    private const int SessionTotalMs = 5 * 60 * 60 * 1000;
    private const long WeekTotalMs = 7L * 24 * 60 * 60 * 1000;
    private const int ForcedRefreshDebounceSeconds = 30;

    public MainWindow(UsageService usage, ClaudeApiService api, StorageService storage,
                       GeminiAccountService geminiAccounts, GeminiProvider geminiProvider,
                       AnthropicApiAccountService anthropicAccounts,
                       OpenAiApiAccountService openAiAccounts,
                       CodexCliService codex,
                       GrokApiAccountService grokAccounts,
                       GrokCliService grokCli,
                       GeminiRelayService geminiRelay)
    {
        _usage = usage;
        _api = api;
        _storage = storage;
        _geminiAccounts = geminiAccounts;
        _geminiProvider = geminiProvider;
        _anthropicAccounts = anthropicAccounts;
        _openAiAccounts = openAiAccounts;
        _codex = codex;
        _grokAccounts = grokAccounts;
        _grokCli = grokCli;
        _geminiRelay = geminiRelay;

        InitializeComponent();

        _onGrokChanged = () => Dispatcher.Invoke(RefreshGrokUi);
        _grokAccounts.AccountsChanged += _onGrokChanged;
        _grokAccounts.SelectedAccountChanged += _onGrokChanged;

        _onGeminiChanged = () => Dispatcher.Invoke(RefreshGeminiUi);
        _geminiAccounts.AccountsChanged += _onGeminiChanged;
        _geminiAccounts.SelectedAccountChanged += _onGeminiChanged;

        _onGeminiRelayStatus = () => Dispatcher.Invoke(RefreshGeminiRelayUi);
        _geminiRelay.StatusChanged += _onGeminiRelayStatus;
        _onGeminiRelayUsage = _ => Dispatcher.Invoke(() =>
        {
            RefreshGeminiStats();
            RefreshGeminiRelayUi();
        });
        _geminiRelay.UsageRecorded += _onGeminiRelayUsage;

        _onAnthropicChanged = () => Dispatcher.Invoke(RefreshAnthropicUi);
        _anthropicAccounts.AccountsChanged += _onAnthropicChanged;
        _anthropicAccounts.SelectedAccountChanged += _onAnthropicChanged;

        _onOpenAiChanged = () => Dispatcher.Invoke(RefreshOpenAiUi);
        _openAiAccounts.AccountsChanged += _onOpenAiChanged;
        _openAiAccounts.SelectedAccountChanged += _onOpenAiChanged;

        _onStatusChanged = () => Dispatcher.Invoke(UpdateStatus);
        _onUsageUpdated = () => Dispatcher.Invoke(UpdateUI);
        _usage.StatusChanged += _onStatusChanged;
        _usage.UsageUpdated += _onUsageUpdated;

        _pollTimer = new DispatcherTimer { Interval = CurrentPollInterval() };
        _pollTimer.Tick += async (_, _) => await Fetch();

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => Tick();
        _tickTimer.Start();

        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheckTimer.Start();

        Loaded += async (_, _) => await StartUp();
        SizeChanged += (_, _) => { if (MainTabs?.SelectedIndex == 0) RefreshGlobalUi(); };

        VersionLabel.Text = $"v{UpdateService.CurrentVersion}";

        bool isDog = (_storage.Settings.Theme ?? "dark") == "dog";
        // 테마 토글: Path Geometry 리소스 사용 (BrandIcons.xaml). 다크=좌반원, 강아지=우반원.
        ThemeToggleBtn.Content = TryFindResource(isDog ? "ChromeIconThemeDog" : "ChromeIconThemeDark");
        ApplyDogDecorations(isDog);
    }

    private bool _suppressDogToggle;

    private void ApplyDogDecorations(bool isDog)
    {
        var vis = isDog ? Visibility.Visible : Visibility.Collapsed;
        var inv = isDog ? Visibility.Collapsed : Visibility.Visible;
        DogTitleIcon.Visibility    = vis;
        DogTitlePaw.Visibility     = vis;
        NormalTitleIcon.Visibility = inv;
        DogWatermark.Visibility    = vis;
        DogAnimCanvas.Visibility   = vis;
        DogSettingsBtn.Visibility  = vis;     // 견종 선택 버튼은 강아지 모드일 때만

        // 체크박스 초기화 (저장된 enabled 목록 반영) — 초기화 중엔 핸들러 무시
        _suppressDogToggle = true;
        var enabled = new HashSet<string>(_storage.Settings.GetEnabledBreedNames(),
            StringComparer.OrdinalIgnoreCase);
        DogBreedCorgi.IsChecked  = enabled.Contains("Corgi");
        DogBreedBichon.IsChecked = enabled.Contains("Bichon");
        DogBreedGolden.IsChecked = enabled.Contains("Golden");
        DogBreedPoodle.IsChecked = enabled.Contains("Poodle");
        _suppressDogToggle = false;

        if (isDog)
        {
            // 레이아웃이 완성된 후 시작해야 카드 위치를 정확히 감지할 수 있음
            Dispatcher.InvokeAsync(StartDogAnim,
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _dogAnim?.Stop();
            _dogAnim = null;
        }
    }

    /// <summary>현재 저장된 견종 선택을 기준으로 애니메이션 재시작.
    /// 강아지 모드 ON 일 때만 호출. 빈 선택이면 캔버스가 비어있게 됨.</summary>
    private void StartDogAnim()
    {
        _dogAnim?.Stop();
        _dogAnim = new DogAnimationController(DogAnimCanvas, RootGrid);
        var breeds = ParseBreeds(_storage.Settings.GetEnabledBreedNames());
        _dogAnim.Start(breeds);
    }

    private static List<DogBreed> ParseBreeds(IReadOnlyList<string> names)
    {
        var result = new List<DogBreed>();
        foreach (var n in names)
            if (Enum.TryParse<DogBreed>(n, true, out var b) && !result.Contains(b))
                result.Add(b);
        return result;
    }

    private void DogSettingsBtn_Click(object sender, RoutedEventArgs e)
        => DogSettingsPopup.IsOpen = !DogSettingsPopup.IsOpen;

    private void DogBreed_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressDogToggle) return;
        var enabled = new List<string>();
        if (DogBreedCorgi.IsChecked  == true) enabled.Add("Corgi");
        if (DogBreedBichon.IsChecked == true) enabled.Add("Bichon");
        if (DogBreedGolden.IsChecked == true) enabled.Add("Golden");
        if (DogBreedPoodle.IsChecked == true) enabled.Add("Poodle");
        _storage.Settings.EnabledDogBreeds = enabled;
        _storage.SaveSettings(_storage.Settings);
        if (_dogAnim != null) StartDogAnim();   // 즉시 반영
    }

    private void DogBreedAll_Click(object sender, RoutedEventArgs e)
    {
        _suppressDogToggle = true;
        DogBreedCorgi.IsChecked  = true;
        DogBreedBichon.IsChecked = true;
        DogBreedGolden.IsChecked = true;
        DogBreedPoodle.IsChecked = true;
        _suppressDogToggle = false;
        DogBreed_Toggled(sender, e);   // 한 번만 저장 + 재시작
    }

    private TimeSpan CurrentPollInterval() =>
        TimeSpan.FromSeconds(_storage.Settings.ClampedPollIntervalSeconds());

    // ────────── Startup ──────────

    private async Task StartUp()
    {
        _usage.SetStatus("Loading claude.ai...", "loading");

        // Initialize hidden WebView2 (shares cookies with LoginWindow)
        await _api.InitializeAsync(BgWebView);

        // Try fetching immediately
        var result = await Fetch();

        // If needs login, open login window
        if (result == null)
            OpenLogin();

        // Check for updates in background (tracked for cleanup)
        _updateCheckTask = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var info = await _update.CheckForUpdateAsync();
        if (info != null)
        {
            _pendingUpdate = info;
            _checkUpdateBtnIsDownloadable = true;
            UpdateBtn.Visibility = Visibility.Visible;
            SetCheckUpdateBtnText($"📥 v{info.Version} 다운로드");
            App.ShowBalloon("업데이트 알림", $"새 버전 v{info.Version}이 있습니다!");
        }
    }

    // ────────── Fetch ──────────

    private async Task<bool?> Fetch()
    {
        var result = await _usage.FetchUsageAsync();
        if (result == true)
        {
            _pollTimer.Start();
            LastUpdateLabel.Text = $"Last update: {DateTime.Now:HH:mm:ss}";
            CheckNotify();
        }
        return result;
    }

    private void CheckNotify()
    {
        if (!_storage.Settings.NotifyEnabled) return;
        var threshold = _storage.Settings.ClampedNotifyThreshold();
        var l = _usage.Latest;
        // 리셋 지난 stale % 기준으로 알림 발송 안 함
        var sPct = EffectivePct(l.SessionPct, l.SessionResetAt);
        var wPct = EffectivePct(l.WeekPct, l.WeekResetAt);
        if ((sPct >= threshold || wPct >= threshold) && !_notified)
        {
            _notified = true;
            App.ShowBalloon("Claude CLI Usage Alert", $"Session: {sPct:F0}% · Week: {wPct:F0}%");
        }
        else if (sPct < threshold && wPct < threshold)
            _notified = false;
    }

    // ────────── Status ──────────

    private void UpdateStatus()
    {
        StatusLabel.Text = _usage.StatusText;
        StatusLabel.Foreground = _usage.StatusKind switch
        {
            "connected" => BR("StatusGoodBrush"),
            "loading" => BR("StatusWarnBrush"),
            "error" => BR("StatusBadBrush"),
            _ => BR("TxtSubBrush")
        };

        if (_usage.IsLoggedIn)
        {
            LoginBtn.Content = "Logout";
            LoginBtn.Style = (Style)FindResource("BtnDanger");
        }
        else
        {
            LoginBtn.Content = "Login";
            LoginBtn.Style = _usage.StatusKind == "error"
                ? (Style)FindResource("BtnDanger")
                : (Style)FindResource("BtnPrimary");
        }
    }

    // ────────── Full UI Update ──────────

    private void UpdateUI()
    {
        var l = _usage.Latest;

        // 리셋 지났으면 stale 옛 % 대신 0% 표시 — 다음 fetch에서 진짜 % 갱신
        var sessionPct = EffectivePct(l.SessionPct, l.SessionResetAt);
        var weekPct = EffectivePct(l.WeekPct, l.WeekResetAt);
        var subPct = EffectivePct(l.SubPct, l.SubResetAt);

        SetBar(UsageBar, sessionPct);
        UsagePctText.Text = $"{sessionPct:F0}%";
        UsagePctText.Foreground = UsageColor(sessionPct);

        UpdateTimeRing(l);

        WeekAllPctText.Text = $"{weekPct:F0}%";
        WeekAllPctText.Foreground = UsageColor(weekPct);
        SetBar(WeekAllBar, weekPct);
        SetMarker(WeekAllMarker, WeekAllMarkerLabel, WeekAllMarkerCanvas, l.WeekResetAt);
        WeekAllResetText.Text = FmtResetIn(l.WeekResetAt);

        SubModelTitle.Text = $"WEEKLY · {l.SubModelName.ToUpper()}";
        SubPctText.Text = $"{subPct:F0}%";
        SubPctText.Foreground = UsageColor(subPct);
        SetBar(SubBar, subPct);
        SetMarker(SubMarker, SubMarkerLabel, SubMarkerCanvas, l.SubResetAt);
        SubResetText.Text = FmtResetIn(l.SubResetAt);

        RenderDesign(l);
        RenderRoutine(l);
        RenderExtra(l.Extra);
        DrawChart();

        // Keep Global tab in sync
        if (MainTabs?.SelectedIndex == 0) RefreshGlobalUi();
    }

    // ────────── Tick (1s) ──────────

    private int _tickCounter;

    private void Tick()
    {
        // 30초마다 릴레이 UI 갱신 — 활성 클라이언트의 'X분 ago' 표시·5분 윈도우 경과 반영
        if ((++_tickCounter % 30) == 0 && _geminiRelay.IsRunning)
            RefreshGeminiRelayUi();

        var l = _usage.Latest;
        if (l.SessionResetAt == null) return;

        // 세션/주간 리셋 시각 지나면 강제 fetch — 폴링 간격을 안 기다리고 즉시 새로고침
        if (_usage.IsLoggedIn && AnyResetPassed(l)
            && (DateTimeOffset.Now - _lastForcedResetRefresh).TotalSeconds >= ForcedRefreshDebounceSeconds)
        {
            _lastForcedResetRefresh = DateTimeOffset.Now;
            _ = Fetch();
        }

        UpdateTimeRing(l);
        SetMarker(WeekAllMarker, WeekAllMarkerLabel, WeekAllMarkerCanvas, l.WeekResetAt);
        SetMarker(SubMarker, SubMarkerLabel, SubMarkerCanvas, l.SubResetAt);
        if (l.HasDesign)
            SetMarker(DesignMarker, DesignMarkerLabel, DesignMarkerCanvas, l.DesignResetAt);
    }

    private static bool IsResetPassed(string? iso) =>
        !string.IsNullOrEmpty(iso)
        && DateTimeOffset.TryParse(iso, out var dt)
        && dt <= DateTimeOffset.Now;

    private static bool AnyResetPassed(LatestUsage l) =>
        IsResetPassed(l.SessionResetAt) || IsResetPassed(l.WeekResetAt)
        || IsResetPassed(l.SubResetAt) || (l.HasDesign && IsResetPassed(l.DesignResetAt));

    /// <summary>리셋 시각이 지났으면 0% 반환 — 다음 fetch까지 stale 옛 % 가리는 용도</summary>
    private static double EffectivePct(double pct, string? resetAt) =>
        IsResetPassed(resetAt) ? 0 : pct;

    private void UpdateTimeRing(LatestUsage l)
    {
        if (l.SessionResetAt == null || !DateTimeOffset.TryParse(l.SessionResetAt, out var rst)) return;

        // 리셋 시각이 지났으면 — 새 세션 시작됨, 다음 fetch까지 "Resetting..." 표시
        if (rst <= DateTimeOffset.Now)
        {
            SessionTimeMarker.Visibility = Visibility.Collapsed;
            TimeLeftText.Text = "5h 00m";
            TimeLeftPctText.Text = " · 새 세션 시작 (새로고침 중)";
            SessionResetAtLabel.Text = "Resetting...";
            SessionPaceText.Text = "—";
            SessionBurnText.Text = "—";
            TimeLeftText.Foreground = BR("AccentBlueBrush");
            return;
        }

        var rem = (rst - DateTimeOffset.Now).TotalMilliseconds;
        var elapsedPct = Math.Clamp((SessionTotalMs - rem) / SessionTotalMs * 100, 0, 100);
        var remPct = 100 - elapsedPct;
        var usagePct = EffectivePct(l.SessionPct, l.SessionResetAt);

        SetMarker(SessionTimeMarker, SessionTimeMarkerLabel, SessionTimeMarkerCanvas, l.SessionResetAt, SessionTotalMs);
        TimeLeftText.Text = FmtRemain((long)rem);
        TimeLeftPctText.Text = " left";
        SessionResetAtLabel.Text = $"Resets at {rst.ToLocalTime():ddd HH:mm}";
        TimeLeftText.Foreground = remPct > 30 ? BR("AccentBlueBrush") : remPct > 10 ? BR("StatusWarnBrush") : BR("StatusBadBrush");

        // ── 페이스 인디케이터: 사용 % vs 시간 % 비교 ──
        // delta > 0  → 시간보다 빠르게 소진 중 (위험)
        // delta < 0  → 시간보다 느리게 소진 중 (여유)
        var delta = usagePct - elapsedPct;
        if (elapsedPct < 1)
        {
            SessionPaceText.Text = "🍃 세션 시작";
            SessionPaceText.Foreground = BR("TxtSubBrush");
        }
        else if (delta >= 15)
        {
            SessionPaceText.Text = $"🔥 빠른 소진 (+{delta:F0}%p)";
            SessionPaceText.Foreground = BR("StatusBadBrush");
        }
        else if (delta >= 5)
        {
            SessionPaceText.Text = $"⚡ 약간 빠름 (+{delta:F0}%p)";
            SessionPaceText.Foreground = BR("StatusWarnBrush");
        }
        else if (delta <= -10)
        {
            SessionPaceText.Text = $"🍃 여유 ({delta:F0}%p)";
            SessionPaceText.Foreground = BR("StatusGoodBrush");
        }
        else
        {
            SessionPaceText.Text = $"✓ 적정 페이스 ({delta:+0;-0}%p)";
            SessionPaceText.Foreground = BR("StatusGoodBrush");
        }

        // ── Burn rate: 현재 페이스가 끝까지 유지되면 종료 시점 사용 % 예측 ──
        if (elapsedPct >= 1)
        {
            var projected = Math.Min(200, usagePct / elapsedPct * 100);
            SessionBurnText.Text = $"종료시 예상 {projected:F0}%";
            SessionBurnText.Foreground = projected >= 100 ? BR("StatusBadBrush")
                                       : projected >= 80  ? BR("StatusWarnBrush")
                                                          : BR("TxtSubBrush");
        }
        else
        {
            SessionBurnText.Text = "—";
            SessionBurnText.Foreground = BR("TxtSubBrush");
        }
    }

    // ────────── Ring ──────────

    private static void SetRing(PathFigure fig, ArcSegment arc, Path path, double pct,
        SolidColorBrush brush, bool isUsage)
    {
        pct = Math.Clamp(pct, 0, 100);
        if (pct < 0.5) { path.Visibility = Visibility.Collapsed; return; }
        path.Visibility = Visibility.Visible;

        var angle = Math.Min(pct / 100.0 * 360.0, 359.99);
        var rad = angle * Math.PI / 180.0;
        const double cx = 100, cy = 100, r = 86;

        fig.StartPoint = new Point(cx, cy - r);
        arc.Point = new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
        arc.Size = new Size(r, r);
        arc.IsLargeArc = angle > 180;

        if (isUsage)
            brush.Color = pct >= 90 ? CR("StatusBadBrush") : pct >= 70 ? CR("StatusWarnBrush") : CR("StatusGoodBrush");
    }

    // ────────── Bar / Marker ──────────

    private static void SetBar(Border bar, double pct)
    {
        if (bar.Parent is not Grid g || g.ActualWidth <= 0) return;
        bar.Width = g.ActualWidth * Math.Clamp(pct, 0, 100) / 100.0;
        bar.Background = UsageColor(pct);
        bar.ToolTip = $"{pct:F1}% 사용";
    }

    // 주간 카드 호환 오버로드 — 라벨이 있고 totalMs는 한 주
    private static void SetMarker(Grid marker, TextBlock label, Canvas canvas, string? iso)
        => SetMarker(marker, label, canvas, iso, WeekTotalMs);

    /// <summary>리셋 시각 ISO와 윈도우 길이(ms)를 받아 marker 위치를 갱신.
    /// label이 null이면 텍스트는 안 건드림 (세션 카드처럼 외부에 별도 텍스트가 있을 때).</summary>
    private static void SetMarker(Grid marker, TextBlock? label, Canvas canvas, string? iso, double totalMs)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, out var rst))
        { marker.Visibility = Visibility.Collapsed; return; }

        marker.Visibility = Visibility.Visible;
        var rem = Math.Max(0, (rst - DateTimeOffset.Now).TotalMilliseconds);
        var elapsed = Math.Max(0, totalMs - rem);
        var pct = Math.Min(100, elapsed / totalMs * 100);
        var w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 300;

        // 마커의 세로선(2px) 중심이 정확히 pct 위치에 오도록 정렬.
        // 배지(라벨 박스)가 캔버스 오른쪽 밖으로 살짝 나가더라도 ClipToBounds=False 이므로 그대로 보임 —
        // 100% 일 때 라인이 88~95% 위치에 갇혀 보이던 우측 클램프 제거.
        marker.UpdateLayout();
        var halfW = marker.ActualWidth / 2;
        var left = w * pct / 100.0 - halfW;
        // 좌측만 음수 방지 (0% 에서 마커가 캔버스 왼쪽 밖으로 빠지는 것만 막음)
        if (left < -halfW) left = -halfW;
        Canvas.SetLeft(marker, left);
        if (label != null) label.Text = $"{pct:F0}%";

        // 호버 툴팁 — 정확한 pct + 리셋 잔여 시간
        marker.ToolTip = $"{pct:F1}% · 리셋 {FmtResetIn(iso)}";
    }

    // ────────── Claude Design ──────────

    private void RenderDesign(LatestUsage l)
    {
        if (!l.HasDesign)
        {
            DesignCard.Opacity = 0.4;
            DesignPctText.Text = "--";
            DesignPctText.Foreground = BR("TxtHintBrush");
            DesignBar.Width = 0;
            DesignMarker.Visibility = Visibility.Collapsed;
            DesignResetText.Text = "Not available";
            return;
        }
        DesignCard.Opacity = 1;
        var dPct = EffectivePct(l.DesignPct, l.DesignResetAt);
        DesignPctText.Text = $"{dPct:F0}%";
        DesignPctText.Foreground = UsageColor(dPct);
        SetBar(DesignBar, dPct);
        SetMarker(DesignMarker, DesignMarkerLabel, DesignMarkerCanvas, l.DesignResetAt);
        DesignResetText.Text = FmtResetIn(l.DesignResetAt);
    }

    // ────────── Daily Routine ──────────

    private void RenderRoutine(LatestUsage l)
    {
        if (!l.HasRoutine)
        {
            RoutineCard.Opacity = 0.4;
            RoutineUsedText.Text = "--";
            RoutineLimitText.Text = "/ --";
            RoutineResetText.Text = "Not available";
            return;
        }
        RoutineCard.Opacity = 1;
        RoutineUsedText.Text = l.RoutineUsed.ToString();
        RoutineLimitText.Text = $"/ {l.RoutineLimit}";

        var pct = l.RoutineLimit > 0 ? (double)l.RoutineUsed / l.RoutineLimit * 100 : 0;
        RoutineUsedText.Foreground = UsageColor(pct);

        RoutineResetText.Text = string.IsNullOrEmpty(l.RoutineResetAt)
            ? "Daily limit"
            : FmtResetIn(l.RoutineResetAt);
    }

    // ────────── Extra Usage ──────────

    private void RenderExtra(ExtraUsage? ex)
    {
        if (ex == null || !ex.IsEnabled)
        {
            ExtraCard.Opacity = 0.5;
            ExtraUsedText.Text = "$0.00";
            ExtraLimitText.Text = "of $0.00";
            ExtraPctText.Text = "0%";
            ExtraDisabledText.Visibility = ex?.IsEnabled == false ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        ExtraCard.Opacity = 1;
        ExtraDisabledText.Visibility = Visibility.Collapsed;
        var used = (ex.UsedCredits ?? 0) / 100.0;
        var limit = (ex.MonthlyLimit ?? 0) / 100.0;
        var pct = Math.Round(ex.Utilization ?? 0);
        ExtraUsedText.Text = $"${used:F2}";
        ExtraLimitText.Text = $"of ${limit:F2}";
        ExtraPctText.Text = $"{pct:F0}%";
        if (ExtraBar.Parent is Grid g && g.ActualWidth > 0)
            ExtraBar.Width = g.ActualWidth * Math.Min(100, pct) / 100.0;
    }

    // ────────── Delta Chart ──────────

    private void DrawChart()
    {
        var hist = _usage.GetHistory();
        DeltaChartCanvas.Children.Clear();

        if (hist.Count < 2)
        {
            DeltaEmptyText.Text = $"Collecting data... {hist.Count} snapshot(s) so far";
            DeltaEmptyText.Visibility = Visibility.Visible;
            return;
        }
        DeltaEmptyText.Visibility = Visibility.Collapsed;

        var recent = hist.TakeLast(61).ToList();
        var points = new List<(double delta, long ts)>();
        for (int i = 1; i < recent.Count; i++)
        {
            var diff = recent[i].FiveHourUtilization - recent[i - 1].FiveHourUtilization;
            points.Add((Math.Max(0, diff), recent[i].Timestamp));
        }
        if (points.Count < 2) return;

        var cw = DeltaChartCanvas.ActualWidth;
        var ch = DeltaChartCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        double left = 32, right = 8, top = 4, bottom = 18;
        var plotW = cw - left - right;
        var plotH = ch - top - bottom;

        var maxPct = Math.Max(5, points.Max(p => p.delta));
        maxPct = maxPct <= 5 ? 5 : maxPct <= 25 ? Math.Ceiling(maxPct / 5) * 5 : Math.Ceiling(maxPct / 10) * 10;

        // Grid lines
        var gridStep = maxPct <= 20 ? 5.0 : maxPct <= 50 ? 10.0 : 20.0;
        for (double y = 0; y <= maxPct; y += gridStep)
        {
            var py = top + plotH - (y / maxPct * plotH);
            DeltaChartCanvas.Children.Add(MkText($"{y:F0}%", 0, py - 6, 8, BR("TxtHintBrush")));
            DeltaChartCanvas.Children.Add(new Line
            {
                X1 = left, X2 = cw - right, Y1 = py, Y2 = py,
                Stroke = BR("BorderBrushBase"), StrokeThickness = 0.5
            });
        }

        // Build polyline points
        var linePoints = new PointCollection();
        var fillPoints = new PointCollection();
        var baseY = top + plotH;

        for (int i = 0; i < points.Count; i++)
        {
            var x = left + (plotW * i / (points.Count - 1));
            var y = top + plotH - (points[i].delta / maxPct * plotH);
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        // Gradient fill under the line
        fillPoints.Add(new Point(left + plotW, baseY));
        fillPoints.Add(new Point(left, baseY));

        var fillPolygon = new Polygon
        {
            Points = fillPoints,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(WithAlpha(CR("StatusGoodBrush"), 60), 0),
                    new(WithAlpha(CR("StatusGoodBrush"), 5),  1)
                }, 90)
        };
        DeltaChartCanvas.Children.Add(fillPolygon);

        // Main line
        var polyline = new Polyline
        {
            Points = linePoints,
            Stroke = BR("StatusGoodBrush"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        DeltaChartCanvas.Children.Add(polyline);

        // Dots at each data point
        for (int i = 0; i < linePoints.Count; i++)
        {
            var pt = linePoints[i];
            var val = points[i].delta;
            var dotColor = val >= 15 ? "#f87171" : val >= 8 ? "#facc15" : "#4ade80";
            var dot = new Ellipse
            {
                Width = 5, Height = 5,
                Fill = B(dotColor)
            };
            Canvas.SetLeft(dot, pt.X - 2.5);
            Canvas.SetTop(dot, pt.Y - 2.5);
            DeltaChartCanvas.Children.Add(dot);
        }

        // Time labels
        var t0 = DateTimeOffset.FromUnixTimeMilliseconds(points[0].ts).ToLocalTime();
        var t1 = DateTimeOffset.FromUnixTimeMilliseconds(points[^1].ts).ToLocalTime();
        DeltaChartCanvas.Children.Add(MkText(t0.ToString("HH:mm"), left, ch - 14, 8, BR("TxtHintBrush")));
        DeltaChartCanvas.Children.Add(MkText(t1.ToString("HH:mm"), cw - right - 28, ch - 14, 8, BR("TxtHintBrush")));

        // Latest value label
        var lastPt = linePoints[^1];
        var lastVal = points[^1].delta;
        DeltaChartCanvas.Children.Add(MkText($"+{lastVal:F1}%", lastPt.X + 4, lastPt.Y - 6, 9,
            BR(lastVal >= 15 ? "StatusBadBrush" : lastVal >= 8 ? "StatusWarnBrush" : "StatusGoodBrush")));
    }

    private void DeltaChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>7-day trend 캔버스 크기 변경 시 캐시된 데이터로 다시 그림 — 첫 레이아웃에서
    /// ActualWidth=0 → 480 폴백 → 실제 크기 확정 후 정렬 안 맞던 짤림 문제 해결.</summary>
    private void TrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lastTrendRecords != null) Update7DayTrend(_lastTrendRecords);
    }

    // ────────── Login ──────────

    private async void OpenLogin()
    {
        var loginWin = new LoginWindow { Owner = this };
        loginWin.ShowDialog();

        // If LoginWindow already fetched usage data successfully, use it directly
        if (loginWin.LoginSuccess && !string.IsNullOrEmpty(loginWin.FetchResultJson))
        {
            if (_usage.ProcessRawFetchResult(loginWin.FetchResultJson))
            {
                LastUpdateLabel.Text = $"Last update: {DateTime.Now:HH:mm:ss}";
                CheckNotify();

                _ = Dispatcher.InvokeAsync(async () => await _api.ReloadAsync());
                _pollTimer.Start();
                return;
            }
        }

        // Fallback: reload hidden WebView and retry fetch
        _usage.SetStatus("Reloading...", "loading");
        await _api.ReloadAsync();

        for (int i = 0; i < 3; i++)
        {
            var result = await Fetch();
            if (result == true) return;
            await Task.Delay(2000);
            if (i < 2) await _api.ReloadAsync();
        }
    }

    // ────────── Events ──────────

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;

        void SetBtnText(string text)
        {
            StatusLabel.Text = text;
            StatusLabel.Foreground = BR("StatusWarnBrush");
        }

        SetBtnText("최신 버전 확인 중...");

        var fresh = await _update.CheckForUpdateAsync();
        if (fresh == null)
        {
            _pendingUpdate = null;
            UpdateBtn.Visibility = Visibility.Collapsed;
            SetBtnText("이미 최신 버전입니다");
            return;
        }

        _pendingUpdate = fresh;
        SetBtnText($"v{fresh.Version} 다운로드 중...");

        var success = await _update.DownloadAndInstallAsync(fresh, pct =>
        {
            Dispatcher.Invoke(() => SetBtnText($"다운로드 중... {pct}%"));
        });

        if (success)
        {
            SetBtnText("설치 프로그램 실행됨. 앱을 종료합니다...");
            await Task.Delay(1500);
            _reallyClosing = true;
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            SetBtnText("다운로드 실패");
            UpdateBtn.IsEnabled = true;
        }
    }

    private void OpenClaudeBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://claude.ai") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn("OpenClaudeBtn_Click failed", ex); }
    }

    private bool _checkUpdateBtnIsDownloadable;

    private void SetCheckUpdateBtnText(string text)
    {
        if (CheckUpdateBtn.Template.FindName("CheckUpdateBtnText", CheckUpdateBtn) is TextBlock tb)
            tb.Text = text;
    }

    private void SetCheckUpdateBtnProgress(int pct)
    {
        if (CheckUpdateBtn.Template.FindName("ProgressFill", CheckUpdateBtn) is Border fill)
        {
            var w = CheckUpdateBtn.ActualWidth;
            if (w <= 0) return;
            fill.Width = w * Math.Clamp(pct, 0, 100) / 100.0;
        }
    }

    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        // 2번째 클릭 — 이미 발견한 업데이트를 다운로드
        if (_checkUpdateBtnIsDownloadable && _pendingUpdate != null)
        {
            await DownloadPendingUpdateAsync(_pendingUpdate);
            return;
        }

        CheckUpdateBtn.IsEnabled = false;
        SetCheckUpdateBtnText("🔄 확인 중...");

        var info = await _update.CheckForUpdateAsync();
        if (info != null)
        {
            _pendingUpdate = info;
            _checkUpdateBtnIsDownloadable = true;
            UpdateBtn.Visibility = Visibility.Visible;
            SetCheckUpdateBtnText($"📥 v{info.Version} 다운로드");
        }
        else
        {
            SetCheckUpdateBtnText("✓ 최신 버전");
            await Task.Delay(2000);
            SetCheckUpdateBtnText("🔄 업데이트");
        }
        CheckUpdateBtn.IsEnabled = true;
    }

    private async Task DownloadPendingUpdateAsync(UpdateInfo info)
    {
        CheckUpdateBtn.IsEnabled = false;
        SetCheckUpdateBtnText($"다운로드 중... 0%");
        SetCheckUpdateBtnProgress(0);

        var success = await _update.DownloadAndInstallAsync(info, pct =>
        {
            Dispatcher.Invoke(() =>
            {
                SetCheckUpdateBtnText($"다운로드 중... {pct}%");
                SetCheckUpdateBtnProgress(pct);
            });
        });

        if (success)
        {
            SetCheckUpdateBtnText("설치 실행됨, 앱 종료 중...");
            SetCheckUpdateBtnProgress(100);
            await Task.Delay(1500);
            _reallyClosing = true;
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            SetCheckUpdateBtnText("✗ 다운로드 실패 — 재시도");
            SetCheckUpdateBtnProgress(0);
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private void AppIconSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AppIconSettingsDialog(_storage) { Owner = this };
        dlg.ShowDialog();
    }

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        var cur = _storage.Settings.Theme ?? "dark";
        var next = cur == "dog" ? "dark" : "dog";
        _storage.Settings.Theme = next;
        _storage.SaveSettings(_storage.Settings);
        ThemeToggleBtn.Content = TryFindResource(next == "dog" ? "ChromeIconThemeDog" : "ChromeIconThemeDark");
        System.Windows.MessageBox.Show("테마가 변경되었습니다.\n다음 실행 시 적용됩니다.",
                        "테마 변경", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TopMostBtn_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        // 글리프 자체는 그대로(⌃), 색만 토글: 활성 시 노랑 액센트, 비활성 시 타이틀바 기본색
        TopMostBtn.Foreground = Topmost ? BR("AccentYellowBrush") : BR("TitleBarTextBrush");
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MaxBtn.Content = "❐";
            MaxBtn.ToolTip = "이전 크기로 복원";
            RootBorder.BorderThickness = new Thickness(0);
            RootGrid.Margin = new Thickness(7);
        }
        else
        {
            MaxBtn.Content = "▢";
            MaxBtn.ToolTip = "최대화";
            RootBorder.BorderThickness = new Thickness(1);
            RootGrid.Margin = new Thickness(0);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            AdjustMaximizedBounds(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var work = info.rcWork;
        var full = info.rcMonitor;
        mmi.ptMaxPosition.x = Math.Abs(work.left - full.left);
        mmi.ptMaxPosition.y = Math.Abs(work.top - full.top);
        mmi.ptMaxSize.x = Math.Abs(work.right - work.left);
        mmi.ptMaxSize.y = Math.Abs(work.bottom - work.top);
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await Fetch();

    private async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_usage.IsLoggedIn)
        {
            // Logout: clear WebView2 cookies and reset state
            _pollTimer.Stop();
            _usage.SetStatus("Logging out...", "loading");

            if (BgWebView.CoreWebView2 != null)
            {
                var cookieManager = BgWebView.CoreWebView2.CookieManager;
                cookieManager.DeleteAllCookies();
            }

            _usage.Logout();
            UpdateUI();

            // Reload BgWebView with cleared cookies
            await _api.ReloadAsync();

            // Open login window for new account
            OpenLogin();
        }
        else
        {
            OpenLogin();
        }
    }

    private void CreditLink_Click(object sender, MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://zitify.co.kr") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn("CreditLink_Click failed", ex); }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClosing) { e.Cancel = true; Hide(); }
    }

    public void RealClose()
    {
        _dogAnim?.Stop();
        _dogAnim = null;
        _reallyClosing = true;
        _pollTimer.Stop();
        _tickTimer.Stop();
        _updateCheckTimer.Stop();
        _usage.StatusChanged -= _onStatusChanged;
        _usage.UsageUpdated -= _onUsageUpdated;
        _grokAccounts.AccountsChanged -= _onGrokChanged;
        _grokAccounts.SelectedAccountChanged -= _onGrokChanged;
        _geminiAccounts.AccountsChanged -= _onGeminiChanged;
        _geminiAccounts.SelectedAccountChanged -= _onGeminiChanged;
        _geminiRelay.StatusChanged -= _onGeminiRelayStatus;
        _geminiRelay.UsageRecorded -= _onGeminiRelayUsage;
        _anthropicAccounts.AccountsChanged -= _onAnthropicChanged;
        _anthropicAccounts.SelectedAccountChanged -= _onAnthropicChanged;
        _openAiAccounts.AccountsChanged -= _onOpenAiChanged;
        _openAiAccounts.SelectedAccountChanged -= _onOpenAiChanged;
        Close();
    }

    public void TriggerRefresh() => Dispatcher.InvokeAsync(async () => await Fetch());

    public string GetTrayTooltip()
    {
        var l = _usage.Latest;
        var sPct = EffectivePct(l.SessionPct, l.SessionResetAt);
        var wPct = EffectivePct(l.WeekPct, l.WeekResetAt);
        return $"A.I. Usage Tracker\nClaude CLI · Session: {sPct:F0}% · Week: {wPct:F0}%";
    }

    // ────────── Helpers ──────────

    private static Color C(string hex) =>
        (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush B(string hex) => new(C(hex));

    /// <summary>Theme-aware brush lookup — uses current theme's resource so dog mode gets darker tones.</summary>
    private static SolidColorBrush BR(string key) =>
        System.Windows.Application.Current.Resources[key] as SolidColorBrush ?? B("#888");

    /// <summary>Theme-aware color lookup — same as BR but returns Color for ColorAnimation/SolidColorBrush.Color.</summary>
    private static Color CR(string key) =>
        (System.Windows.Application.Current.Resources[key] as SolidColorBrush)?.Color ?? C("#888");

    /// <summary>현재 색상에 알파 채널을 덧입혀 반환 (그라디언트·반투명 오버레이용).</summary>
    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static SolidColorBrush UsageColor(double pct)
    {
        var key = pct >= 90 ? "StatusBadBrush" : pct >= 70 ? "StatusWarnBrush" : "StatusGoodBrush";
        return System.Windows.Application.Current.Resources[key] as SolidColorBrush ?? BR("StatusGoodBrush");
    }

    private static string FmtRemain(long ms)
    {
        if (ms <= 0) return "0:00";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m" : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private static string FmtResetIn(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, out var dt)) return "Resets in --";
        var r = dt - DateTimeOffset.Now;
        if (r.TotalMilliseconds <= 0) return "Resetting...";
        if (r.TotalDays >= 1) return $"Resets in {(int)r.TotalDays}d {r.Hours}h";
        if (r.TotalHours >= 1) return $"Resets in {(int)r.TotalHours}h {r.Minutes}m";
        return $"Resets in {r.Minutes}m";
    }

    private static TextBlock MkText(string text, double x, double y, double size, SolidColorBrush brush)
    {
        var tb = new TextBlock { Text = text, FontSize = size, Foreground = brush };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        return tb;
    }

    // ────────── Gemini Tab ──────────

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not System.Windows.Controls.TabControl) return;
        switch (MainTabs?.SelectedIndex)
        {
            case 0: RefreshGlobalUi(); break;    // Global
            case 2: RefreshAnthropicUi(); break; // Claude API
            case 3: RefreshGeminiUi(); break;    // Gemini API
            case 4: RefreshOpenAiUi(); break;    // OpenAI API
            case 5: RefreshCodexUi(); break;     // OpenAI CLI
            case 6: RefreshGrokUi(); break;      // Grok API
            case 7: RefreshGrokCliUi(); break;   // Grok CLI
        }
    }

    private void GlobalRefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshGlobalUi();

    private async void RefreshGlobalUi()
    {
        var now = DateTimeOffset.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var dayOfMonth = now.Day;

        long TsMs(DateTime d) => new DateTimeOffset(d, now.Offset).ToUnixTimeMilliseconds();

        var all = _storage.GetGeminiUsageHistory();
        var oaHist = _storage.GetOpenAiApiUsageHistory();
        var antHist = _storage.GetAnthropicApiUsageHistory();
        var accounts = _geminiAccounts.GetAccounts();

        // Codex(CLI) 일별 비용 — rollout JSONL 파싱이 무거우니 백그라운드 위임
        var codexSince = new DateTimeOffset(monthStart.AddDays(-7), now.Offset); // 트렌드용 7일 여유 포함
        var codexDailyCost = await Task.Run(() => _codex.GetCostByDay(codexSince));
        if (HeroTodayCost == null) return; // 비동기 대기 중 unload 방어

        // 헬퍼: 한 윈도우의 모든 프로바이더 비용 합산
        double SumWindow(DateTime start, DateTime end)
        {
            var s = TsMs(start); var e = TsMs(end);
            var g = all.Where(r => r.Timestamp >= s && r.Timestamp < e).Sum(r => r.CostUsd);
            var o = oaHist.Where(r => r.Timestamp >= s && r.Timestamp < e).Sum(r => r.CostUsd);
            var a = antHist.Where(r => r.Timestamp >= s && r.Timestamp < e).Sum(r => r.CostUsd);
            var c = codexDailyCost.Where(kv => kv.Key >= start && kv.Key < end).Sum(kv => kv.Value);
            return g + o + a + c;
        }

        // ─── Row 1: Hero tiles (전체 프로바이더 통합) ───
        var todayCost = SumWindow(today, today.AddDays(1));
        var yesterdayCost = SumWindow(yesterday, today);
        var monthRecs = all.Where(r => r.Timestamp >= TsMs(monthStart) && r.Timestamp < TsMs(monthEnd)).ToList();
        var monthCost = SumWindow(monthStart, monthEnd);

        HeroTodayCost.Text = $"${todayCost:F2}";
        if (yesterdayCost > 0 || todayCost > 0)
        {
            var delta = todayCost - yesterdayCost;
            var sign = delta >= 0 ? "+" : "−";
            HeroTodayDelta.Text = $"yesterday ${yesterdayCost:F2} · {sign}${Math.Abs(delta):F2}";
            HeroTodayDelta.Foreground = delta > 0 ? BR("StatusBadBrush") : delta < 0 ? BR("StatusGoodBrush") : BR("TxtSubBrush");
        }
        else
        {
            HeroTodayDelta.Text = "no usage yet";
            HeroTodayDelta.Foreground = BR("TxtSubBrush");
        }

        HeroMonthCost.Text = $"${monthCost:F2}";
        var totalMonthlyBudget = accounts.Sum(a => a.MonthlyBudgetUsd);
        var daysRemaining = daysInMonth - dayOfMonth + 1;

        if (totalMonthlyBudget > 0)
        {
            var pct = monthCost / totalMonthlyBudget;
            SetRatioBar(HeroMonthBarFill, pct, BudgetColor(pct));
            HeroMonthSub.Text = $"${monthCost:F2} / ${totalMonthlyBudget:F2} ({pct:P0}) · {daysRemaining}d left";
            HeroMonthBarFill.ToolTip = $"이번 달 ${monthCost:F2} / ${totalMonthlyBudget:F2} · {pct:P1}";
        }
        else
        {
            HeroMonthBarFill.Width = 0;
            HeroMonthSub.Text = $"no budget set · {daysRemaining}d left";
            HeroMonthBarFill.ToolTip = $"이번 달 ${monthCost:F2} · 예산 미설정";
        }

        var avgPerDay = dayOfMonth > 0 ? monthCost / dayOfMonth : 0;
        var projected = avgPerDay * daysInMonth;
        HeroProjectedCost.Text = $"${projected:F2}";
        if (totalMonthlyBudget > 0)
        {
            var projPct = projected / totalMonthlyBudget;
            HeroProjectedCost.Foreground = BudgetColor(projPct);
            HeroProjectedSub.Text = $"~${avgPerDay:F2}/day · {projPct:P0} of budget";
        }
        else
        {
            HeroProjectedCost.Foreground = BR("TxtHighlightBrush");
            HeroProjectedSub.Text = avgPerDay > 0 ? $"based on ${avgPerDay:F2}/day avg" : "no usage yet";
        }

        // ─── Row 2: Claude quota — 가로 막대 + 시간 마커 (CLI 탭 5h 세션 카드와 동일 패턴) ───
        var l = _usage.Latest;
        var gSessionPct = EffectivePct(l.SessionPct, l.SessionResetAt);
        var gWeekPct = EffectivePct(l.WeekPct, l.WeekResetAt);

        SetBar(GSessionBar, gSessionPct);
        SetMarker(GSessionMarker, GSessionMarkerLabel, GSessionMarkerCanvas, l.SessionResetAt, SessionTotalMs);
        ClaudeSessionText.Text = $"{gSessionPct:F0}%";
        ClaudeSessionText.Foreground = UsageColor(gSessionPct);
        ClaudeSessionReset.Text = FmtResetIn(l.SessionResetAt);

        SetBar(GWeekBar, gWeekPct);
        SetMarker(GWeekMarker, GWeekMarkerLabel, GWeekMarkerCanvas, l.WeekResetAt);   // WeekTotalMs 기본
        ClaudeWeekText.Text = $"{gWeekPct:F0}%";
        ClaudeWeekText.Foreground = UsageColor(gWeekPct);
        ClaudeWeekReset.Text = FmtResetIn(l.WeekResetAt);

        // ─── Row 2: Gemini budgets ───
        var dayStartMs = TsMs(today);
        var todayByAcc = all.Where(r => r.Timestamp >= dayStartMs)
            .GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CostUsd));
        var monthByAcc = monthRecs.GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CostUsd));

        double budgetBarMax = 240;
        var budgetsContainer = GeminiBudgetsList.Parent as FrameworkElement;
        if (budgetsContainer != null && budgetsContainer.ActualWidth > 40)
            budgetBarMax = Math.Max(120, budgetsContainer.ActualWidth - 20);

        var budgetRows = accounts.Select(a => new GeminiBudgetRow(
            a,
            todayByAcc.TryGetValue(a.Id, out var d) ? d : 0,
            monthByAcc.TryGetValue(a.Id, out var m) ? m : 0,
            budgetBarMax
        )).ToList();
        GeminiBudgetsList.ItemsSource = budgetRows;
        GeminiBudgetsEmpty.Visibility = budgetRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // ─── OpenAI · 비용 카드 (Codex CLI + OpenAI API 합산) ───
        var oaTodayApi = oaHist.Where(r => r.Timestamp >= TsMs(today) && r.Timestamp < TsMs(today.AddDays(1))).Sum(r => r.CostUsd);
        var oaTodayCli = codexDailyCost.TryGetValue(today, out var cliToday) ? cliToday : 0;
        var oaTodayTotal = oaTodayApi + oaTodayCli;
        var oaMonthApi = oaHist.Where(r => r.Timestamp >= TsMs(monthStart) && r.Timestamp < TsMs(monthEnd)).Sum(r => r.CostUsd);
        var oaMonthCli = codexDailyCost.Where(kv => kv.Key >= monthStart && kv.Key < monthEnd).Sum(kv => kv.Value);
        var oaMonthTotal = oaMonthApi + oaMonthCli;
        OpenAiTodayText.Text = $"${oaTodayTotal:F2}";
        OpenAiMonthText.Text = $"${oaMonthTotal:F2}";
        OpenAiBreakdown.Text = $"이번 달 — CLI ${oaMonthCli:F2} · API ${oaMonthApi:F2}";
        _openAiMiniDaily = BuildOpenAiMiniSeries(today, codexDailyCost, oaHist);
        DrawOpenAiMini();

        // ─── Row 3: Trend (7-day) ───
        Update7DayTrend(all);

        // ─── Row 3: Top models (전체 프로바이더 통합) ───
        var modelCosts = new Dictionary<string, double>();
        foreach (var r in monthRecs)
            modelCosts[r.Model] = modelCosts.GetValueOrDefault(r.Model) + r.CostUsd;
        foreach (var r in oaHist.Where(r => r.Timestamp >= TsMs(monthStart) && r.Timestamp < TsMs(monthEnd)))
            modelCosts[r.Model] = modelCosts.GetValueOrDefault(r.Model) + r.CostUsd;
        foreach (var r in antHist.Where(r => r.Timestamp >= TsMs(monthStart) && r.Timestamp < TsMs(monthEnd)))
            modelCosts[r.Model] = modelCosts.GetValueOrDefault(r.Model) + r.CostUsd;
        // Codex 월간 모델 분포
        var codexMonthSum = await Task.Run(() => _codex.Aggregate(new DateTimeOffset(monthStart, now.Offset)));
        if (HeroTodayCost == null) return;
        foreach (var m in codexMonthSum.Models)
            modelCosts[m.Model] = modelCosts.GetValueOrDefault(m.Model) + m.Cost;

        var byModel = modelCosts
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => new { Model = kv.Key, Cost = kv.Value })
            .ToList();
        var topMax = byModel.FirstOrDefault()?.Cost ?? 1;
        if (topMax <= 0) topMax = 1;
        var totalMonth = monthRecs.Sum(r => r.CostUsd);
        if (totalMonth <= 0) totalMonth = 1;

        double topBarMax = 140;
        var topContainer = TopModelsList.Parent as FrameworkElement;
        if (topContainer != null && topContainer.ActualWidth > 40)
            topBarMax = Math.Max(80, topContainer.ActualWidth - 90);

        var topRows = byModel.Select(x => new TopModelRow(
            x.Model, x.Cost, x.Cost / topMax * topBarMax, x.Cost / totalMonth)).ToList();
        TopModelsList.ItemsSource = topRows;
        TopModelsEmpty.Visibility = topRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── OpenAI 미니 7일 추세 (Codex CLI + OpenAI API 합산) ───
    private double[] _openAiMiniDaily = new double[7];

    private double[] BuildOpenAiMiniSeries(DateTime today, Dictionary<DateTime, double> codexDaily,
        IReadOnlyList<OpenAiApiUsageSnapshot> oaHist)
    {
        var series = new double[7];
        for (int i = 0; i < 7; i++)
        {
            var d = today.AddDays(-6 + i);
            var dStart = new DateTimeOffset(d, DateTimeOffset.Now.Offset).ToUnixTimeMilliseconds();
            var dEnd = new DateTimeOffset(d.AddDays(1), DateTimeOffset.Now.Offset).ToUnixTimeMilliseconds();
            var api = oaHist.Where(r => r.Timestamp >= dStart && r.Timestamp < dEnd).Sum(r => r.CostUsd);
            var cli = codexDaily.TryGetValue(d, out var c) ? c : 0;
            series[i] = api + cli;
        }
        return series;
    }

    private void OpenAiMiniCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawOpenAiMini();

    private void DrawOpenAiMini()
    {
        if (OpenAiMiniCanvas == null) return;
        var canvas = OpenAiMiniCanvas;
        canvas.Children.Clear();
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var series = _openAiMiniDaily;
        var max = Math.Max(0.01, series.Max());
        var lineBrush = (System.Windows.Media.Brush)FindResource("OpenAiBrandBrush");
        var fillBrush = lineBrush.Clone();
        if (fillBrush is System.Windows.Media.SolidColorBrush sb) sb.Opacity = 0.18;

        var points = new System.Windows.Media.PointCollection(7);
        for (int i = 0; i < 7; i++)
        {
            var x = (w / 6.0) * i;
            var y = h - (series[i] / max) * (h - 4) - 2;
            points.Add(new System.Windows.Point(x, y));
        }

        var polyPoints = new System.Windows.Media.PointCollection(points) {
            new System.Windows.Point(w, h),
            new System.Windows.Point(0, h),
        };
        canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = polyPoints, Fill = fillBrush });
        canvas.Children.Add(new System.Windows.Shapes.Polyline
        {
            Points = points, Stroke = lineBrush, StrokeThickness = 1.5,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
        });

        // 데이터 포인트 + 툴팁
        var today = DateTime.Now.Date;
        for (int i = 0; i < 7; i++)
        {
            var pt = points[i];
            var d = today.AddDays(-6 + i);
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 6, Height = 6,
                Fill = lineBrush,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1,
                ToolTip = $"{d:MM/dd}: ${series[i]:F2}",
            };
            Canvas.SetLeft(dot, pt.X - 3);
            Canvas.SetTop(dot, pt.Y - 3);
            canvas.Children.Add(dot);
        }
    }

    private static void SetRatioBar(Border bar, double ratio, SolidColorBrush color)
    {
        if (bar.Parent is not Grid g || g.ActualWidth <= 0)
        {
            bar.Width = 0;
            bar.Background = color;
            return;
        }
        bar.Width = Math.Max(0, Math.Min(1.0, ratio)) * g.ActualWidth;
        bar.Background = color;
    }

    private static SolidColorBrush BudgetColor(double pct)
    {
        if (pct >= 1.0) return BR("StatusBadBrush");
        if (pct >= 0.8) return BR("StatusHighBrush");
        if (pct >= 0.6) return BR("StatusWarnBrush");
        return BR("StatusGoodBrush");
    }

    private static string ModelFamily(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (m.Contains("flash")) return "Flash";
        if (m.Contains("pro")) return "Pro";
        return "Other";
    }

    private static readonly Dictionary<string, string> FamilyColors = new()
    {
        ["Flash"] = "#4F7CE8",
        ["Pro"] = "#9B72CB",
        ["Other"] = "#64748b"
    };

    private void Update7DayTrend(IReadOnlyList<GeminiUsageRecord> all)
    {
        _lastTrendRecords = all;
        TrendCanvas.Children.Clear();
        TrendLegend.Children.Clear();

        var today = DateTimeOffset.Now.Date;
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToList();

        var grid = days.ToDictionary(
            d => d,
            _ => new Dictionary<string, double> { ["Flash"] = 0, ["Pro"] = 0, ["Other"] = 0 });

        foreach (var r in all)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().Date;
            if (!grid.ContainsKey(dt)) continue;
            grid[dt][ModelFamily(r.Model)] += r.CostUsd;
        }

        var dayLabels = new[] { TrendDay0, TrendDay1, TrendDay2, TrendDay3, TrendDay4, TrendDay5, TrendDay6 };
        for (int i = 0; i < 7; i++)
            dayLabels[i].Text = days[i].ToString("M/d");

        var dayTotals = days.Select(d => grid[d].Values.Sum()).ToList();
        var trendTotal = dayTotals.Sum();
        TrendTotalLabel.Text = $"7d · ${trendTotal:F2}";

        foreach (var fam in new[] { "Flash", "Pro", "Other" })
        {
            if (grid.Values.Any(x => x[fam] > 0))
                AddLegendItem(fam, FamilyColors[fam]);
        }

        var canvasWidth = TrendCanvas.ActualWidth > 0 ? TrendCanvas.ActualWidth : 480;
        var canvasHeight = TrendCanvas.ActualHeight > 0 ? TrendCanvas.ActualHeight : 160;
        var maxTotal = dayTotals.Count > 0 ? dayTotals.Max() : 0;
        if (maxTotal <= 0) maxTotal = 1;
        var slotW = canvasWidth / 7.0;
        var topPad = 14.0;
        var bottomPad = 4.0;
        var drawableH = Math.Max(1, canvasHeight - topPad - bottomPad);

        double X(int i) => i * slotW + slotW / 2.0;
        double Y(double v) => topPad + (1 - v / maxTotal) * drawableH;

        for (int g = 0; g <= 3; g++)
        {
            var y = topPad + drawableH * g / 3.0;
            TrendCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0,
                X2 = canvasWidth,
                Y1 = y,
                Y2 = y,
                Stroke = BR("BorderBrushBase"),
                StrokeThickness = 1
            });
        }

        foreach (var fam in new[] { "Other", "Pro", "Flash" })
        {
            if (!grid.Values.Any(x => x[fam] > 0)) continue;

            var poly = new System.Windows.Shapes.Polyline
            {
                Stroke = B(FamilyColors[fam]),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            for (int i = 0; i < 7; i++)
                poly.Points.Add(new Point(X(i), Y(grid[days[i]][fam])));
            TrendCanvas.Children.Add(poly);

            for (int i = 0; i < 7; i++)
            {
                var cost = grid[days[i]][fam];
                if (cost <= 0) continue;
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = B(FamilyColors[fam]),
                    Stroke = BR("BgCardBrush") is SolidColorBrush bgSolid ? bgSolid : BR("BorderBrushBase"),
                    StrokeThickness = 1,
                    ToolTip = $"{days[i]:MM/dd} · {fam}: ${cost:F2}",
                };
                Canvas.SetLeft(dot, X(i) - 2.5);
                Canvas.SetTop(dot, Y(cost) - 2.5);
                TrendCanvas.Children.Add(dot);
            }
        }

        for (int i = 0; i < 7; i++)
        {
            var total = dayTotals[i];
            if (total <= 0) continue;
            var lbl = new TextBlock
            {
                Text = total >= 1 ? $"${total:F2}" : $"${total:F3}",
                FontSize = 9,
                Foreground = BR("TxtHintBrush")
            };
            lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var lx = X(i) - lbl.DesiredSize.Width / 2;
            var ly = Y(total) - 14;
            if (ly < 0) ly = Y(total) + 6;
            Canvas.SetLeft(lbl, Math.Max(0, Math.Min(canvasWidth - lbl.DesiredSize.Width, lx)));
            Canvas.SetTop(lbl, ly);
            TrendCanvas.Children.Add(lbl);
        }
    }

    private void AddLegendItem(string label, string color)
    {
        var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            Background = B(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = BR("TxtHintBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        TrendLegend.Children.Add(sp);
    }

    private void RefreshGeminiUi()
    {
        var accounts = _geminiAccounts.GetAccounts();
        if (accounts.Count == 0)
        {
            GeminiEmptyState.Visibility = Visibility.Visible;
            GeminiDashboard.Visibility = Visibility.Collapsed;
            GeminiAccountCombo.ItemsSource = null;
            return;
        }

        GeminiEmptyState.Visibility = Visibility.Collapsed;
        GeminiDashboard.Visibility = Visibility.Visible;

        _suppressGeminiSelection = true;
        GeminiAccountCombo.ItemsSource = accounts.Select(a => new GeminiAccountDisplay(a)).ToList();
        var selected = _geminiAccounts.GetSelected();
        if (selected != null)
            GeminiAccountCombo.SelectedIndex = accounts.ToList().FindIndex(a => a.Id == selected.Id);
        _suppressGeminiSelection = false;

        if (selected != null)
        {
            GeminiActiveAlias.Text = selected.Alias;
            GeminiActiveKeyPreview.Text = selected.KeyPreview;
        }

        RefreshGeminiStats();
        RefreshGeminiRelayUi();
    }

    private void RefreshGeminiRelayUi()
    {
        if (GeminiRelayPortBox == null) return; // not yet loaded

        // Sync port box from settings (one-shot on first load)
        if (string.IsNullOrWhiteSpace(GeminiRelayPortBox.Text) ||
            GeminiRelayPortBox.Text == "47821" && _storage.Settings.GeminiRelayPort != 47821)
        {
            GeminiRelayPortBox.Text = _storage.Settings.ClampedGeminiRelayPort().ToString();
        }

        GeminiRelayAutoStartCheck.IsChecked = _storage.Settings.GeminiRelayAutoStart;

        if (_geminiRelay.IsRunning)
        {
            var clients = _geminiRelay.ActiveClients;
            GeminiRelayStatusText.Text = $"● Running on 127.0.0.1:{_geminiRelay.Port}";
            GeminiRelayStatusText.Foreground = BR("StatusGoodBrush");
            GeminiRelayStartBtn.Content = "⏹ Stop";
            GeminiRelayStartBtn.Style = (Style)FindResource("BtnDanger");
            GeminiRelayPortBox.IsEnabled = false;

            var stats = $"served: {_geminiRelay.RequestsServed}";
            if (clients.Count > 0) stats += $" · {clients.Count} client{(clients.Count == 1 ? "" : "s")}";
            if (_geminiRelay.StartedAt.HasValue)
            {
                var up = DateTime.Now - _geminiRelay.StartedAt.Value;
                stats += $" · up {(up.TotalHours >= 1 ? $"{up.TotalHours:F1}h" : $"{up.TotalMinutes:F0}m")}";
            }
            GeminiRelayStatsText.Text = stats;

            // 활성 클라이언트 박스 갱신
            if (clients.Count > 0)
            {
                GeminiRelayClientsTitle.Text = $"👥 ACTIVE CLIENTS ({clients.Count})";
                GeminiRelayClientsList.ItemsSource = clients.Select(c => new RelayClientRow(c)).ToList();
                GeminiRelayClientsBox.Visibility = Visibility.Visible;
            }
            else
            {
                GeminiRelayClientsBox.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            var err = _geminiRelay.LastError;
            GeminiRelayStatusText.Text = string.IsNullOrEmpty(err) ? "● Stopped" : $"● Error: {err}";
            GeminiRelayStatusText.Foreground = BR(string.IsNullOrEmpty(err) ? "TxtSubBrush" : "StatusBadBrush");
            GeminiRelayStartBtn.Content = "▶ Start";
            GeminiRelayStartBtn.Style = (Style)FindResource("BtnPrimary");
            GeminiRelayPortBox.IsEnabled = true;
            GeminiRelayStatsText.Text = "";
            GeminiRelayClientsBox.Visibility = Visibility.Collapsed;
        }
    }

    private void GeminiRelayStartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_geminiRelay.IsRunning)
        {
            _geminiRelay.Stop();
            return;
        }

        if (!int.TryParse(GeminiRelayPortBox.Text?.Trim(), out var port) || port < 1024 || port > 65535)
        {
            GeminiRelayStatusText.Text = "Invalid port (1024-65535)";
            GeminiRelayStatusText.Foreground = BR("StatusBadBrush");
            return;
        }

        _storage.Settings.GeminiRelayPort = port;
        _storage.SaveSettings(_storage.Settings);

        if (!_geminiRelay.Start(port, out var err))
        {
            GeminiRelayStatusText.Text = $"● Start failed: {err}";
            GeminiRelayStatusText.Foreground = BR("StatusBadBrush");
        }
    }

    private void GeminiRelayAutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (GeminiRelayAutoStartCheck == null) return;
        _storage.Settings.GeminiRelayAutoStart = GeminiRelayAutoStartCheck.IsChecked == true;
        _storage.SaveSettings(_storage.Settings);
    }

    private void GeminiRelayCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var port = _geminiRelay.IsRunning ? _geminiRelay.Port
                                          : _storage.Settings.ClampedGeminiRelayPort();
        var selected = _geminiAccounts.GetSelected();
        var effectiveKey = selected?.EffectiveRelayKey ?? "tracker-default";

        GeminiRelaySettings.Load(port, effectiveKey);
        GeminiRelaySettings.CloseRequested -= HideSettingsPage;
        GeminiRelaySettings.CloseRequested += HideSettingsPage;
        GeminiRelaySettings.Visibility = Visibility.Visible;
    }

    private void HideSettingsPage()
    {
        GeminiRelaySettings.Visibility = Visibility.Collapsed;
    }

    private void RefreshGeminiStats()
    {
        var selected = _geminiAccounts.GetSelected();
        if (selected == null) return;

        var history = _storage.GetGeminiUsageHistory(selected.Id);
        var todayStart = DateTimeOffset.Now.Date;
        var todayMs = new DateTimeOffset(todayStart).ToUnixTimeMilliseconds();
        var today = history.Where(r => r.Timestamp >= todayMs).ToList();
        var last24h = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        var last24hRecords = history.Where(r => r.Timestamp >= last24h).ToList();

        var totalTokens = today.Sum(r => r.InputTokens + r.OutputTokens);
        var totalCost = today.Sum(r => r.CostUsd);

        GeminiTodayTokens.Text = totalTokens.ToString("N0");
        GeminiTodayCost.Text = $"${totalCost:F4}";
        GeminiRequestCount.Text = last24hRecords.Count.ToString();

        if (selected.LastUsedAtMs.HasValue)
        {
            var last = DateTimeOffset.FromUnixTimeMilliseconds(selected.LastUsedAtMs.Value).ToLocalTime();
            GeminiLastCallText.Text = $"last: {last:HH:mm:ss}";
        }
        else
        {
            GeminiLastCallText.Text = "no calls yet";
        }

        // Recent history (newest first, top 50)
        var recent = history.OrderByDescending(r => r.Timestamp).Take(50)
            .Select(r => new GeminiHistoryItem(r)).ToList();
        GeminiHistoryList.ItemsSource = recent;
        GeminiHistoryEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Account comparison (today cost per account)
        RefreshGeminiCompareList(todayMs);
    }

    private void RefreshGeminiCompareList(long todayMs)
    {
        var all = _storage.GetGeminiUsageHistory();
        var todayByAcc = all.Where(r => r.Timestamp >= todayMs)
            .GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CostUsd));

        var accounts = _geminiAccounts.GetAccounts();
        var rows = accounts.Select(a => new GeminiCompareRow(
            a.Alias,
            todayByAcc.TryGetValue(a.Id, out var c) ? c : 0
        )).ToList();

        var max = rows.Count > 0 ? rows.Max(r => r.Cost) : 0;
        if (max <= 0) max = 1;
        foreach (var r in rows) r.BarWidth = Math.Max(1, r.Cost / max * 320);
        GeminiCompareList.ItemsSource = rows;
    }

    private void CheckGeminiBudget(GeminiAccount account)
    {
        if (account.DailyBudgetUsd <= 0 && account.MonthlyBudgetUsd <= 0) return;

        var history = _storage.GetGeminiUsageHistory(account.Id);
        var now = DateTimeOffset.Now;

        var dayStart = new DateTimeOffset(now.Date, now.Offset).ToUnixTimeMilliseconds();
        var monthStart = new DateTimeOffset(new DateTime(now.Year, now.Month, 1), now.Offset).ToUnixTimeMilliseconds();

        var dailyUsed = history.Where(r => r.Timestamp >= dayStart).Sum(r => r.CostUsd);
        var monthlyUsed = history.Where(r => r.Timestamp >= monthStart).Sum(r => r.CostUsd);

        var threshold = Math.Clamp(account.AlertThresholdPct, 1, 100) / 100.0;

        // Daily
        if (account.DailyBudgetUsd > 0)
        {
            var dayKey = now.Date.ToString("yyyy-MM-dd");
            var pct = dailyUsed / account.DailyBudgetUsd;

            if (pct >= 1.0 && account.LastAlertedMaxKey != $"D:{dayKey}")
            {
                account.LastAlertedMaxKey = $"D:{dayKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 일간 예산 초과 · {account.Alias}",
                    $"${dailyUsed:F4} / ${account.DailyBudgetUsd:F2} ({pct:P0})");
            }
            else if (pct >= threshold && pct < 1.0 && account.LastAlertedWarnKey != $"D:{dayKey}")
            {
                account.LastAlertedWarnKey = $"D:{dayKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 일간 예산 경고 · {account.Alias}",
                    $"${dailyUsed:F4} / ${account.DailyBudgetUsd:F2} ({pct:P0})");
            }
        }

        // Monthly
        if (account.MonthlyBudgetUsd > 0)
        {
            var monthKey = now.ToString("yyyy-MM");
            var pct = monthlyUsed / account.MonthlyBudgetUsd;

            if (pct >= 1.0 && account.LastAlertedMaxKey != $"M:{monthKey}")
            {
                account.LastAlertedMaxKey = $"M:{monthKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 월간 예산 초과 · {account.Alias}",
                    $"${monthlyUsed:F2} / ${account.MonthlyBudgetUsd:F2} ({pct:P0})");
            }
            else if (pct >= threshold && pct < 1.0 && account.LastAlertedWarnKey != $"M:{monthKey}")
            {
                account.LastAlertedWarnKey = $"M:{monthKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 월간 예산 경고 · {account.Alias}",
                    $"${monthlyUsed:F2} / ${account.MonthlyBudgetUsd:F2} ({pct:P0})");
            }
        }
    }

    private void GeminiAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGeminiSelection) return;
        if (GeminiAccountCombo.SelectedItem is GeminiAccountDisplay d)
            _geminiAccounts.SelectAccount(d.Account.Id);
    }

    private async void GeminiAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new GeminiAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        GeminiStatusLabel.Text = "키 검증 중...";
        GeminiStatusLabel.Foreground = BR("StatusWarnBrush");

        var (ok, err, acc) = await _geminiAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        if (!ok)
        {
            GeminiStatusLabel.Text = $"실패: {err}";
            GeminiStatusLabel.Foreground = BR("StatusBadBrush");
            System.Windows.MessageBox.Show(this, $"계정 추가 실패: {err}",
                "Gemini", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        GeminiStatusLabel.Text = $"'{acc?.Alias}' 추가됨";
        GeminiStatusLabel.Foreground = BR("StatusGoodBrush");
    }

    private void GeminiManageBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new GeminiAccountManagerWindow(_geminiAccounts) { Owner = this };
        win.ShowDialog();
        RefreshGeminiUi();
    }

    private void GeminiPricingBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new GeminiPricingEditorWindow(_storage) { Owner = this };
        win.ShowDialog();
        RefreshGeminiStats();
    }

    private async void GeminiTestBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _geminiAccounts.GetSelected();
        if (selected == null) return;

        GeminiTestBtn.IsEnabled = false;
        GeminiStatusLabel.Text = "연결 테스트 중...";
        GeminiStatusLabel.Foreground = BR("StatusWarnBrush");

        var key = _geminiAccounts.GetApiKey(selected.Id);
        if (string.IsNullOrEmpty(key))
        {
            GeminiStatusLabel.Text = "키 복호화 실패";
            GeminiStatusLabel.Foreground = BR("StatusBadBrush");
            GeminiTestBtn.IsEnabled = true;
            return;
        }

        var (ok, err, count) = await _geminiProvider.ValidateKeyAsync(key);
        if (ok)
        {
            GeminiStatusLabel.Text = $"연결 성공 · 사용 가능 모델 {count}개";
            GeminiStatusLabel.Foreground = BR("StatusGoodBrush");

            selected.LastUsedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _storage.Save();
            RefreshGeminiStats();
        }
        else
        {
            GeminiStatusLabel.Text = $"연결 실패: {err}";
            GeminiStatusLabel.Foreground = BR("StatusBadBrush");
        }
        GeminiTestBtn.IsEnabled = true;
    }

    private void GeminiRefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshGeminiStats();

    private void GeminiExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _geminiAccounts.GetSelected();
        var allAccounts = _geminiAccounts.GetAccounts();
        if (allAccounts.Count == 0)
        {
            GeminiStatusLabel.Text = "내보낼 계정이 없습니다";
            GeminiStatusLabel.Foreground = BR("StatusBadBrush");
            return;
        }

        var defaultName = selected != null
            ? $"gemini-usage-{selected.Alias}-{DateTime.Now:yyyyMMdd-HHmmss}"
            : $"gemini-usage-all-{DateTime.Now:yyyyMMdd-HHmmss}";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Gemini 사용 기록 내보내기",
            FileName = defaultName,
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog(this) != true) return;

        var records = selected != null
            ? _storage.GetGeminiUsageHistory(selected.Id)
            : _storage.GetGeminiUsageHistory();

        var aliasMap = allAccounts.ToDictionary(a => a.Id, a => a.Alias);

        try
        {
            if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var payload = records.Select(r => new
                {
                    timestamp = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    timestampMs = r.Timestamp,
                    accountId = r.AccountId,
                    accountAlias = aliasMap.GetValueOrDefault(r.AccountId, "(deleted)"),
                    model = r.Model,
                    inputTokens = r.InputTokens,
                    outputTokens = r.OutputTokens,
                    cacheTokens = r.CacheTokens,
                    thinkingTokens = r.ThinkingTokens,
                    toolTokens = r.ToolTokens,
                    costUsd = r.CostUsd,
                    latencyMs = r.LatencyMs
                });
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                IOFile.WriteAllText(dlg.FileName, json, Encoding.UTF8);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("timestamp,account_alias,account_id,model,input_tokens,output_tokens,cache_tokens,thinking_tokens,tool_tokens,cost_usd,latency_ms");
                foreach (var r in records)
                {
                    var t = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    var alias = CsvEscape(aliasMap.GetValueOrDefault(r.AccountId, "(deleted)"));
                    sb.Append(t).Append(',')
                      .Append(alias).Append(',')
                      .Append(r.AccountId).Append(',')
                      .Append(CsvEscape(r.Model)).Append(',')
                      .Append(r.InputTokens).Append(',')
                      .Append(r.OutputTokens).Append(',')
                      .Append(r.CacheTokens).Append(',')
                      .Append(r.ThinkingTokens).Append(',')
                      .Append(r.ToolTokens).Append(',')
                      .Append(r.CostUsd.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                      .Append(r.LatencyMs)
                      .AppendLine();
                }
                IOFile.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            }

            GeminiStatusLabel.Text = $"내보냄 · {records.Count}건 → {IOPath.GetFileName(dlg.FileName)}";
            GeminiStatusLabel.Foreground = BR("StatusGoodBrush");
        }
        catch (Exception ex)
        {
            GeminiStatusLabel.Text = $"내보내기 실패: {ex.Message}";
            GeminiStatusLabel.Foreground = BR("StatusBadBrush");
        }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // ────────── Claude API Tab (v2.6.0) ──────────

    private void RefreshAnthropicUi()
    {
        if (AnthropicAccountCombo == null) return;
        var accounts = _anthropicAccounts.GetAccounts();

        if (accounts.Count == 0)
        {
            AnthropicEmptyState.Visibility = Visibility.Visible;
            AnthropicDashboard.Visibility = Visibility.Collapsed;
            AnthropicAccountCombo.ItemsSource = null;
            return;
        }

        AnthropicEmptyState.Visibility = Visibility.Collapsed;
        AnthropicDashboard.Visibility = Visibility.Visible;

        _suppressAnthropicSelection = true;
        AnthropicAccountCombo.ItemsSource = accounts.Select(a => new AnthropicAccountDisplay(a)).ToList();
        var selected = _anthropicAccounts.GetSelected();
        if (selected != null)
        {
            for (int i = 0; i < AnthropicAccountCombo.Items.Count; i++)
            {
                if (AnthropicAccountCombo.Items[i] is AnthropicAccountDisplay d && d.Account.Id == selected.Id)
                {
                    AnthropicAccountCombo.SelectedIndex = i;
                    break;
                }
            }
            AnthropicActiveAlias.Text = selected.Alias;
            AnthropicActiveKeyPreview.Text = selected.KeyPreview;
            AnthropicActiveOrg.Text = string.IsNullOrEmpty(selected.OrganizationId) ? "" : $"org: {selected.OrganizationId}";
        }
        _suppressAnthropicSelection = false;

        RenderAnthropicUsage();
    }

    private void RenderAnthropicUsage()
    {
        var selected = _anthropicAccounts.GetSelected();
        if (selected == null) return;

        var history = _storage.GetAnthropicApiUsageHistory(selected.Id);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_anthropicRangeDays).ToUnixTimeMilliseconds();
        var recent = history.Where(r => r.Timestamp >= cutoff).ToList();

        var grouped = recent
            .GroupBy(r => r.Model)
            .Select(g => new AnthropicModelRow(
                g.Key,
                g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens),
                g.Sum(x => x.CacheWriteTokens),
                g.Sum(x => x.CacheReadTokens),
                g.Sum(x => x.CostUsd)))
            .OrderByDescending(r => r.Cost)
            .ToList();

        AnthropicModelGrid.ItemsSource = grouped;
        AnthropicTotalCost.Text = $"${grouped.Sum(g => g.Cost):F4}";
        AnthropicTotalInput.Text = grouped.Sum(g => g.Input).ToString("N0");
        AnthropicTotalOutput.Text = grouped.Sum(g => g.Output).ToString("N0");
        AnthropicTotalCache.Text = (grouped.Sum(g => g.CacheWrite) + grouped.Sum(g => g.CacheRead)).ToString("N0");
    }

    private void AnthropicAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnthropicSelection) return;
        if (AnthropicAccountCombo.SelectedItem is AnthropicAccountDisplay d)
            _anthropicAccounts.SelectAccount(d.Account.Id);
    }

    private void AnthropicRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AnthropicRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _anthropicRangeDays = days;
            RenderAnthropicUsage();
        }
    }

    private async void AnthropicAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AnthropicAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AnthropicStatusLabel.Text = "검증 중...";
        var (ok, err, _) = await _anthropicAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        AnthropicStatusLabel.Text = ok ? "추가 완료" : $"실패: {err}";
        if (ok) await FetchAnthropicUsageAsync();
    }

    private async void AnthropicRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await FetchAnthropicUsageAsync();
    }

    private async Task FetchAnthropicUsageAsync()
    {
        var selected = _anthropicAccounts.GetSelected();
        if (selected == null) return;
        AnthropicStatusLabel.Text = "조회 중...";
        AnthropicRefreshBtn.IsEnabled = false;
        try
        {
            var end = DateTimeOffset.UtcNow;
            var start = end.AddDays(-_anthropicRangeDays);
            var result = await _anthropicAccounts.FetchUsageAsync(selected.Id, start, end);
            if (!result.Ok)
            {
                AnthropicStatusLabel.Text = $"실패: {result.Error}";
                return;
            }
            AnthropicStatusLabel.Text = $"갱신 {DateTime.Now:HH:mm:ss} · {result.Buckets.Count} models";
            RenderAnthropicUsage();
        }
        finally
        {
            AnthropicRefreshBtn.IsEnabled = true;
        }
    }

    private void AnthropicRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _anthropicAccounts.GetSelected();
        if (selected == null) return;
        var r = System.Windows.MessageBox.Show(this,
            $"'{selected.Alias}' 계정을 제거하시겠습니까?\n관련 사용량 이력도 함께 삭제됩니다.",
            "Claude API", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _anthropicAccounts.RemoveAccount(selected.Id);
    }

    // ──────────────── OpenAI API tab ────────────────

    private void RefreshOpenAiUi()
    {
        if (OpenAiAccountCombo == null) return;
        var accounts = _openAiAccounts.GetAccounts();

        if (accounts.Count == 0)
        {
            OpenAiEmptyState.Visibility = Visibility.Visible;
            OpenAiDashboard.Visibility = Visibility.Collapsed;
            OpenAiAccountCombo.ItemsSource = null;
            return;
        }

        OpenAiEmptyState.Visibility = Visibility.Collapsed;
        OpenAiDashboard.Visibility = Visibility.Visible;

        _suppressOpenAiSelection = true;
        OpenAiAccountCombo.ItemsSource = accounts.Select(a => new OpenAiAccountDisplay(a)).ToList();
        var selected = _openAiAccounts.GetSelected();
        if (selected != null)
        {
            for (int i = 0; i < OpenAiAccountCombo.Items.Count; i++)
            {
                if (OpenAiAccountCombo.Items[i] is OpenAiAccountDisplay d && d.Account.Id == selected.Id)
                {
                    OpenAiAccountCombo.SelectedIndex = i;
                    break;
                }
            }
            OpenAiActiveAlias.Text = selected.Alias;
            OpenAiActiveKeyPreview.Text = selected.KeyPreview;
            OpenAiActiveOrg.Text = string.IsNullOrEmpty(selected.OrganizationId) ? "" : $"org: {selected.OrganizationId}";
        }
        _suppressOpenAiSelection = false;

        RenderOpenAiUsage();
    }

    private void RenderOpenAiUsage()
    {
        var selected = _openAiAccounts.GetSelected();
        if (selected == null) return;

        var history = _storage.GetOpenAiApiUsageHistory(selected.Id);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_openAiRangeDays).ToUnixTimeMilliseconds();
        var recent = history.Where(r => r.Timestamp >= cutoff).ToList();

        var grouped = recent
            .GroupBy(r => r.Model)
            .Select(g => new OpenAiModelRow(
                g.Key,
                g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens),
                g.Sum(x => x.CachedInputTokens),
                g.Sum(x => x.CostUsd)))
            .OrderByDescending(r => r.Cost)
            .ToList();

        OpenAiModelGrid.ItemsSource = grouped;
        OpenAiTotalCost.Text = $"${grouped.Sum(g => g.Cost):F4}";
        OpenAiTotalInput.Text = grouped.Sum(g => g.Input).ToString("N0");
        OpenAiTotalOutput.Text = grouped.Sum(g => g.Output).ToString("N0");
        OpenAiTotalCached.Text = grouped.Sum(g => g.Cached).ToString("N0");

        // USAGE PATTERN — LAST 12H (시간대별 비용)
        UpdateOpenAiPattern(history);
    }

    private double[] _openAiHourlyCache = new double[12];

    private void UpdateOpenAiPattern(IReadOnlyList<OpenAiApiUsageSnapshot> history)
    {
        if (OpenAiPatternCanvas == null) return;
        var now = DateTime.Now;
        var twelveAgo = now.AddHours(-12);
        var twelveAgoMs = new DateTimeOffset(twelveAgo, DateTimeOffset.Now.Offset).ToUnixTimeMilliseconds();
        var buckets = new double[12];

        foreach (var r in history)
        {
            if (r.Timestamp < twelveAgoMs) continue;
            var t = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).LocalDateTime;
            var hoursAgo = (int)Math.Floor((now - t).TotalHours);
            if (hoursAgo < 0) hoursAgo = 0;
            if (hoursAgo > 11) continue;
            var bucket = 11 - hoursAgo; // 오래된 → 0, 최신 → 11
            buckets[bucket] += r.CostUsd;
        }

        _openAiHourlyCache = buckets;
        var total = buckets.Sum();
        OpenAiPatternMeta.Text = total > 0 ? $"합계 ${total:F2}" : "데이터 없음";

        var labels = new List<string>(12);
        for (int i = 0; i < 12; i++)
        {
            var hoursAgo = 11 - i;
            labels.Add(hoursAgo % 2 == 0 ? $"-{hoursAgo}h" : "");
        }
        OpenAiPatternAxis.ItemsSource = labels;

        DrawOpenAiPattern();
    }

    private void OpenAiPatternCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawOpenAiPattern();

    private void DrawOpenAiPattern()
    {
        if (OpenAiPatternCanvas == null) return;
        var canvas = OpenAiPatternCanvas;
        canvas.Children.Clear();
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var series = _openAiHourlyCache;
        var max = Math.Max(0.0001, series.Max());
        var lineBrush = (System.Windows.Media.Brush)FindResource("OpenAiBrandBrush");
        var fillBrush = lineBrush.Clone();
        if (fillBrush is System.Windows.Media.SolidColorBrush sb) sb.Opacity = 0.18;
        var subBrush = (System.Windows.Media.Brush)FindResource("BorderBrushBase");

        // 25/50/75% 보조선
        for (int g = 1; g <= 3; g++)
        {
            var y = h - h * (g * 0.25);
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke = subBrush, StrokeThickness = 0.5,
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 2, 3 }
            });
        }

        var points = new System.Windows.Media.PointCollection(12);
        for (int i = 0; i < 12; i++)
        {
            var x = (w / 11.0) * i;
            var y = h - (series[i] / max) * (h - 6) - 3;
            points.Add(new System.Windows.Point(x, y));
        }

        var polyPoints = new System.Windows.Media.PointCollection(points) {
            new System.Windows.Point(w, h),
            new System.Windows.Point(0, h),
        };
        canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = polyPoints, Fill = fillBrush });
        canvas.Children.Add(new System.Windows.Shapes.Polyline
        {
            Points = points, Stroke = lineBrush, StrokeThickness = 2,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
        });

        for (int i = 0; i < 12; i++)
        {
            var hoursAgo = 11 - i;
            var pt = points[i];
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = lineBrush,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1,
                ToolTip = $"-{hoursAgo}h: ${series[i]:F4}",
            };
            Canvas.SetLeft(dot, pt.X - 4);
            Canvas.SetTop(dot, pt.Y - 4);
            canvas.Children.Add(dot);
        }
    }

    private void OpenAiAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOpenAiSelection) return;
        if (OpenAiAccountCombo.SelectedItem is OpenAiAccountDisplay d)
            _openAiAccounts.SelectAccount(d.Account.Id);
    }

    private void OpenAiRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpenAiRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _openAiRangeDays = days;
            RenderOpenAiUsage();
        }
    }

    private async void OpenAiAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenAiAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        OpenAiStatusLabel.Text = "검증 중...";
        var (ok, err, _) = await _openAiAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        OpenAiStatusLabel.Text = ok ? "추가 완료" : $"실패: {err}";
        if (ok) await FetchOpenAiUsageAsync();
    }

    private async void OpenAiRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await FetchOpenAiUsageAsync();
    }

    private async Task FetchOpenAiUsageAsync()
    {
        var selected = _openAiAccounts.GetSelected();
        if (selected == null) return;
        OpenAiStatusLabel.Text = "조회 중...";
        OpenAiRefreshBtn.IsEnabled = false;
        try
        {
            var end = DateTimeOffset.UtcNow;
            var start = end.AddDays(-_openAiRangeDays);
            var result = await _openAiAccounts.FetchUsageAsync(selected.Id, start, end);
            if (!result.Ok)
            {
                OpenAiStatusLabel.Text = $"실패: {result.Error}";
                return;
            }
            OpenAiStatusLabel.Text = $"갱신 {DateTime.Now:HH:mm:ss} · {result.Buckets.Count} models";
            RenderOpenAiUsage();
        }
        finally
        {
            OpenAiRefreshBtn.IsEnabled = true;
        }
    }

    private void OpenAiRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _openAiAccounts.GetSelected();
        if (selected == null) return;
        var r = System.Windows.MessageBox.Show(this,
            $"'{selected.Alias}' 계정을 제거하시겠습니까?\n관련 사용량 이력도 함께 삭제됩니다.",
            "OpenAI API", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _openAiAccounts.RemoveAccount(selected.Id);
    }

    // ──────────────── OpenAI CLI (Codex) tab ────────────────

    private void RefreshCodexUi()
    {
        if (CodexLogPathHint != null)
            CodexLogPathHint.Text = $"{_codex.SessionsDir}\\rollout-*.jsonl 경로를 확인하세요";
        UpdateCodexAuthButtons();
        RenderCodexUsage();
    }

    private void UpdateCodexAuthButtons()
    {
        if (CodexLoginBtn == null || CodexLogoutBtn == null) return;
        var loggedIn = _codex.IsLoggedIn();
        CodexLoginBtn.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        CodexLogoutBtn.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RenderCodexUsage()
    {
        if (CodexDashboard == null) return;
        var since = DateTimeOffset.UtcNow.AddDays(-_codexRangeDays);
        // I/O 비중이 큰 rollout JSONL 파싱을 백그라운드 스레드로 위임해 UI 블로킹 회피
        var summary = await Task.Run(() => _codex.Aggregate(since));
        if (CodexDashboard == null) return; // 비동기 대기 중 unload 방어

        if (summary.Models.Count == 0)
        {
            CodexEmptyState.Visibility = Visibility.Visible;
            CodexDashboard.Visibility = Visibility.Collapsed;
            return;
        }

        CodexEmptyState.Visibility = Visibility.Collapsed;
        CodexDashboard.Visibility = Visibility.Visible;
        CodexModelGrid.ItemsSource = summary.Models;
        CodexSessionGrid.ItemsSource = summary.Sessions;
        CodexSessionsCount.Text = summary.SessionsTotal.ToString("N0");
        CodexInputTokens.Text = FormatTokensShort(summary.InputTotal);
        CodexOutputTokens.Text = FormatTokensShort(summary.OutputTotal);
        CodexEstCost.Text = $"${summary.CostTotal:F2}";

        // LOCAL USAGE
        CodexLocalToday.Text = FormatTokensShort(summary.TodayTokens);
        CodexLocalAvg.Text = FormatTokensShort(summary.Last7dAvgTokens);
        var (deltaText, deltaBrush) = ComputeDelta(summary.TodayTokens, summary.Last7dAvgTokens);
        CodexLocalDelta.Text = deltaText;
        CodexLocalDelta.Foreground = deltaBrush;
        UpdateLocalUsageBar(summary.TodayTokens, summary.Last7dAvgTokens);

        // USAGE PATTERN — LAST 12H
        UpdateUsagePattern(summary.HourlyTokens);
    }

    private static string FormatTokensShort(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
        if (n >= 1_000)      return $"{n / 1_000.0:F0}k";
        return n.ToString("N0");
    }

    private (string text, System.Windows.Media.Brush brush) ComputeDelta(long today, long avg)
    {
        if (avg <= 0) return ("—", (System.Windows.Media.Brush)FindResource("TxtSubBrush"));
        var pct = (today - avg) * 100.0 / avg;
        var sign = pct >= 0 ? "▲" : "▼";
        var brushKey = pct >= 0 ? "StatusWarnBrush" : "StatusGoodBrush";
        return ($"{sign} {Math.Abs(pct):F0}%", (System.Windows.Media.Brush)FindResource(brushKey));
    }

    private void UpdateLocalUsageBar(long today, long avg)
    {
        if (CodexLocalBar == null) return;
        double pct;
        if (avg <= 0) pct = today > 0 ? 1.0 : 0.0;
        else pct = Math.Min(1.5, today / (double)avg);

        // 색 임계값: <50% good, 50-75 warn, 75-90 high, >90 bad
        string brushKey = pct switch
        {
            < 0.5  => "StatusGoodBrush",
            < 0.75 => "StatusWarnBrush",
            < 0.9  => "StatusHighBrush",
            _       => "StatusBadBrush"
        };
        CodexLocalBar.Background = (System.Windows.Media.Brush)FindResource(brushKey);

        var trackWidth = CodexLocalBar.Parent is FrameworkElement parent ? parent.ActualWidth : 240.0;
        if (trackWidth <= 0) trackWidth = 240.0;
        CodexLocalBar.Width = Math.Min(trackWidth, trackWidth * Math.Min(1.0, pct));
        CodexLocalPercent.Text = $"{(int)Math.Round(pct * 100)}%";
        CodexLocalBar.ToolTip = $"오늘 {FormatTokensShort(today)} · 7일 평균 {FormatTokensShort(avg)} · {pct:P1}";
    }

    private int[] _codexHourlyCache = new int[12];

    private void UpdateUsagePattern(int[] hourly)
    {
        if (CodexPatternCanvas == null) return;
        _codexHourlyCache = (int[])hourly.Clone();

        var total = hourly.Sum();
        CodexPatternMeta.Text = total > 0 ? $"합계 {FormatTokensShort(total)}" : "데이터 없음";

        // X축 라벨: -11h, -9h, … (짝수 시간만 표시)
        var labels = new List<string>(12);
        for (int i = 0; i < 12; i++)
        {
            var hoursAgo = 11 - i;
            labels.Add(hoursAgo % 2 == 0 ? $"-{hoursAgo}h" : "");
        }
        CodexPatternAxis.ItemsSource = labels;

        DrawCodexPattern();
    }

    private void CodexPatternCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawCodexPattern();

    private void DrawCodexPattern()
    {
        if (CodexPatternCanvas == null) return;
        var canvas = CodexPatternCanvas;
        canvas.Children.Clear();
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var hourly = _codexHourlyCache;
        var max = Math.Max(1, hourly.Max());
        var lineBrush = (System.Windows.Media.Brush)FindResource("StatusGoodBrush");
        var fillBrush = lineBrush.Clone();
        if (fillBrush is System.Windows.Media.SolidColorBrush sb) sb.Opacity = 0.18;
        var subBrush = (System.Windows.Media.Brush)FindResource("BorderBrushBase");

        // 가로 보조선 3개 (25/50/75%)
        for (int g = 1; g <= 3; g++)
        {
            var y = h - h * (g * 0.25);
            var grid = new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke = subBrush, StrokeThickness = 0.5, StrokeDashArray = new System.Windows.Media.DoubleCollection { 2, 3 }
            };
            canvas.Children.Add(grid);
        }

        var points = new System.Windows.Media.PointCollection(12);
        for (int i = 0; i < 12; i++)
        {
            var x = (w / 11.0) * i;
            var y = h - (hourly[i] / (double)max) * (h - 6) - 3;
            points.Add(new System.Windows.Point(x, y));
        }

        // 영역 채우기 (Polygon: 라인 + 바닥)
        var polyPoints = new System.Windows.Media.PointCollection(points) {
            new System.Windows.Point(w, h),
            new System.Windows.Point(0, h),
        };
        var area = new System.Windows.Shapes.Polygon { Points = polyPoints, Fill = fillBrush };
        canvas.Children.Add(area);

        // 라인
        var line = new System.Windows.Shapes.Polyline
        {
            Points = points,
            Stroke = lineBrush,
            StrokeThickness = 2,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
        };
        canvas.Children.Add(line);

        // 데이터 포인트 (호버 시 툴팁)
        for (int i = 0; i < 12; i++)
        {
            var hoursAgo = 11 - i;
            var pt = points[i];
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = lineBrush,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1,
                ToolTip = $"-{hoursAgo}h: {FormatTokensShort(hourly[i])} 토큰",
            };
            Canvas.SetLeft(dot, pt.X - 4);
            Canvas.SetTop(dot, pt.Y - 4);
            canvas.Children.Add(dot);
        }
    }

    private void CodexRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CodexRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _codexRangeDays = days;
            RenderCodexUsage();
        }
    }

    private void CodexRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RenderCodexUsage();
    }

    private void CodexLoginBtn_Click(object sender, RoutedEventArgs e) =>
        RunCodexCommand("login", "Codex CLI 로그인");

    private void CodexLogoutBtn_Click(object sender, RoutedEventArgs e)
    {
        var r = System.Windows.MessageBox.Show(this,
            "Codex CLI 세션에서 로그아웃하시겠습니까?\n(`codex logout` 명령을 실행합니다.)",
            "Codex CLI", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        RunCodexCommand("logout", "Codex CLI 로그아웃");
    }

    private void RunCodexCommand(string subcommand, string title)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/k codex {subcommand}")
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            using var p = Process.Start(psi);
            // Best-effort: refresh usage view shortly after to pick up new logs
            _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(RefreshCodexUi));
        }
        catch (Exception ex)
        {
            Logger.Warn($"{title} failed", ex);
            System.Windows.MessageBox.Show(this,
                $"{title} 실행 실패: {ex.Message}\n\nCodex CLI가 설치되어 있는지 확인하세요:\nnpm i -g @openai/codex",
                title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ──────────────── Grok / xAI API tab ────────────────

    private void RefreshGrokUi()
    {
        if (GrokAccountCombo == null) return;
        var accounts = _grokAccounts.GetAccounts();

        if (accounts.Count == 0)
        {
            GrokEmptyState.Visibility = Visibility.Visible;
            GrokDashboard.Visibility = Visibility.Collapsed;
            GrokAccountCombo.ItemsSource = null;
            return;
        }

        GrokEmptyState.Visibility = Visibility.Collapsed;
        GrokDashboard.Visibility = Visibility.Visible;

        _suppressGrokSelection = true;
        GrokAccountCombo.ItemsSource = accounts.Select(a => new GrokAccountDisplay(a)).ToList();
        var selected = _grokAccounts.GetSelected();
        if (selected != null)
        {
            for (int i = 0; i < GrokAccountCombo.Items.Count; i++)
            {
                if (GrokAccountCombo.Items[i] is GrokAccountDisplay d && d.Account.Id == selected.Id)
                {
                    GrokAccountCombo.SelectedIndex = i;
                    break;
                }
            }
            GrokActiveAlias.Text = selected.Alias;
            GrokActiveKeyPreview.Text = selected.KeyPreview;
            GrokActiveMeta.Text = selected.AllowedModels.Count > 0
                ? $"{selected.AllowedModels.Count} allowed models"
                : "";

            GrokKeyName.Text = selected.KeyName ?? "--";
            GrokUserId.Text = selected.UserId ?? "--";
            GrokTeamId.Text = selected.TeamId ?? "--";
            GrokKeyStatus.Text = selected.IsActive ? "ACTIVE" : "DISABLED";
            GrokKeyStatus.Foreground = BR(selected.IsActive ? "StatusGoodBrush" : "StatusBadBrush");
            GrokAllowedModels.ItemsSource = selected.AllowedModels;
        }
        _suppressGrokSelection = false;
    }

    private void GrokAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGrokSelection) return;
        if (GrokAccountCombo.SelectedItem is GrokAccountDisplay d)
            _grokAccounts.SelectAccount(d.Account.Id);
    }

    private async void GrokAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new GrokAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        GrokStatusLabel.Text = "검증 중...";
        var (ok, err, _) = await _grokAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        GrokStatusLabel.Text = ok ? "추가 완료" : $"실패: {err}";
    }

    private async void GrokRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _grokAccounts.GetSelected();
        if (selected == null) return;
        GrokStatusLabel.Text = "조회 중...";
        GrokRefreshBtn.IsEnabled = false;
        try
        {
            var (ok, err, _) = await _grokAccounts.RefreshKeyInfoAsync(selected.Id);
            GrokStatusLabel.Text = ok ? $"갱신 {DateTime.Now:HH:mm:ss}" : $"실패: {err}";
            if (ok) RefreshGrokUi();
        }
        finally { GrokRefreshBtn.IsEnabled = true; }
    }

    private void GrokRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _grokAccounts.GetSelected();
        if (selected == null) return;
        var r = System.Windows.MessageBox.Show(this,
            $"'{selected.Alias}' 계정을 제거하시겠습니까?",
            "Grok API", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _grokAccounts.RemoveAccount(selected.Id);
    }

    // ──────────────── Grok CLI tab ────────────────

    private void RefreshGrokCliUi()
    {
        if (GrokCliPathHint != null)
            GrokCliPathHint.Text = $"검사 경로: {string.Join(" · ", _grokCli.CandidateDirs)}";
        RenderGrokCliUsage();
    }

    private void RenderGrokCliUsage()
    {
        if (GrokCliDashboard == null) return;
        var since = DateTimeOffset.UtcNow.AddDays(-_grokCliRangeDays);
        var summary = _grokCli.Aggregate(since);

        if (summary.Models.Count == 0)
        {
            GrokCliEmptyState.Visibility = Visibility.Visible;
            GrokCliDashboard.Visibility = Visibility.Collapsed;
            return;
        }

        GrokCliEmptyState.Visibility = Visibility.Collapsed;
        GrokCliDashboard.Visibility = Visibility.Visible;
        GrokCliModelGrid.ItemsSource = summary.Models;
        GrokCliSessionsCount.Text = summary.SessionsTotal.ToString("N0");
        GrokCliInputTokens.Text = summary.InputTotal.ToString("N0");
        GrokCliOutputTokens.Text = summary.OutputTotal.ToString("N0");
        GrokCliEstCost.Text = $"${summary.CostTotal:F4}";
    }

    private void GrokCliRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GrokCliRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _grokCliRangeDays = days;
            RenderGrokCliUsage();
        }
    }

    private void GrokCliRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RenderGrokCliUsage();
    }
}

internal class GrokAccountDisplay
{
    public GrokApiAccount Account { get; }
    public GrokAccountDisplay(GrokApiAccount a) { Account = a; }
    public override string ToString()
    {
        var pri = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}{pri}  ({Account.KeyPreview})";
    }
}

internal class OpenAiAccountDisplay
{
    public OpenAiApiAccount Account { get; }
    public OpenAiAccountDisplay(OpenAiApiAccount a) { Account = a; }
    public override string ToString()
    {
        var pri = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}{pri}  ({Account.KeyPreview})";
    }
}

internal class OpenAiModelRow
{
    public string Model { get; }
    public long Input { get; }
    public long Output { get; }
    public long Cached { get; }
    public double Cost { get; }

    public string InputDisplay => Input.ToString("N0");
    public string OutputDisplay => Output.ToString("N0");
    public string CachedDisplay => Cached.ToString("N0");
    public string CostDisplay => $"${Cost:F4}";

    public OpenAiModelRow(string model, long input, long output, long cached, double cost)
    {
        Model = model; Input = input; Output = output; Cached = cached; Cost = cost;
    }
}

internal class AnthropicAccountDisplay
{
    public AnthropicApiAccount Account { get; }
    public AnthropicAccountDisplay(AnthropicApiAccount a) { Account = a; }
    public override string ToString()
    {
        var pri = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}{pri}  ({Account.KeyPreview})";
    }
}

internal class AnthropicModelRow
{
    public string Model { get; }
    public long Input { get; }
    public long Output { get; }
    public long CacheWrite { get; }
    public long CacheRead { get; }
    public double Cost { get; }

    public string InputDisplay => Input.ToString("N0");
    public string OutputDisplay => Output.ToString("N0");
    public string CacheWriteDisplay => CacheWrite.ToString("N0");
    public string CacheReadDisplay => CacheRead.ToString("N0");
    public string CostDisplay => $"${Cost:F4}";

    public AnthropicModelRow(string model, long input, long output, long cacheWrite, long cacheRead, double cost)
    {
        Model = model; Input = input; Output = output;
        CacheWrite = cacheWrite; CacheRead = cacheRead; Cost = cost;
    }
}

internal class GeminiAccountDisplay
{
    public GeminiAccount Account { get; }
    public GeminiAccountDisplay(GeminiAccount a) { Account = a; }
    public override string ToString()
    {
        var primary = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}  ({Account.KeyPreview}){primary}";
    }
}

internal class GeminiBudgetRow
{
    public string Alias { get; }
    public string SpendText { get; }
    public SolidColorBrush BarBrush { get; }
    public double BarWidth { get; }
    public string SubText { get; }
    public string Tooltip { get; }

    public GeminiBudgetRow(GeminiAccount a, double todayCost, double monthCost, double barMaxPx)
    {
        Alias = a.Alias;
        double pct;
        if (a.MonthlyBudgetUsd > 0)
        {
            pct = monthCost / a.MonthlyBudgetUsd;
            SpendText = $"${monthCost:F2} / ${a.MonthlyBudgetUsd:F2}";
            SubText = $"month {pct:P0} · today ${todayCost:F2}";
        }
        else if (a.DailyBudgetUsd > 0)
        {
            pct = todayCost / a.DailyBudgetUsd;
            SpendText = $"${todayCost:F2} / ${a.DailyBudgetUsd:F2}";
            SubText = $"today {pct:P0} · month ${monthCost:F2}";
        }
        else
        {
            pct = 0;
            SpendText = $"${monthCost:F2}";
            SubText = $"no budget · today ${todayCost:F2}";
        }
        BarBrush = PickBrush(pct);
        BarWidth = Math.Max(0, Math.Min(1.0, pct)) * barMaxPx;
        Tooltip = $"{a.Alias} · 오늘 ${todayCost:F2} · 이번 달 ${monthCost:F2}" +
                  (a.MonthlyBudgetUsd > 0 ? $" / ${a.MonthlyBudgetUsd:F2} ({pct:P1})"
                                          : a.DailyBudgetUsd > 0 ? $" · 일 예산 ${a.DailyBudgetUsd:F2} ({pct:P1})"
                                                                 : " · 예산 미설정");
    }

    private static SolidColorBrush PickBrush(double pct) => Services.ThemeBrush.UsageColor(pct);
}

internal class TopModelRow
{
    public string Label { get; }
    public string CostText { get; }
    public SolidColorBrush ColorBrush { get; }
    public double BarWidth { get; }
    public string ShareText { get; }
    public string Tooltip { get; }

    public TopModelRow(string model, double cost, double barWidth, double share)
    {
        Label = model;
        CostText = $"${cost:F2}";
        BarWidth = Math.Max(0, barWidth);
        ShareText = $"{share:P0}";
        Tooltip = $"{model} · ${cost:F2} · 전체 대비 {share:P1}";
        var m = (model ?? "").ToLowerInvariant();
        Color c = m.Contains("flash")
            ? Color.FromRgb(0x4F, 0x7C, 0xE8)
            : m.Contains("pro")
                ? Color.FromRgb(0x9B, 0x72, 0xCB)
                : Color.FromRgb(0x64, 0x74, 0x8b);
        ColorBrush = new SolidColorBrush(c);
    }
}

internal class GeminiCompareRow
{
    public string Label { get; }
    public double Cost { get; }
    public string CostText { get; }
    public double BarWidth { get; set; }

    public GeminiCompareRow(string alias, double cost)
    {
        Label = $"👤 {alias}";
        Cost = cost;
        CostText = $"${cost:F4}";
    }
}

internal class GeminiHistoryItem
{
    public string TimeText { get; }
    public string Model { get; }
    public string InputText { get; }
    public string OutputText { get; }
    public string LatencyText { get; }
    public string CostText { get; }

    public GeminiHistoryItem(GeminiUsageRecord r)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime();
        TimeText = t.ToString("MM/dd HH:mm:ss");
        Model = r.Model;
        InputText = r.InputTokens.ToString("N0");
        OutputText = r.OutputTokens.ToString("N0");
        LatencyText = $"{r.LatencyMs}";
        CostText = $"${r.CostUsd:F5}";
    }
}

internal class RelayClientRow
{
    public string Title { get; }     // "google-genai/0.5.2" 또는 UA 첫 토큰
    public string Stats { get; }     // "192.168.1.5 · 24 req · alias=work"
    public string LastSeen { get; }  // "12s ago"

    public RelayClientRow(AIUsageTracker.Services.ClientInfo c)
    {
        Title = c.DisplayName;
        Stats = $"{c.RemoteIp} · {c.RequestCount} req · {c.AccountAlias}";
        var since = DateTime.Now - c.LastSeenAt;
        LastSeen = since.TotalSeconds < 60 ? $"{(int)since.TotalSeconds}s ago"
                 : since.TotalMinutes < 60 ? $"{since.TotalMinutes:F0}m ago"
                 : $"{since.TotalHours:F1}h ago";
    }
}
