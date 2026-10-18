# Release descriptors

Each public plugin release has one committed descriptor named `<tag>.json`. Create it in the
release PR and merge the PR — the Release workflow triggers on that push to `master`, resolves the
tag from the descriptor the merge added, and creates the GitHub release (and its tag) itself. No
separate tagging step. The workflow refuses to run without a matching file, when a merge adds more
than one descriptor, or when the descriptor's own `tag` field disagrees with its filename.

```json
{
  "tag": "v2.7.2",
  "channel": "stable",
  "plugins": ["SignaturePlugin"],
  "notes": "SignaturePlugin v2.7.2. Built on Ocrx.Sdk 2.7.0; tested against engine v2.0.52.",
  "changes": [
    "Fixed: The overlay no longer flickers when the badge is lost for one frame.",
    "Added: A setting to hide the overlay while the game is paused."
  ]
}
```

`changes` is the release's changelog, and it is what users read: the engine shows these lines in the
plugin's detail pane before they click **Update**, keeps them there after, and lists them under the
plugin's **Changelog** button. The release workflow turns them into the release's `CHANGELOG.md` asset,
on top of the previous stable release's, so the file always carries the whole history.

- One entry per change, `Kind: text`, with `Kind` one of `Added`, `Changed`, `Fixed`, `Removed` (exact
  case).
- One sentence a user would understand, 200 characters at most. Say what they see or get, not what the
  code does: "Fixed: The overlay no longer flickers." is right; "Fixed: debounce in OverlayController" is
  not.
- Required, with at least one entry, on a stable release; optional on a preview (a preview with none is
  recorded as "No user-visible changes.").
- `notes` stays the prose header of the GitHub release body (build and test context); the `changes` lines
  follow it there.

CI validates the descriptor a pull request adds, `changes` included, before it can merge. Descriptors
from before `changes` existed are history and are not re-checked.

A preview descriptor looks the same with `"channel": "preview"`, a preview tag, and `changes` optional:

```json
{
  "tag": "v1.0.17-signature-alpha.1",
  "channel": "preview",
  "plugins": ["SignaturePlugin"],
  "notes": "SignaturePlugin alpha preview. Known issue: replay corpus coverage is incomplete."
}
```

Stable tags use `v<major>.<minor>.<patch>`. Preview tags use
`v<major>.<minor>.<patch>-<plugin>-<alpha|beta|rc>.<n>` and select exactly one plugin.

`SignaturePlugin` is the only selectable plugin; see `RELEASING.md` for why the other two under
`src/` are not.
