$script:TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$script:Seq = 0

function Get-HarnessDir([string]$Instance) { Join-Path $script:TestRoot "$Instance\UserData\TestHarness" }

function Get-HarnessStatus([string]$Instance) {
    $path = Join-Path (Get-HarnessDir $Instance) "status.json"
    try { Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json } catch { $null }
}

function Wait-HarnessStatus {
    param([string]$Instance, [scriptblock]$Condition, [int]$TimeoutSec = 300, [string]$What = "condition")
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $status = Get-HarnessStatus $Instance
        if ($status -and (& $Condition $status)) { return $status }
        Start-Sleep -Milliseconds 500
    }
    throw "Timeout after $TimeoutSec s waiting for $Instance : $What (last status: $(Get-HarnessStatus $Instance | ConvertTo-Json -Compress))"
}

function Send-HarnessCommand {
    param([string]$Instance, [string]$Verb, [string]$Arguments = "", [int]$TimeoutSec = 30)
    $script:Seq++
    $dir = Get-HarnessDir $Instance
    $seq = "{0}{1}" -f (Get-Date -Format "HHmmss"), $script:Seq
    $tmp = Join-Path $dir "command.tmp"
    Set-Content -LiteralPath $tmp -Value "$seq $Verb $Arguments".Trim() -Encoding ascii
    Move-Item -LiteralPath $tmp -Destination (Join-Path $dir "command.txt") -Force

    $reply = Join-Path $dir "reply_$seq.json"
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $reply) {
            $json = Get-Content -LiteralPath $reply -Raw | ConvertFrom-Json
            Remove-Item -LiteralPath $reply -Force
            if (-not $json.ok) { throw "Harness command '$Verb' failed on $Instance : $($json.error)" }
            return $json.result
        }
        Start-Sleep -Milliseconds 200
    }
    throw "No reply from $Instance to '$Verb' within $TimeoutSec s"
}

function Save-HarnessDump {
    param([string]$Instance, [string]$RunDir, [string]$Label)
    $dump = Send-HarnessCommand -Instance $Instance -Verb dump
    $dump | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $RunDir "dump_${Label}_$Instance.json") -Encoding utf8
    return $dump
}

function Wait-HarnessDump {
    param([string]$Instance, [scriptblock]$Condition, [int]$TimeoutSec = 30, [string]$What = "condition")
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $dump = $null
    while ((Get-Date) -lt $deadline) {
        $dump = Send-HarnessCommand -Instance $Instance -Verb dump
        if (& $Condition $dump) { return $dump }
        Start-Sleep -Milliseconds 500
    }
    throw "Timeout after $TimeoutSec s waiting for $Instance dump: $What (last: roster $($dump.roster | ConvertTo-Json -Compress -Depth 5), local $($dump.local | ConvertTo-Json -Compress))"
}

# Polls both dumps until the given sections are equal; throws with the names of the sections that still differ.
function Wait-HarnessDumpsEqual {
    param([string]$Left, [string]$Right, [string[]]$Sections, [int]$TimeoutSec = 30)
    $differ = @()
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $a = Send-HarnessCommand -Instance $Left -Verb dump
        $b = Send-HarnessCommand -Instance $Right -Verb dump
        $differ = @(Compare-HarnessDumps -Left $a -Right $b -Sections $Sections)
        if ($differ.Count -eq 0) { return $a }
        Start-Sleep -Milliseconds 500
    }
    throw "Timeout after $TimeoutSec s waiting for equal dumps of $Left and $Right (differ: $($differ -join ', '))"
}

function Save-HarnessScreenshot {
    param([string]$Instance, [string]$RunDir, [string]$Label)
    Send-HarnessCommand -Instance $Instance -Verb screenshot -Arguments (Join-Path $RunDir "shot_${Label}_$Instance.png") | Out-Null
}

# Returns the names of the shared-state sections (stats, inventory, cars) that differ between two dumps.
function Compare-HarnessDumps {
    param($Left, $Right, [string[]]$Sections = @("stats", "inventory", "cars"))
    $differences = @()
    foreach ($section in $Sections) {
        $a = $Left.$section | ConvertTo-Json -Depth 10 -Compress
        $b = $Right.$section | ConvertTo-Json -Depth 10 -Compress
        if ($a -ne $b) { $differences += $section }
    }
    return $differences
}

$script:ServerDir = $null
$script:ServerCommandFile = $null
$script:ServerProcess = $null
$script:ConnectAddress = "127.0.0.1"

function Initialize-TestServer {
    param([string]$ServerDir, [string]$CommandFile, [string]$ConnectAddress = "127.0.0.1")
    $script:ServerDir = $ServerDir
    $script:ServerCommandFile = $CommandFile
    $script:ConnectAddress = $ConnectAddress
}

function Connect-HarnessInstance([string]$Instance) {
    Send-HarnessCommand -Instance $Instance -Verb connect -Arguments $script:ConnectAddress | Out-Null
}

function Get-TestServerProcesses {
    $exe = Join-Path $script:ServerDir "CMS21_Together_Server.exe"
    Get-Process -Name "CMS21_Together_Server" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -ieq $exe }
}

function Get-ServerLogPath { Join-Path $script:ServerDir "Log\Latest.txt" }

function Get-ServerLogLines {
    $path = Get-ServerLogPath
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    try {
        $stream = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            return @($reader.ReadToEnd() -split "\r?\n")
        } finally { $stream.Dispose() }
    } catch { return @() }
}

# Returns a mark for Wait-ServerLog -After: the number of lines in the current server log.
function Get-ServerLogMark {
    $lines = Get-ServerLogLines
    if ($lines.Count -gt 0 -and $lines[-1] -eq "") { $lines.Count - 1 } else { $lines.Count }
}

function Wait-ServerLog {
    param([string]$Pattern, [int]$After = 0, [int]$TimeoutSec = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $lines = Get-ServerLogLines
        $start = if ($lines.Count -lt $After) { 0 } else { $After }
        for ($i = $start; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $Pattern) { return $lines[$i] }
        }
        Start-Sleep -Milliseconds 300
    }
    throw "Timeout after $TimeoutSec s waiting for server log line matching '$Pattern'"
}

function Start-TestServer {
    param([string[]]$Arguments = @())
    if (Get-TestServerProcesses) { throw "The server in $script:ServerDir is already running." }
    $exe = Join-Path $script:ServerDir "CMS21_Together_Server.exe"
    Remove-Item -LiteralPath (Get-ServerLogPath) -ErrorAction SilentlyContinue
    $script:ServerProcess = Start-Process -FilePath $exe -WorkingDirectory $script:ServerDir -WindowStyle Minimized `
        -ArgumentList (@("--command-file", "`"$script:ServerCommandFile`"") + $Arguments) -PassThru
    Start-Sleep -Milliseconds 500
    Wait-ServerLog -Pattern "Server started\. Listening port" -TimeoutSec 60 | Out-Null
    return $script:ServerProcess
}

function Stop-TestServer {
    Get-TestServerProcesses | Stop-Process -Force
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-TestServerProcesses) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    $script:ServerProcess = $null
}

function Send-ServerCommand {
    param([string]$Line)
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            [System.IO.File]::AppendAllText($script:ServerCommandFile, "$Line`r`n")
            return
        } catch { Start-Sleep -Milliseconds 100 }
    }
    throw "Could not write server command '$Line' to $script:ServerCommandFile"
}

Export-ModuleMember -Function Get-HarnessDir, Get-HarnessStatus, Wait-HarnessStatus, Send-HarnessCommand,
    Save-HarnessDump, Wait-HarnessDump, Wait-HarnessDumpsEqual, Save-HarnessScreenshot, Compare-HarnessDumps,
    Initialize-TestServer, Connect-HarnessInstance, Get-ServerLogMark, Wait-ServerLog, Start-TestServer, Stop-TestServer,
    Send-ServerCommand
