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

Export-ModuleMember -Function Get-HarnessDir, Get-HarnessStatus, Wait-HarnessStatus, Send-HarnessCommand,
    Save-HarnessDump, Save-HarnessScreenshot, Compare-HarnessDumps
