$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $workspace "outputs\CodexApiSwitcher.exe"
$root = Join-Path $PSScriptRoot ("exe-test-root-" + [guid]::NewGuid().ToString("N"))
$config = Join-Path $root "config.toml"

New-Item -ItemType Directory -Path $root | Out-Null

$fixture = @'
model_provider = "custom"
model = "gpt-5.5"
disable_response_storage = true

[windows]
sandbox = "unelevated"

[mcp_servers.example]
command = "example.exe"

[model_providers.custom]
name = "old"
wire_api = "responses"
requires_openai_auth = true
base_url = "https://old.example"
'@
[System.IO.File]::WriteAllText($config, $fixture, [System.Text.UTF8Encoding]::new($false))

$statePath = Join-Path $root "state_5.sqlite"
$sessionDir = Join-Path $root "sessions\2026\06\11"
New-Item -ItemType Directory -Path $sessionDir | Out-Null
$userRollout = Join-Path $sessionDir "rollout-user-1.jsonl"
$cliRollout = Join-Path $sessionDir "rollout-cli-1.jsonl"
$bodyLine = '{"type":"event_msg","payload":{"type":"user_message","message":"正文必须保持不变"}}'
[System.IO.File]::WriteAllText($userRollout, '{"type":"session_meta","payload":{"id":"user-1","model_provider":"openai","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($cliRollout, '{"type":"session_meta","payload":{"id":"cli-1","model_provider":"OpenAI","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('create table threads(id text primary key, rollout_path text not null, source text not null, first_user_message text not null, has_user_event integer not null default 0, model_provider text not null)'); c.executemany('insert into threads values(?,?,?,?,?,?)',[('user-1',r'\\?\$userRollout','vscode','hello',0,'openai'),('cli-1',r'$cliRollout','cli','hello',0,'OpenAI'),('agent-1','', '{\""subagent\"":{}}','worker',0,'openai')]); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to create history synchronization fixture." }

$originalPath = $env:PATH
try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    & $exe --switch-third-party --root $root --url "https://api.example.test" --model "test-model" --key "test-token-not-a-real-key"
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Third-party switch failed with exit code $LASTEXITCODE." }

$third = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($third -notmatch '(?m)^model_provider = "custom"\r?$') { throw "Third-party provider missing." }
if ($third -notmatch '(?m)^wire_api = "responses"\r?$') { throw "Responses wire API missing." }
if ($third -notmatch '(?m)^base_url = "https://api\.example\.test/v1"\r?$') { throw "Base URL was not normalized to /v1." }
if ($third -notmatch '(?m)^\[model_providers\.custom\.auth\]\r?$') { throw "Credential helper missing." }
if ($third -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "MCP config was not preserved." }
if ($third -match 'test-token-not-a-real-key') { throw "Plaintext test key leaked into config." }

$thirdCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $statePath
if ($thirdCounts -ne "2,2,0") { throw "Third-party switch changed the wrong visible rows: $thirdCounts" }
$thirdProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($thirdProviders -ne "openai,custom,custom") { throw "Third-party history synchronization failed: $thirdProviders" }
$thirdUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$thirdCliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($thirdUserMeta -ne "custom" -or $thirdCliMeta -ne "custom") { throw "Third-party JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $userRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Third-party synchronization changed conversation content." }

$token = & $exe --emit-token --root $root
if ($LASTEXITCODE -ne 0) { throw "Credential helper failed with exit code $LASTEXITCODE." }
if ($token -ne "test-token-not-a-real-key") { throw "Credential helper returned the wrong token." }

try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    & $exe --switch-official --root $root --model "official-test-model"
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Official switch failed with exit code $LASTEXITCODE." }

$official = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($official -notmatch '(?m)^model_provider = "openai"\r?$') { throw "Official provider missing." }
if ($official -notmatch '(?m)^model = "official-test-model"\r?$') { throw "Official model missing." }
if ($official -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Official switch changed MCP config." }

$officialProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($officialProviders -ne "openai,openai,openai") { throw "Official history synchronization failed: $officialProviders" }
$officialUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$officialCliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($officialUserMeta -ne "openai" -or $officialCliMeta -ne "openai") { throw "Official JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $cliRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Official synchronization changed conversation content." }

$brokenFixture = @'
model = "broken-model"
disable_response_storage = true

[windows]
sandbox = "unelevated"

[mcp_servers.example]
command = "example.exe"

[model_providers.custom]
name = "broken"
wire_api = "responses"
'@
[System.IO.File]::WriteAllText($config, $brokenFixture, [System.Text.UTF8Encoding]::new($false))

& $exe --reset-config --root $root --model "reset-official-model"
if ($LASTEXITCODE -ne 0) { throw "Model configuration reset failed with exit code $LASTEXITCODE." }

$reset = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($reset -notmatch '(?m)^model_provider = "openai"\r?$') { throw "Reset did not restore model_provider." }
if ($reset -notmatch '(?m)^model = "reset-official-model"\r?$') { throw "Reset did not restore the official model." }
if ($reset -match '(?m)^\[model_providers\.custom\]\r?$') { throw "Reset did not remove the broken custom provider section." }
if ($reset -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Reset removed MCP configuration." }
if ($reset -notmatch '(?m)^disable_response_storage = true\r?$') { throw "Reset removed unrelated top-level configuration." }

$backups = Get-ChildItem -LiteralPath (Join-Path $root "config-switcher-backups") -File -Filter "config.toml.*.bak"
if ($backups.Count -lt 3) { throw "Expected automatic backups." }
$historyBackups = Get-ChildItem -LiteralPath (Join-Path $root "history_sync_backups") -File -Filter "state_5.sqlite.pre-provider-sync.*.bak"
if ($historyBackups.Count -lt 2) { throw "Expected automatic history database backups." }
$sessionBackups = Get-ChildItem -LiteralPath (Join-Path $root "history_sync_backups") -Recurse -File -Filter "*rollout-*.jsonl"
if ($sessionBackups.Count -lt 4) { throw "Expected automatic JSONL metadata backups." }

python -c "import tomllib; d=tomllib.load(open(r'$config','rb')); assert d['model_provider']=='openai'; assert d['model']=='reset-official-model'; assert d['mcp_servers']['example']['command']=='example.exe'; assert 'custom' not in d.get('model_providers',{})"
if ($LASTEXITCODE -ne 0) { throw "Generated TOML is invalid." }

python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('update threads set has_user_event=0'); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to reset sidebar repair fixture." }

try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    & $exe --repair-sidebar --root $root
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Sidebar repair failed with exit code $LASTEXITCODE." }

$counts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $statePath
if ($counts -ne "2,2,0") { throw "Sidebar repair changed the wrong thread rows: $counts" }

Write-Output "PASS: Persistent SQLite and JSONL provider synchronization, unchanged conversation content, Python-free switching, model configuration reset, URL normalization, DPAPI storage, backups, TOML parsing, sidebar repair, and config preservation."
