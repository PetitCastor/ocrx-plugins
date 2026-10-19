# Fixture-driven checks for scripts/ReleaseDescriptor.Validate.ps1's gating rules. No Pester
# dependency (the repo pins none for PowerShell): plain pass/throw assertions, run directly with
# `powershell -File scripts/Test-ReleaseDescriptor.Tests.ps1`.

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseDescriptor.Validate.ps1"

$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Valid {
    param([string]$Name, [hashtable]$Manifest, [string]$Tag, [string]$LatestStableTag)
    try {
        Test-ReleaseDescriptorManifest -Manifest ([pscustomobject]$Manifest) -Tag $Tag -LatestStableTag $LatestStableTag
    }
    catch {
        $failures.Add("$Name : expected valid, but got error: $($_.Exception.Message)")
    }
}

function Assert-Invalid {
    param([string]$Name, [hashtable]$Manifest, [string]$Tag, [string]$LatestStableTag)
    try {
        Test-ReleaseDescriptorManifest -Manifest ([pscustomobject]$Manifest) -Tag $Tag -LatestStableTag $LatestStableTag
        $failures.Add("$Name : expected an error, but validation passed.")
    }
    catch {
        # Expected.
    }
}

Assert-Valid 'stable tag with one plugin' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'release notes'; plugins = @('SignaturePlugin') }

Assert-Valid 'preview tag matching plugin and stage' `
    -Tag 'v1.2.4-signature-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-signature-alpha.1'; channel = 'preview'; notes = 'preview notes'; plugins = @('SignaturePlugin') }

Assert-Invalid 'tag mismatch between descriptor and pushed tag' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v9.9.9'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'unknown channel' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'nightly'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'empty notes' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = '  '; plugins = @('SignaturePlugin') }

Assert-Invalid 'no plugins selected' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'n'; plugins = @() }

Assert-Invalid 'duplicate plugin in selection' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'n'; plugins = @('SignaturePlugin', 'SignaturePlugin') }

Assert-Invalid 'unknown plugin name' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'n'; plugins = @('NotAPlugin') }

Assert-Invalid 'stable tag with a prerelease suffix' `
    -Tag 'v1.2.3-rc.1' `
    -Manifest @{ tag = 'v1.2.3-rc.1'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'preview tag naming a plugin that is not the selected one' `
    -Tag 'v1.2.4-refinery-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-refinery-alpha.1'; channel = 'preview'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'preview tag with an unrecognized stage' `
    -Tag 'v1.2.4-signature-nightly.1' `
    -Manifest @{ tag = 'v1.2.4-signature-nightly.1'; channel = 'preview'; notes = 'n'; plugins = @('SignaturePlugin') }

# changes: the release's changelog entries (see ChangelogEntry.ps1). Required for stable, optional for
# preview, every entry one "Kind: text" line.
Assert-Invalid 'stable release with no changes' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'stable release with an empty changes list' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @(); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'change with an unknown kind' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Improved: faster'); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'change kind in the wrong case' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('fixed: lower case'); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'change with no text' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed:   '); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'change over 200 characters' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: ' + ('x' * 201)); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'the same change twice' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.', 'Fixed: A fix.'); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'a change that is not a string' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @(42); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Valid 'stable release with several kinds of change' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Added: A theme.', 'Fixed: No flicker.'); notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Valid 'preview without changes' `
    -Tag 'v1.2.4-signature-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-signature-alpha.1'; channel = 'preview'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'preview with a malformed change' `
    -Tag 'v1.2.4-signature-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-signature-alpha.1'; channel = 'preview'; changes = @('nope'); notes = 'n'; plugins = @('SignaturePlugin') }

# One rule in Test-ReleaseDescriptorManifest has no test here: "a preview release must select
# exactly one plugin". With a single-name $AllowedPlugins it cannot be reached — a repeated name
# fails the duplicate check and a second distinct name fails the allowlist check, both before the
# channel branch — so any assertion written for it would pass for the wrong reason. Restore a case
# for it when a second plugin is allow-listed.

# The frozen proofs of concept under src/ are outside the solution: nothing builds or tests them, so
# a descriptor must never be able to ship one. This is the regression guard on $AllowedPlugins.
Assert-Invalid 'frozen proof-of-concept plugin selected' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; changes = @('Fixed: A fix.'); notes = 'n'; plugins = @('RefineryPlugin') }

# Version progression against the latest stable release (the CI pull-request check passes it; the shape
# checks above omit it). A tag must be strictly greater, and its bump must cover the kinds in `changes`:
# `Added` is minor at least, Changed/Fixed/Removed are patch at least.
function New-StableManifest {
    param([string]$Tag, [string[]]$Changes)
    return @{ tag = $Tag; channel = 'stable'; changes = $Changes; notes = 'n'; plugins = @('SignaturePlugin') }
}

Assert-Valid 'patch bump for a Fixed entry' `
    -Tag 'v2.7.3' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.7.3' @('Fixed: A fix.'))

Assert-Valid 'minor bump for an Added entry' `
    -Tag 'v2.8.0' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.8.0' @('Added: A theme.'))

Assert-Valid 'minor bump is enough for a Fixed entry' `
    -Tag 'v2.8.0' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.8.0' @('Fixed: A fix.'))

Assert-Valid 'major bump is allowed for any entry' `
    -Tag 'v3.0.0' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v3.0.0' @('Fixed: A fix.'))

Assert-Valid 'patch bump across a double-digit component (v2.10.0 is above v2.9.0)' `
    -Tag 'v2.10.1' -LatestStableTag 'v2.10.0' `
    -Manifest (New-StableManifest 'v2.10.1' @('Changed: A tweak.'))

Assert-Invalid 'tag equal to the latest stable release' `
    -Tag 'v2.7.2' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.7.2' @('Fixed: A fix.'))

Assert-Invalid 'tag below the latest stable release' `
    -Tag 'v2.7.1' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.7.1' @('Fixed: A fix.'))

Assert-Invalid 'lower minor within the same major' `
    -Tag 'v2.6.9' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.6.9' @('Fixed: A fix.'))

Assert-Invalid 'Added entry with only a patch bump' `
    -Tag 'v2.7.3' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.7.3' @('Added: A theme.'))

Assert-Valid 'patch bump for a Removed entry' `
    -Tag 'v2.7.3' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.7.3' @('Removed: An old option.'))

Assert-Invalid 'Added among other entries with only a patch bump' `
    -Tag 'v2.7.3' -LatestStableTag 'v2.7.2' `
    -Manifest (New-StableManifest 'v2.7.3' @('Fixed: A fix.', 'Added: A theme.'))

Assert-Valid 'a preview is not compared with the stable line' `
    -Tag 'v2.7.2-signature-alpha.1' -LatestStableTag 'v2.7.2' `
    -Manifest @{ tag = 'v2.7.2-signature-alpha.1'; channel = 'preview'; notes = 'n'; plugins = @('SignaturePlugin') }

# Get-LatestStableTag: highest plain stable tag, by version not by string or position.
function Assert-LatestStable {
    param([string]$Name, [string[]]$Tags, $Expected)
    $actual = Get-LatestStableTag -Tags $Tags
    if ($actual -cne $Expected) { $failures.Add("$Name : expected '$Expected', got '$actual'.") }
}

Assert-LatestStable 'highest by version, not string' @('v2.9.0', 'v2.10.0', 'v2.2.0') 'v2.10.0'
Assert-LatestStable 'order of input does not matter' @('v2.7.2', 'v2.7.1', 'v2.7.0') 'v2.7.2'
Assert-LatestStable 'previews are ignored' @('v2.7.2', 'v2.8.0-signature-alpha.1') 'v2.7.2'
Assert-LatestStable 'no stable tag at all' @('v2.8.0-signature-alpha.1') $null
Assert-LatestStable 'a single tag' @('v2.7.2') 'v2.7.2'
Assert-LatestStable 'empty list' @() $null

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    throw "$($failures.Count) release-descriptor validation check(s) failed."
}

Write-Host 'All release-descriptor validation checks passed.'
