<#
.SYNOPSIS
    Plays lessons start to finish in a test-profile Gambit window (UI Automation, no mouse) and
    reports how each step responded: the feedback shown, whether the lesson ended with 3 stars.
.DESCRIPTION
    Follows the "Next lesson" button from lesson to lesson, so -Count can run through a course.
    Needs artifacts/lesson-moves.json (dotnet run tools/lesson-moves.cs > artifacts/lesson-moves.json).
    Stars steps (the piece-movement games in Chess basics) aren't supported.
.EXAMPLE
    ./tools/play-lessons.ps1 -From tactics/fork -Count 10 -Shots artifacts/lesson-shots
#>
param(
    [Parameter(Mandatory)][string]$From,
    [int]$Count = 1,
    [string]$Shots = '',
    [string]$Moves = (Join-Path $PSScriptRoot '..\artifacts\lesson-moves.json'),
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
. (Join-Path $PSScriptRoot 'gambit-window.ps1')
$proc = Get-GambitProcess $ProcessId
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$data = Get-Content -Raw -Encoding utf8 $Moves | ConvertFrom-Json
$keys = @($data.PSObject.Properties.Name)
if ($Shots) { New-Item -ItemType Directory -Force -Path $Shots | Out-Null }

function Find([string]$id, [string]$name, [System.Windows.Automation.AutomationElement]$under = $root) {
    $cond = if ($id) { New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id) }
            else { New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name) }
    return $under.FindFirst($Scope::Descendants, $cond)
}

function Wait-For([scriptblock]$test, [string]$what, [int]$timeoutMs = 6000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $r = & $test
        if ($r) { return $r }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $what."
}

function Invoke-El($el) {
    $patterns = $el.GetSupportedPatterns()
    if ($patterns -contains [System.Windows.Automation.InvokePattern]::Pattern) {
        $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    } else {
        $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    }
}

function Text([string]$id) { $el = Find $id; if ($el) { $el.Current.Name } else { '' } }

function Shot([string]$name) {
    if ($Shots) { & (Join-Path $PSScriptRoot 'screenshot-quiet.ps1') -Out (Join-Path $Shots "$name.png") -ProcessId $proc.Id | Out-Null }
}

function Play-Uci([string]$uci) {
    if ($uci.Length -gt 4) { throw "Promotion moves aren't supported ($uci)." }
    foreach ($sq in $uci.Substring(0, 2), $uci.Substring(2, 2)) {
        Invoke-El (Wait-For { Find "sq-$sq" } "square $sq")
        Start-Sleep -Milliseconds 120
    }
}

# Open the first lesson from the Learn page.
$index = [array]::IndexOf($keys, $From)
if ($index -lt 0) { throw "Unknown lesson $From." }
Invoke-El (Find '' 'Learn')
Start-Sleep -Milliseconds 900
Invoke-El (Wait-For { Find '' "Lesson: $($data.$From.title)" } "the lesson tile")

for ($n = 0; $n -lt $Count -and $index -lt $keys.Count; $n++, $index++) {
    $key = $keys[$index]
    $lesson = $data.$key
    $steps = @($lesson.steps)
    $slug = $key.Replace('/', '-')
    Wait-For { (Text 'LessonTitle') -eq $lesson.title } "lesson '$($lesson.title)'" | Out-Null
    $notes = @()
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $step = $steps[$i]
        Wait-For { (Text 'StepCounter') -eq "$($i + 1) / $($steps.Count)" } "step $($i + 1)" | Out-Null
        Start-Sleep -Milliseconds 400
        Shot "$slug-$($i + 1)"
        switch ($step.kind) {
            'info' { }
            'quiz' {
                $answer = @($step.choices)[[int]$step.answer]
                Invoke-El (Wait-For { Find '' $answer } "the answer button '$answer'")
            }
            'moves' {
                $uci = @($step.uci)
                for ($m = 0; $m -lt $uci.Count; $m += 2) {
                    Play-Uci $uci[$m]
                    Start-Sleep -Milliseconds 300
                    if ((Text 'FeedbackText') -like 'Not quite*') { throw "$key step $($i + 1): $($uci[$m]) was rejected." }
                    if ($m + 1 -lt $uci.Count) { Start-Sleep -Milliseconds 700 }  # scripted reply after 500 ms
                }
            }
            default { throw "$key step $($i + 1): $($step.kind) steps aren't supported." }
        }
        $button = Find 'ContinueButton'
        Wait-For { $button.Current.IsEnabled } "Continue to enable on $key step $($i + 1)" | Out-Null
        if ($step.kind -ne 'info') {
            $notes += "step $($i + 1): $(Text 'FeedbackText')"
            Start-Sleep -Milliseconds 400
            Shot "$slug-$($i + 1)-done"
        }
        Invoke-El $button
    }
    $dialog = Wait-For { Find '' 'Lesson complete!' } 'the lesson-complete dialog'
    Start-Sleep -Milliseconds 300
    $texts = $dialog.FindAll($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $stars = ($texts | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match "[$([char]0x2605)$([char]0x2606)]" } | Select-Object -First 1)
    "$key - $stars"
    $notes | ForEach-Object { "    $_" }
    $next = Find 'PrimaryButton' '' $dialog
    if ($n + 1 -lt $Count -and $next -and $next.Current.IsEnabled -and $next.Current.Name) { Invoke-El $next }
    else { Invoke-El (Find 'CloseButton' '' $dialog) }
}
