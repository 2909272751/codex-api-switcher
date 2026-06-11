# Codex API Switcher

Windows GUI tool for switching Codex Desktop between official OpenAI login and a third-party OpenAI-compatible Responses API provider.

## Features

- Switch between official OpenAI login and a custom Responses API provider.
- Store the third-party API key with Windows DPAPI for the current Windows user.
- Preserve MCP, plugins, memory files, session content, and `auth.json`.
- Back up `config.toml`, `state_5.sqlite`, and changed session metadata before edits.
- Keep visible conversation history aligned across providers by syncing provider metadata.
- Rebuild a damaged model/provider section while preserving unrelated config.
- Single EXE on Windows 10/11; no Python or SQLite installation required.

## Files

- `outputs/CodexApiSwitcher.exe`: ready-to-run GUI.
- `outputs/使用说明.txt`: Chinese usage guide.
- `work/CodexApiSwitcher.cs`: WinForms source.
- `work/build-codex-api-switcher.ps1`: build script.
- `work/test-codex-api-switcher-exe.ps1`: regression test.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File work/build-codex-api-switcher.ps1
```

## Test

```powershell
powershell -ExecutionPolicy Bypass -File work/test-codex-api-switcher-exe.ps1
```

## Safety Notes

Do not commit your real Codex home, `credential.dat`, `settings.dat`, backups, session files, `auth.json`, or API keys. This repository intentionally ignores those files.
