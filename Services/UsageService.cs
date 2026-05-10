using System.Text.Json;
using AIUsageTracker.Models;

namespace AIUsageTracker.Services;

public class UsageService
{
    private readonly StorageService _storage;
    private readonly ClaudeApiService _api;

    public LatestUsage Latest { get; private set; } = new();
    public bool IsLoggedIn { get; private set; }
    public string StatusText { get; private set; } = "Loading...";
    public string StatusKind { get; private set; } = "loading";

    public event Action? UsageUpdated;
    public event Action? StatusChanged;

    public UsageService(StorageService storage, ClaudeApiService api)
    {
        _storage = storage;
        _api = api;
    }

    public static double ToPercent(double v)
    {
        if (double.IsNaN(v)) return 0;
        // Claude /usage API returns utilization already in percent (0-100).
        // Past code multiplied by 100 when v<=1 to handle a hypothetical fraction
        // payload — but that misfires on real values like 1.0 (=1%, becomes 100%).
        return v;
    }

    public void SetStatus(string text, string kind)
    {
        StatusText = text;
        StatusKind = kind;
        StatusChanged?.Invoke();
    }

    public void Logout()
    {
        IsLoggedIn = false;
        Latest = new LatestUsage();
        SetStatus("Login required", "error");
        UsageUpdated?.Invoke();
    }

    /// <summary>Returns: true=success, false=error, null=needs login</summary>
    public async Task<bool?> FetchUsageAsync()
    {
        if (!_api.IsReady)
        {
            SetStatus("WebView not ready", "loading");
            return false;
        }

        SetStatus("Refreshing...", "loading");

        var (ok, error, data) = await _api.FetchUsageAsync();

        if (ok && data != null)
        {
            IsLoggedIn = true;
            SetStatus("Connected", "connected");
            ParseUsageData(data);
            return true;
        }

        if (error == "not_logged_in")
        {
            IsLoggedIn = false;
            SetStatus("Login required", "error");
            return null;
        }

        SetStatus($"Error: {error}", "error");
        return false;
    }

    private void ParseUsageData(UsageApiResponse data)
    {
        var fiveHour = data.GetFiveHour();
        var sevenDay = data.GetSevenDay();
        var sub = data.GetSevenDaySub();
        var design = data.GetClaudeDesign();
        var routine = data.GetDailyRoutines();
        var extra = data.GetExtraUsage();

        Logger.Info($"Claude usage parsed: 5h={fiveHour?.Utilization:F3}/{fiveHour?.GetResetTime() ?? "—"}, " +
                    $"week={sevenDay?.Utilization:F3}/{sevenDay?.GetResetTime() ?? "—"}, " +
                    $"sub({data.GetSubModelName()})={sub?.Utilization:F3}/{sub?.GetResetTime() ?? "—"}, " +
                    $"design={design?.Utilization:F3}, routine={routine?.GetUsed()}/{routine?.GetLimit()}");

        // SubResetAt이 누락되거나 이미 지난 시각이면 WeekResetAt으로 fallback —
        // Claude API가 sub-model 카테고리의 reset_at을 새 주 시점으로 갱신하지 않는 경우,
        // EffectivePct가 작동 안 해서 옛 % 값이 새 주에도 표시되는 버그 방지.
        var weekReset = sevenDay?.GetResetTime();
        var subResetRaw = sub?.GetResetTime();
        string? subReset = subResetRaw;
        if (string.IsNullOrEmpty(subResetRaw) ||
            (DateTimeOffset.TryParse(subResetRaw, out var subDt) && subDt <= DateTimeOffset.Now))
        {
            subReset = weekReset;
        }

        Latest = new LatestUsage
        {
            SessionPct = ToPercent(fiveHour?.Utilization ?? 0),
            SessionResetAt = fiveHour?.GetResetTime(),
            WeekPct = ToPercent(sevenDay?.Utilization ?? 0),
            WeekResetAt = weekReset,
            SubPct = ToPercent(sub?.Utilization ?? 0),
            SubResetAt = subReset,
            SubModelName = data.GetSubModelName(),
            HasDesign = design != null,
            DesignPct = ToPercent(design?.Utilization ?? 0),
            DesignResetAt = design?.GetResetTime(),
            HasRoutine = routine != null && routine.GetLimit() > 0,
            RoutineUsed = routine?.GetUsed() ?? 0,
            RoutineLimit = routine?.GetLimit() ?? 0,
            RoutineResetAt = routine?.GetResetTime(),
            Extra = extra
        };

        var snapshot = new UsageSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            FiveHourUtilization = Latest.SessionPct,
            FiveHourResetsAt = Latest.SessionResetAt,
            SevenDayUtilization = Latest.WeekPct,
            SevenDayResetsAt = Latest.WeekResetAt,
            SubModelUtilization = Latest.SubPct,
            SubModelResetsAt = Latest.SubResetAt
        };

        _storage.SaveSnapshot(snapshot);
        UsageUpdated?.Invoke();
    }

    /// <summary>
    /// Process a raw JSON result string (from LoginWindow's direct fetch).
    /// Returns true if data was parsed successfully.
    /// </summary>
    public bool ProcessRawFetchResult(string resultJson)
    {
        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);
            var ok = result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (!ok) return false;

            var dataStr = result.GetProperty("data").GetString();
            if (dataStr == null) return false;

            var usage = JsonSerializer.Deserialize<UsageApiResponse>(dataStr,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (usage == null) return false;

            IsLoggedIn = true;
            SetStatus("Connected", "connected");
            ParseUsageData(usage);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ProcessRawFetchResult failed", ex);
            return false;
        }
    }

    public List<UsageSnapshot> GetHistory() => _storage.GetHistory();
}
