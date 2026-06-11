$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "CodexApiSwitcher.cs"
$output = Join-Path (Split-Path -Parent $PSScriptRoot) "outputs\CodexApiSwitcher.exe"

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output
}

Add-Type `
    -Path $source `
    -ReferencedAssemblies @(
        "System.dll",
        "System.Core.dll",
        "System.Drawing.dll",
        "System.Security.dll",
        "System.Web.Extensions.dll",
        "System.Windows.Forms.dll"
    ) `
    -OutputAssembly $output `
    -OutputType ConsoleApplication

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
