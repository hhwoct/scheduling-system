-- ============================================================
-- 20260820: AI 文档识别配置表（DeepSeek API）
-- 内容：ai_configs 表，按门店保存 DeepSeek API Key / 接口地址 / 模型，
--       用于「人数需求 → AI 识别导入」把 Excel/图片解析成结构化需求。
-- 说明：api_key 明文保存（内部工具），GET 接口只返回掩码，不回传完整 Key。
--      api_key='' 为「未配置」哨兵（后端 AiConfigService 将空串视为未配置），故保留 DEFAULT ''
--      且不加 CHECK(api_key<>'')，以免破坏「空=未配置」语义；如需强制非空，需后端改为「无行=未配置」。
--      生产建议由 KMS/密钥管理注入或应用层加密存储，数据库层无法强制。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260820_add_ai_config.sql
-- ============================================================
USE shift_mvp;

CREATE TABLE IF NOT EXISTS ai_configs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  provider VARCHAR(30) NOT NULL DEFAULT 'DEEPSEEK',
  api_key VARCHAR(255) NOT NULL DEFAULT '',
  base_url VARCHAR(255) NOT NULL DEFAULT 'https://api.deepseek.com',
  model VARCHAR(80) NOT NULL DEFAULT 'deepseek-chat',
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_ai_config_store_provider (store_id, provider),
  CONSTRAINT fk_ai_config_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
