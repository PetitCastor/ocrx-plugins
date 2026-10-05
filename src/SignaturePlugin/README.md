# SignaturePlugin

An [OCRX](https://ocrx.org) plugin for Star Citizen mining. It reads the **RS signature number**
shown in scan mode and tells you, on screen, which ore cluster it is — `Ice x4`, `Bexalite x5`, and
so on — so you do not have to look the number up.

## Using it

1. Start the OCRX engine and install SignaturePlugin: right-click the tray icon, choose
   **Plugins…**, and install it.
2. Launch Star Citizen. The engine offers to capture it: accept the **Capture Star Citizen** offer,
   which also starts the plugin. (Otherwise pick **Launch** for SignaturePlugin in the tray menu;
   the engine never starts a plugin on its own.)
3. In game, open scan mode and point at a rock. The overlay appears as soon as the signature is
   read and disappears when the badge does.

That is all there is to it. Nothing needs calibrating: the plugin follows your in-game field of
view by reading it from the game's own profile, and moves its reading zone to match.

A few things worth knowing:

- **Ambiguous totals show both candidates.** Some totals can be two different clusters — 19200 is
  Savrilium x6 *and* Aslarite x5, and nothing on screen tells them apart. The overlay then reads
  `Savrilium x6 / Aslarite x5` rather than guessing.
- **It stays put on a bad read.** One misread digit will not rename the ore, and a brief blank does
  not hide the overlay. A different value has to be read twice in a row before it replaces the
  current one, and the overlay only clears after about 3 seconds of blank reads (at the default
  scan rate). As a backstop it also clears about 10 seconds after the last real match, so a stale
  value never stays on screen.
- **Everything stays on your machine.** Nothing is sent anywhere.

## Settings

Two settings, both about the overlay. Change them from the plugin's settings in the engine. The change applies at once
while the plugin is running; otherwise it is kept and applied the next time the plugin starts.

| Setting | Choices (as shown in the UI) | Default |
| --- | --- | --- |
| **Theme** | Default, Star Citizen, Retro | Default |
| **Position** | Top Left, Top Center, Top Right, Middle Left, Center, Middle Right, Bottom Left, Bottom Center, Bottom Right | Top Center |

**Theme** is the look of the overlay pill. Default is a plain dark pill whose colours and size can
be tuned by hand; Star Citizen is a compact navy pill with a cyan outline; Retro is a square-cornered
purple pill with pixel text.

**Position** is where the overlay is anchored on your primary screen. It is independent of the
theme: changing one never changes the other.

### Editing by hand

Both settings live in `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json` as `overlayTheme` and
`position`, if you prefer a file to the UI (restart the plugin after editing it). The file takes
lowercase ids: `default`, `citizen` or `retro`, and `topleft`, `topcenter`, `topright`,
`middleleft`, `center`, `middleright`, `bottomleft`, `bottomcenter` or `bottomright`:

```json
"overlayTheme": "citizen",
"position": "topright"
```

The same file holds the `outputs` list. By default every change of reading (and every clear) is appended to
`captures/signatures.jsonl` next to the config, one JSON line each, for use in your own tools. Set `outputs` to `[]` to switch off both the file and the overlay.

## Troubleshooting

- **Nothing shows up.** Make sure scan mode is open and the signature number is visible, and that
  the engine is running. If you installed an older version, check two values under `overlay` in
  `config.json`: `lingerMs` must be `0`, otherwise the overlay hides itself a moment after
  appearing and never comes back for that rock; and `template` should be `{cluster}`, otherwise an
  ambiguous total shows only its first candidate.
- **Wrong ore.** The table of ore signatures is `signature-table.json` in the same folder. It is
  created on first launch and never overwritten, so you can edit it (then restart the plugin) if a
  game patch changes the values.
- **Still stuck?** Stop the plugin from the tray menu, then run `SignaturePlugin.exe --verbose`
  from its install folder (`%LOCALAPPDATA%\OCRX\plugins\<plugin-id>`) with the engine running. The
  per-tick log shows the raw OCR text, the number parsed from it, and what the table made of it.

## For contributors

ROI calibration, the replay corpus, the overlay's debounce rules, and build and test commands are
in [DEVELOPMENT.md](DEVELOPMENT.md).
