#Requires -Version 5.1
<#
Collects the CMS21 Together logs for a bug report into one zip on the Desktop, without the game running.
Run it (or Collect-Logs.bat) from the game folder or from a server folder. From the game folder it also takes the
logs of the server in TogetherServer\ (or the one CMS21Together.ServerPath points to); from TogetherServer\ it also
takes the game's logs.

The zip has the layout of the in-game bug report (F8): client\ and server\, each with info.json. Passwords, admin
keys and Steam tokens are replaced by <redacted>, UserData\CMS21Together\player.json (your identity key) is left out.
The server save is only included with -IncludeSave, and then without the players' identity keys.
#>
param(
    [switch]$IncludeSave,
    [string]$GameDir,
    [string]$ServerDir,
    [string]$OutDir = [Environment]::GetFolderPath("Desktop"),
    [switch]$NoExplorer
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$Redacted = "<redacted>"
$NewestLogs = 5
$NewestBundles = 3
$MinScrubLength = 4
$IdentityFile = "player.json"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Test-SecretName([string]$Name) {
    if (-not $Name -or $Name.IndexOf("Hotkey", [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $false }
    foreach ($word in @("token", "password", "secret", "key")) {
        if ($Name.IndexOf($word, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Split-Lines([string]$Text) {
    if ($null -eq $Text) { $Text = "" }
    return $Text.Replace("`r`n", "`n").TrimEnd("`n").Split("`n")
}

function Get-EntryParts([string]$Line) {
    $trimmed = $Line.TrimStart()
    if ($trimmed.Length -eq 0 -or $trimmed[0] -in @('#', ';', '[')) { return $null }
    $equals = $Line.IndexOf('=')
    if ($equals -le 0) { return $null }
    [pscustomobject]@{
        Name = $Line.Substring(0, $equals).Trim().Trim('"')
        Left = $Line.Substring(0, $equals).TrimEnd()
        Value = $Line.Substring($equals + 1).Trim()
    }
}

function ConvertTo-RedactedConfig([string]$Text) {
    if (-not $Text) { return $Text }
    $lines = foreach ($line in Split-Lines $Text) {
        $entry = Get-EntryParts $line
        if (-not $entry -or -not (Test-SecretName $entry.Name) -or $entry.Value.Length -eq 0 -or $entry.Value -eq '""') { $line; continue }
        if ($entry.Value.StartsWith('"')) { "$($entry.Left) = `"$Redacted`"" } else { "$($entry.Left) = $Redacted" }
    }
    return ($lines -join "`r`n") + "`r`n"
}

function Select-PreferenceCategories([string]$Toml, [string]$Prefix) {
    $keep = $false
    $kept = New-Object System.Text.StringBuilder
    foreach ($line in Split-Lines $Toml) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("[")) {
            $keep = $trimmed.Trim('[', ']', ' ').Trim('"').StartsWith($Prefix, [StringComparison]::OrdinalIgnoreCase)
        }
        if ($keep) { [void]$kept.Append($line).Append("`r`n") }
    }
    return $kept.ToString()
}

function Get-ConfigSecretValues([string]$Text) {
    foreach ($line in Split-Lines $Text) {
        $entry = Get-EntryParts $line
        if (-not $entry -or -not (Test-SecretName $entry.Name)) { continue }
        $value = $entry.Value
        if ($value.StartsWith('"')) {
            $end = $value.IndexOf('"', 1)
            if ($end -gt 0) { $value = $value.Substring(1, $end - 1) }
        } elseif ($value.Contains('#')) {
            $value = $value.Split('#')[0].Trim()
        }
        if ($value) { $value.Replace('\\', '\') }
    }
}

function Get-JsonProperties($Node) {
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            [pscustomobject]@{ Owner = $Node; Property = $property }
            Get-JsonProperties $property.Value
        }
    } elseif ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) { Get-JsonProperties $item }
    }
}

# Windows PowerShell's ConvertTo-Json writes < > ' & as unicode escapes; JSON allows them as they are.
function ConvertTo-JsonText($Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 100
    $escape = [string][char]92 + "u00"
    return $json.Replace("${escape}3c", "<").Replace("${escape}3e", ">").Replace("${escape}27", "'").Replace("${escape}26", "&")
}

function ConvertTo-RedactedJson([string]$Text) {
    try { $root = ConvertFrom-Json -InputObject $Text }
    catch { return ConvertTo-RedactedConfig $Text }
    foreach ($item in @(Get-JsonProperties $root)) {
        $value = $item.Property.Value
        if (-not (Test-SecretName $item.Property.Name) -or $null -eq $value -or ($value -is [string] -and $value -eq "")) { continue }
        $item.Property.Value = $Redacted
    }
    return ConvertTo-JsonText $root
}

function Get-JsonSecretValues([string]$Text) {
    try { $root = ConvertFrom-Json -InputObject $Text } catch { return }
    foreach ($item in @(Get-JsonProperties $root)) {
        if ((Test-SecretName $item.Property.Name) -and $item.Property.Value -is [string]) { $item.Property.Value }
    }
}

function Get-AllJsonStrings([string]$Text) {
    try { $root = ConvertFrom-Json -InputObject $Text } catch { return }
    foreach ($item in @(Get-JsonProperties $root)) { if ($item.Property.Value -is [string]) { $item.Property.Value } }
}

function Read-Shared([string]$Path) {
    $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
        ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    try {
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true)
        return $reader.ReadToEnd()
    } finally { $stream.Dispose() }
}

function Read-SharedBytes([string]$Path) {
    $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
        ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    try {
        $buffer = New-Object System.IO.MemoryStream
        $stream.CopyTo($buffer)
        return $buffer.ToArray()
    } finally { $stream.Dispose() }
}

function Get-Newest([string]$Directory, [string]$Pattern, [int]$Count) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Directory -Filter $Pattern -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First $Count)
}

function Get-FileList([string]$BaseDirectory, [string[]]$Folders) {
    $prefixLength = $BaseDirectory.TrimEnd('\', '/').Length + 1
    foreach ($folder in $Folders) {
        $directory = Join-Path $BaseDirectory $folder
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { "$folder\ (missing)"; continue }
        Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object { $_.FullName.ToLowerInvariant() } | ForEach-Object {
            "{0}`t{1}`t{2}" -f $_.FullName.Substring($prefixLength), $_.Length, $_.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
        }
    }
}

function Read-Manifest([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try { return ConvertFrom-Json -InputObject (Read-Shared $Path) } catch { return $null }
}

function Find-LogMatch([string]$Path, [string]$Pattern) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $match = [regex]::Match((Read-Shared $Path), $Pattern)
        if ($match.Success) { return $match.Groups[1].Value }
    } catch { }
    return $null
}

function Test-GameDir([string]$Dir) {
    $Dir -and ((Test-Path -LiteralPath (Join-Path $Dir "Car Mechanic Simulator 2021.exe")) -or
        (Test-Path -LiteralPath (Join-Path $Dir "MelonLoader") -PathType Container))
}

function Test-ServerDir([string]$Dir) { $Dir -and (Test-Path -LiteralPath (Join-Path $Dir "CMS21_Together_Server.exe")) }

function Get-PreferenceValue([string]$Toml, [string]$Name) {
    foreach ($line in Split-Lines (Select-PreferenceCategories $Toml "CMS21Together")) {
        $entry = Get-EntryParts $line
        if ($entry -and $entry.Name -eq $Name) { return $entry.Value.Trim('"').Replace('\\', '\') }
    }
    return $null
}

$here = $PSScriptRoot
if (-not $GameDir -and -not $ServerDir) {
    if (Test-ServerDir $here) {
        $ServerDir = $here
        $parent = Split-Path $here -Parent
        if (Test-GameDir $parent) { $GameDir = $parent }
    } elseif (Test-GameDir $here) {
        $GameDir = $here
    } else {
        throw "Run Collect-Logs from the game folder (where Car Mechanic Simulator 2021.exe is) or from a CMS21 Together server folder."
    }
}
$GameDir = if ($GameDir) { (Resolve-Path -LiteralPath $GameDir).Path } else { $null }
$preferencesPath = if ($GameDir) { Join-Path $GameDir "UserData\MelonPreferences.cfg" } else { $null }
$preferences = if ($preferencesPath -and (Test-Path -LiteralPath $preferencesPath)) { Read-Shared $preferencesPath } else { "" }
if ($GameDir -and -not $ServerDir) {
    $configuredServer = Get-PreferenceValue $preferences "ServerPath"
    if ($configuredServer) {
        $candidate = Split-Path ([System.IO.Path]::GetFullPath([System.IO.Path]::Combine($GameDir, $configuredServer))) -Parent
        if (Test-ServerDir $candidate) { $ServerDir = $candidate }
    }
    if (-not $ServerDir -and (Test-ServerDir (Join-Path $GameDir "TogetherServer"))) { $ServerDir = Join-Path $GameDir "TogetherServer" }
}
$ServerDir = if ($ServerDir) { (Resolve-Path -LiteralPath $ServerDir).Path } else { $null }

$utcNow = (Get-Date).ToUniversalTime()
$id = "{0}-{1}" -f $utcNow.ToString("yyyyMMdd-HHmmss", [Globalization.CultureInfo]::InvariantCulture), [guid]::NewGuid().ToString("N").Substring(0, 4)
$entries = New-Object System.Collections.Generic.List[object]
$secrets = New-Object System.Collections.Generic.List[string]
$errors = @{ client = New-Object System.Collections.Generic.List[string]; server = New-Object System.Collections.Generic.List[string] }

function Add-Text([string]$Root, [string]$Name, [string]$Text) {
    $entries.Add([pscustomobject]@{ Name = "$Root/$($Name.Replace('\', '/'))"; Text = $(if ($null -eq $Text) { "" } else { $Text }); Bytes = $null })
}

function Add-Json([string]$Root, [string]$Name, $Value) { Add-Text $Root $Name (ConvertTo-JsonText $Value) }

function Add-File([string]$Root, [string]$Name, [string]$Path, [scriptblock]$Transform) {
    try {
        $text = Read-Shared $Path
        if ($Transform) { $text = & $Transform $text }
        Add-Text $Root $Name $text
    } catch {
        $errors[$Root].Add("${Name}: $($_.Exception.Message)")
    }
}

function Add-Bundles([string]$Root, [string]$Directory) {
    foreach ($file in Get-Newest $Directory "*.zip" $NewestBundles) {
        try { $entries.Add([pscustomobject]@{ Name = "$Root/BugReports/$($file.Name)"; Text = $null; Bytes = (Read-SharedBytes $file.FullName) }) }
        catch { $errors[$Root].Add("BugReports/$($file.Name): $($_.Exception.Message)") }
    }
}

$included = @()

if ($GameDir) {
    $included += "game ($GameDir)"
    $melonLoader = Join-Path $GameDir "MelonLoader"
    $modData = Join-Path $GameDir "UserData\CMS21Together"
    $manifest = Read-Manifest (Join-Path $GameDir "CMS21-Together-release.json")
    $latestLog = Join-Path $melonLoader "Latest.log"
    $loggedVersion = Find-LogMatch $latestLog 'Together Mod (\S+) initialized'

    $categories = Select-PreferenceCategories $preferences "CMS21Together"
    foreach ($value in Get-ConfigSecretValues $categories) { $secrets.Add($value) }
    $identityPath = Join-Path $modData $IdentityFile
    if (Test-Path -LiteralPath $identityPath) {
        try { foreach ($value in Get-AllJsonStrings (Read-Shared $identityPath)) { $secrets.Add($value) } } catch { }
    }

    $info = [ordered]@{
        id = $id
        utc = $utcNow.ToString("o")
        origin = "collector"
        modVersion = $(if ($manifest) { $manifest.version } else { $null })
        fullVersion = $(if ($manifest) { $manifest.fullVersion } elseif ($loggedVersion) { $loggedVersion } else { $null })
        lastLoggedVersion = $loggedVersion
        gameVersion = Find-LogMatch $latestLog 'Game Version:\s*(\S+)'
        connection = [ordered]@{ state = "offline"; connected = $false; serverPart = $false }
        scene = "offline"
        gameDirectory = $GameDir
        steamDll = Test-Path -LiteralPath (Join-Path $GameDir "UserLibs\steam_api64.dll")
        releaseManifest = [bool]$manifest
    }
    Add-Json "client" "info.json" $info
    if (Test-Path -LiteralPath $latestLog) { Add-File "client" "MelonLoader/Latest.log" $latestLog }
    else { $errors["client"].Add("MelonLoader/Latest.log: not found (has the game run with MelonLoader?)") }
    foreach ($file in Get-Newest (Join-Path $melonLoader "Logs") "*.log" $NewestLogs) { Add-File "client" "MelonLoader/Logs/$($file.Name)" $file.FullName }
    if ($preferences) { Add-Text "client" "MelonPreferences.cfg" (ConvertTo-RedactedConfig $categories) }
    if (Test-Path -LiteralPath $modData -PathType Container) {
        Get-ChildItem -LiteralPath $modData -Filter "*.json" -File | Where-Object { $_.Name -ne $IdentityFile } | ForEach-Object {
            Add-File "client" "UserData/CMS21Together/$($_.Name)" $_.FullName { param($text) ConvertTo-RedactedJson $text }
        }
    }
    Add-Text "client" "files.txt" ((@(Get-FileList $GameDir @("Mods", "UserLibs"))) -join "`r`n")
    Add-Bundles "client" (Join-Path $modData "BugReports")
}

if ($ServerDir) {
    $included += "server ($ServerDir)"
    $logDir = Join-Path $ServerDir "Log"
    $configPath = Join-Path $ServerDir "server_config.ini"
    $manifest = Read-Manifest (Join-Path $ServerDir "release.json")
    $loggedVersion = Find-LogMatch (Join-Path $logDir "Latest.txt") 'CMS21 Together Server v(\S+)'
    if (Test-Path -LiteralPath $configPath) {
        try { foreach ($value in Get-ConfigSecretValues (Read-Shared $configPath)) { $secrets.Add($value) } } catch { }
    }

    $savePath = Join-Path $ServerDir "Saves\server_save.json"
    $saveText = $null
    if ($IncludeSave) {
        if (Test-Path -LiteralPath $savePath) {
            try {
                $save = ConvertFrom-Json -InputObject (Read-Shared $savePath)
                $playersData = $null
                if ($save.Sections -and $save.Sections.players) { $playersData = $save.Sections.players.Data }
                foreach ($item in @(Get-JsonProperties $playersData)) {
                    if (-not (Test-SecretName $item.Property.Name)) { continue }
                    if ($item.Property.Value -is [string]) { $secrets.Add($item.Property.Value) }
                    $item.Owner.PSObject.Properties.Remove($item.Property.Name)
                }
                $saveText = ConvertTo-JsonText $save
            } catch {
                $errors["server"].Add("save.json: $($_.Exception.Message)")
            }
        } else {
            $errors["server"].Add("save.json: Saves\server_save.json not found")
        }
    }

    $info = [ordered]@{
        id = $id
        utc = $utcNow.ToString("o")
        origin = "collector"
        serverFullVersion = $(if ($manifest) { $manifest.fullVersion } elseif ($loggedVersion) { $loggedVersion } else { $null })
        modVersion = $(if ($manifest) { $manifest.version } else { $null })
        lastLoggedVersion = $loggedVersion
        state = "offline"
        serverDirectory = $ServerDir
        steamDll = Test-Path -LiteralPath (Join-Path $ServerDir "steam_api64.dll")
        releaseManifest = [bool]$manifest
        saveIncluded = [bool]$saveText
    }
    Add-Json "server" "info.json" $info
    $latest = Join-Path $logDir "Latest.txt"
    if (Test-Path -LiteralPath $latest) { Add-File "server" "Log/Latest.txt" $latest }
    else { $errors["server"].Add("Log/Latest.txt: not found (has this server run?)") }
    foreach ($file in Get-Newest $logDir "Log_*.txt" $NewestLogs) { Add-File "server" "Log/$($file.Name)" $file.FullName }
    if (Test-Path -LiteralPath $configPath) { Add-File "server" "server_config.ini" $configPath { param($text) ConvertTo-RedactedConfig $text } }
    if ($saveText) { Add-Text "server" "save.json" $saveText }
    Add-Bundles "server" (Join-Path $ServerDir "BugReports")
}

foreach ($root in @("client", "server")) {
    if ($errors[$root].Count -gt 0) { Add-Text $root "errors.txt" ($errors[$root] -join "`r`n") }
}

$distinct = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($secret in $secrets) { if ($secret -and $secret.Length -ge $MinScrubLength) { [void]$distinct.Add($secret) } }
$scrub = @($distinct | Sort-Object Length -Descending)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zipPath = Join-Path (Resolve-Path -LiteralPath $OutDir).Path "CMS21Together-logs-$id.zip"
$tempPath = "$zipPath.tmp"
$stream = New-Object System.IO.FileStream($tempPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
try {
    $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            $bytes = $entry.Bytes
            if ($null -ne $entry.Text) {
                $text = $entry.Text
                foreach ($secret in $scrub) { $text = $text.Replace($secret, $Redacted) }
                $bytes = $utf8.GetBytes($text)
            }
            $output = $zip.CreateEntry($entry.Name, [System.IO.Compression.CompressionLevel]::Optimal).Open()
            try { $output.Write($bytes, 0, $bytes.Length) } finally { $output.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Move-Item -LiteralPath $tempPath -Destination $zipPath

Write-Host "Collected: $($included -join ', ')"
if (-not $ServerDir) { Write-Host "No server folder found next to the game, so the zip has no server logs." }
if (-not $IncludeSave -and $ServerDir) { Write-Host "The server save is not included (run with -IncludeSave if you are asked for it)." }
Write-Host ""
Write-Host "Bug report logs written to:"
Write-Host "  $zipPath"
Write-Host "Send this file with what you did, what you expected, what happened and roughly when."
if (-not $NoExplorer) { Start-Process explorer.exe -ArgumentList "/select,`"$zipPath`"" }
