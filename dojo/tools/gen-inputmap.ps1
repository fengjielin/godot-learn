<#
  gen-inputmap.ps1 - regenerate the [input] (InputMap) section of a Godot project.godot.

  Why a script: the InputEventKey serialization is long and easy to typo by hand.
  Idempotent: it strips any existing [input] section before appending a fresh one.

  ASCII-only on purpose: Windows PowerShell 5.1 decodes BOM-less files as the system
  ANSI codepage, so trailing multi-byte characters would swallow the next line.
#>
param(
  [string]$ProjectFile = 'E:\Code\GameDev\Godot\dojo\project.godot'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ProjectFile)) { throw "project file not found: $ProjectFile" }

# --- Godot key constants (Key enum) ---
$K = @{
  Space = 32; Shift = 4194325; Tab = 4194306
  Left = 4194319; Up = 4194320; Right = 4194321; Down = 4194322
  A = 65; D = 68; W = 87; S = 83; E = 69; G = 71; J = 74; R = 82
}
# unicode (lowercase ascii) for letters, used only for display in the editor
function U([int]$code) { if ($code -ge 65 -and $code -le 90) { return $code + 32 } else { return 0 } }

$keyObj = 'Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,' +
          '"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,' +
          '"keycode":0,"physical_keycode":__PHYS__,"key_label":0,"unicode":__UNI__,"location":0,"echo":false,"script":null)'
$mouseObj = 'Object(InputEventMouseButton,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,' +
            '"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"button_mask":0,' +
            '"position":Vector2(0, 0),"global_position":Vector2(0, 0),"factor":1.0,"button_index":__BTN__,' +
            '"canceled":false,"pressed":false,"double_click":false,"script":null)'

function New-Key([int]$physical) {
  $uni = U $physical
  return $keyObj.Replace('__PHYS__', "$physical").Replace('__UNI__', "$uni")
}
function New-Mouse([int]$button) { return $mouseObj.Replace('__BTN__', "$button") }

# --- action -> list of events ---
$actions = [ordered]@{}
$actions['move_left']    = @((New-Key $K.A), (New-Key $K.Left))
$actions['move_right']   = @((New-Key $K.D), (New-Key $K.Right))
$actions['move_up']      = @((New-Key $K.W), (New-Key $K.Up))
$actions['move_down']    = @((New-Key $K.S), (New-Key $K.Down))
$actions['interact']     = @((New-Key $K.E))
$actions['sprint']       = @((New-Key $K.Shift))
$actions['dash']         = @((New-Key $K.Space))
$actions['attack']       = @((New-Key $K.J), (New-Mouse 1))
$actions['restart']      = @((New-Key $K.R))
$actions['toggle_tasks'] = @((New-Key $K.Tab))
# "spawn" intentionally shares the Space key with "dash": actions are LOGICAL,
# not physical. Two different actions may share one key, and one action may have
# several keys. That separation is the whole point of the InputMap.
$actions['spawn']        = @((New-Key $K.Space))
$actions['command']      = @((New-Key $K.G))

# --- build section text ---
$nl = "`r`n"
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('')
$lines.Add('[input]')
$lines.Add('')
$lines.Add('; InputMap: editor equivalent is Project Settings -> Input Map.')
$lines.Add('; One action may have several events; "deadzone" applies to analog sticks.')
$lines.Add('; Station s02_input walks through these concepts.')
$lines.Add('')
foreach ($name in $actions.Keys) {
  $lines.Add($name + '={')
  $lines.Add('"deadzone": 0.5,')
  $lines.Add('"events": [' + ($actions[$name] -join (',' + $nl)))
  $lines.Add(']')
  $lines.Add('}')
  $lines.Add('')
}
$section = ($lines -join $nl)

# --- strip an existing [input] section, then append ---
$text = [System.IO.File]::ReadAllText($ProjectFile)
$idx = $text.IndexOf("$nl[input]")
if ($idx -lt 0 -and $text.StartsWith('[input]')) { $idx = 0 }
if ($idx -ge 0) {
  $text = $text.Substring(0, $idx)
  Write-Host "[gen-inputmap] stripped existing [input] section"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ProjectFile, $text.TrimEnd() + $nl + $section, $utf8NoBom)
Write-Host "[gen-inputmap] wrote $($actions.Count) actions to $ProjectFile"
