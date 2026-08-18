namespace ShiftScheduling.Api.Application.Ai;

/// <summary>AI 配置（返回端）；ApiKey 只回传掩码。</summary>
public sealed record AiConfigItem(
    bool Configured,
    string Provider,
    string BaseUrl,
    string Model,
    string MaskedKey);

/// <summary>保存 AI 配置；ApiKey 留空表示保持现有 Key 不变。</summary>
public sealed record AiConfigSaveRequest(
    string? ApiKey,
    string? BaseUrl,
    string? Model);

/// <summary>
/// AI 解析请求。Kind = sheet 时用 Rows（表格内容）；Kind = image 时用 ImageBase64 + ImageMimeType。
/// </summary>
public sealed record AiParseRequest(
    string Kind,
    string? FileName,
    List<List<string>>? Rows,
    string? ImageBase64,
    string? ImageMimeType);

/// <summary>AI 解析出的单条人数需求（工作站已解析为 Id）。</summary>
public sealed record AiParsedEntry(
    string DayType,
    long WorkstationId,
    string TimeSlot,
    int RequiredCount,
    int IdealCount);

/// <summary>AI 解析结果；Warnings 为跳过的条目说明。</summary>
public sealed record AiParseResponse(
    int Parsed,
    int Skipped,
    List<AiParsedEntry> Entries,
    List<string> Warnings);

/// <summary>AI 连通性测试结果。</summary>
public sealed record AiTestResult(
    bool Success,
    string Message);
