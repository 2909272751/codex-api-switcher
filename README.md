# Codex API Switcher

[中文](#中文) | [English](#english)

## 中文

Codex API Switcher 是一个 Windows 图形界面工具，用来在 Codex Desktop 的官方 OpenAI 登录和第三方 OpenAI 兼容 Responses API 之间切换。

### 功能

- 在官方 OpenAI 登录和自定义 Responses API provider 之间切换。
- 使用 Windows DPAPI 为当前 Windows 用户加密保存第三方 API Key。
- 保留 MCP、插件、记忆文件、会话正文和 `auth.json`。
- 修改前自动备份 `config.toml`、检测到的新旧状态数据库和被改动的会话元数据。
- 兼容根目录旧库 `state_5.sqlite` 与新版活动库 `sqlite/state_5.sqlite`。
- 通过同步 provider 元数据，让官方模式和第三方模式看到一致的历史会话。
- 在模型/provider 配置损坏时，一键重建基础配置，同时保留无关配置。
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
- Preserve MCP, plugins, memory files, session content, and `auth.json`.
- Back up `config.toml`, detected legacy/current state databases, and changed session metadata before edits.
- Support both legacy `state_5.sqlite` and current `sqlite/state_5.sqlite` locations.
- Keep visible conversation history aligned across providers by syncing provider metadata.
- Rebuild a damaged model/provider section while preserving unrelated config.
- Single EXE on Windows 10/11; no Python or SQLite installation required.

### Files

- `outputs/CodexApiSwitcher.exe`: ready-to-run GUI.
- `outputs/使用说明.txt`: Chinese usage guide.
- `work/CodexApiSwitcher.cs`: WinForms source.
- `work/build-codex-api-switcher.ps1`: build script.
- `work/test-codex-api-switcher-exe.ps1`: regression test.

