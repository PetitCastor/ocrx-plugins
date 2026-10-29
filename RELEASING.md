# Releasing plugins

`SignaturePlugin` and `ItemTrackerPlugin` are the releasable plugins. `SignaturePlugin` ships on both
the stable and the preview channel; `ItemTrackerPlugin` ships on the preview channel only, as tags like
`v0.1.0-itemtracker-alpha.1`; `$PreviewOnlyPlugins` in the same script rejects a stable descriptor that
selects it (see "Promoting a second plugin to stable" below).
`MissionPlugin` and `RefineryPlugin` remain under `src/` as frozen proofs of concept: they are
outside `OcrxPlugins.slnx`, nothing builds or tests them, and `$AllowedPlugins` in
`scripts/ReleaseDescriptor.Validate.ps1` rejects any descriptor that selects one. Reviving either
means restoring its test project from git history and adding it back to the solution, the allowlist
and CI in the same PR.

## Prepare and publish a release

1. Add `.github/release-manifests/<tag>.json` in the release PR. Stable tags are `v1.2.3`; preview
   tags name the plugin and the stage, as in `v1.2.4-signature-alpha.1`, `v1.2.4-signature-beta.2` or
   `v0.1.0-itemtracker-alpha.1`.
   A stable descriptor must list its `changes`, one user-facing `Kind: text` line per change; they become
   the release's `CHANGELOG.md` asset, which the engine shows before and after an update. See
   `.github/release-manifests/README.md`.
2. Merge the PR into `master`. The merge itself is what ships the release: the Release workflow
   triggers on the push to `master`, resolves the tag from whichever descriptor the merge added,
   validates it, builds and publishes the selected plugin(s), and creates the GitHub release —
   `gh release create` cuts the tag on the merge commit as part of creating the release, so there is
   no separate tagging step and nothing to push by hand. Editing an existing descriptor releases it
   too, unless its tag already exists as a release: then the workflow finds the release and skips, so
   a typo fix on a descriptor that has shipped changes nothing. A descriptor whose tag has not shipped
   yet (for instance an edit made after a failed release run) is released by that push.
   Nothing else releases: a PR that touches `src/` but adds no descriptor merges without publishing
   anything, and its changes go out with the next descriptor. CI says so with a notice on the PR.
3. Pick the tag so CI accepts it. On a stable descriptor, CI requires the tag to be strictly greater
   than the latest stable release (`gh release list --exclude-pre-releases`) and the bump to cover
   `changes`: any `Added` entry needs at least a minor bump (`v2.7.2` to `v2.8.0`); `Changed`, `Fixed`
   and `Removed` need at least a patch bump (`v2.7.3`). A larger bump is always allowed. Previews are
   not compared with the stable line.
4. After a successful **stable** release, check that `plugins.json` still points at the version-less
   `https://github.com/PetitCastor/ocrx-plugins/releases/latest/download/<Plugin>-win-x64.zip`. That
   form is what lets the engine detect updates, and CI (the "Catalog download URL format gate")
   rejects a version-pinned `downloadUrl` in `plugins.json`, so no per-release catalog PR is needed
   for a plugin that is already listed. Preview releases belong in `plugins.preview.json` only after
   an engine version that supports the opt-in preview catalog has shipped.
5. Every `plugins.preview.json` entry must set `"channel": "preview"` explicitly, and its
   `downloadUrl` must pin the exact release tag — previews have no `releases/latest` alias. An entry
   missing `channel` (or carrying the wrong value) is not merely dropped: the engine's
   `PluginCatalog.TryParse` rejects the entire preview catalog for every user, so no preview shows
   up at all. Promoting a plugin to stable must remove its id from `plugins.preview.json` in the
   same PR that adds it to `plugins.json` — an id present in both is also rejected outright.
   `scripts/Test-PluginCatalog.ps1` (run in CI on every PR) enforces all of this before merge.

## Promoting a second plugin to stable

Not yet possible without breaking SignaturePlugin's updates. GitHub's `releases/latest` is the newest
stable release of the whole repository, not of one plugin. A stable `ItemTrackerPlugin` release would
become `latest`, and `releases/latest/download/SignaturePlugin-win-x64.zip`, the URL SignaturePlugin's
update detection depends on, would 404 because that release carries no SignaturePlugin zip. Previews
are GitHub prereleases (the Release workflow passes `--prerelease` for `"channel": "preview"`), which
never become `latest`, so they are safe. Give each plugin its own update URL first, in its own change.

## Preview testers

Preview builds are public GitHub prereleases, unsigned, opt-in, and may change incompatibly. Testers
download the exact release asset from the GitHub prerelease page and should report the full release
tag, the engine version from the release notes ("Tested against OCRX engine ..."), and reproduction details. Plugin previews are executable
release assets; they do not require prerelease NuGet SDK packages.
