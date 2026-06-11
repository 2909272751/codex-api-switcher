# Codex API Switcher Code Map

本文件索引官方登录与第三方 Responses API 切换工具。

最近完整验收提交：`unknown`

## 先看这里

| 目标 | 主要文件 | 配套测试 | 验证命令 |
| --- | --- | --- | --- |
| 使用 GUI 切换模式 | `outputs/CodexApiSwitcher.exe` | `work/test-codex-api-switcher-exe.ps1` | `powershell -ExecutionPolicy Bypass -File work/test-codex-api-switcher-exe.ps1` |
| 修改 GUI、切换或基础配置恢复逻辑 | `work/CodexApiSwitcher.cs` | `work/test-codex-api-switcher-exe.ps1` | `powershell -ExecutionPolicy Bypass -File work/build-codex-api-switcher.ps1` |
| 维护旧版脚本 | `outputs/codex-api-switcher.ps1` | `work/test-codex-api-switcher.ps1` | `powershell -ExecutionPolicy Bypass -File work/test-codex-api-switcher.ps1` |

## End-To-End Flow

```text
用户选择 Codex 根目录和模式
-> 读取 <CODEX_HOME>\config.toml
-> 创建 config-switcher-backups 时间戳备份
-> 第三方 Key 以当前 Windows 用户 DPAPI 密文保存到 api-switcher
-> 根 URL 自动规范为 Codex 所需的 /v1 API 基址
-> 通过 Windows winsqlite3.dll 备份 state_5.sqlite
-> 从 threads.rollout_path 定位顶层用户会话 JSONL
-> 逐文件备份并原子更新 JSONL 第一行 payload.model_provider
-> 同步 state_5.sqlite 中的 model_provider 与 has_user_event
-> 修改 model/model_provider/model_providers.custom
-> 同目录临时文件覆盖 config.toml
-> 重启 Codex 后生效
```

## Code Map

### GUI 与 API 模式切换

`outputs/CodexApiSwitcher.exe`

面向用户的 WinForms 单文件程序。支持选择 Codex 根目录、填写 URL/Key/模型、查看当前状态、切换官方/第三方模式、恢复最近备份，以及在模型配置损坏时一键重建官方基础配置。
每次切换都会先确认 Codex 已退出，备份 `state_5.sqlite`，再通过 `threads.rollout_path` 定位 `vscode`/`cli` 顶层用户会话。工具逐文件备份并原子修改 JSONL 第一行的 `payload.model_provider`，随后同步数据库的 `model_provider` 与 `has_user_event`。会话正文和后续 JSONL 行保持不变。独立的“修复会话列表”仍可只修复可见标记。
“一键恢复基础配置”会先备份 `config.toml`，补回顶层 `model_provider`/`model`，移除损坏的 `model_providers.custom` 段，并保留 MCP、插件、沙箱等其他配置；第三方切换时会重新生成 custom provider。

`work/CodexApiSwitcher.cs`

GUI 与切换核心源码。EXE 同时提供 `--emit-token` 凭据助手模式，供 Codex 获取 DPAPI 解密后的第三方 Key。SQLite 操作通过 P/Invoke 调用 Windows 自带 `winsqlite3.dll`，不启动 Python 或外部 SQLite 程序。

`work/build-codex-api-switcher.ps1`

使用 Windows PowerShell 自带 C# 编译器构建 EXE，不依赖额外 SDK。

### 旧版脚本

`outputs/codex-api-switcher.ps1`

保留的命令行版切换工具。

### 测试

`work/test-codex-api-switcher.ps1`

使用隔离配置副本验证双向切换、自动备份、MCP 配置保留以及明文 Key 不落盘。

`work/test-codex-api-switcher-exe.ps1`

验证 EXE 在 PATH 不含 Python 时仍可双向切换，同时同步 SQLite 与 JSONL provider 元数据，确认会话正文不变，并覆盖侧栏修复、基础配置重建、Base URL `/v1` 规范化、DPAPI 凭据助手、自动备份、TOML 解析和无关配置保留。

## Known Runtime Notes

- EXE 只允许修改所选根目录内的 `config.toml`、`config-switcher-backups`、`api-switcher`、`history_sync_backups` 和 `state_5.sqlite`。
- 运行依赖仅为 Windows 10/11 自带的 .NET Framework 与 `winsqlite3.dll`；不需要 Python 或另装 SQLite。
- 每次切换会更新顶层用户会话 JSONL 第一行的 `payload.model_provider`，并更新 `state_5.sqlite` 中对应线程的 `model_provider` 与 `has_user_event`；每个 JSONL 修改前都会备份。
- JSONL 备份使用“原路径哈希 + 原文件名”的扁平命名，避免长 Codex 根目录触发 Windows 路径长度限制。
- Codex 数据库中的 `rollout_path` 可能使用 `\\?\` Windows 扩展路径前缀；工具会在安全校验和文件访问前移除该前缀，避免把 `?` 误判为非法字符。
- 不修改 JSONL 后续会话正文、`memories*`、`session_index.jsonl` 或 `auth.json`。
- 切换前必须彻底退出 Codex；否则工具会中止，避免数据库并发写入。
- DPAPI 密文只能由生成密文的当前 Windows 用户在本机解密。
- 切换配置后必须重启 Codex。
- `wire_api = "responses"` 的自定义 provider 需要 API 基址包含 `/v1`；站点根 URL 会导致 Codex 请求到网页路由并表现为 `stream closed before response.completed`。
- 侧栏文件仍在但列表为空时，先检查 `threads.has_user_event`；修复前必须退出 Codex并使用 SQLite backup API 备份数据库。
