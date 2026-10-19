param(
    [Parameter(Mandatory)]
    [string]$Tag,

    # The latest stable release's tag. When given, a stable descriptor is also checked against it (tag
    # strictly greater, bump large enough for `changes`). CI passes it; the release workflow, which
    # runs after the tag is chosen, does not.
    [string]$LatestStableTag
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseDescriptor.Validate.ps1"

$manifestPath = ".github/release-manifests/$Tag.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "A release descriptor is required at $manifestPath."
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}
catch {
    throw "Release descriptor is not valid JSON: $manifestPath"
}

Test-ReleaseDescriptorManifest -Manifest $manifest -Tag $Tag -LatestStableTag $LatestStableTag
