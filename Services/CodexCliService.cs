using System.IO;
using System.Text.Json;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

/// <summary>
/// Best-effort parser for OpenAI Codex CLI session logs at ~/.codex/sessions/rollout-*.jsonl.
/// Each rollout file records a single Codex CLI session as JSON-Lines events; we look for
/// usage payloads that carry input/output/cached token counts and a model id.
/// </summary>
public class CodexCliService
{
    public string SessionsDir { get; }

    public CodexCliService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SessionsDir = Path.Combine(home, ".codex", "sessions");
    }

    public class CodexModelRow
    {
        public string Model { get; set; } = "";
        public int Sessions { get; set; }
        public long Input { get; set; }
        public long Output { get; set; }
        public long Cached { get; set; }
        public double Cost { get; set; }
        public string InputDisplay => Input.ToString("N0");
        public string OutputDisplay => Output.ToString("N0");
        public string CachedDisplay => Cached.ToString("N0");
        public string CostDisplay => $"${Cost:F4}";
    }

    public class CodexSessionRow
    {
        public string Status { get; set; } = "";          // 활성 / 종료 / 오류
        public string StatusToken { get; set; } = "good"; // good / warn / high / bad
        public string Project { get; set; } = "";          // cwd basename
        public string Task { get; set; } = "";             // 첫 user 메시지 발췌
        public DateTime LastWriteLocal { get; set; }
        public string LastDisplay { get; set; } = "";      // 5m / 2h / 어제 / Mon
        public long Tokens { get; set; }
        public string TokenDisplay => Tokens >= 1000 ? $"{Tokens / 1000.0:F1}k" : Tokens.ToString("N0");
        public string Model { get; set; } = "";
        public double Cost { get; set; }
    }

    public class CodexSummary
    {
        public List<CodexModelRow> Models { get; set; } = new();
        public List<CodexSessionRow> Sessions { get; set; } = new();
        public int SessionsTotal { get; set; }
        public long InputTotal { get; set; }
        public long OutputTotal { get; set; }
        public double CostTotal { get; set; }

        // Phase 2 신규
        public long TodayTokens { get; set; }
        public long Last7dAvgTokens { get; set; }
        public int[] HourlyTokens { get; set; } = new int[12]; // 최근 12시간 버킷 (오래된→최신)
    }

    public CodexSummary Aggregate(DateTimeOffset since)
    {
        var summary = new CodexSummary();
        if (!Directory.Exists(SessionsDir)) return summary;

        var perModel = new Dictionary<string, CodexModelRow>();
        var sessionFiles = 0;
        var nowLocal = DateTime.Now;
        var todayStart = nowLocal.Date;
        var sevenDaysAgo = todayStart.AddDays(-7);
        var twelveHoursAgo = nowLocal.AddHours(-12);

        long todayTokens = 0;
        long last7dTokensExclToday = 0;
        var hourly = new long[12];

        foreach (var path in EnumerateSessionFiles(SessionsDir))
        {
            try
            {
                var fi = new FileInfo(path);
                var lastWriteLocal = fi.LastWriteTime;
                if (fi.LastWriteTimeUtc < since.UtcDateTime) continue;

                var parsed = ParseFile(path);
                if (!parsed.touched) continue;

                sessionFiles++;
                var key = string.IsNullOrEmpty(parsed.model) ? "unknown" : parsed.model;
                if (!perModel.TryGetValue(key, out var row))
                {
                    row = new CodexModelRow { Model = key };
                    perModel[key] = row;
                }
                row.Sessions++;
                row.Input += parsed.input;
                row.Output += parsed.output;
                row.Cached += parsed.cached;
                var sessionCost = OpenAiPricing.CalculateCost(key, parsed.input, parsed.output, parsed.cached);
                row.Cost += sessionCost;

                var totalTokens = parsed.input + parsed.output;

                // 오늘 / 7일 평균
                if (lastWriteLocal >= todayStart)
                    todayTokens += totalTokens;
                else if (lastWriteLocal >= sevenDaysAgo)
                    last7dTokensExclToday += totalTokens;

                // 12h 히스토그램 (1시간 버킷)
                if (lastWriteLocal >= twelveHoursAgo)
                {
                    var hoursAgo = (int)Math.Floor((nowLocal - lastWriteLocal).TotalHours);
                    var bucket = 11 - Math.Clamp(hoursAgo, 0, 11); // 오래된 → 0, 최신 → 11
                    hourly[bucket] += totalTokens;
                }

                // 세션 행 누적
                var minutesIdle = (nowLocal - lastWriteLocal).TotalMinutes;
                string status;
                string statusToken;
                if (minutesIdle < 5)         { status = "활성";   statusToken = "good"; }
                else if (minutesIdle < 60)   { status = "최근";   statusToken = "warn"; }
                else                          { status = "종료";   statusToken = "high"; }

                summary.Sessions.Add(new CodexSessionRow
                {
                    Status = status,
                    StatusToken = statusToken,
                    Project = parsed.project,
                    Task = parsed.task,
                    LastWriteLocal = lastWriteLocal,
                    LastDisplay = FormatRelative(nowLocal, lastWriteLocal),
                    Tokens = totalTokens,
                    Model = parsed.model,
                    Cost = sessionCost,
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Codex parse failed: {path}", ex);
            }
        }

        summary.Models = perModel.Values.OrderByDescending(r => r.Cost).ToList();
        summary.Sessions = summary.Sessions
            .OrderByDescending(s => s.LastWriteLocal)
            .Take(20)
            .ToList();
        summary.SessionsTotal = sessionFiles;
        summary.InputTotal = summary.Models.Sum(r => r.Input);
        summary.OutputTotal = summary.Models.Sum(r => r.Output);
        summary.CostTotal = summary.Models.Sum(r => r.Cost);
        summary.TodayTokens = todayTokens;
        summary.Last7dAvgTokens = last7dTokensExclToday / 7;
        for (int i = 0; i < 12; i++)
            summary.HourlyTokens[i] = (int)Math.Min(int.MaxValue, hourly[i]);
        return summary;
    }

    private static string FormatRelative(DateTime now, DateTime then)
    {
        var diff = now - then;
        if (diff.TotalMinutes < 1) return "방금";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h";
        if (diff.TotalDays < 2) return "어제";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}일";
        return then.ToString("MM/dd");
    }

    private static IEnumerable<string> EnumerateSessionFiles(string dir)
    {
        // Codex stores files as ~/.codex/sessions/<yyyy>/<mm>/<dd>/rollout-*.jsonl in newer
        // builds and flat in older builds. EnumerateFiles with recursive search covers both.
        return Directory.EnumerateFiles(dir, "rollout-*.jsonl", SearchOption.AllDirectories);
    }

    private record ParseResult(bool touched, string model, long input, long output, long cached, string project, string task);

    private static ParseResult ParseFile(string path)
    {
        string model = "";
        string project = "";
        string task = "";
        long input = 0, output = 0, cached = 0;
        bool hasTotalUsage = false;  // true = new format found; don't accumulate legacy lines

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                // Model: check payload.model first (turn_context, response_item, etc.),
                // then fall back to root-level model field
                if (string.IsNullOrEmpty(model))
                {
                    if (root.TryGetProperty("payload", out var pModel))
                    {
                        var m = FindString(pModel, "model");
                        if (!string.IsNullOrEmpty(m)) model = m;
                    }
                    if (string.IsNullOrEmpty(model))
                    {
                        var rootModel = FindString(root, "model");
                        if (!string.IsNullOrEmpty(rootModel)) model = rootModel;
                    }
                }

                // cwd → project basename
                if (string.IsNullOrEmpty(project))
                {
                    var cwd = FindString(root, "cwd");
                    if (string.IsNullOrEmpty(cwd) && root.TryGetProperty("payload", out var pCwd))
                        cwd = FindString(pCwd, "cwd");
                    if (!string.IsNullOrEmpty(cwd))
                    {
                        try { project = new DirectoryInfo(cwd).Name; }
                        catch { project = cwd; }
                    }
                }

                // 첫 user 메시지 → task 발췌
                if (string.IsNullOrEmpty(task) &&
                    root.TryGetProperty("payload", out var pTask) &&
                    pTask.ValueKind == JsonValueKind.Object)
                {
                    var role = FindString(pTask, "role");
                    if (role == "user")
                    {
                        var text = ExtractFirstText(pTask);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            text = text.Trim().Replace('\n', ' ').Replace('\r', ' ');
                            task = text.Length > 60 ? text.Substring(0, 60) + "…" : text;
                        }
                    }
                }

                // New Codex CLI format (v0.128+):
                //   { "type": "event_msg", "payload": { "type": "token_count",
                //     "info": { "total_token_usage": { "input_tokens": N, ... } } } }
                // total_token_usage is already cumulative for the session, so we overwrite.
                if (root.TryGetProperty("payload", out var payload) &&
                    FindString(payload, "type") == "token_count" &&
                    payload.TryGetProperty("info", out var info) &&
                    info.ValueKind == JsonValueKind.Object &&
                    info.TryGetProperty("total_token_usage", out var ttu) &&
                    ttu.ValueKind == JsonValueKind.Object)
                {
                    input  = FindLong(ttu, "input_tokens", "prompt_tokens");
                    output = FindLong(ttu, "output_tokens", "completion_tokens");
                    cached = FindLong(ttu, "cached_input_tokens", "input_cached_tokens", "cached_tokens");
                    hasTotalUsage = true;
                    continue;
                }

                // Legacy format: usage/token_usage/tokens at root level
                if (!hasTotalUsage)
                {
                    var usage = FindObject(root, "usage")
                             ?? FindObject(root, "token_usage")
                             ?? FindObject(root, "tokens");
                    if (usage is { } u)
                    {
                        input  += FindLong(u, "input_tokens", "prompt_tokens", "total_input_tokens");
                        output += FindLong(u, "output_tokens", "completion_tokens", "total_output_tokens");
                        cached += FindLong(u, "input_cached_tokens", "cached_tokens", "cache_read_input_tokens");
                    }
                }
            }
            catch (JsonException) { /* skip malformed line */ }
        }
        bool touched = hasTotalUsage || input > 0 || output > 0;
        return new ParseResult(touched, model, input, output, cached, project, task);
    }

    private static string ExtractFirstText(JsonElement payload)
    {
        // content can be a string or an array of {type, text}
        if (payload.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? "";
            if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        return item.GetString() ?? "";
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var t = FindString(item, "text");
                        if (!string.IsNullOrEmpty(t)) return t;
                    }
                }
            }
        }
        return FindString(payload, "text");
    }

    private static string FindString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        return "";
    }

    private static JsonElement? FindObject(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object)
                return v;
        return null;
    }

    private static long FindLong(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt64();
        return 0;
    }
}
