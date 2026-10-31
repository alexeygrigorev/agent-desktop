# agent-desktop Windows helper (Windows PowerShell 5.1+, user scope).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File helper.ps1 -Request <base64 UTF-8 JSON>
#        (or -RequestFile <path to a file holding the base64 request>, for long text)
# Prints one line "AGENT_DESKTOP_RESULT:<base64 UTF-8 JSON>".
param([string]$Request, [string]$RequestFile)

$ErrorActionPreference = 'Stop'

function Write-Result([string]$json) {
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    [Console]::Out.WriteLine('AGENT_DESKTOP_RESULT:' + $b64)
}

try {
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AgentDesktop.cs'))
    $refs = @('System.Drawing', 'System.Web.Extensions')

    if (-not ('AgentDesktop.Helper' -as [type])) {
        # Cache the compiled helper per source hash under %LOCALAPPDATA% so
        # only the first call pays the compile cost.
        $sha = [Security.Cryptography.SHA256]::Create()
        $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source))) -replace '-', '').Substring(0, 16)
        $base = $env:LOCALAPPDATA
        if (-not $base) { $base = [IO.Path]::GetTempPath() }
        $cacheDir = Join-Path $base 'agent-desktop\cache'
        $dll = Join-Path $cacheDir "AgentDesktop-$hash.dll"
        if (-not (Test-Path -LiteralPath $dll)) {
            try {
                [void](New-Item -ItemType Directory -Force -Path $cacheDir)
                $tmp = "$dll.$PID.tmp"
                Add-Type -TypeDefinition $source -ReferencedAssemblies $refs -OutputAssembly $tmp -OutputType Library
                Move-Item -LiteralPath $tmp -Destination $dll -Force
            } catch { }
        }
        if (-not ('AgentDesktop.Helper' -as [type]) -and (Test-Path -LiteralPath $dll)) {
            try { Add-Type -LiteralPath $dll } catch { }
        }
        if (-not ('AgentDesktop.Helper' -as [type])) {
            Add-Type -TypeDefinition $source -ReferencedAssemblies $refs
        }
    }

    if ($RequestFile) { $Request = [IO.File]::ReadAllText($RequestFile).Trim() }
    if (-not $Request) { throw 'no request given' }
    $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Request))
    Write-Result ([AgentDesktop.Helper]::Run($json))
    exit 0
} catch {
    $msg = $_.Exception.Message -replace '\\', '\\' -replace '"', '\"' -replace "`r", '' -replace "`n", '\n' -replace "`t", '\t'
    Write-Result ('{"ok":false,"error":"' + $msg + '"}')
    exit 1
}
