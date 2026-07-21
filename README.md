# Codex API Switcher

> Enhanced fork maintained by [@2909272751](https://github.com/2909272751), based on
> [yin-yizhen/codex-api-switcher](https://github.com/yin-yizhen/codex-api-switcher).
> This version adds transactional rollback, provider compatibility checks, model
> discovery, stable credential-helper installation, and stronger configuration safety.

[中文](#中文) | [English](#english)

## 中文

Codex API Switcher 是一个 Windows 图形界面工具，用来在 Codex Desktop 的官方 OpenAI 登录和第三方 OpenAI 兼容 Responses API 之间切换。

### 功能

- 在官方 OpenAI 登录和自定义 Responses API provider 之间切换。
- 使用 Windows DPAPI 为当前 Windows 用户加密保存第三方 API Key。
- 在切换前测试 Responses API、SSE、工具定义和远程压缩，并可读取 `/v1/models`。
- 测试接口或读取模型后保留尚未提交的 URL、模型和 Key 输入。
- 保留 MCP、插件、记忆文件、会话正文和 `auth.json`。
- 修改前自动备份 `config.toml`、检测到的新旧状态数据库和被改动的会话元数据。
- 兼容根目录旧库 `state_5.sqlite` 与新版活动库 `sqlite/state_5.sqlite`。
- 通过同步 provider 元数据，让官方模式和第三方模式看到一致的历史会话。
- 使用事务清单执行增量完整回滚：恢复旧会话字段，同时保留切换后新建的会话。
- 将 credential helper 安装到 Codex 数据目录的稳定路径，移动主程序不影响取 Key。
- 解析并校验 TOML 结构，写入损坏配置前停止操作。
- 检查 Codex 进程和 SQLite 文件锁；允许 HTTP provider，但远程 HTTP 会显示明文传输警告。
- 记住切换前的官方模型，不再依赖硬编码模型名。
- 在模型/provider 配置损坏时，一键重建基础配置，同时保留无关配置。
- 基础配置恢复时同步清理残留的 `custom` 会话元数据，避免引用已移除 provider。
- Windows 10/11 单 EXE 运行，不需要安装 Python 或 SQLite。

### 文件

- `outputs/CodexApiSwitcher.exe`：可直接运行的图形界面程序。
- `outputs/使用说明.txt`：中文使用说明。
- `work/CodexApiSwitcher.cs`：WinForms 源码。
- `work/build-codex-api-switcher.ps1`：构建脚本。
- `work/test-codex-api-switcher-exe.ps1`：回归测试。


## English

Codex API Switcher is a Windows GUI tool for switching Codex Desktop between official OpenAI login and a third-party OpenAI-compatible Responses API provider.

### Features

- Switch between official OpenAI login and a custom Responses API provider.
- Store the third-party API key with Windows DPAPI for the current Windows user.
- Probe Responses API, SSE, tool schema, remote compaction, and `/v1/models` before switching.
- Preserve unsaved URL, model, and key input after provider tests or model discovery.
- Preserve MCP, plugins, memory files, session content, and `auth.json`.
- Back up `config.toml`, detected legacy/current state databases, and changed session metadata before edits.
- Support both legacy `state_5.sqlite` and current `sqlite/state_5.sqlite` locations.
- Keep visible conversation history aligned across providers by syncing provider metadata.
- Use transaction manifests for incremental full rollback while preserving sessions created after a switch.
- Install the credential helper at a stable location under the Codex data directory.
- Validate TOML structure, Codex process state, SQLite locks, and transport security before writes.
- Remember the previous official model instead of relying on a hard-coded model name.
- Rebuild a damaged model/provider section while preserving unrelated config.
- Reset stale conversation provider metadata when rebuilding the official configuration.
- Single EXE on Windows 10/11; no Python or SQLite installation required.

### Files

- `outputs/CodexApiSwitcher.exe`: ready-to-run GUI.
- `outputs/使用说明.txt`: Chinese usage guide.
- `work/CodexApiSwitcher.cs`: WinForms source.
- `work/build-codex-api-switcher.ps1`: build script.
- `work/test-codex-api-switcher-exe.ps1`: regression test.
