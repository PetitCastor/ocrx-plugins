# Fixture-driven checks for Build-Changelog.ps1. No Pester dependency (the repo pins none for
# PowerShell): plain assertions, run directly with `powershell -File scripts/Build-Changelog.Tests.ps1`;
# ci.yml runs it on every pull request.

$ErrorActionPreference = 'Stop'

$failures = [System.Collections.Generic.List[string]]::new()
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("plugin-changelog-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Assert-Equal {
    param([string]$Name, $Expected, $Actual)
    if ($Expected -cne $Actual) { $failures.Add("$Name : expected [$Expected] but got [$Actual]") }
}

function Assert-Throws {
    # $Expect: a fragment of the message the error must carry, so a case cannot pass on an unrelated failure.
    param([string]$Name, [scriptblock]$Action, [string]$Expect)
    try {
        & $Action
        $failures.Add("$Name : expected an error, but none was raised.")
    }
    catch {
        if ($Expect -and -not $_.Exception.Message.Contains($Expect)) {
            $failures.Add("$Name : expected an error mentioning '$Expect', got: $($_.Exception.Message)")
        }
    }
}

function New-Descriptor {
    param([string]$Name, [hashtable]$Manifest)
    $path = Join-Path $work "$Name.json"
    [System.IO.File]::WriteAllText($path, ($Manifest | ConvertTo-Json -Depth 4))
    return $path
}

function Invoke-Build {
    param([string]$Descriptor, [string]$Base, [string]$Out)
    $arguments = @{ Descriptor = $Descriptor; OutDir = $Out; Date = '2026-10-20' }
    if ($Base) { $arguments.BasePath = $Base }
    & "$PSScriptRoot/Build-Changelog.ps1" @arguments | Out-Null
    return [System.IO.File]::ReadAllText((Join-Path $Out 'CHANGELOG.md'))
}

$v271 = New-Descriptor 'v2.7.1' @{
    tag = 'v2.7.1'; channel = 'stable'; plugins = @('SignaturePlugin'); notes = 'n'
    changes = @('Fixed: The overlay no longer flickers.', 'Added: A Theme setting.', 'Fixed: The overlay no longer flickers.')
}
$first = Invoke-Build -Descriptor $v271 -Out (Join-Path $work 'o1')
$expectedFirst = "# Changelog`n`n## [2.7.1] - 2026-10-20`n`n### Added`n- A Theme setting.`n`n### Fixed`n- The overlay no longer flickers.`n"
Assert-Equal 'sections in fixed order, duplicates collapsed, leading v dropped' $expectedFirst $first
Assert-Equal 'the release-body part is the section body alone' `
    "### Added`n- A Theme setting.`n`n### Fixed`n- The overlay no longer flickers.`n" `
    ([System.IO.File]::ReadAllText((Join-Path $work 'o1/release-notes-changes.md')))

# A stable release builds on the previous stable release's asset: new section first, older ones untouched.
$v272 = New-Descriptor 'v2.7.2' @{ tag = 'v2.7.2'; channel = 'stable'; plugins = @('SignaturePlugin'); notes = 'n'; changes = @('Changed: Faster start.') }
$second = Invoke-Build -Descriptor $v272 -Base (Join-Path $work 'o1/CHANGELOG.md') -Out (Join-Path $work 'o2')
Assert-Equal 'new section on top of the previous asset' `
    ("# Changelog`n`n## [2.7.2] - 2026-10-20`n`n### Changed`n- Faster start.`n`n" + ($first -replace '^# Changelog\n\n', '')) `
    $second

$again = Invoke-Build -Descriptor $v272 -Base (Join-Path $work 'o2/CHANGELOG.md') -Out (Join-Path $work 'o3')
Assert-Equal 'a re-run replaces its own section, never duplicates it' $second $again

$crlf = Join-Path $work 'crlf.md'
[System.IO.File]::WriteAllText($crlf, ($first -replace "`n", "`r`n"))
Assert-Equal 'a CRLF base yields the same file' $second (Invoke-Build -Descriptor $v272 -Base $crlf -Out (Join-Path $work 'o4'))

# A preview with no changes still records that it existed; the workflow gives previews no base.
$preview = New-Descriptor 'preview' @{ tag = 'v2.7.3-signature-alpha.1'; channel = 'preview'; plugins = @('SignaturePlugin'); notes = 'n' }
Assert-Equal 'a preview with no changes says so' `
    "# Changelog`n`n## [2.7.3-signature-alpha.1] - 2026-10-20`n`n### Changed`n- No user-visible changes.`n" `
    (Invoke-Build -Descriptor $preview -Out (Join-Path $work 'o5'))

Assert-Throws 'a malformed change fails the build' -Expect "unknown kind 'Nope'" -Action {
    Invoke-Build -Descriptor (New-Descriptor 'bad' @{ tag = 'v2.7.4'; channel = 'stable'; plugins = @('SignaturePlugin'); notes = 'n'; changes = @('Nope: x') }) -Out (Join-Path $work 'o6')
}
Assert-Throws 'a malformed tag fails the build' -Expect 'Descriptor tag must be' -Action {
    Invoke-Build -Descriptor (New-Descriptor 'badtag' @{ tag = 'latest'; channel = 'stable'; plugins = @('SignaturePlugin'); notes = 'n'; changes = @('Fixed: x') }) -Out (Join-Path $work 'o7')
}

Remove-Item -Recurse -Force $work

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    throw "$($failures.Count) changelog build check(s) failed."
}
Write-Host 'All changelog build checks passed.'
