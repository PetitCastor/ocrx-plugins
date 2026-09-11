# Release descriptors

Each public plugin release has one committed descriptor named `<tag>.json`. Create it in the
release PR, merge the PR, then create the matching tag from the merged commit. The release workflow
refuses to run without this file or when its tag/channel/plugin selection does not match the ref.

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
