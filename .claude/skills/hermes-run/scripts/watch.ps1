<#
.SYNOPSIS
    Runs HermesProxy under `dotnet watch`, so that saved edits are patched into the running proxy.

.DESCRIPTION
    Everything given is passed to the proxy as it is (`--set KEY=VALUE`, `--config PATH`, ...).
    run.ps1 -Mode watch ends up here; a launcher that builds its own argument list can call this
    directly.

    Which edits are patched in and which restart the proxy is in the root CLAUDE.md,
    "Dev Loop: Hot Reload".

.EXAMPLE
    ./watch.ps1 --config C:\path\to\profile.json
    ./watch.ps1 --set ClientBuild=V3_4_3_54261 --set ServerAddress=127.0.0.1
#>
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$AppArgs = @()
)

$ErrorActionPreference = 'Stop'

$projectRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $projectRoot) {
    $projectRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
}
$hermesDir = Join-Path $projectRoot.Trim() 'HermesProxy'

# Debug is dotnet watch's default and the only build the runtime takes patches for.
# --verbose, because only then does dotnet watch name the rule (ENC0021, ENC0118, ...) that made
# it restart the proxy; without it the line is "Restart is needed" and nothing more.
# Without a console to answer on, dotnet watch must not stop and ask.
$dotnetArgs = @('watch', '--verbose')
if ([Console]::IsInputRedirected) { $dotnetArgs += '--non-interactive' }
$dotnetArgs += @('run', '--') + $AppArgs

# An edit dotnet watch cannot patch in (a new enum member, a method with stackalloc, a changed
# static initializer) restarts the proxy instead of asking at a prompt nobody is watching.
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = '1'

# What an edit came to (applied, compile error, restart and why) is said by dotnet watch on the
# console and nowhere else. The lines that say it are kept in a file beside the hermes logs.
# Not the proxy's lines, which are in the hermes log already. And the rest of what --verbose
# prints for every saved file (a dozen lines of its own bookkeeping) goes neither to the file nor
# to the window.
$watchLog = Join-Path $hermesDir 'bin\Debug\Logs\dotnet-watch.log'
[void](New-Item -ItemType Directory -Path (Split-Path $watchLog -Parent) -Force)
Write-Host "[hermes-watch] log: $watchLog"
$watchLines = @(
    'Files? updated:'                                   # what was saved
    'changes applied in'                                # patched in
    'Unable to apply|: error [A-Z]+\d+|ENC\d+'          # not patched in, and why
    'Restart is needed|Hot reload session started'      # a restart, and the proxy that followed it
    'Exited with error|Waiting for a file to change|Fix the error'
    '^Build (succeeded|FAILED)|^Time Elapsed'
) -join '|'
# Shown in the window, not worth a line in the file.
$windowOnly = 'Press Ctrl\+R|Building .+proj'
# Every patch repeats the project's compiler warnings. Only the ENC ones are about the edit.
$noise = ': warning (?!ENC)[A-Z]+\d+'

# dotnet watch writes UTF-8; read as the console's code page its markers turn to mojibake.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$writer = [IO.StreamWriter]::new($watchLog, $false, [Text.UTF8Encoding]::new($false))
$writer.AutoFlush = $true

# dotnet watch starts the app in its own working directory, and the proxy reads appsettings.json
# from there, so it has to be the project folder.
Push-Location $hermesDir
try {
    & dotnet @dotnetArgs 2>&1 | ForEach-Object {
        $line = [string]$_
        $text = $line.TrimStart()
        $kept = $text -match $watchLines -and $text -notmatch $noise
        if ($kept -or -not $text.StartsWith('dotnet watch') -or $text -match $windowOnly) {
            [Console]::Out.WriteLine($line)
        }
        if ($kept) {
            $writer.WriteLine("$([DateTime]::Now.ToString('HH:mm:ss')) $text")
        }
    }
    exit $LASTEXITCODE
}
finally {
    $writer.Dispose()
    Pop-Location
}
