using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Application.Ai;

public sealed record WorkstationRef(long Id, string Code, string Name);

public sealed class DocumentAiService : IDocumentAiService
{
    private const int MaxSheetRows = 1500;
    private const int MaxSheetCols = 40;
    private const int MaxImageBytes = 6 * 1024 * 1024;

    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAiConfigService _aiConfigService;
    private readonly IHttpClientFactory _httpClientFactory;

    public DocumentAiService(
        ShiftSchedulingDbContext dbContext,
        IAiConfigService aiConfigService,
        IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _aiConfigService = aiConfigService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AiParseResponse> ParseAsync(long storeId, AiParseRequest request, CancellationToken cancellationToken)
    {
        var (apiKey, baseUrl, model) = await _aiConfigService.GetEffectiveAsync(storeId, cancellationToken);

        var workstations = await _dbContext.Workstations.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new WorkstationRef(x.Id, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        var systemPrompt = BuildSystemPrompt(workstations.Select(x => x.Name + "(" + x.Code + ")"));
        var userContent = BuildUserContent(request);

        var rawContent = await CallDeepSeekAsync(apiKey, baseUrl, model, systemPrompt, userContent, cancellationToken);
        var parsed = ParseModelOutput(rawContent, workstations);

        return new AiParseResponse(parsed.Entries.Count, parsed.Warnings.Count, parsed.Entries, parsed.Warnings);
    }

    public async Task<AiTestResult> TestAsync(long storeId, CancellationToken cancellationToken)
    {
        var (apiKey, baseUrl, model) = await _aiConfigService.GetEffectiveAsync(storeId, cancellationToken);
        try
        {
            var reply = await CallDeepSeekAsync(
                apiKey,
                baseUrl,
                model,
                "你是连通性测试助手。无论用户说什么，只回复两个字符：OK",
                new { role = "user", content = "测试" },
                cancellationToken,
                jsonMode: false);
            var ok = reply.Trim().ToUpperInvariant().StartsWith("OK");
            return new AiTestResult(true, "连接成功，模型回复：" + (ok ? "OK" : reply.Trim()));
        }
        catch (BusinessException ex)
        {
            return new AiTestResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            return new AiTestResult(false, "连接失败：" + ex.Message);
        }
    }

    // ============ Prompt 与内容 ============

    private static string BuildSystemPrompt(IEnumerable<string> workstationNames)
    {
        return $$"""
你是排班系统的数据解析助手。把用户提供的表格或图片中的「人数需求」解析为结构化 JSON。

规则：
1. 日期类型只能是三个值之一：WORKDAY（平日）、WEEKEND（周末）、HOLIDAY（节假日）。识别不出时按工作表名/表格上下文推断；实在推断不出放入 skipped。
2. 工作站必须匹配下列已知工作站：{{string.Join("、", workstationNames)}}。名称模糊时选最接近的一个；匹配不上放入 skipped。
3. 时段为 30 分钟对齐的 HH:mm（00:00-23:30）。"8:00" 规范化为 "08:00"；"08:00-08:30" 取开始时间；时间序列小数（如 0.5）换算为 12:00。
4. 人数语义：单数字 N = 最少 N 且最好 N；"M,N" 或 "(M,N)" = 最少 M 最好 N；空白视为 0。min、ideal 都是 0-99 的整数，ideal 不小于 min。
5. 输出必须是唯一一个 JSON 对象，不要输出任何 JSON 以外的文字，格式：
{"entries":[{"dayType":"WORKDAY","workstation":"前台","timeSlot":"18:00","min":2,"ideal":3}],"skipped":[{"reason":"第3行：无法识别"}]}
""";
    }

    private static object BuildUserContent(AiParseRequest request)
    {
        if (request.Kind == "image")
        {
            if (string.IsNullOrWhiteSpace(request.ImageBase64))
            {
                throw new BusinessException("图片内容为空", "INVALID_AI_IMAGE");
            }

            // 3.1 修复：MIME 白名单 + 大小上限（MaxImageBytes 此前从未被使用）
            var mime = (string.IsNullOrWhiteSpace(request.ImageMimeType) ? "image/png" : request.ImageMimeType)
                .Trim().ToLowerInvariant();
            var allowedMimes = new[] { "image/png", "image/jpeg", "image/jpg", "image/webp", "image/gif" };
            if (!allowedMimes.Contains(mime))
            {
                throw new BusinessException("仅支持 png/jpeg/webp/gif 格式的图片", "INVALID_AI_IMAGE_MIME");
            }

            // base64 长度上限按 6MB 原始字节折算（base64 约 4:3 膨胀）
            if (request.ImageBase64.Length > (long)MaxImageBytes * 4 / 3)
            {
                throw new BusinessException("图片过大（最大 6MB）", "AI_IMAGE_TOO_LARGE");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.ImageBase64, "^[A-Za-z0-9+/=]+$"))
            {
                throw new BusinessException("图片数据格式不正确", "INVALID_AI_IMAGE");
            }

            return new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = "请识别图片中的排班人数需求表格并解析为 JSON。" },
                    new { type = "image_url", image_url = new { url = "data:" + mime + ";base64," + request.ImageBase64 } }
                }
            };
        }

        var rows = request.Rows ?? new List<List<string>>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("文件名：" + (request.FileName ?? "未知"));
        sb.AppendLine("表格内容（行号 | 单元格，制表符分隔）：");
        var rowCount = Math.Min(rows.Count, MaxSheetRows);
        for (var i = 0; i < rowCount; i++)
        {
            var cells = rows[i].Take(MaxSheetCols).Select(c => (c ?? string.Empty).Replace("\t", " ").Replace("\n", " "));
            sb.AppendLine((i + 1) + " | " + string.Join("\t", cells));
        }

        return new { role = "user", content = sb.ToString() };
    }

    // ============ DeepSeek 调用 ============

    private async Task<string> CallDeepSeekAsync(
        string apiKey,
        string baseUrl,
        string model,
        string systemPrompt,
        object userMessage,
        CancellationToken cancellationToken,
        bool jsonMode = true)
    {
        var http = _httpClientFactory.CreateClient("DeepSeek");
        http.Timeout = TimeSpan.FromSeconds(120);

        // json_object 模式要求提示词中出现 json 字样（DeepSeek/OpenAI 校验），兜底追加
        var effectiveSystem = systemPrompt;
        if (jsonMode && !systemPrompt.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            effectiveSystem = systemPrompt + "\n请只输出 JSON。";
        }

        var messages = new object[]
        {
            new { role = "system", content = effectiveSystem },
            userMessage
        };

        object payload = jsonMode
            ? new { model, messages, temperature = 0, max_tokens = 30000, response_format = new { type = "json_object" } }
            : new { model, messages, temperature = 0, max_tokens = 4096 };

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errMsg = ExtractErrorMessage(body);
            throw new BusinessException(
                "DeepSeek 接口调用失败（HTTP " + (int)response.StatusCode + "）：" + errMsg,
                "AI_API_ERROR");
        }

        using var doc = JsonDocument.Parse(body);
        // 3.4 修复：校验 choices/message 结构，避免空数组或缺字段时抛未处理异常 → 500
        if (!doc.RootElement.TryGetProperty("choices", out var choicesEl) ||
            choicesEl.ValueKind != JsonValueKind.Array ||
            choicesEl.GetArrayLength() == 0)
        {
            throw new BusinessException("DeepSeek 返回格式异常（缺少 choices）", "AI_API_ERROR");
        }

        var choice = choicesEl[0];
        if (!choice.TryGetProperty("message", out var message))
        {
            throw new BusinessException("DeepSeek 返回格式异常（缺少 message）", "AI_API_ERROR");
        }

        var content = message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
            ? contentEl.GetString()
            : null;
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

        if (string.IsNullOrWhiteSpace(content))
        {
            // 推理模型（如 deepseek-v4-pro）可能把全部 token 花在推理上导致输出为空
            var reason = message.TryGetProperty("reasoning_content", out var rc) && rc.GetString() is { Length: > 0 } r
                ? "（推理耗尽 token，输出被截断）"
                : string.Empty;
            throw new BusinessException(
                "DeepSeek 返回内容为空" + reason + "（finish_reason=" + finishReason + "）",
                "AI_EMPTY_RESPONSE");
        }

        return content;
    }

    private static string ExtractErrorMessage(string body)
    {
        // 模型不支持图片输入时的友好提示（DeepSeek 返回反序列化错误）
        if (body.Contains("image_url", StringComparison.Ordinal) || body.Contains("unknown variant", StringComparison.Ordinal))
        {
            return "当前模型不支持图片输入。请改用 Excel/表格文件，或在 AI 设置中更换为支持视觉识别的模型";
        }

        try
        {
            // 3.5 修复：仅提取 JSON 错误对象的 message 字段并截断；
            // 非 JSON 错误体不回显，避免 BaseUrl 指向内部服务时把任意响应透传给客户端
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) &&
                msg.GetString() is { Length: > 0 } message)
            {
                return message.Length > 200 ? message[..200] : message;
            }
        }
        catch
        {
            // 非 JSON 错误体
        }

        return "接口调用失败，请检查 AI 配置";
    }

    // ============ 输出解析与校验 ============

    private static (List<AiParsedEntry> Entries, List<string> Warnings) ParseModelOutput(
        string content,
        IReadOnlyList<WorkstationRef> workstations)
    {
        var entries = new List<AiParsedEntry>();
        var warnings = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("entries", out var entriesEl) || entriesEl.ValueKind != JsonValueKind.Array)
            {
                return (entries, new List<string> { "模型未返回 entries 数组" });
            }

            foreach (var item in entriesEl.EnumerateArray())
            {
                var dayType = NormalizeDayType(item.TryGetProperty("dayType", out var dt) ? dt.GetString() : null);
                if (dayType is null)
                {
                    warnings.Add("跳过：日期类型无法识别（" + JsonSerializer.Serialize(item) + "）");
                    continue;
                }

                var wsName = item.TryGetProperty("workstation", out var ws) ? ws.GetString() : null;
                var wsId = ResolveWorkstation(wsName, workstations);
                if (wsId is null)
                {
                    warnings.Add("跳过：工作站「" + wsName + "」不存在");
                    continue;
                }

                var slot = item.TryGetProperty("timeSlot", out var ts) ? ts.GetString() : null;
                if (!TryNormalizeSlot(slot, out var slotHhmm))
                {
                    warnings.Add("跳过：时段「" + slot + "」无效（需 30 分钟对齐）");
                    continue;
                }

                var (min, ideal) = ParseCount(item);
                if (min is null || ideal is null)
                {
                    warnings.Add("跳过：人数无效（" + JsonSerializer.Serialize(item) + "）");
                    continue;
                }

                entries.Add(new AiParsedEntry(dayType, wsId.Value, slotHhmm, min.Value, ideal.Value));
            }

            if (root.TryGetProperty("skipped", out var skippedEl) && skippedEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in skippedEl.EnumerateArray())
                {
                    var reason = s.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        warnings.Add(reason);
                    }
                }
            }

            return (entries, warnings);
        }
        catch (Exception ex)
        {
            return (entries, new List<string> { "模型输出解析失败：" + ex.Message });
        }
    }

    private static string? NormalizeDayType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = value.Trim().ToUpperInvariant();
        if (s is "WORKDAY" or "WD" || value.Contains("平日") || value.Contains("工作日") || value.Contains("平常"))
        {
            return "WORKDAY";
        }

        if (s is "WEEKEND" or "WE" || value.Contains("周末") || value.Contains("周六") || value.Contains("周日") || value.Contains("双休"))
        {
            return "WEEKEND";
        }

        if (s is "HOLIDAY" or "HD" || value.Contains("节假日") || value.Contains("假期") || value.Contains("节日"))
        {
            return "HOLIDAY";
        }

        return null;
    }

    private static long? ResolveWorkstation(string? name, IReadOnlyList<WorkstationRef> workstations)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var s = name.Trim();
        var norm = s.ToLowerInvariant();
        foreach (var w in workstations)
        {
            if (string.Equals(w.Name, s, StringComparison.Ordinal) || string.Equals(((string)w.Name).ToLowerInvariant(), norm, StringComparison.Ordinal))
            {
                return w.Id;
            }
        }

        foreach (var w in workstations)
        {
            if (string.Equals(((string)w.Code).ToLowerInvariant(), norm, StringComparison.Ordinal))
            {
                return w.Id;
            }
        }

        var stripped = s.Replace("工作站", "").Replace("工位", "").Replace("岗位", "").Trim();
        if (stripped != s && stripped.Length > 0)
        {
            var n2 = stripped.ToLowerInvariant();
            foreach (var w in workstations)
            {
                if (string.Equals(((string)w.Name).ToLowerInvariant(), n2, StringComparison.Ordinal))
                {
                    return w.Id;
                }
            }
        }

        return null;
    }

    private static bool TryNormalizeSlot(string? value, out string slot)
    {
        slot = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var s = value.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(s, "(?<h>[0-9]{1,2}):(?<m>[0-9]{2})(?::[0-9]{2})?");
        if (!match.Success)
        {
            // 尝试 Excel 时间序列小数
            if (double.TryParse(s, out var frac) && frac >= 0 && frac < 1)
            {
                var totalMin = (int)Math.Round(frac * 24 * 60);
                if (totalMin % 30 == 0 && totalMin < 1440)
                {
                    slot = $"{(totalMin / 60):D2}:{(totalMin % 60):D2}";
                    return true;
                }
            }

            return false;
        }

        var h = int.Parse(match.Groups["h"].Value);
        var m = int.Parse(match.Groups["m"].Value);
        if (h > 23 || m > 59 || m % 30 != 0)
        {
            return false;
        }

        slot = $"{h:D2}:{m:D2}";
        return true;
    }

    private static (int? Min, int? Ideal) ParseCount(JsonElement item)
    {
        if (!item.TryGetProperty("min", out var minEl) || !item.TryGetProperty("ideal", out var idealEl))
        {
            return (null, null);
        }

        var min = ParseSingleCount(minEl);
        var ideal = ParseSingleCount(idealEl);
        if (min is null || ideal is null)
        {
            return (null, null);
        }

        var minValue = min.Value;
        var idealValue = ideal.Value;
        if (minValue < 0 || minValue > 99 || idealValue > 99)
        {
            return (null, null);
        }

        return (minValue, Math.Max(minValue, idealValue));
    }

    private static int? ParseSingleCount(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number)
        {
            return el.GetInt32();
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString()?.Trim();
            if (int.TryParse(s, out var n))
            {
                return n;
            }

            // "2,3" 形式：取第一个数字作 min 时外层已分开，这里只处理单数字
        }

        return null;
    }
}
