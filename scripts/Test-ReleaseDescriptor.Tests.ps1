# Fixture-driven checks for scripts/ReleaseDescriptor.Validate.ps1's gating rules. No Pester
# dependency (the repo pins none for PowerShell): plain pass/throw assertions, run directly with
# `powershell -File scripts/Test-ReleaseDescriptor.Tests.ps1`.

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseDescriptor.Validate.ps1"

$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Valid {
    param([string]$Name, [hashtable]$Manifest, [string]$Tag)
    try {
        Test-ReleaseDescriptorManifest -Manifest ([pscustomobject]$Manifest) -Tag $Tag
    }
    catch {
        $failures.Add("$Name : expected valid, but got error: $($_.Exception.Message)")
    }
}

function Assert-Invalid {
    param([string]$Name, [hashtable]$Manifest, [string]$Tag)
    try {
        Test-ReleaseDescriptorManifest -Manifest ([pscustomobject]$Manifest) -Tag $Tag
        $failures.Add("$Name : expected an error, but validation passed.")
    }
    catch {
        # Expected.
    }
}

Assert-Valid 'stable tag with one plugin' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'release notes'; plugins = @('SignaturePlugin') }

Assert-Valid 'preview tag matching plugin and stage' `
    -Tag 'v1.2.4-signature-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-signature-alpha.1'; channel = 'preview'; notes = 'preview notes'; plugins = @('SignaturePlugin') }

Assert-Invalid 'tag mismatch between descriptor and pushed tag' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v9.9.9'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'unknown channel' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'nightly'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'empty notes' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = '  '; plugins = @('SignaturePlugin') }

Assert-Invalid 'no plugins selected' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @() }

Assert-Invalid 'duplicate plugin in selection' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin', 'SignaturePlugin') }

Assert-Invalid 'unknown plugin name' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @('NotAPlugin') }

Assert-Invalid 'stable tag with a prerelease suffix' `
    -Tag 'v1.2.3-rc.1' `
    -Manifest @{ tag = 'v1.2.3-rc.1'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'preview tag naming a plugin that is not the selected one' `
    -Tag 'v1.2.4-refinery-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-refinery-alpha.1'; channel = 'preview'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'preview tag with an unrecognized stage' `
    -Tag 'v1.2.4-signature-nightly.1' `
    -Manifest @{ tag = 'v1.2.4-signature-nightly.1'; channel = 'preview'; notes = 'n'; plugins = @('SignaturePlugin') }

# One rule in Test-ReleaseDescriptorManifest has no test here: "a preview release must select
# exactly one plugin". With a single-name $AllowedPlugins it cannot be reached — a repeated name
# fails the duplicate check and a second distinct name fails the allowlist check, both before the
# channel branch — so any assertion written for it would pass for the wrong reason. Restore a case
# for it when a second plugin is allow-listed.

# The frozen proofs of concept under src/ are outside the solution: nothing builds or tests them, so
# a descriptor must never be able to ship one. This is the regression guard on $AllowedPlugins.
Assert-Invalid 'frozen proof-of-concept plugin selected' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @('RefineryPlugin') }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    throw "$($failures.Count) release-descriptor validation check(s) failed."
}

Write-Host 'All release-descriptor validation checks passed.'
