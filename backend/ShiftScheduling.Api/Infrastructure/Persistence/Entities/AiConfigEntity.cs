namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

/// <summary>AI 文档识别配置（按门店 + 提供商，目前支持 DeepSeek）。</summary>
public sealed class AiConfigEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string Provider { get; set; } = "DEEPSEEK";

    /// <summary>
    /// API Key。明文保存（内部工具；接口仅返回掩码，日志不输出）。
    /// 风险：数据库备份/导出会包含明文 Key；接入 KMS/Vault 等密钥管理前，请控制备份与导出权限。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string Model { get; set; } = "deepseek-chat";

    public int Status { get; set; } = 1;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
