<#
.SYNOPSIS
  Builds the two release zips for Decision Council, the same way Session Deck's releases are built.

.DESCRIPTION
  Reads the version from src/DecisionCouncil.App/DecisionCouncil.App.csproj and writes to release/:
    DecisionCouncil-<version>-win-x64.zip            self-contained single-file exe, no .NET install needed
    DecisionCouncil-<version>-win-x64-framework.zip  single-file exe that needs the .NET 10 Desktop Runtime
    notes.md                                         release notes with the download table and SHA-256 hashes
  The app's updater picks the zip that matches the build it runs (see Updater.cs), so keep both names as they are.

  With -Publish it also creates the GitHub release v<version> on main with both zips, using gh.
  Bump <Version> in the csproj first: the updater only offers a release whose tag is newer than the running exe.

.EXAMPLE
  ./release.ps1 -WhatsNew whats-new.md            # build and write notes; check them, then
  ./release.ps1 -WhatsNew whats-new.md -Publish   # build again and publish
#>
param(
    # Markdown for the "New" section of the notes; a placeholder is used without it
    [string]$WhatsNew,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$csproj = Get-Content 'src/DecisionCouncil.App/DecisionCouncil.App.csproj'
$version = @($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'No <Version> in DecisionCouncil.App.csproj.' }
$tag = "v$version"
Write-Host "Decision Council $version" -ForegroundColor Cyan

$out = Join-Path $PSScriptRoot 'release'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

$builds = @(
    @{ Name = "DecisionCouncil-$version-win-x64.zip";           SelfContained = 'true';  Extra = @('-p:EnableCompressionInSingleFile=true') },
    @{ Name = "DecisionCouncil-$version-win-x64-framework.zip"; SelfContained = 'false'; Extra = @() }
)
foreach ($b in $builds) {
    $dir = Join-Path $out ([IO.Path]::GetFileNameWithoutExtension($b.Name))
    Write-Host "Publishing $($b.Name)..."
    & dotnet publish src/DecisionCouncil.App -c Release -r win-x64 --self-contained $b.SelfContained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none @($b.Extra) -o $dir -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($b.Name)." }

    $files = @(Get-ChildItem $dir -File)
    if ($files.Name -notcontains 'DecisionCouncil.exe' -or $files.Count -ne 1) {
        throw "Expected only DecisionCouncil.exe in $dir, found: $($files.Name -join ', ')"
    }
    Copy-Item LICENSE $dir
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath (Join-Path $out $b.Name)
}

$zips = $builds | ForEach-Object { Get-Item (Join-Path $out $_.Name) }
$hashes = $zips | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name }
$mb = { param($f) '{0:0.#} MB' -f ($f.Length / 1MB) }

$new = if ($WhatsNew) { (Get-Content $WhatsNew -Raw).Trim() } else { '- (Describe what changed in this version.)' }
$notes = @"
### New

$new

### Updating

Click **Update now** in the banner at the top of the window, or under **Settings → Updates**. Decision Council checks GitHub a minute after it starts and then twice a day. Your data and settings are kept.

### Download

| File | For |
|---|---|
| **$($zips[0].Name)** | Most people. Self-contained, no .NET install needed ($(& $mb $zips[0])). |
| $($zips[1].Name) | If you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) ($(& $mb $zips[1])). |

Unzip and run ``DecisionCouncil.exe``. The app isn't code-signed yet, so Windows SmartScreen may warn you: click **More info → Run anyway**.

**Requires:** Windows 10/11 64-bit. Demo mode needs nothing else; real debates need Claude Code installed and signed in on the same PC.

### SHA-256

``````
$($hashes -join "`n")
``````
"@
$notesFile = Join-Path $out 'notes.md'
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))
Write-Host "`nWrote $notesFile"
$hashes | ForEach-Object { Write-Host "  $_" }

if ($Publish) {
    if (git status --porcelain) { throw 'Commit your changes before publishing: the release is tagged on main.' }
    git fetch -q origin
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'Push main first: GitHub tags the release on origin/main.' }
    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
    if (-not $gh) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
    & $gh release create $tag $zips.FullName --target main --title "Decision Council $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
    Write-Host "Published $tag." -ForegroundColor Green
}
