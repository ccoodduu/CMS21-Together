#Requires -Version 5.1
<#
Builds a dev release from the current commit: the client zip (game folder layout, with the dedicated server in
TogetherServer\) and the server zip, each with a release.json manifest, plus SHA256SUMS.txt, in tools\release\out.
The version is TogetherVersion from Directory.Build.props with the label dev.<n>, n = commits since the last v* tag.
Each zip is checked against the expected file list and the version built into it; on failure no zip is left.

-Release builds the plain version (no label). It refuses unless the tree is clean, CHANGELOG.md has a
"## [<version>]" section and HEAD carries the tag v<version>; it prints the tag command instead of tagging.
-DraftGitHubRelease (with -Release, only when the user asks for it) creates a draft GitHub release for the pushed tag
with the changelog section and both zips.
-AllowDirty builds from uncommitted changes (the full version then ends in .dirty); not with -Release.
-DropFromStaging removes staged files (paths relative to the staging folder, e.g. client\UserLibs\steam_api64.dll)
before zipping, to test the content check.
#>
param(
    [switch]$Release,
    [switch]$DraftGitHubRelease,
    [switch]$AllowDirty,
    [string]$OutDir = (Join-Path $PSScriptRoot "out"),
    [string[]]$DropFromStaging = @()
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

$serverFiles = @(
    "CMS21_Together_Server.exe", "CMS21_Together_Server.exe.config", "CMS21_Together_Server.pdb",
    "CMS21_Together_Core.dll", "CMS21_Together_Core.pdb", "Facepunch.Steamworks.Win64.dll", "Newtonsoft.Json.dll",
    "Terminal.Gui.dll", "NStack.dll", "steam_api64.dll",
    "Database/garage_upgrade_database.json", "Database/item_database.json", "Database/player_upgrade_database.json",
    "TRY-IT.txt", "release.json", "Collect-Logs.ps1", "Collect-Logs.bat"
)
$clientFiles = @(
    "Mods/CMS21-Together.dll", "Mods/CMS21-Together.pdb",
    "UserLibs/CMS21_Together_Core.dll", "UserLibs/CMS21_Together_Core.pdb",
    "UserLibs/Facepunch.Steamworks.Win64.dll", "UserLibs/steam_api64.dll",
    "CMS21-Together-TRY-IT.txt", "CMS21-Together-QUICKSTART-DA.txt", "CMS21-Together-release.json", "Collect-Logs.ps1",
    "Collect-Logs.bat"
) + @($serverFiles | ForEach-Object { "TogetherServer/$_" })

function Invoke-Git {
    $output = & git -C $repo @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed" }
    return $output
}

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Get-RelativeFiles([string]$Root) {
    $prefix = $Root.TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object { $_.FullName.Substring($prefix.Length).Replace('\', '/') } | Sort-Object
}

function Write-Manifest([string]$Root, [string]$Name) {
    $files = [ordered]@{}
    foreach ($relative in Get-RelativeFiles $Root) { $files[$relative] = Get-Sha256 (Join-Path $Root $relative) }
    $manifest = [ordered]@{
        version = $script:modVersion; fullVersion = $script:fullVersion; commit = $script:commit
        builtUtc = (Get-Date).ToUniversalTime().ToString("s") + "Z"; files = $files
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root $Name) -Encoding utf8
}

function New-Zip([string]$Root, [string]$ZipPath) {
    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in Get-RelativeFiles $Root) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $Root $relative), $relative,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
}

function Read-ZipEntry($Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    if (-not $entry) { return $null }
    $stream = $entry.Open()
    try {
        $buffer = New-Object System.IO.MemoryStream
        $stream.CopyTo($buffer)
        return $buffer.ToArray()
    } finally { $stream.Dispose() }
}

function Get-CoreVersion([byte[]]$Bytes) {
    $type = [System.Reflection.Assembly]::Load($Bytes).GetType("CMS21_Together_Core.BuildInfo", $true)
    [pscustomobject]@{
        ModVersion = $type.GetField("ModVersion").GetRawConstantValue()
        FullVersion = $type.GetField("FullVersion").GetRawConstantValue()
    }
}

# A string constant is stored as its UTF-16 bytes behind a one-byte length (for strings under 64 characters), so
# the length byte keeps "0.6.0-dev.5" from matching inside "0.6.0-dev.53".
function Test-ContainsConstant([byte[]]$Bytes, [string]$Value) {
    $utf16 = [System.Text.Encoding]::Unicode.GetBytes($Value)
    $needle = [System.Text.Encoding]::GetEncoding(28591).GetString([byte[]](@([byte]$utf16.Length) + $utf16))
    return [System.Text.Encoding]::GetEncoding(28591).GetString($Bytes).Contains($needle)
}

function Test-Zip([string]$ZipPath, [string[]]$Expected, [string[]]$CorePaths, [string[]]$AssemblyPaths) {
    $problems = @()
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $actual = @($zip.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName })
        foreach ($missing in $Expected | Where-Object { $_ -notin $actual }) { $problems += "missing $missing" }
        foreach ($extra in $actual | Where-Object { $_ -notin $Expected }) { $problems += "unexpected $extra" }
        foreach ($path in $CorePaths) {
            $bytes = Read-ZipEntry $zip $path
            if (-not $bytes) { continue }
            $built = Get-CoreVersion $bytes
            if ($built.ModVersion -ne $script:modVersion) { $problems += "$path is version $($built.ModVersion), not $script:modVersion" }
            if ($built.FullVersion -ne $script:fullVersion) { $problems += "$path is $($built.FullVersion), not $script:fullVersion" }
        }
        foreach ($path in $AssemblyPaths) {
            $bytes = Read-ZipEntry $zip $path
            if ($bytes -and -not (Test-ContainsConstant $bytes $script:modVersion)) { $problems += "$path was not built as $script:modVersion" }
        }
    } finally { $zip.Dispose() }
    return $problems
}

function Get-ChangelogSection([string]$Version) {
    $path = Join-Path $repo "CHANGELOG.md"
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $lines = @(Get-Content -LiteralPath $path -Encoding utf8)
    $heading = "^## \[$([regex]::Escape($Version))\]"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $heading) { $start = $i; break } }
    if ($start -lt 0) { return $null }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^## |^\[[^\]]+\]:\s') { $end = $i; break } }
    if ($end -le $start + 1) { return "" }
    return ($lines[($start + 1)..($end - 1)] -join "`r`n").Trim()
}

if ($DraftGitHubRelease -and -not $Release) { throw "-DraftGitHubRelease needs -Release." }
if ($Release -and $AllowDirty) { throw "-Release cannot be combined with -AllowDirty: a release is built from a clean, tagged commit." }

$changes = @(Invoke-Git status --porcelain)
if ($changes.Count -gt 0 -and -not $AllowDirty) {
    Write-Host "Uncommitted changes (commit them$(if (-not $Release) { ', or build with -AllowDirty' })):"
    $changes | ForEach-Object { Write-Host "  $_" }
    throw "Working tree is not clean; nothing was built."
}
$dirty = $changes.Count -gt 0

[xml]$props = Get-Content -LiteralPath (Join-Path $repo "Directory.Build.props") -Raw
$version = ([string]$props.Project.PropertyGroup.TogetherVersion).Trim()
if (-not $version) { throw "TogetherVersion not found in Directory.Build.props" }
$commit = (Invoke-Git rev-parse --short HEAD).Trim()

if ($Release) {
    $tag = "v$version"
    $changelog = Get-ChangelogSection $version
    if ($null -eq $changelog) {
        throw "CHANGELOG.md has no '## [$version]' section. Add the changelog entry for $version (move the Unreleased notes under it) and commit it first."
    }
    if (-not $changelog) { throw "The '## [$version]' section of CHANGELOG.md is empty." }
    $tagsAtHead = @(Invoke-Git tag --points-at HEAD)
    if ($tag -notin $tagsAtHead) {
        Write-Host "HEAD ($commit) does not carry the tag $tag. After checking the changelog, tag it with:"
        Write-Host "  git tag -a $tag -m `"CMS21 Together $version`""
        Write-Host "  git push origin $tag"
        throw "Tag $tag missing on HEAD; nothing was built."
    }
    $label = ""
    $modVersion = $version
    Write-Host "Building release $modVersion+$commit (tag $tag)"
} else {
    $lastTag = @(Invoke-Git tag --list "v*" --merged HEAD --sort=-v:refname) | Select-Object -First 1
    $count = [int](Invoke-Git rev-list --count $(if ($lastTag) { "$lastTag..HEAD" } else { "HEAD" }))
    $label = "dev.$count"
    $modVersion = "$version-$label"
    Write-Host "Building $modVersion+$commit$(if ($dirty) { '.dirty' })$(if ($lastTag) { " ($count commits since $lastTag)" } else { " (no v* tag yet)" })"
}
$fullVersion = "$modVersion+$commit" + $(if ($dirty) { ".dirty" } else { "" })

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$clientZipName = "CMS21-Together-$modVersion-client.zip"
$serverZipName = "CMS21-Together-$modVersion-server.zip"
foreach ($name in @($clientZipName, $serverZipName, "SHA256SUMS.txt")) {
    Remove-Item -LiteralPath (Join-Path $OutDir $name) -ErrorAction SilentlyContinue
}

$buildArgs = @("-c", "Release", "-nologo", "-v", "q", "-p:TogetherBuildLabel=$label", "-p:TogetherDirty=$($dirty.ToString().ToLowerInvariant())")
foreach ($project in @("CMS21-Together-Client\CMS21-Together.csproj", "CMS21-Together-Server\CMS21-Together-Server.csproj")) {
    $output = & dotnet build (Join-Path $repo $project) @buildArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Select-String -Pattern "error" | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
        throw "Build failed: $project"
    }
}

$clientBin = Join-Path $repo "CMS21-Together-Client\bin\Release"
$serverBin = Join-Path $repo "CMS21-Together-Server\bin\Release"
$tryIt = Join-Path $repo "docs\try-it.md"
$quickstartDa = Join-Path $repo "docs\playtest-quickstart-da.md"
$steamLib = Join-Path $repo "CMS21-Together-Server\Libs\steam_api64.dll"

$stage = Join-Path $OutDir "staging"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$serverStage = Join-Path $stage "server"
$clientStage = Join-Path $stage "client"
New-Item -ItemType Directory -Force -Path $serverStage, (Join-Path $clientStage "Mods"), (Join-Path $clientStage "UserLibs") | Out-Null

$tryItText = (Get-Content -LiteralPath $tryIt -Encoding utf8) -join "`r`n"

Get-ChildItem -LiteralPath $serverBin | Where-Object { $_.Name -notin @("Log", "Saves", "BugReports", "server_config.ini") } |
    Copy-Item -Destination $serverStage -Recurse -Force
Copy-Item -LiteralPath $steamLib -Destination $serverStage
Set-Content -LiteralPath (Join-Path $serverStage "TRY-IT.txt") -Value $tryItText -Encoding utf8
$collector = @("Collect-Logs.ps1", "Collect-Logs.bat") | ForEach-Object { Join-Path $PSScriptRoot $_ }
Copy-Item -LiteralPath $collector -Destination $serverStage
Copy-Item -LiteralPath $collector -Destination $clientStage

foreach ($file in @("CMS21-Together.dll", "CMS21-Together.pdb")) {
    Copy-Item -LiteralPath (Join-Path $clientBin $file) -Destination (Join-Path $clientStage "Mods")
}
foreach ($file in @("CMS21_Together_Core.dll", "CMS21_Together_Core.pdb", "Facepunch.Steamworks.Win64.dll")) {
    Copy-Item -LiteralPath (Join-Path $clientBin $file) -Destination (Join-Path $clientStage "UserLibs")
}
Copy-Item -LiteralPath $steamLib -Destination (Join-Path $clientStage "UserLibs")
Set-Content -LiteralPath (Join-Path $clientStage "CMS21-Together-TRY-IT.txt") -Value $tryItText -Encoding utf8
Copy-Item -LiteralPath $quickstartDa -Destination (Join-Path $clientStage "CMS21-Together-QUICKSTART-DA.txt")

foreach ($drop in $DropFromStaging) {
    Remove-Item -LiteralPath (Join-Path $stage $drop) -Force
    Write-Host "Dropped $drop from staging"
}

Write-Manifest $serverStage "release.json"
Copy-Item -LiteralPath $serverStage -Destination (Join-Path $clientStage "TogetherServer") -Recurse
Write-Manifest $clientStage "CMS21-Together-release.json"

$clientZip = Join-Path $stage $clientZipName
$serverZip = Join-Path $stage $serverZipName
New-Zip $clientStage $clientZip
New-Zip $serverStage $serverZip

$problems = @()
$problems += @(Test-Zip $clientZip $clientFiles `
    -CorePaths @("UserLibs/CMS21_Together_Core.dll", "TogetherServer/CMS21_Together_Core.dll") `
    -AssemblyPaths @("Mods/CMS21-Together.dll", "TogetherServer/CMS21_Together_Server.exe") | ForEach-Object { "${clientZipName}: $_" })
$problems += @(Test-Zip $serverZip $serverFiles -CorePaths @("CMS21_Together_Core.dll") -AssemblyPaths @("CMS21_Together_Server.exe") |
    ForEach-Object { "${serverZipName}: $_" })
if ($problems.Count -gt 0) {
    Remove-Item -LiteralPath $clientZip, $serverZip -Force
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw "Release check failed ($($problems.Count) problems); no zip was written. Staging kept in $stage"
}

Move-Item -LiteralPath $clientZip, $serverZip -Destination $OutDir
$sums = @($clientZipName, $serverZipName) | ForEach-Object { "{0}  {1}" -f (Get-Sha256 (Join-Path $OutDir $_)), $_ }
Set-Content -LiteralPath (Join-Path $OutDir "SHA256SUMS.txt") -Value $sums -Encoding ascii
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Host "Release $fullVersion written to $OutDir"
Write-Host "  $clientZipName"
Write-Host "  $serverZipName"

if ($DraftGitHubRelease) {
    $notes = Join-Path $OutDir "release-notes-$version.md"
    Set-Content -LiteralPath $notes -Value $changelog -Encoding utf8
    & gh release create $tag --draft --verify-tag --title "CMS21 Together $version" --notes-file $notes `
        (Join-Path $OutDir $clientZipName) (Join-Path $OutDir $serverZipName) (Join-Path $OutDir "SHA256SUMS.txt")
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed (is the tag $tag pushed to GitHub?)" }
    Write-Host "Draft GitHub release $tag created; publishing it is a separate, manual step."
}
