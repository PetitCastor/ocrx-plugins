# Release descriptors

Each public plugin release has one committed descriptor named `<tag>.json`. Create it in the
release PR and merge the PR — the Release workflow triggers on that push to `master`, resolves the
tag from the descriptor the merge added, and creates the GitHub release (and its tag) itself. No
separate tagging step. The workflow refuses to run without a matching file, when a merge adds more
than one descriptor, or when the descriptor's own `tag` field disagrees with its filename.

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
