# Helpers for the part-locks scenarios (locks-*.ps1): the server's "locks" output, lock-take answers and the clients'
# lock mirrors.

function Get-ServerLocks {
    param([int]$TimeoutSec = 10)
    $mark = Get-ServerLogMark
    Send-ServerCommand "/locks"
    $summary = Wait-ServerLog -Pattern "\[Locks\] locks: \d+;" -After $mark -TimeoutSec $TimeoutSec
    Start-Sleep -Milliseconds 300
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
    $start = [array]::IndexOf($lines, ($lines | Where-Object { $_ -match "\[Locks\] locks: \d+;" } | Select-Object -Last 1))
    $records = @()
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch "\[Locks\]\s+lock (\d+) client (\d+) loader (\d+) (\w+) phase (\d+) X\[([^\]]*)\] S\[([^\]]*)\]") { break }
        $records += [pscustomobject]@{
            LockId = [int]$Matches[1]; Owner = [int]$Matches[2]; Loader = [int]$Matches[3]; Kind = $Matches[4]; Phase = [int]$Matches[5]
            X = @($Matches[6] -split "," | Where-Object { $_ }); S = @($Matches[7] -split "," | Where-Object { $_ })
        }
    }
    $counters = @{}
    foreach ($m in [regex]::Matches(($summary -replace "^.*locks: \d+;", ""), "([\w\.]+) (\d+)")) { $counters[$m.Groups[1].Value] = [int]$m.Groups[2].Value }
    [pscustomobject]@{ Count = [int]([regex]::Match($summary, "locks: (\d+);").Groups[1].Value); Records = $records; Counters = $counters; Line = $summary }
}

function Get-LockCounter($Locks, [string]$Name) { if ($Locks.Counters.ContainsKey($Name)) { $Locks.Counters[$Name] } else { 0 } }

function Request-Lock {
    param([string]$Instance, [string]$Arguments, [int]$TimeoutSec = 10)
    $r = Send-HarnessCommand -Instance $Instance -Verb lock-take -Arguments $Arguments
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $answer = Send-HarnessCommand -Instance $Instance -Verb lock-take -Arguments "result $($r.requestId)"
        if ($answer.result -ne "pending") { return $answer }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $answer
}

function Get-LockMirror([string]$Instance) { (Send-HarnessCommand -Instance $Instance -Verb dump).locks }

function Wait-LockMirror {
    param([string]$Instance, [scriptblock]$Condition, [string]$What, [int]$TimeoutSec = 10)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $locks = Get-LockMirror $Instance
        if (& $Condition $locks) { return $locks }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "$Instance lock mirror: timeout waiting for $What"
}

Export-ModuleMember -Function Get-ServerLocks, Get-LockCounter, Request-Lock, Get-LockMirror, Wait-LockMirror
