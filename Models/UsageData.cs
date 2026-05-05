using System.Text.Json.Serialization;

namespace AIUsageTracker.Models;

public class UsageCategory
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    [JsonPropertyName("reset_at")]
    public string? ResetAt { get; set; }

    public string? GetResetTime() => ResetsAt ?? ResetAt;
}

public class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("monthly_limit")]
    public double? MonthlyLimit { get; set; }

    [JsonPropertyName("used_credits")]
    public double? UsedCredits { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }
}

public class RoutineUsage
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("used")]
    public int? Used { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("executions")]
    public int? Executions { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("max")]
    public int? Max { get; set; }

    [JsonPropertyName("daily_limit")]
    public int? DailyLimit { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    [JsonPropertyName("reset_at")]
    public string? ResetAt { get; set; }

    public int GetUsed() => Used ?? Count ?? Executions ?? 0;
    public int GetLimit() => Limit ?? Max ?? DailyLimit ?? 0;
    public string? GetResetTime() => ResetsAt ?? ResetAt;
}

public class UsageApiResponse
{
    [JsonPropertyName("five_hour")]
    public UsageCategory? FiveHour { get; set; }

    [JsonPropertyName("fiveHour")]
    public UsageCategory? FiveHourAlt { get; set; }

    [JsonPropertyName("session")]
    public UsageCategory? Session { get; set; }

    [JsonPropertyName("current_session")]
    public UsageCategory? CurrentSession { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageCategory? SevenDay { get; set; }

    [JsonPropertyName("sevenDay")]
    public UsageCategory? SevenDayAlt { get; set; }

    [JsonPropertyName("weekly")]
    public UsageCategory? Weekly { get; set; }

    [JsonPropertyName("seven_day_all")]
    public UsageCategory? SevenDayAll { get; set; }

    [JsonPropertyName("seven_day_all_models")]
    public UsageCategory? SevenDayAllModels { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageCategory? SevenDaySonnet { get; set; }

    [JsonPropertyName("sevenDaySonnet")]
    public UsageCategory? SevenDaySonnetAlt { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageCategory? SevenDayOpus { get; set; }

    [JsonPropertyName("sevenDayOpus")]
    public UsageCategory? SevenDayOpusAlt { get; set; }

    [JsonPropertyName("seven_day_claude_design")]
    public UsageCategory? SevenDayClaudeDesign { get; set; }

    [JsonPropertyName("sevenDayClaudeDesign")]
    public UsageCategory? SevenDayClaudeDesignAlt { get; set; }

    [JsonPropertyName("seven_day_design")]
    public UsageCategory? SevenDayDesign { get; set; }

    [JsonPropertyName("claude_design")]
    public UsageCategory? ClaudeDesign { get; set; }

    [JsonPropertyName("design")]
    public UsageCategory? Design { get; set; }

    [JsonPropertyName("daily_routines")]
    public RoutineUsage? DailyRoutines { get; set; }

    [JsonPropertyName("dailyRoutines")]
    public RoutineUsage? DailyRoutinesAlt { get; set; }

    [JsonPropertyName("routines")]
    public RoutineUsage? Routines { get; set; }

    [JsonPropertyName("routine_executions")]
    public RoutineUsage? RoutineExecutions { get; set; }

    [JsonPropertyName("daily_routine_executions")]
    public RoutineUsage? DailyRoutineExecutions { get; set; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; set; }

    [JsonPropertyName("extraUsage")]
    public ExtraUsage? ExtraUsageAlt { get; set; }

    public UsageCategory? GetFiveHour() => FiveHour ?? FiveHourAlt ?? Session ?? CurrentSession;
    public UsageCategory? GetSevenDay() => SevenDay ?? SevenDayAlt ?? Weekly ?? SevenDayAll ?? SevenDayAllModels;
    public UsageCategory? GetSevenDaySub() => SevenDaySonnet ?? SevenDaySonnetAlt ?? SevenDayOpus ?? SevenDayOpusAlt;
    public string GetSubModelName() => (SevenDaySonnet ?? SevenDaySonnetAlt) != null ? "Sonnet" : "Opus";
    public UsageCategory? GetClaudeDesign() =>
        SevenDayClaudeDesign ?? SevenDayClaudeDesignAlt ?? SevenDayDesign ?? ClaudeDesign ?? Design;
    public RoutineUsage? GetDailyRoutines() =>
        DailyRoutines ?? DailyRoutinesAlt ?? DailyRoutineExecutions ?? RoutineExecutions ?? Routines;
    public ExtraUsage? GetExtraUsage() => ExtraUsage ?? ExtraUsageAlt;
}

public class UsageSnapshot
{
    public long Timestamp { get; set; }
    public double FiveHourUtilization { get; set; }
    public string? FiveHourResetsAt { get; set; }
    public double SevenDayUtilization { get; set; }
    public string? SevenDayResetsAt { get; set; }
    public double SubModelUtilization { get; set; }
    public string? SubModelResetsAt { get; set; }
}

public class AppSettings
{
    public int PollIntervalSeconds { get; set; } = 300;
    public int NotifyThreshold { get; set; } = 80;
    public bool NotifyEnabled { get; set; } = true;

    public int GeminiRelayPort { get; set; } = 47821;
    public bool GeminiRelayAutoStart { get; set; } = false;

    public string Theme { get; set; } = "dark"; // "dark" | "dog"

    /// <summary>강아지 모드에서 등장시킬 견종 (Corgi / Bichon / Golden / Poodle).
    /// 기본값 4종 모두. 빈 리스트는 사용자가 의도적으로 0마리를 원한 것이므로 그대로 존중.
    /// null 일 때만 (구버전 설정 파일에 필드 없음) 4종 전체로 폴백.</summary>
    public List<string> EnabledDogBreeds { get; set; } = new() { "Corgi", "Bichon", "Golden", "Poodle" };

    /// <summary>실행 중 보이는 아이콘 (Window·작업표시줄·트레이)용 사용자 선택 경로.
    /// null/빈 문자열이면 기본 Assets/icon.ico 사용.
    /// 'builtin:A' / 'builtin:B' / 'builtin:C' 같은 표기로 빌트인 3개 컨셉 식별.
    /// 그 외에는 절대 파일 경로 (.ico / .png).
    /// 주의: Windows 탐색기·시작메뉴의 .exe 아이콘은 빌드 시 PE 리소스에 박혀 변경 불가.</summary>
    public string? AppIconPath { get; set; }

    public IReadOnlyList<string> GetEnabledBreedNames() =>
        EnabledDogBreeds ?? new List<string> { "Corgi", "Bichon", "Golden", "Poodle" };

    public int ClampedPollIntervalSeconds() => Math.Clamp(PollIntervalSeconds, 30, 3600);
    public int ClampedNotifyThreshold() => Math.Clamp(NotifyThreshold, 1, 100);
    public int ClampedGeminiRelayPort() => Math.Clamp(GeminiRelayPort, 1024, 65535);
}

public class StorageData
{
    public List<UsageSnapshot> UsageHistory { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    public List<GeminiAccount> GeminiAccounts { get; set; } = new();
    public string? SelectedGeminiAccountId { get; set; }
    public List<GeminiUsageRecord> GeminiUsageHistory { get; set; } = new();
    public List<GeminiPricingOverride> GeminiPricingOverrides { get; set; } = new();

    public List<AnthropicApiAccount> AnthropicApiAccounts { get; set; } = new();
    public string? SelectedAnthropicApiAccountId { get; set; }
    public List<AnthropicApiUsageSnapshot> AnthropicApiUsageHistory { get; set; } = new();

    public List<OpenAiApiAccount> OpenAiApiAccounts { get; set; } = new();
    public string? SelectedOpenAiApiAccountId { get; set; }
    public List<OpenAiApiUsageSnapshot> OpenAiApiUsageHistory { get; set; } = new();

    public List<GrokApiAccount> GrokApiAccounts { get; set; } = new();
    public string? SelectedGrokApiAccountId { get; set; }
}

public class GrokApiAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public string KeyPreview { get; set; } = "";
    public string? KeyName { get; set; }
    public string? UserId { get; set; }
    public string? TeamId { get; set; }
    public List<string> AllowedModels { get; set; } = new();
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? LastUsedAtMs { get; set; }
}

public class OpenAiApiAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public string KeyPreview { get; set; } = "";
    public string? OrganizationId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? LastUsedAtMs { get; set; }

    public double DailyBudgetUsd { get; set; } = 0;
    public double MonthlyBudgetUsd { get; set; } = 0;
    public int AlertThresholdPct { get; set; } = 80;
}

public class OpenAiApiUsageSnapshot
{
    public long Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public double CostUsd { get; set; }
    public string PeriodStart { get; set; } = "";
    public string PeriodEnd { get; set; } = "";
}

public class AnthropicApiAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public string KeyPreview { get; set; } = "";
    public string? OrganizationId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? LastUsedAtMs { get; set; }

    public double DailyBudgetUsd { get; set; } = 0;
    public double MonthlyBudgetUsd { get; set; } = 0;
    public int AlertThresholdPct { get; set; } = 80;
}

public class AnthropicApiUsageSnapshot
{
    public long Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public double CostUsd { get; set; }
    public string PeriodStart { get; set; } = "";
    public string PeriodEnd { get; set; } = "";
}

public class GeminiPricingOverride
{
    public string ModelId { get; set; } = "";
    public double InputPricePerMTok { get; set; }
    public double OutputPricePerMTok { get; set; }
    public double CachePricePerMTok { get; set; }
}

public class GeminiAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public string KeyPreview { get; set; } = "";
    public string? ProjectId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? LastUsedAtMs { get; set; }

    public double DailyBudgetUsd { get; set; } = 0;    // 0 = disabled
    public double MonthlyBudgetUsd { get; set; } = 0;  // 0 = disabled
    public int AlertThresholdPct { get; set; } = 80;

    public string? LastAlertedWarnKey { get; set; }
    public string? LastAlertedMaxKey { get; set; }

    /// <summary>사용자가 직접 정한 릴레이 키. 비어 있으면 기본 'tracker-{alias}' 사용.</summary>
    public string? CustomRelayKey { get; set; }

    public string EffectiveRelayKey =>
        !string.IsNullOrWhiteSpace(CustomRelayKey) ? CustomRelayKey! : $"tracker-{Alias}";
}

public class GeminiUsageRecord
{
    public long Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheTokens { get; set; }
    public long ThinkingTokens { get; set; }
    public long ToolTokens { get; set; }
    public double CostUsd { get; set; }
    public int LatencyMs { get; set; }
    public string Source { get; set; } = "playground"; // "playground" | "relay"
    public string? ClientUserAgent { get; set; }
}

public class LatestUsage
{
    public double SessionPct { get; set; }
    public string? SessionResetAt { get; set; }
    public double WeekPct { get; set; }
    public string? WeekResetAt { get; set; }
    public double SubPct { get; set; }
    public string? SubResetAt { get; set; }
    public string SubModelName { get; set; } = "Sonnet";

    public bool HasDesign { get; set; }
    public double DesignPct { get; set; }
    public string? DesignResetAt { get; set; }

    public bool HasRoutine { get; set; }
    public int RoutineUsed { get; set; }
    public int RoutineLimit { get; set; }
    public string? RoutineResetAt { get; set; }

    public ExtraUsage? Extra { get; set; }
}
