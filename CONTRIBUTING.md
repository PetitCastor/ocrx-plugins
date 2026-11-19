# Contributing

Thanks for your interest in OCRX Plugins. Bug reports, screenshots of wrong reads, new signature
values and pull requests are all welcome.

## Where to go

| You want to… | Use |
|---|---|
| Ask how a plugin works, or get help with an install | [Discussions](https://github.com/PetitCastor/ocrx-plugins/discussions) |
| Report a wrong ore or a missed read | [Discussions](https://github.com/PetitCastor/ocrx-plugins/discussions), with a screenshot |
| Share a signature value the plugin does not know | [Discussions](https://github.com/PetitCastor/ocrx-plugins/discussions) |
| Pitch an idea for a new plugin | [Discussions](https://github.com/PetitCastor/ocrx-plugins/discussions) |
| Report a bug you can reproduce | [Open an issue](https://github.com/PetitCastor/ocrx-plugins/issues/new/choose) with the bug template |
| Suggest an improvement to an existing plugin | [Open an issue](https://github.com/PetitCastor/ocrx-plugins/issues/new/choose) with the feature template |
| Report a security problem | Privately, as described in [SECURITY.md](SECURITY.md) |

Before opening an issue, search the existing issues and discussions, and read the plugin's
troubleshooting section, for instance
[SignaturePlugin's](src/SignaturePlugin/README.md#troubleshooting).

## Sharing what the plugin saw

The plugins read text off the screen, so a screenshot of the moment something went wrong is usually
the most useful thing you can attach. Give your resolution, display mode (windowed, borderless or
fullscreen) and, for SignaturePlugin, your in-game FOV: all three change where the text is.

For logs, run `SignaturePlugin.exe --verbose` as its README describes, or attach Item Tracker's
`tracker.log` from `%LOCALAPPDATA%\OCRX\ItemTracker`.

`Game.log` contains your RSI handle and other account details. Share only the lines around the
problem, and remove anything you do not want public before posting. Crop screenshots the same way.

## Pull requests

1. For anything larger than a small fix, start a discussion or open an issue first so we can agree
   on the approach.
2. Fork the repository and create a branch from `master`.
3. Build and test as described in the [README](README.md#4-run-and-test) and, for SignaturePlugin,
   its [development guide](src/SignaturePlugin/DEVELOPMENT.md). All tests must pass:

   ```powershell
   dotnet test OcrxPlugins.slnx --filter "Category!=Integration"
   ```

   The `Integration` tests replay recorded frames through a real OCRX engine and Windows OCR; CI
   runs them, so you can leave them out locally.
4. Add tests for new behaviour. For a misread, the best test is the frame that caused it, added to
   the replay corpus as SignaturePlugin's development guide describes.
5. Open the pull request against `master`. Pull requests are squash-merged and use
   [Conventional Commits](https://www.conventionalcommits.org/) titles, for example
   `fix(signature): keep the overlay on a one-frame blank`.

Merging a pull request does not release anything by itself. Releases and their player-facing
changelog are cut by the maintainer, as described in [RELEASING.md](RELEASING.md).

## Writing a new plugin

The [README](README.md#create-a-plugin) walks through a minimal plugin, and the
[SDK plugin-authoring guide](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md)
covers the SDK in depth. You do not need to host your plugin here: a plugin is any program built on
the `Ocrx.Sdk` packages. Open a discussion if you would like it considered for the official catalog.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
