$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $workspace "outputs\CodexApiSwitcher.exe"
$root = Join-Path $PSScriptRoot ("exe-test-root-" + [guid]::NewGuid().ToString("N"))
$config = Join-Path $root "config.toml"

if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne "2.1.0.0") {
    throw "The executable version is not 2.1.0.0."
}

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
$activeStatePath = Join-Path $root "sqlite\state_5.sqlite"
$sessionDir = Join-Path $root "sessions\2026\06\11"
New-Item -ItemType Directory -Path $sessionDir | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $activeStatePath) | Out-Null
$userRollout = Join-Path $sessionDir "rollout-user-1.jsonl"
$cliRollout = Join-Path $sessionDir "rollout-cli-1.jsonl"
$bodyLine = '{"type":"event_msg","payload":{"type":"user_message","message":"正文必须保持不变"}}'
[System.IO.File]::WriteAllText($userRollout, '{"type":"session_meta","payload":{"id":"user-1","model_provider":"openai","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($cliRollout, '{"type":"session_meta","payload":{"id":"cli-1","model_provider":"OpenAI","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('create table threads(id text primary key, rollout_path text not null, source text not null, first_user_message text not null, has_user_event integer not null default 0, model_provider text not null)'); c.executemany('insert into threads values(?,?,?,?,?,?)',[('user-1',r'\\?\$userRollout','vscode','hello',0,'openai'),('cli-1',r'$cliRollout','cli','hello',0,'OpenAI'),('agent-1','', '{\""subagent\"":{}}','worker',0,'openai')]); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to create history synchronization fixture." }
Copy-Item -LiteralPath $statePath -Destination $activeStatePath

$originalPath = $env:PATH
try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    & $exe --switch-third-party --root $root --url "http://api.example.test" --model "test-model" --key "test-token-not-a-real-key"
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Third-party switch failed with exit code $LASTEXITCODE." }

$third = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($third -notmatch '(?m)^model_provider = "custom"\r?$') { throw "Third-party provider missing." }
if ($third -notmatch '(?m)^wire_api = "responses"\r?$') { throw "Responses wire API missing." }
if ($third -notmatch '(?m)^base_url = "http://api\.example\.test/v1"\r?$') { throw "Remote HTTP Base URL was rejected or not normalized to /v1." }
if ($third -notmatch '(?m)^\[model_providers\.custom\.auth\]\r?$') { throw "Credential helper missing." }
if ($third -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "MCP config was not preserved." }
if ($third -match 'test-token-not-a-real-key') { throw "Plaintext test key leaked into config." }

$thirdCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $statePath
if ($thirdCounts -ne "2,2,0") { throw "Third-party switch changed the wrong visible rows: $thirdCounts" }
$activeThirdCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $activeStatePath
if ($activeThirdCounts -ne "2,2,0") { throw "Third-party switch did not update the active SQLite database: $activeThirdCounts" }
$thirdProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($thirdProviders -ne "openai,custom,custom") { throw "Third-party history synchronization failed: $thirdProviders" }
$activeThirdProviders = python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($activeThirdProviders -ne "openai,custom,custom") { throw "Active SQLite third-party synchronization failed: $activeThirdProviders" }
$thirdUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$thirdCliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($thirdUserMeta -ne "custom" -or $thirdCliMeta -ne "custom") { throw "Third-party JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $userRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Third-party synchronization changed conversation content." }

$token = & $exe --emit-token --root $root
if ($LASTEXITCODE -ne 0) { throw "Credential helper failed with exit code $LASTEXITCODE." }
if ($token -ne "test-token-not-a-real-key") { throw "Credential helper returned the wrong token." }
$installedHelper = Join-Path $root "api-switcher\bin\CodexApiCredentialHelper.exe"
if (-not (Test-Path -LiteralPath $installedHelper)) { throw "Stable credential helper was not installed." }
$installedToken = & $installedHelper --emit-token --root $root
if ($LASTEXITCODE -ne 0) { throw "Installed credential helper failed with exit code $LASTEXITCODE." }
if ($installedToken -ne "test-token-not-a-real-key") { throw "Installed credential helper returned the wrong token." }

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
$activeOfficialProviders = python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($activeOfficialProviders -ne "openai,openai,openai") { throw "Active SQLite official synchronization failed: $activeOfficialProviders" }
$officialUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$officialCliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($officialUserMeta -ne "openai" -or $officialCliMeta -ne "openai") { throw "Official JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $cliRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Official synchronization changed conversation content." }

# A complete rollback must restore only pre-switch rows. Sessions created after
# the switch are intentionally absent from the transaction manifest and survive.
$newRollout = Join-Path $sessionDir "rollout-created-after-switch.jsonl"
[System.IO.File]::WriteAllText($newRollout, '{"type":"session_meta","payload":{"id":"new-after-switch","model_provider":"openai","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
python -c "import sqlite3; p=[r'$statePath',r'$activeStatePath']; [sqlite3.connect(x).execute('insert into threads values(?,?,?,?,?,?)',('new-after-switch',r'$newRollout','cli','new',1,'openai')).connection.commit() for x in p]"
if ($LASTEXITCODE -ne 0) { throw "Failed to create the post-switch rollback fixture." }

& $exe --rollback --root $root
if ($LASTEXITCODE -ne 0) { throw "Complete rollback failed with exit code $LASTEXITCODE." }
$rollbackConfig = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($rollbackConfig -notmatch '(?m)^model_provider = "custom"\r?$') { throw "Rollback did not restore the previous provider configuration." }
$rollbackProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($rollbackProviders -ne "openai,custom,openai,custom") { throw "Rollback removed or rewrote the post-switch session: $rollbackProviders" }
if (-not (Test-Path -LiteralPath $newRollout)) { throw "Rollback deleted a post-switch session file." }

& $exe --switch-sub2api --root $root --url "http://127.0.0.1:18080" --model "sub2-test-model" --key "sub2-test-token-not-a-real-key"
if ($LASTEXITCODE -ne 0) { throw "Sub2API switch failed with exit code $LASTEXITCODE." }

$sub2 = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($sub2 -notmatch '(?m)^model_provider = "sub2api"\r?$') { throw "Sub2API provider missing." }
if ($sub2 -notmatch '(?m)^model = "sub2-test-model"\r?$') { throw "Sub2API model missing." }
if ($sub2 -notmatch '(?m)^base_url = "http://127\.0\.0\.1:18080/v1"\r?$') { throw "Sub2API Base URL was not normalized to /v1." }
if ($sub2 -notmatch '(?m)^supports_websockets = false\r?$') { throw "Sub2API WebSocket transport was not disabled." }
if ($sub2 -notmatch '(?m)^\[model_providers\.sub2api\.auth\]\r?$') { throw "Sub2API credential helper missing." }
if ($sub2 -notmatch '"--profile", "sub2api"') { throw "Sub2API credential helper uses the wrong key profile." }
if ($sub2 -match '(?m)^\[model_providers\.custom\]\r?$') { throw "Stale custom provider section remained after Sub2API switch." }
if ($sub2 -match 'sub2-test-token-not-a-real-key') { throw "Plaintext Sub2API key leaked into config." }

$storedDrafts = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $root "api-switcher\settings.dat") -Encoding UTF8) {
    $separator = $line.IndexOf('=')
    if ($separator -gt 0) {
        $storedDrafts[$line.Substring(0, $separator)] = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($line.Substring($separator + 1)))
    }
}
if ($storedDrafts.url -ne "http://api.example.test/v1" -or $storedDrafts.thirdModel -ne "test-model") {
    throw "Sub2API switch overwrote the generic third-party draft."
}
if ($storedDrafts.sub2Url -ne "http://127.0.0.1:18080/v1" -or $storedDrafts.sub2Model -ne "sub2-test-model") {
    throw "Sub2API draft was not saved independently."
}

$sub2Token = & $exe --emit-token --root $root --profile sub2api
if ($LASTEXITCODE -ne 0 -or $sub2Token -ne "sub2-test-token-not-a-real-key") { throw "Sub2API credential profile returned the wrong token." }
$preservedCustomToken = & $exe --emit-token --root $root --profile custom
if ($LASTEXITCODE -ne 0 -or $preservedCustomToken -ne "test-token-not-a-real-key") { throw "Sub2API switch overwrote the generic third-party token." }
$installedSub2Token = & $installedHelper --emit-token --root $root --profile sub2api
if ($LASTEXITCODE -ne 0 -or $installedSub2Token -ne "sub2-test-token-not-a-real-key") { throw "Installed helper could not read the Sub2API token." }

$mockPortFile = Join-Path $root "mock-sub2api-port.txt"
$mockServer = Start-Process -FilePath "python" -ArgumentList @((Join-Path $PSScriptRoot "mock-sub2api-server.py"), $mockPortFile) -PassThru -WindowStyle Hidden
try {
    for ($attempt = 0; $attempt -lt 50 -and -not (Test-Path -LiteralPath $mockPortFile); $attempt++) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $mockPortFile)) { throw "The mock Sub2API server did not start." }
    $mockPort = (Get-Content -LiteralPath $mockPortFile -Raw -Encoding ASCII).Trim()
    $mockUrl = "http://127.0.0.1:$mockPort"
    $sub2Probe = & $exe --test-provider --root $root --profile sub2api --url $mockUrl --model "sub2-test-model"
    if ($LASTEXITCODE -ne 0 -or $sub2Probe -notmatch "PASS:") { throw "Sub2API Responses/SSE/compact preflight failed." }
    $sub2Models = & $exe --list-models --root $root --profile sub2api --url $mockUrl
    if ($LASTEXITCODE -ne 0 -or ($sub2Models | Select-String "sub2-test-model").Count -ne 1) { throw "Sub2API model discovery failed." }
} finally {
    if ($mockServer -and -not $mockServer.HasExited) { Stop-Process -Id $mockServer.Id -Force }
}

$sub2Providers = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($sub2Providers -ne "openai,sub2api,sub2api,sub2api") { throw "Sub2API history synchronization failed: $sub2Providers" }
$activeSub2Providers = python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($activeSub2Providers -ne "openai,sub2api,sub2api,sub2api") { throw "Active SQLite Sub2API synchronization failed: $activeSub2Providers" }
$sub2UserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$sub2CliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$sub2NewMeta = (Get-Content -LiteralPath $newRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($sub2UserMeta -ne "sub2api" -or $sub2CliMeta -ne "sub2api" -or $sub2NewMeta -ne "sub2api") { throw "Sub2API JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $userRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Sub2API synchronization changed conversation content." }

if (-not [string]::IsNullOrWhiteSpace($env:CODEX_TEST_BINARY) -and (Test-Path -LiteralPath $env:CODEX_TEST_BINARY)) {
    $previousCodexHome = $env:CODEX_HOME
    try {
        $env:CODEX_HOME = $root
        & $env:CODEX_TEST_BINARY features list *> $null
        if ($LASTEXITCODE -ne 0) { throw "The installed Codex CLI rejected the generated Sub2API config." }
    } finally {
        $env:CODEX_HOME = $previousCodexHome
    }
}

& $exe --switch-official --root $root --model "official-test-model"
if ($LASTEXITCODE -ne 0) { throw "Official switch after rollback failed with exit code $LASTEXITCODE." }

& $exe --switch-third-party --root $root --url "http://api.example.test" --model "test-model" --key "test-token-not-a-real-key"
if ($LASTEXITCODE -ne 0) { throw "Third-party setup before reset failed with exit code $LASTEXITCODE." }

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

[model_providers.sub2api]
name = "also-broken"
wire_api = "responses"
'@
[System.IO.File]::WriteAllText($config, $brokenFixture, [System.Text.UTF8Encoding]::new($false))

& $exe --reset-config --root $root --model "reset-official-model"
if ($LASTEXITCODE -ne 0) { throw "Model configuration reset failed with exit code $LASTEXITCODE." }

$reset = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($reset -notmatch '(?m)^model_provider = "openai"\r?$') { throw "Reset did not restore model_provider." }
if ($reset -notmatch '(?m)^model = "reset-official-model"\r?$') { throw "Reset did not restore the official model." }
if ($reset -match '(?m)^\[model_providers\.custom\]\r?$') { throw "Reset did not remove the broken custom provider section." }
if ($reset -match '(?m)^\[model_providers\.sub2api\]\r?$') { throw "Reset did not remove the broken Sub2API provider section." }
if ($reset -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Reset removed MCP configuration." }
if ($reset -notmatch '(?m)^disable_response_storage = true\r?$') { throw "Reset removed unrelated top-level configuration." }
$resetProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($resetProviders -ne "openai,openai,openai,openai") { throw "Reset left conversation rows assigned to a removed custom provider: $resetProviders" }
$resetUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($resetUserMeta -ne "openai") { throw "Reset left JSONL metadata assigned to a removed custom provider." }

$backups = Get-ChildItem -LiteralPath (Join-Path $root "config-switcher-backups") -File -Filter "config.toml.*.bak"
if ($backups.Count -lt 3) { throw "Expected automatic backups." }
$historyBackups = Get-ChildItem -LiteralPath (Join-Path $root "history_sync_backups") -File -Filter "state_5.sqlite.*.pre-provider-sync.*.bak"
if ($historyBackups.Count -lt 4) { throw "Expected automatic backups for both history databases." }
$sessionBackups = Get-ChildItem -LiteralPath (Join-Path $root "history_sync_backups") -Recurse -File -Filter "*rollout-*.jsonl"
if ($sessionBackups.Count -lt 4) { throw "Expected automatic JSONL metadata backups." }

python -c "import tomllib; d=tomllib.load(open(r'$config','rb')); assert d['model_provider']=='openai'; assert d['model']=='reset-official-model'; assert d['mcp_servers']['example']['command']=='example.exe'; assert 'custom' not in d.get('model_providers',{}); assert 'sub2api' not in d.get('model_providers',{})"
if ($LASTEXITCODE -ne 0) { throw "Generated TOML is invalid." }

python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('update threads set has_user_event=0'); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to reset sidebar repair fixture." }
python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); c.execute('update threads set has_user_event=0'); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to reset active sidebar repair fixture." }

try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    & $exe --repair-sidebar --root $root
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Sidebar repair failed with exit code $LASTEXITCODE." }

$counts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $statePath
if ($counts -ne "3,3,0") { throw "Sidebar repair changed the wrong thread rows: $counts" }
$activeCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $activeStatePath
if ($activeCounts -ne "3,3,0") { throw "Sidebar repair did not update the active SQLite database: $activeCounts" }

Write-Output "PASS: Official, generic, and Sub2API switching; separate DPAPI credentials; Sub2API models/Responses/SSE/compact preflight; WebSocket disablement; real Codex config parsing when available; root and active SQLite synchronization; incremental rollback; provider cleanup; unchanged conversation content; stable credential helper execution; model reset; URL normalization; backups; TOML parsing; sidebar repair; and config preservation."
