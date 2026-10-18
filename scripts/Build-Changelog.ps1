# Builds a plugin release's changelog assets from its release descriptor.
#
#   <OutDir>/CHANGELOG.md               the cumulative file: title, this release's section, then every
#                                       older section carried over from the previous stable release's asset
#   <OutDir>/release-notes-changes.md   this release's entries alone, appended to the GitHub release body
#
# The engine's plugin manager reads CHANGELOG.md off the release (ocrx-engine PluginChangelogSource) to
# show what an update changes. The release workflow decides which previous asset, if any, to build on:
# a stable release builds on the previous stable release; a preview release carries only its own section,
# because previews are pinned and have no "latest" lineage. This script is a pure function of its inputs.
#
# Section layout copied from ocrx-engine's .github/tools/Build-Changelog.ps1 (no cross-repository
# reference); keep the two in step. Runs on Windows PowerShell 5.1 and pwsh.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Descriptor,
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$BasePath,
    [string]$Date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ChangelogEntry.ps1"

if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') { throw "Date must be YYYY-MM-DD; got '$Date'." }

$manifest = [System.IO.File]::ReadAllText($Descriptor) | ConvertFrom-Json
$version = ([string]$manifest.tag).Trim().TrimStart('v')
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Descriptor tag must be v<major>.<minor>.<patch> with an optional suffix; got '$($manifest.tag)'."
}

$entries = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$raw = if ($manifest.PSObject.Properties['changes']) { @($manifest.changes) } else { @() }
foreach ($change in $raw) {
    $entry = ConvertFrom-ChangelogEntry -Entry ([string]$change) -Source $Descriptor
    if ($seen.Add("$($entry.Kind)|$($entry.Text)")) { $entries.Add($entry) }
}

# A preview may ship without entries; the history still records that it existed.
if ($entries.Count -eq 0) {
    $entries.Add([pscustomobject]@{ Kind = 'Changed'; Text = 'No user-visible changes.' })
}

$body = [System.Text.StringBuilder]::new()
foreach ($kind in $script:ChangelogKinds) {
    $ofKind = @($entries | Where-Object { $_.Kind -ceq $kind })
    if ($ofKind.Count -eq 0) { continue }
    [void]$body.Append("### $kind`n")
    foreach ($entry in $ofKind) { [void]$body.Append("- $($entry.Text)`n") }
    [void]$body.Append("`n")
}
$sectionBody = $body.ToString().TrimEnd("`n") + "`n"
$section = "## [$version] - $Date`n`n$sectionBody"

# Older sections come from the previous asset, minus any section for this very version, so a re-run of
# the same release replaces its section instead of stacking a second one.
$older = ''
if ($BasePath -and (Test-Path -LiteralPath $BasePath)) {
    $baseText = [System.IO.File]::ReadAllText($BasePath) -replace "`r`n", "`n"
    $firstSection = [regex]::Match($baseText, '(?m)^## \[')
    if ($firstSection.Success) {
        $blocks = [regex]::Split($baseText.Substring($firstSection.Index), '(?m)^(?=## \[)') |
            Where-Object { $_.Trim() -ne '' }
        $kept = @($blocks | Where-Object { -not $_.StartsWith("## [$version]") })
        $older = (($kept | ForEach-Object { $_.TrimEnd("`n") + "`n" }) -join "`n")
    }
}

$full = "# Changelog`n`n$section"
if ($older.Trim() -ne '') { $full += "`n$older" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $OutDir 'CHANGELOG.md'), $full, $utf8)
[System.IO.File]::WriteAllText((Join-Path $OutDir 'release-notes-changes.md'), $sectionBody, $utf8)
Write-Host "Changelog for $version : $($entries.Count) entr$(if ($entries.Count -eq 1) { 'y' } else { 'ies' }); wrote $OutDir"
