# Dot-sourced by Test-ReleaseDescriptor.ps1 (the CI entry point), release.yml's "Publish selected
# plugins" step, and Test-ReleaseDescriptor.Tests.ps1 (fixture-driven checks with no file I/O) — so
# the plugin allowlist and the validation rules built on it are pinned in exactly one place.

. "$PSScriptRoot/ChangelogEntry.ps1"

# Every entry here is also a `src/<name>` project directory by convention; release.yml relies on
# that to build its publish path map without hand-duplicating this list.
#
# SignaturePlugin is the only releasable plugin. MissionPlugin and RefineryPlugin still exist under
# src/ but are frozen proofs of concept outside OcrxPlugins.slnx — nothing builds or tests them, so
# they must not be selectable by a release descriptor. Re-add a name here (and to $shortNames below)
# only together with its solution and test entries.
$script:AllowedPlugins = @('SignaturePlugin')

function Test-ReleaseDescriptorManifest {
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string]$Tag
    )

    if ($Manifest.tag -cne $Tag) { throw "Descriptor tag '$($Manifest.tag)' does not match '$Tag'." }
    if ($Manifest.channel -notin @('stable', 'preview')) { throw "channel must be 'stable' or 'preview'." }
    if ([string]::IsNullOrWhiteSpace($Manifest.notes)) { throw 'notes must be non-empty.' }

    # `changes` is the release's changelog: one-line `Kind: text` entries the engine shows the user before
    # an update (see ChangelogEntry.ps1). Required on a stable release, so a stable release never ships
    # with nothing to say; optional on a preview. Only the descriptor being released is checked, so the
    # descriptors that predate the field stay valid history.
    $hasChanges = $null -ne $Manifest.PSObject.Properties['changes']
    $changes = if ($hasChanges) { @($Manifest.changes) } else { @() }
    if ($Manifest.channel -eq 'stable' -and $changes.Count -eq 0) {
        throw 'changes must list at least one "Kind: text" entry for a stable release.'
    }
    $seenChanges = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($change in $changes) {
        if ($change -isnot [string]) { throw 'every changes entry must be a string.' }
        $entry = ConvertFrom-ChangelogEntry -Entry $change -Source 'changes'
        if (-not $seenChanges.Add("$($entry.Kind)|$($entry.Text)")) { throw "changes lists '$change' twice." }
    }

    $plugins = @($Manifest.plugins)
    if ($plugins.Count -eq 0) { throw 'plugins must contain at least one plugin.' }
    if (($plugins | Select-Object -Unique).Count -ne $plugins.Count) { throw 'plugins must not contain duplicates.' }
    if (($plugins | Where-Object { $_ -notin $script:AllowedPlugins }).Count -gt 0) { throw 'plugins contains an unknown plugin.' }

    if ($Manifest.channel -eq 'stable') {
        if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Stable tags must be v<major>.<minor>.<patch>.' }
    }
    else {
        # Unreachable while $AllowedPlugins holds a single name — two identical names die on the
        # duplicate check above, any second distinct name on the allowlist check — so it is also
        # untestable today (see Test-ReleaseDescriptor.Tests.ps1). Kept, not deleted: it is the rule
        # that a preview ships one plugin, and it becomes live and testable again the moment a
        # second plugin is allow-listed.
        if ($plugins.Count -ne 1) { throw 'A preview release must select exactly one plugin.' }
        $shortNames = @{ SignaturePlugin = 'signature' }
        $expected = '^v\d+\.\d+\.\d+-' + $shortNames[$plugins[0]] + '-(alpha|beta|rc)\.\d+$'
        if ($Tag -notmatch $expected) { throw "Preview tag '$Tag' must identify the selected plugin and stage." }
    }
}
