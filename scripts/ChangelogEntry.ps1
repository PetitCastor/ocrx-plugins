# Dot-sourced by ReleaseDescriptor.Validate.ps1 (CI and the release workflow) and Build-Changelog.ps1,
# so the grammar of a release descriptor's `changes` entries is written down once in this repository.
#
# Same grammar as ocrx-engine's changelog fragments (.github/tools/ChangelogFragment.ps1 there), copied
# rather than shared because the two repositories never reference each other's files: a fix to one
# belongs in the other too. The engine reads the resulting CHANGELOG.md, so the two must agree.
#
# One entry is `Kind: text`, Kind one of Added, Changed, Fixed, Removed (exact case), text one sentence
# a user would understand, 200 characters at most. Runs on Windows PowerShell 5.1 and pwsh.

$script:ChangelogKinds = @('Added', 'Changed', 'Fixed', 'Removed')
$script:ChangelogMaxEntryLength = 200

function ConvertFrom-ChangelogEntry {
    # Returns @{ Kind; Text } for one entry, or throws naming what is wrong with it.
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string]$Entry,
        [Parameter(Mandatory)] [string]$Source
    )

    $line = $Entry.Trim()
    if ($line -cnotmatch '^(?<kind>[A-Za-z]+):\s+(?<text>\S.*)$') {
        throw "${Source}: '$line' is not in the form 'Kind: text'."
    }
    $kind = $Matches['kind']
    $text = $Matches['text'].Trim()

    if ($script:ChangelogKinds -cnotcontains $kind) {
        throw "${Source}: unknown kind '$kind'; use one of $($script:ChangelogKinds -join ', ') (exact case)."
    }
    if ($text.Length -gt $script:ChangelogMaxEntryLength) {
        throw "${Source}: entry is $($text.Length) characters; keep it to $($script:ChangelogMaxEntryLength) or fewer."
    }
    # The pattern's `.` never matches a line feed, so only a carriage return can still be inside.
    if ($text.Contains("`r")) {
        throw "${Source}: an entry is one line."
    }

    return [pscustomobject]@{ Kind = $kind; Text = $text }
}
