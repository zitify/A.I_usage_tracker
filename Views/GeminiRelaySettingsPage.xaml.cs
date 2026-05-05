using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Threading;
using AIUsageTracker.Services;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace AIUsageTracker.Views;

public partial class GeminiRelaySettingsPage : UserControl
{
    private string _baseUrl = "";
    private string _trackerKey = "";
    private readonly DispatcherTimer _statusTimer;

    public event Action? CloseRequested;

    private static readonly string[] ManagedVars =
    {
        "GOOGLE_API_KEY",
        "GEMINI_API_KEY",
        "GOOGLE_GENAI_BASE_URL",
        "GOOGLE_GEMINI_BASE_URL"
    };

    private const string BackupPrefix = "AI_TRACKER_BACKUP_";

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public GeminiRelaySettingsPage()
    {
        InitializeComponent();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += (_, _) => { StatusText.Text = ""; _statusTimer.Stop(); };
    }

    public void Load(int port, string effectiveKey)
    {
        _baseUrl = $"http://127.0.0.1:{port}";
        _trackerKey = effectiveKey;

        SubtitleText.Text = $"· Port {port}  ·  Key: {_trackerKey}";
        BaseUrlBox.Text = $"Base URL : {_baseUrl}\r\nAPI Key  : {_trackerKey}";

        EnvBox.Text =
            "# 이 스니펫을 본인 PowerShell 창에 붙여넣으면\r\n" +
            "# 해당 창에 즉시 적용 + 사용자 환경변수에도 영구 저장됩니다.\r\n" +
            "$vars = @{\r\n" +
            $"    GOOGLE_API_KEY         = '{_trackerKey}'\r\n" +
            $"    GEMINI_API_KEY         = '{_trackerKey}'\r\n" +
            $"    GOOGLE_GENAI_BASE_URL  = '{_baseUrl}'\r\n" +
            $"    GOOGLE_GEMINI_BASE_URL = '{_baseUrl}'\r\n" +
            "}\r\n" +
            "foreach ($k in $vars.Keys) {\r\n" +
            "    Set-Item \"env:$k\" $vars[$k]\r\n" +
            "    [Environment]::SetEnvironmentVariable($k, $vars[$k], 'User')\r\n" +
            "}";

        EnvCmdBox.Text =
            "REM 현재 cmd.exe 창에 즉시 적용 (set) + 영구 저장 (setx) 쌍\r\n" +
            $"set GOOGLE_API_KEY={_trackerKey}\r\n" +
            $"setx GOOGLE_API_KEY {_trackerKey}\r\n" +
            $"set GEMINI_API_KEY={_trackerKey}\r\n" +
            $"setx GEMINI_API_KEY {_trackerKey}\r\n" +
            $"set GOOGLE_GENAI_BASE_URL={_baseUrl}\r\n" +
            $"setx GOOGLE_GENAI_BASE_URL {_baseUrl}\r\n" +
            $"set GOOGLE_GEMINI_BASE_URL={_baseUrl}\r\n" +
            $"setx GOOGLE_GEMINI_BASE_URL {_baseUrl}";

        CurlBox.Text =
            $"curl -X POST \"{_baseUrl}/v1beta/models/gemini-2.5-flash:generateContent?key={_trackerKey}\" ^\r\n" +
            $"  -H \"Content-Type: application/json\" ^\r\n" +
            $"  -d \"{{\\\"contents\\\":[{{\\\"parts\\\":[{{\\\"text\\\":\\\"hi\\\"}}]}}]}}\"";

        PyLegacyBox.Text =
            "# pip install google-generativeai\r\n" +
            "import google.generativeai as genai\r\n" +
            "\r\n" +
            $"genai.configure(\r\n" +
            $"    api_key=\"{_trackerKey}\",\r\n" +
            $"    transport=\"rest\",\r\n" +
            $"    client_options={{\"api_endpoint\": \"{_baseUrl}\"}},\r\n" +
            ")\r\n" +
            "\r\n" +
            "model = genai.GenerativeModel(\"gemini-2.5-flash\")\r\n" +
            "print(model.generate_content(\"hi\").text)";

        PyNewBox.Text =
            "# pip install google-genai\r\n" +
            "from google import genai\r\n" +
            "from google.genai import types\r\n" +
            "\r\n" +
            $"client = genai.Client(\r\n" +
            $"    api_key=\"{_trackerKey}\",\r\n" +
            $"    http_options=types.HttpOptions(base_url=\"{_baseUrl}\"),\r\n" +
            ")\r\n" +
            "\r\n" +
            "resp = client.models.generate_content(model=\"gemini-2.5-flash\", contents=\"hi\")\r\n" +
            "print(resp.text)";

        NodeBox.Text =
            "// npm i @google/generative-ai\r\n" +
            "import { GoogleGenerativeAI } from \"@google/generative-ai\";\r\n" +
            "\r\n" +
            $"const genAI = new GoogleGenerativeAI(\"{_trackerKey}\");\r\n" +
            $"const model = genAI.getGenerativeModel(\r\n" +
            $"    {{ model: \"gemini-2.5-flash\" }},\r\n" +
            $"    {{ baseUrl: \"{_baseUrl}\" }}\r\n" +
            $");\r\n" +
            "\r\n" +
            "const r = await model.generateContent(\"hi\");\r\n" +
            "console.log(r.response.text());";

        RefreshStatus();
        RefreshRuleStatus();
    }

    // ─── Claude CLI CLAUDE.md rule install/remove ───

    private const string RuleVersion = "v1";
    private const string RuleMarkerBegin = "<!-- AI_TRACKER_GEMINI_RULE " + RuleVersion + " BEGIN";
    private const string RuleMarkerEnd = "<!-- AI_TRACKER_GEMINI_RULE " + RuleVersion + " END -->";

    private const string RuleContent = """

<!-- AI_TRACKER_GEMINI_RULE v1 BEGIN — A.I. Usage Tracker가 관리합니다. 수동 편집하지 마세요. -->

## Gemini 위임 규칙 (A.I. Usage Tracker)

사용자가 "Gemini로 X해줘", "재미나이로 X", "여기부터 여기까지는 Gemini/재미나이" 같이 특정 범위를 Gemini에 위임하면, 그 범위는 직접 답하지 말고 PowerShell 도구로 아래 명령을 실행해서 받은 응답을 사용자에게 그대로 전달한다.

```powershell
$prompt = @"
<사용자가 위임한 요청·컨텍스트를 여기에>
"@
$body = @{contents=@(@{parts=@(@{text=$prompt})})} | ConvertTo-Json -Depth 5 -Compress
(Invoke-RestMethod -Method Post `
    -Uri "$env:GOOGLE_GENAI_BASE_URL/v1beta/models/gemini-2.5-flash:generateContent?key=$env:GOOGLE_API_KEY" `
    -ContentType "application/json; charset=utf-8" `
    -Body ([Text.Encoding]::UTF8.GetBytes($body))).candidates[0].content.parts[0].text
```

- 기본 모델은 `gemini-2.5-flash`. 사용자가 "Pro로", "flash-lite로" 등 명시하면 URL의 모델명을 교체.
- 프롬프트 작성 시 따옴표·특수문자 깨짐 방지를 위해 여기-스트링(`@"..."@`) 안에 원문 그대로 넣는다.
- `$env:GOOGLE_API_KEY`가 비어 있으면 "A.I. Usage Tracker의 Gemini Local Relay 자동 설정을 적용하고 새 터미널에서 claude를 다시 시작하세요"라고 안내.
- Gemini 응답을 받으면 결과를 사용자에게 보여주고, 필요하면 그 결과를 바탕으로 이어서 작업한다.

<!-- AI_TRACKER_GEMINI_RULE v1 END -->
""";

    private static string ClaudeMdPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "CLAUDE.md");

    private static bool IsRuleInstalled()
    {
        var path = ClaudeMdPath;
        if (!File.Exists(path)) return false;
        try { return File.ReadAllText(path, Encoding.UTF8).Contains(RuleMarkerBegin); }
        catch { return false; }
    }

    private void RefreshRuleStatus()
    {
        if (IsRuleInstalled())
        {
            RuleStatusBadgeText.Text = "✓ 설치됨 — Claude CLI가 규칙을 자동 인식합니다";
            RuleStatusBadgeText.Foreground = ThemeBrush.BR("StatusGoodBrush");
            InstallRuleBtn.Visibility = Visibility.Collapsed;
            RemoveRuleBtn.Visibility = Visibility.Visible;
        }
        else
        {
            RuleStatusBadgeText.Text = "⚪ 미설치";
            RuleStatusBadgeText.Foreground = ThemeBrush.BR("TxtSubBrush");
            InstallRuleBtn.Visibility = Visibility.Visible;
            InstallRuleLabel.Text = "규칙 설치";
            RemoveRuleBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void InstallRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ClaudeMdPath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            string existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";

            if (existing.Contains(RuleMarkerBegin))
            {
                existing = StripRuleBlock(existing);
            }

            var trimmed = existing.TrimEnd('\r', '\n');
            var combined = trimmed.Length == 0
                ? RuleContent.TrimStart('\r', '\n')
                : trimmed + "\r\n" + RuleContent;

            File.WriteAllText(path, combined, new UTF8Encoding(false));
            RefreshRuleStatus();
            FlashStatus("✓ CLAUDE.md에 규칙 설치 완료 · 새 Claude 세션부터 반영됩니다", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"설치 실패: {ex.Message}", ok: false);
        }
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            $"{ClaudeMdPath}\n\n파일에서 Gemini 위임 규칙 블록만 제거합니다. 수동으로 작성한 다른 내용은 건드리지 않습니다.\n\n진행할까요?",
            "Claude CLI 규칙 제거",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (res != MessageBoxResult.OK) return;

        try
        {
            var path = ClaudeMdPath;
            if (!File.Exists(path))
            {
                RefreshRuleStatus();
                return;
            }
            var content = File.ReadAllText(path, Encoding.UTF8);
            var stripped = StripRuleBlock(content);

            if (stripped.Trim().Length == 0)
            {
                File.WriteAllText(path, "", new UTF8Encoding(false));
            }
            else
            {
                File.WriteAllText(path, stripped.TrimEnd('\r', '\n') + "\r\n", new UTF8Encoding(false));
            }
            RefreshRuleStatus();
            FlashStatus("✓ 규칙 제거 완료 · 새 Claude 세션부터 적용됩니다", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"제거 실패: {ex.Message}", ok: false);
        }
    }

    private static string StripRuleBlock(string content)
    {
        var pattern = @"\r?\n?\r?\n?" + Regex.Escape(RuleMarkerBegin) + @"[\s\S]*?" + Regex.Escape(RuleMarkerEnd) + @"\r?\n?";
        return Regex.Replace(content, pattern, "");
    }

    private string ExpectedValue(string varName) => varName.EndsWith("_KEY") ? _trackerKey : _baseUrl;

    private enum VarState { Missing, Match, Foreign }

    private VarState GetVarState(string varName)
    {
        var current = Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.User);
        if (string.IsNullOrEmpty(current)) return VarState.Missing;
        return current == ExpectedValue(varName) ? VarState.Match : VarState.Foreign;
    }

    private void RefreshStatus()
    {
        int match = 0, missing = 0, foreign = 0;
        foreach (var v in ManagedVars)
        {
            switch (GetVarState(v))
            {
                case VarState.Match: match++; break;
                case VarState.Missing: missing++; break;
                case VarState.Foreign: foreign++; break;
            }
        }

        if (match == ManagedVars.Length)
        {
            StatusBadgeText.Text = "✓ 적용됨 — 환경변수 4개 모두 트래커로 향함";
            StatusBadgeText.Foreground = ThemeBrush.BR("StatusGoodBrush");
            PrimaryActionBtn.Visibility = Visibility.Collapsed;
            RevertBtn.Visibility = Visibility.Visible;
        }
        else if (missing == ManagedVars.Length)
        {
            StatusBadgeText.Text = "⚪ 미적용 — 설정된 환경변수 없음";
            StatusBadgeText.Foreground = ThemeBrush.BR("TxtSubBrush");
            PrimaryActionLabel.Text = "자동 설정";
            PrimaryActionBtn.Visibility = Visibility.Visible;
            RevertBtn.Visibility = Visibility.Collapsed;
        }
        else if (foreign > 0)
        {
            StatusBadgeText.Text = $"⚠ 다른 값 감지됨 ({foreign}/{ManagedVars.Length}) — 적용 시 백업 후 교체";
            StatusBadgeText.Foreground = ThemeBrush.BR("StatusWarnBrush");
            PrimaryActionLabel.Text = "백업 후 자동 설정";
            PrimaryActionBtn.Visibility = Visibility.Visible;
            RevertBtn.Visibility = foreign == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            StatusBadgeText.Text = $"⚠ 부분 적용 ({match}/{ManagedVars.Length})";
            StatusBadgeText.Foreground = ThemeBrush.BR("StatusWarnBrush");
            PrimaryActionLabel.Text = "마저 적용하기";
            PrimaryActionBtn.Visibility = Visibility.Visible;
            RevertBtn.Visibility = Visibility.Visible;
        }
    }

    private void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        int foreignCount = 0;
        foreach (var v in ManagedVars)
            if (GetVarState(v) == VarState.Foreign) foreignCount++;

        if (foreignCount > 0)
        {
            var res = MessageBox.Show(
                $"현재 환경변수 중 {foreignCount}개가 트래커가 아닌 다른 값으로 설정되어 있습니다.\n\n" +
                $"기존 값을 백업({BackupPrefix}…)한 뒤 트래커 값으로 교체합니다.\n되돌리기 시 자동 복원됩니다.\n\n진행할까요?",
                "환경변수 덮어쓰기 확인",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.OK) return;
        }

        try
        {
            foreach (var v in ManagedVars)
            {
                var current = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.User);
                var expected = ExpectedValue(v);

                if (!string.IsNullOrEmpty(current) && current != expected)
                {
                    var backupName = BackupPrefix + v;
                    var existingBackup = Environment.GetEnvironmentVariable(backupName, EnvironmentVariableTarget.User);
                    if (string.IsNullOrEmpty(existingBackup))
                    {
                        Environment.SetEnvironmentVariable(backupName, current, EnvironmentVariableTarget.User);
                    }
                }

                Environment.SetEnvironmentVariable(v, expected, EnvironmentVariableTarget.User);
            }

            BroadcastEnvironmentChange();
            RefreshStatus();
            FlashStatus("✓ 자동 설정 완료 · 새로 여는 프로세스에 자동 반영됩니다", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"설정 실패: {ex.Message}", ok: false);
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "환경변수를 원래 상태로 되돌립니다.\n\n" +
            "• 백업된 값이 있으면 복원합니다.\n" +
            "• 백업이 없으면 변수를 제거합니다.\n\n진행할까요?",
            "되돌리기 확인",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (res != MessageBoxResult.OK) return;

        try
        {
            foreach (var v in ManagedVars)
            {
                var backupName = BackupPrefix + v;
                var backup = Environment.GetEnvironmentVariable(backupName, EnvironmentVariableTarget.User);

                if (!string.IsNullOrEmpty(backup))
                {
                    Environment.SetEnvironmentVariable(v, backup, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable(backupName, null, EnvironmentVariableTarget.User);
                }
                else
                {
                    Environment.SetEnvironmentVariable(v, null, EnvironmentVariableTarget.User);
                }
            }

            BroadcastEnvironmentChange();
            RefreshStatus();
            FlashStatus("✓ 되돌리기 완료 · 새로 여는 프로세스는 원래 상태로 복귀합니다", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"되돌리기 실패: {ex.Message}", ok: false);
        }
    }

    private static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout(
            new IntPtr(HWND_BROADCAST),
            WM_SETTINGCHANGE,
            UIntPtr.Zero,
            "Environment",
            SMTO_ABORTIFHUNG,
            5000,
            out _);
    }

    private void Copy(TextBox box, string label)
    {
        try
        {
            Clipboard.SetText(box.Text);
            FlashStatus($"✓ {label} 복사됨", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"복사 실패: {ex.Message}", ok: false);
        }
    }

    private void FlashStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = ThemeBrush.BR(ok ? "StatusGoodBrush" : "StatusBadBrush");
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void CopyBaseUrl_Click(object sender, RoutedEventArgs e) => Copy(BaseUrlBox, "Base URL");
    private void CopyEnv_Click(object sender, RoutedEventArgs e) => Copy(EnvBox, "PowerShell 스니펫");
    private void CopyEnvCmd_Click(object sender, RoutedEventArgs e) => Copy(EnvCmdBox, "cmd.exe 스니펫");
    private void CopyCurl_Click(object sender, RoutedEventArgs e) => Copy(CurlBox, "cURL");
    private void CopyPyLegacy_Click(object sender, RoutedEventArgs e) => Copy(PyLegacyBox, "Python (legacy)");
    private void CopyPyNew_Click(object sender, RoutedEventArgs e) => Copy(PyNewBox, "Python (genai)");
    private void CopyNode_Click(object sender, RoutedEventArgs e) => Copy(NodeBox, "Node.js");

    private void Back_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
