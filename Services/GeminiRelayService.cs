using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Models;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

/// <summary>
/// Local HTTP relay that mimics generativelanguage.googleapis.com.
/// External clients (SDKs, custom scripts) point their base URL here with a
/// placeholder key "tracker-&lt;alias&gt;"; the relay swaps in the stored real key,
/// forwards upstream, and records usage from the response.
/// </summary>
public class GeminiRelayService : IDisposable
{
    private const string UpstreamBase = "https://generativelanguage.googleapis.com";
    private const string AliasPrefix = "tracker-";

    private readonly StorageService _storage;
    private readonly GeminiAccountService _accounts;
    private readonly HttpClient _upstream = new() { Timeout = TimeSpan.FromMinutes(10) };

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action? StatusChanged;
    public event Action<GeminiUsageRecord>? UsageRecorded;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public int RequestsServed { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? StartedAt { get; private set; }

    public GeminiRelayService(StorageService storage, GeminiAccountService accounts)
    {
        _storage = storage;
        _accounts = accounts;
    }

    public bool Start(int port, out string? error)
    {
        error = null;
        if (IsRunning) return true;
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _listener = listener;
            Port = port;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => AcceptLoop(_cts.Token));
            IsRunning = true;
            StartedAt = DateTime.Now;
            LastError = null;
            Logger.Info($"GeminiRelay started on 127.0.0.1:{port}");
            StatusChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            LastError = ex.Message;
            IsRunning = false;
            _listener = null;
            Logger.Error("GeminiRelay start failed", ex);
            StatusChanged?.Invoke();
            return false;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        StartedAt = null;
        Logger.Info("GeminiRelay stopped");
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _upstream.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        string? exitReason = null;
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { exitReason = "listener disposed"; break; }
            catch (HttpListenerException ex) { exitReason = $"HttpListenerException(code={ex.ErrorCode}): {ex.Message}"; break; }
            catch (Exception ex)
            {
                Logger.Warn("GeminiRelay accept error", ex);
                exitReason = ex.Message;
                break;
            }

            var token = _cts?.Token ?? CancellationToken.None;
            _ = Task.Run(() => HandleAsync(ctx, token));
        }

        // 비정상 종료(취소·정상 Stop이 아닌 경우)면 좀비 상태 방지를 위해 IsRunning 해제 + 사유 로그
        if (!ct.IsCancellationRequested && IsRunning)
        {
            LastError = exitReason ?? "accept loop exited";
            Logger.Warn($"GeminiRelay accept loop ended unexpectedly: {LastError}");
            IsRunning = false;
            try { _listener?.Close(); } catch { }
            _listener = null;
            StartedAt = null;
            // UI 스레드에 알림 — Dispatcher 사용 (이 메서드는 ThreadPool에서 실행됨)
            try { System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusChanged?.Invoke()); } catch { }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (req.HttpMethod == "OPTIONS")
            {
                res.Headers["Access-Control-Allow-Origin"] = "*";
                res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                res.Headers["Access-Control-Allow-Headers"] = "*";
                res.StatusCode = 204;
                res.Close();
                return;
            }

            if (req.Url == null) { await WriteError(res, 400, "Missing URL"); return; }

            var path = req.Url.AbsolutePath;

            if (path == "/" || path == "/health")
            {
                res.StatusCode = 200;
                res.ContentType = "application/json";
                var buf = Encoding.UTF8.GetBytes(
                    $"{{\"ok\":true,\"service\":\"A.I. Usage Tracker — Gemini Relay\",\"accounts\":{_storage.GeminiAccounts.Count},\"requests\":{RequestsServed}}}");
                await res.OutputStream.WriteAsync(buf);
                res.Close();
                return;
            }

            if (!path.StartsWith("/v1beta/") && !path.StartsWith("/v1/"))
            {
                await WriteError(res, 404, $"Unsupported path: {path}. Relay supports /v1beta/* and /v1/*.");
                return;
            }

            var (account, aliasUsed, missing) = ResolveAccount(req);
            if (account == null)
            {
                var hint = missing
                    ? "Missing credentials. Send '?key=tracker-<alias>' or header 'x-goog-api-key: tracker-<alias>'."
                    : $"No Gemini account matches alias '{aliasUsed}'. Use an existing account alias or 'tracker-default'.";
                await WriteError(res, 401, hint);
                return;
            }

            var realKey = _accounts.GetApiKey(account.Id);
            if (string.IsNullOrEmpty(realKey))
            {
                await WriteError(res, 500, "Stored key decryption failed");
                return;
            }

            var upstreamUrl = BuildUpstreamUrl(req.Url, realKey);
            var (model, action) = ExtractModelAndAction(path);

            byte[] reqBody = Array.Empty<byte>();
            if (req.HasEntityBody)
            {
                using var ms = new MemoryStream();
                await req.InputStream.CopyToAsync(ms);
                reqBody = ms.ToArray();
            }

            using var upReq = new HttpRequestMessage(new HttpMethod(req.HttpMethod), upstreamUrl);
            if (reqBody.Length > 0)
            {
                upReq.Content = new ByteArrayContent(reqBody);
                if (!string.IsNullOrEmpty(req.ContentType))
                {
                    try
                    {
                        upReq.Content.Headers.ContentType =
                            System.Net.Http.Headers.MediaTypeHeaderValue.Parse(req.ContentType);
                    }
                    catch { }
                }
            }

            var ua = req.Headers["User-Agent"];
            if (!string.IsNullOrEmpty(ua))
                upReq.Headers.TryAddWithoutValidation("User-Agent", ua);

            var isStream = string.Equals(action, "streamGenerateContent", StringComparison.OrdinalIgnoreCase);
            var completionOption = isStream
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;

            using var upRes = await _upstream.SendAsync(upReq, completionOption, ct).ConfigureAwait(false);

            res.StatusCode = (int)upRes.StatusCode;
            foreach (var h in upRes.Headers)
                TrySetHeader(res, h.Key, string.Join(",", h.Value));
            foreach (var h in upRes.Content.Headers)
                TrySetHeader(res, h.Key, string.Join(",", h.Value));

            if (isStream && upRes.IsSuccessStatusCode)
            {
                using var upStream = await upRes.Content.ReadAsStreamAsync();
                var capture = new MemoryStream();
                var buf = new byte[8192];
                int n;
                while ((n = await upStream.ReadAsync(buf).ConfigureAwait(false)) > 0)
                {
                    await res.OutputStream.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
                    await res.OutputStream.FlushAsync().ConfigureAwait(false);
                    capture.Write(buf, 0, n);
                }
                sw.Stop();
                var captured = Encoding.UTF8.GetString(capture.ToArray());
                RecordUsageFromStream(captured, model, account.Id, ua, (int)sw.ElapsedMilliseconds);
            }
            else
            {
                var body = await upRes.Content.ReadAsByteArrayAsync();
                sw.Stop();
                if (body.Length > 0)
                    await res.OutputStream.WriteAsync(body);

                if (upRes.IsSuccessStatusCode &&
                    string.Equals(action, "generateContent", StringComparison.OrdinalIgnoreCase))
                {
                    RecordUsageFromJson(body, model, account.Id, ua, (int)sw.ElapsedMilliseconds);
                }
            }

            try { res.Close(); } catch { }
            RequestsServed++;

            account.LastUsedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _storage.Save();
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Warn("GeminiRelay handle error", ex);
            try { await WriteError(res, 502, $"Relay error: {ex.Message}"); } catch { }
        }
    }

    private (GeminiAccount? account, string? alias, bool missing) ResolveAccount(HttpListenerRequest req)
    {
        string? keyValue = req.Headers["x-goog-api-key"];

        if (string.IsNullOrEmpty(keyValue) && !string.IsNullOrEmpty(req.Url?.Query))
        {
            foreach (var (k, v) in ParseQuery(req.Url.Query))
            {
                if (string.Equals(k, "key", StringComparison.OrdinalIgnoreCase))
                {
                    keyValue = v;
                    break;
                }
            }
        }

        // Bearer token fallback (some SDKs use Authorization header for OAuth-style flows)
        if (string.IsNullOrEmpty(keyValue))
        {
            var auth = req.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                keyValue = auth.Substring(7).Trim();
        }

        if (string.IsNullOrEmpty(keyValue))
            return (null, null, true);

        if (!keyValue.StartsWith(AliasPrefix, StringComparison.OrdinalIgnoreCase))
            return (null, keyValue, false);

        var alias = keyValue.Substring(AliasPrefix.Length);
        if (string.IsNullOrWhiteSpace(alias)) return (null, alias, false);

        var acc = _storage.GeminiAccounts.FirstOrDefault(a =>
            string.Equals(a.Alias, alias, StringComparison.OrdinalIgnoreCase));

        if (acc == null &&
            (string.Equals(alias, "default", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(alias, "primary", StringComparison.OrdinalIgnoreCase)))
        {
            acc = _storage.GeminiAccounts.FirstOrDefault(a => a.IsPrimary)
               ?? _storage.GeminiAccounts.FirstOrDefault();
        }

        return (acc, alias, false);
    }

    private static string BuildUpstreamUrl(Uri reqUrl, string realKey)
    {
        var parts = ParseQuery(reqUrl.Query).ToList();
        parts.RemoveAll(kv => string.Equals(kv.Key, "key", StringComparison.OrdinalIgnoreCase));
        parts.Add(new KeyValuePair<string, string>("key", realKey));
        var qs = string.Join("&", parts.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{UpstreamBase}{reqUrl.AbsolutePath}?{qs}";
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) yield break;
        var q = query.StartsWith("?") ? query.Substring(1) : query;
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            string k, v;
            if (eq < 0) { k = pair; v = ""; }
            else { k = pair.Substring(0, eq); v = pair.Substring(eq + 1); }
            yield return new KeyValuePair<string, string>(
                Uri.UnescapeDataString(k), Uri.UnescapeDataString(v));
        }
    }

    private static (string model, string? action) ExtractModelAndAction(string path)
    {
        var last = path.Split('/').LastOrDefault() ?? "";
        var colon = last.IndexOf(':');
        if (colon < 0) return (last, null);
        return (last.Substring(0, colon), last.Substring(colon + 1));
    }

    private static void TrySetHeader(HttpListenerResponse res, string name, string value)
    {
        if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(name, "Keep-Alive", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(name, "Server", StringComparison.OrdinalIgnoreCase)) return;
        try { res.Headers[name] = value; } catch { }
    }

    private static async Task WriteError(HttpListenerResponse res, int status, string message)
    {
        try
        {
            res.StatusCode = status;
            res.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new
            {
                error = new { code = status, message, source = "ai-usage-tracker-relay" }
            });
            var buf = Encoding.UTF8.GetBytes(body);
            await res.OutputStream.WriteAsync(buf);
            res.Close();
        }
        catch { }
    }

    private void RecordUsageFromJson(byte[] body, string model, string accountId, string? ua, int latencyMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("usageMetadata", out var meta)) return;
            SaveAndNotify(BuildRecord(meta, model, accountId, ua, latencyMs));
        }
        catch (Exception ex) { Logger.Warn("GeminiRelay json parse failed", ex); }
    }

    private void RecordUsageFromStream(string body, string model, string accountId, string? ua, int latencyMs)
    {
        JsonElement? lastMeta = null;

        foreach (var raw in body.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line.Substring(5).TrimStart();
            if (string.IsNullOrEmpty(payload) || payload == "[DONE]") continue;
            try
            {
                using var d = JsonDocument.Parse(payload);
                if (d.RootElement.TryGetProperty("usageMetadata", out var m))
                    lastMeta = m.Clone();
            }
            catch { }
        }

        if (lastMeta == null)
        {
            try
            {
                using var d = JsonDocument.Parse(body);
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in d.RootElement.EnumerateArray())
                        if (el.TryGetProperty("usageMetadata", out var m))
                            lastMeta = m.Clone();
                }
                else if (d.RootElement.TryGetProperty("usageMetadata", out var m))
                {
                    lastMeta = m.Clone();
                }
            }
            catch { }
        }

        if (lastMeta == null) return;
        try { SaveAndNotify(BuildRecord(lastMeta.Value, model, accountId, ua, latencyMs)); }
        catch (Exception ex) { Logger.Warn("GeminiRelay stream parse failed", ex); }
    }

    private GeminiUsageRecord BuildRecord(JsonElement meta, string model, string accountId, string? ua, int latencyMs)
    {
        long inTok = ReadLong(meta, "promptTokenCount");
        long outTok = ReadLong(meta, "candidatesTokenCount");
        long cacheTok = ReadLong(meta, "cachedContentTokenCount");
        long thinkTok = ReadLong(meta, "thoughtsTokenCount");
        long toolTok = ReadLong(meta, "toolUsePromptTokenCount");

        var price = _storage.GetEffectivePrice(model);
        var cost = GeminiPricing.CalculateCost(price, inTok, outTok, cacheTok);

        return new GeminiUsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccountId = accountId,
            Model = model,
            InputTokens = inTok,
            OutputTokens = outTok,
            CacheTokens = cacheTok,
            ThinkingTokens = thinkTok,
            ToolTokens = toolTok,
            CostUsd = cost,
            LatencyMs = latencyMs,
            Source = "relay",
            ClientUserAgent = string.IsNullOrWhiteSpace(ua) ? null : ua
        };
    }

    private void SaveAndNotify(GeminiUsageRecord rec)
    {
        _storage.SaveGeminiUsage(rec);
        UsageRecorded?.Invoke(rec);
    }

    private static long ReadLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
