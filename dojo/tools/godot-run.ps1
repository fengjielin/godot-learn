<#
  Run Godot with a hard timeout and force-kill, so a crash dialog can never hang the caller.

  Usage:
    powershell -ExecutionPolicy Bypass -File godot-run.ps1 -ProjectPath <dir> `
        [-Frames 120] [-TimeoutSec 60] [-Scene res://x.tscn] [-Editor] [-Import] [-GodotVerbose]

  Exit codes: 0 = clean, 1 = ERROR/WARNING found or timed out, 2 = Godot binary missing.

  NOTE (important): keep this file ASCII-only.
  Windows PowerShell 5.1 decodes BOM-less files as the system ANSI codepage (GBK here),
  so a trailing multi-byte character can swallow the following newline and silently
  delete the next line of code. English comments only - no exceptions.
#>
param(
  [Parameter(Mandatory = $true)][string]$ProjectPath,
  [int]$Frames = 120,
  [int]$TimeoutSec = 60,
  [string]$Scene = '',
  [string]$WriteMovie = '',
  [string]$Resolution = '1280x720',
  [string[]]$UserArg = @(),
  [switch]$Editor,
  [switch]$Import,
  [switch]$GodotVerbose
)

$ErrorActionPreference = 'Stop'
$Godot = if ($env:GODOT_BIN) { $env:GODOT_BIN } else { 'E:\software\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' }

if (-not (Test-Path $Godot)) { Write-Error "Godot not found: $Godot"; exit 2 }
$ProjectPath = (Resolve-Path $ProjectPath).Path

# --write-movie needs a real rendering driver, so it must NOT run with --headless.
# That is the only way to visually verify HUD/layout/font rendering from a script.
$argList = @('--disable-crash-handler')
if (-not $WriteMovie) { $argList += '--headless' }
if ($GodotVerbose) { $argList += '--verbose' }
$argList += @('--path', $ProjectPath)
if ($Import) { $argList += '--import' }
elseif (-not $Editor) {
  $argList += @('--quit-after', "$Frames")
  if ($Resolution) { $argList += @('--resolution', $Resolution) }
  if ($Scene) { $argList += @('--scene', $Scene) }
  if ($WriteMovie) { $argList += @('--write-movie', $WriteMovie) }
}

# Godot exposes only what follows "--" through OS.GetCmdlineUserArgs().
if ($UserArg.Count -gt 0) { $argList += '--'; $argList += $UserArg }

$logDir = Join-Path $ProjectPath '.logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$outFile = Join-Path $logDir 'godot.stdout.log'
$errFile = Join-Path $logDir 'godot.stderr.log'
Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue

# --- Isolate user:// ---
# Godot resolves user:// to %APPDATA%\Godot\app_userdata\<project>. The DSH file sandbox
# only allows writes inside the workspace, so Godot fails at "Could not create directory:
# 'user://logs'" and then HANGS. That is an environment limit, not a project bug.
# Pointing the child's APPDATA at a workspace-local dir keeps verification self-contained.
$fakeAppData = Join-Path $logDir 'appdata'
New-Item -ItemType Directory -Force -Path $fakeAppData | Out-Null
$prevAppData = $env:APPDATA
$env:APPDATA = $fakeAppData

Write-Host "[godot-run] $Godot $($argList -join ' ')" -ForegroundColor Cyan
$timedOut = $false
try {
  $proc = Start-Process -FilePath $Godot -ArgumentList $argList -WorkingDirectory $ProjectPath `
    -PassThru -NoNewWindow -RedirectStandardOutput $outFile -RedirectStandardError $errFile

  if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
    $timedOut = $true
    Write-Host "[godot-run] TIMEOUT after ${TimeoutSec}s -> killing pid $($proc.Id)" -ForegroundColor Red
    # Kill(bool) only exists on .NET Core+; PS 5.1 / .NET Framework needs the parameterless overload.
    try { $proc.Kill($true) } catch { try { $proc.Kill() } catch { } }
    Start-Sleep -Milliseconds 800
  }
}
finally {
  $env:APPDATA = $prevAppData
}

# Sweep up any Godot process left behind by a crash.
Get-Process -Name 'Godot*' -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }

# Godot writes UTF-8; PS 5.1 would otherwise decode it as ANSI/GBK and mangle CJK text.
$stdout = if (Test-Path $outFile) { Get-Content $outFile -Raw -Encoding UTF8 } else { '' }
$stderr = if (Test-Path $errFile) { Get-Content $errFile -Raw -Encoding UTF8 } else { '' }

if ($stdout) { Write-Host "`n----- stdout -----" -ForegroundColor DarkGray; $stdout.TrimEnd() | Write-Host }
if ($stderr) { Write-Host "`n----- stderr -----" -ForegroundColor DarkGray; $stderr.TrimEnd() | Write-Host }

# Godot writes engine/script errors to stderr. Its exit code is unreliable (often 0 on crash),
# so the presence of problem lines is what decides pass/fail.
#
# Known environment noise (NOT a project problem), ignored on purpose:
#   "Failed to read the root certificate store" -> the sandbox blocks the Windows cert store.
$ignorePatterns = @(
  'Failed to read the root certificate store',
  'get_system_ca_certificates'
)

$problems = @()
foreach ($line in (($stdout + "`n" + $stderr) -split "`r?`n")) {
  if ($line -notmatch '^\s*(ERROR|SCRIPT ERROR|USER ERROR|FATAL|WARNING):') { continue }
  $trimmed = $line.Trim()
  $ignored = $false
  foreach ($pat in $ignorePatterns) { if ($trimmed -match $pat) { $ignored = $true; break } }
  if (-not $ignored) { $problems += $trimmed }
}
if ($timedOut) { $problems += 'TIMEOUT (process had to be killed)' }

Write-Host ''
$unique = $problems | Select-Object -Unique
if ($unique.Count -gt 0) {
  Write-Host "[godot-run] RESULT: FAIL ($($unique.Count) distinct problem line(s))" -ForegroundColor Red
  $unique | Select-Object -First 40 | ForEach-Object { Write-Host "   $_" -ForegroundColor Yellow }
  exit 1
}
Write-Host "[godot-run] RESULT: OK" -ForegroundColor Green
exit 0
