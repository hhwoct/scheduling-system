namespace ShiftScheduling.Api.Application.Ai;

/// <summary>AI 文档识别：把人数需求表格/图片解析成结构化条目。</summary>
public interface IDocumentAiService
{
    Task<AiParseResponse> ParseAsync(long storeId, AiParseRequest request, CancellationToken cancellationToken);

    Task<AiTestResult> TestAsync(long storeId, CancellationToken cancellationToken);
}
